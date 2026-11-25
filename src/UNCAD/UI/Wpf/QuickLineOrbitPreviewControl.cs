using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// U1U preview host: a synchronous WPF scene. Scene failures never block BOQ work.
    /// 3.8.0（D-059）：场景只剩楼层平面 + 轴位 + 线路 + 上游柜，工具条只留
    /// 「轴位」「复位」；WebV（独立 WebView2 窗口）整体删除。
    /// </summary>
    public sealed class QuickLineOrbitPreviewControl : Grid
    {
        private static readonly Brush HintBrush = CreateFrozenBrush(0x8A, 0x8A, 0x8A);
        // D-058：识别出的问题用琥珀色点名，压在视口左下角的半透明底衬上。
        private static readonly Brush IssueBrush = CreateFrozenBrush(0xF2, 0xC9, 0x4C);
        private static readonly Brush InfoBackground = CreateFrozenBrush(0xB3, 0x0D, 0x13, 0x1A);
        private static readonly Brush ButtonBackground =
            CreateFrozenBrush(0x16, 0x1D, 0x26);
        private static readonly Brush ButtonForeground =
            CreateFrozenBrush(0xC7, 0xD3, 0xDF);

        private readonly QuickLine3dViewportControl _viewport =
            new QuickLine3dViewportControl();
        private readonly TextBlock _hintText;
        private readonly TextBlock _legendText;
        private readonly TextBlock _summaryText;
        private readonly TextBlock _issueText;
        private readonly Border _infoPanel;
        private readonly Button _gridButton;
        private readonly Button _resetButton;

        private QuickLine3dScene _scene;
        private QuickLineOrbitPreviewModel _model;
        private QuickLineOrbitPreviewEnvelope _envelope;
        private bool _columnGridEnabled = true;
        private bool _hasColumnGrid;
        private bool _fatal;

        public QuickLineOrbitPreviewControl()
        {
            ClipToBounds = true;

            Children.Add(_viewport);

            _hintText = new TextBlock
            {
                Text = "该图框没有可预览的带毫米标注 U1L 路由",
                Foreground = HintBrush,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            _hintText.SetValue(MarginProperty, new Thickness(24));
            _hintText.SetValue(HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            _hintText.SetValue(VerticalAlignmentProperty,
                VerticalAlignment.Center);
            Children.Add(_hintText);

            var buttons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0)
            };
            _gridButton = CreateSceneButton("轴位 开", OnGridButton);
            _resetButton = CreateSceneButton("复位", OnResetButton);
            buttons.Children.Add(_gridButton);
            buttons.Children.Add(_resetButton);
            Children.Add(buttons);

            _legendText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            _summaryText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            // D-058：识别出的问题（无毫米标的线段、断口、孤立标注…）点名。
            _issueText = new TextBlock
            {
                Foreground = IssueBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            var infoStack = new StackPanel { Orientation = Orientation.Vertical };
            infoStack.Children.Add(_issueText);
            infoStack.Children.Add(_summaryText);
            infoStack.Children.Add(_legendText);
            // 识别摘要/问题/图例自下而上排在一块半透明底衬上，不挡鼠标。
            _infoPanel = new Border
            {
                Background = InfoBackground,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(8, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = 760.0,
                IsHitTestVisible = false,
                Child = infoStack
            };
            Children.Add(_infoPanel);

            ShowIdleState();
        }

        /// <summary>当前预览的场景（宿主/测试可读；为 null 时显示提示）。</summary>
        public QuickLineOrbitPreviewModel Model => _model;

        /// <summary>当前预览的渲染信封（测试读识别摘要/问题清单用；无场景时为 null）。</summary>
        internal QuickLineOrbitPreviewEnvelope CurrentEnvelope => _envelope;

        // ---- 数据级计数器（取自信封；口径与历版一致） ---------------------------

        internal int BuiltSegmentLineCount { get; private set; }
        internal int BuiltLabelCount { get; private set; }
        internal int BuiltColumnGridLineCount =>
            _columnGridEnabled ? _plannedColumnGridLineCount : 0;
        internal int BuiltAxisTickCount =>
            _columnGridEnabled ? _plannedAxisTickCount : 0;
        internal int BuiltLetterLabelCount =>
            _columnGridEnabled ? _plannedLetterLabelCount : 0;
        internal int BuiltEndpointPinCount { get; private set; }
        internal int BuiltTrayChannelCount { get; private set; }
        internal int BuiltConduitChannelCount { get; private set; }
        /// <summary>D-059：楼层平面数。</summary>
        internal int BuiltFloorCount { get; private set; }

        internal bool ColumnGridEnabled => _columnGridEnabled;

        private int _plannedColumnGridLineCount;
        private int _plannedAxisTickCount;
        private int _plannedLetterLabelCount;

        /// <summary>切换场景（含 null=隐藏为提示文字）。同步建场景，无任何异步。</summary>
        public void SetModel(QuickLineOrbitPreviewModel model)
        {
            if (ReferenceEquals(_model, model) && !_fatal) return;
            _model = model;
            _scene = null;
            _fatal = false;
            QuickLine3dScene scene = null;
            var clock = Stopwatch.StartNew();
            try
            {
                _envelope = QuickLineOrbitPreviewEnvelope.Build(model);
                scene = QuickLineOrbitSceneBuilder.Build(_envelope);
                _viewport.SetScene(scene ?? new QuickLine3dScene(),
                    initiallyHiddenGroups: _columnGridEnabled
                        ? null : new[] { QuickLine3dSceneGroupKind.Grid });
                _scene = scene;
            }
            catch (Exception ex)
            {
                Log.Warn("[U1U] 3D 预览场景构建失败: " + ex);
                _envelope = null;
                _viewport.SetScene(new QuickLine3dScene());
                RefreshDataCounters();
                ShowIdleState();
                ShowFatalError("3D 预览场景构建失败：" + ex.Message
                    + "\n（不影响清单写入；可用 U1S/U1F 正常出清单。）");
                return;
            }
            clock.Stop();
            RefreshDataCounters();
            // 现场「卡不卡」靠这行定位：建场景耗时 + 图元/三角形/标签数。
            if (_scene != null)
                Log.Info("[PERF] U1U 3D 场景: " + clock.ElapsedMilliseconds + " ms，图元 "
                    + _scene.Primitives.Count + "，网格 " + _viewport.MeshCount
                    + "，三角形 " + _viewport.TriangleCount + "，标签 "
                    + _scene.Labels.Count);
            if (_envelope == null)
            {
                ShowIdleState();
                return;
            }
            _hintText.Visibility = Visibility.Collapsed;
            _hasColumnGrid = _envelope.Floors.Count > 0;
            ApplyToggles();
            // D-058：摘要＝线路识别结果＋轴位；问题逐条点名（窗格里最多 3 条，
            // 全量在命令行/批量备注里）；图例末尾带鼠标操作提示。
            _legendText.Text = QuickLineOrbitSceneBuilder.BuildLegend(_envelope)
                + "　" + QuickLineOrbitSceneBuilder.InteractionHint;
            _summaryText.Text = QuickLineOrbitSceneBuilder.BuildSummary(_envelope);
            _issueText.Text = BuildIssueText(_envelope);
            _issueText.Visibility = _issueText.Text.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
            _infoPanel.Visibility = Visibility.Visible;
        }

        /// <summary>复位视角。</summary>
        internal void ResetCameraView()
        {
            _viewport.ResetCamera();
        }

        /// <summary>轴位（柱网线 + 边标）显示开关。</summary>
        internal void ToggleColumnGrid()
        {
            _columnGridEnabled = !_columnGridEnabled;
            ApplyToggles();
        }

        /// <summary>致命错误路径：亮出提示文字（场景已清空为暗底，无白窗可遮）。</summary>
        internal void ShowFatalError(string message)
        {
            if (_fatal) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    (Action<string>)ShowFatalError, DispatcherPriority.Normal,
                    message);
                return;
            }
            _fatal = true;
            _hintText.Text = message;
            _hintText.Visibility = Visibility.Visible;
        }

        private void ShowIdleState()
        {
            _hintText.Visibility = Visibility.Visible;
            _infoPanel.Visibility = Visibility.Collapsed;
            _gridButton.Visibility = Visibility.Collapsed;
            _resetButton.Visibility = Visibility.Collapsed;
        }

        private void RefreshDataCounters()
        {
            BuiltSegmentLineCount = 0;
            BuiltLabelCount = 0;
            BuiltFloorCount = 0;
            _plannedColumnGridLineCount = 0;
            _plannedAxisTickCount = 0;
            _plannedLetterLabelCount = 0;
            BuiltEndpointPinCount = 0;
            BuiltTrayChannelCount = 0;
            BuiltConduitChannelCount = 0;
            QuickLineOrbitPreviewEnvelope envelope = _envelope;
            if (envelope == null) return;
            BuiltSegmentLineCount = envelope.Segments.Count;
            BuiltLabelCount = envelope.SegmentLabels.Count;
            BuiltFloorCount = envelope.Floors.Count;
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeFloor floor in envelope.Floors)
                _plannedColumnGridLineCount += floor.GridLines.Count / 2;
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeLabel label in envelope.AxisEdgeLabels)
            {
                if (label.Text.Length > 0 && char.IsDigit(label.Text[0])) _plannedAxisTickCount++;
                else _plannedLetterLabelCount++;
            }
            BuiltEndpointPinCount = envelope.EndpointPins.Count;
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopeSegment segment
                in envelope.Segments)
            {
                if (segment.Channel == null) continue;
                if (string.Equals(segment.Channel.Kind, "tray",
                    StringComparison.OrdinalIgnoreCase))
                    BuiltTrayChannelCount++;
                else if (string.Equals(segment.Channel.Kind, "conduit",
                    StringComparison.OrdinalIgnoreCase))
                    BuiltConduitChannelCount++;
            }
        }

        // ---- 开关按钮 -------------------------------------------------------------

        private void ApplyToggles()
        {
            _gridButton.Visibility = _hasColumnGrid
                ? Visibility.Visible
                : Visibility.Collapsed;
            _resetButton.Visibility = _envelope != null
                ? Visibility.Visible
                : Visibility.Collapsed;

            _gridButton.Content = _columnGridEnabled ? "轴位 开" : "轴位 关";

            _viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Grid,
                _columnGridEnabled);
        }

        private void OnGridButton(object sender, RoutedEventArgs args)
        {
            ToggleColumnGrid();
        }

        private void OnResetButton(object sender, RoutedEventArgs args)
        {
            ResetCameraView();
        }

        private const int IssuesShownInPane = 3;

        /// <summary>D-058：窗格里点名的问题（最多 3 条，其余计数提示）。</summary>
        internal string BuildIssueText(QuickLineOrbitPreviewEnvelope envelope)
        {
            if (envelope == null || envelope.RouteIssues.Count == 0)
                return string.Empty;
            var shown = new List<string>();
            foreach (string issue in envelope.RouteIssues.Take(IssuesShownInPane))
                shown.Add("⚠ " + issue);
            if (envelope.RouteIssues.Count > IssuesShownInPane)
                shown.Add("…另有 " + (envelope.RouteIssues.Count - IssuesShownInPane)
                    + " 项，详见命令行输出");
            return string.Join("\n", shown);
        }

        private static Button CreateSceneButton(string text,
            RoutedEventHandler onClick)
        {
            var button = new Button
            {
                Content = text,
                FontSize = 12,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(4, 0, 0, 0),
                Background = ButtonBackground,
                Foreground = ButtonForeground,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            button.Click += onClick;
            return button;
        }

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
            => CreateFrozenBrush(0xFF, r, g, b);

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// U1U preview host: synchronous WPF scene with an optional isolated WebV
    /// window consuming the same geometry. Scene failures never block BOQ work.
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
        private readonly Button _columnsButton;
        private readonly Button _refButton;
        private readonly Button _detailButton;
        private readonly Button _resetButton;
        private readonly Button _webButton;

        private QuickLine3dScene _scene;
        private QuickLineOrbitPreviewModel _model;
        private QuickLineOrbitPreviewEnvelope _envelope;
        private bool _columnGridEnabled = true;
        private bool _columnsEnabled = true;
        private bool _referenceEnabled = true;
        private bool _detailEnabled;
        private bool _hasReferenceLevels;
        private bool _hasDetailStructure;
        private bool _hasColumnGrid;
        private bool _hasColumns;
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
            _gridButton = CreateSceneButton("柱网 开", OnGridButton);
            _columnsButton = CreateSceneButton("柱子 开", OnColumnsButton);
            _refButton = CreateSceneButton("参照 开", OnRefButton);
            _detailButton = CreateSceneButton("结构 开", OnDetailButton);
            _resetButton = CreateSceneButton("复位", OnResetButton);
            _webButton = CreateSceneButton("WebV", OnWebPreview);
            _webButton.ToolTip = "在独立窗口中用 WebView2 / Three.js 打开当前场景";
            buttons.Children.Add(new TextBlock
            {
                Text = "WPF", Foreground = HintBrush, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            });
            buttons.Children.Add(_gridButton);
            buttons.Children.Add(_columnsButton);
            buttons.Children.Add(_refButton);
            buttons.Children.Add(_detailButton);
            buttons.Children.Add(_resetButton);
            buttons.Children.Add(_webButton);
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

        internal bool ColumnGridEnabled => _columnGridEnabled;
        internal bool ColumnsEnabled => _columnsEnabled;

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
            _hasColumns = false;
            QuickLine3dScene scene = null;
            try
            {
                // 3.3.6：参照层清单来自 U1SET（标签=毫米=类型=锚定；空=不画）。
                // 3.3.7 三轮：清单可带类型/锚定段与「本层=」指令。
                string rawLevels = Settings.Get(ConfigKeys.PreviewReferenceLevels,
                    PreviewReferenceLevels.DefaultList);
                PreviewReferenceLevels.TryParseBaseFloor(rawLevels,
                    out string baseFloorLabel);
                _envelope = QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse(rawLevels), baseFloorLabel);
                scene = QuickLineOrbitSceneBuilder.Build(_envelope);
                var hidden = new List<QuickLine3dSceneGroupKind>();
                if (!_columnGridEnabled) hidden.Add(QuickLine3dSceneGroupKind.Grid);
                if (!_columnsEnabled) hidden.Add(QuickLine3dSceneGroupKind.Columns);
                if (!_referenceEnabled) hidden.Add(QuickLine3dSceneGroupKind.Reference);
                if (!_detailEnabled) hidden.Add(QuickLine3dSceneGroupKind.Detail);
                _viewport.SetScene(scene ?? new QuickLine3dScene(),
                    initiallyHiddenGroups: hidden);
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
            RefreshDataCounters();
            if (_envelope == null)
            {
                ShowIdleState();
                return;
            }
            _hintText.Visibility = Visibility.Collapsed;
            _hasReferenceLevels = _envelope.ReferenceLevels.Count > 0;
            _hasDetailStructure = ContainsDetail(scene);
            _hasColumnGrid = _envelope.ColumnGridLines.Count > 0;
            _hasColumns = _viewport.HasGroup(
                QuickLine3dSceneGroupKind.Columns);
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

        /// <summary>柱网显示开关（数据级计数器随动）。</summary>
        internal void ToggleColumnGrid()
        {
            _columnGridEnabled = !_columnGridEnabled;
            ApplyToggles();
        }

        internal void ToggleColumns()
        {
            _columnsEnabled = !_columnsEnabled;
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
            _columnsButton.Visibility = Visibility.Collapsed;
            _refButton.Visibility = Visibility.Collapsed;
            _detailButton.Visibility = Visibility.Collapsed;
            _resetButton.Visibility = Visibility.Collapsed;
            _webButton.Visibility = Visibility.Collapsed;
        }

        private static bool ContainsDetail(QuickLine3dScene scene)
        {
            foreach (QuickLine3dPrimitive primitive in scene.Primitives)
            {
                if (primitive.Group == QuickLine3dSceneGroupKind.Detail)
                    return true;
            }
            return false;
        }

        private void RefreshDataCounters()
        {
            BuiltSegmentLineCount = 0;
            BuiltLabelCount = 0;
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
            _plannedColumnGridLineCount = envelope.ColumnGridLines.Count / 2;
            _plannedAxisTickCount = envelope.NumericTicks.Count;
            _plannedLetterLabelCount = envelope.LetterLabels.Count;
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
            _columnsButton.Visibility = _hasColumns
                ? Visibility.Visible
                : Visibility.Collapsed;
            _refButton.Visibility = _hasReferenceLevels
                ? Visibility.Visible
                : Visibility.Collapsed;
            _detailButton.Visibility = _hasDetailStructure
                ? Visibility.Visible
                : Visibility.Collapsed;
            _resetButton.Visibility = _envelope != null
                ? Visibility.Visible
                : Visibility.Collapsed;
            _webButton.Visibility = _resetButton.Visibility;

            _gridButton.Content = _columnGridEnabled ? "柱网 开" : "柱网 关";
            _columnsButton.Content = _columnsEnabled ? "柱子 开" : "柱子 关";
            _refButton.Content = _referenceEnabled ? "参照 开" : "参照 关";
            _detailButton.Content = _detailEnabled ? "结构 开" : "结构 关";

            _viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Grid,
                _columnGridEnabled);
            _viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Columns,
                _columnsEnabled);
            _viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Reference,
                _referenceEnabled);
            _viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Detail,
                _detailEnabled);
        }

        private void OnGridButton(object sender, RoutedEventArgs args)
        {
            _columnGridEnabled = !_columnGridEnabled;
            ApplyToggles();
        }

        private void OnColumnsButton(object sender, RoutedEventArgs args)
        {
            ToggleColumns();
        }

        private void OnRefButton(object sender, RoutedEventArgs args)
        {
            _referenceEnabled = !_referenceEnabled;
            ApplyToggles();
        }

        private void OnDetailButton(object sender, RoutedEventArgs args)
        {
            _detailEnabled = !_detailEnabled;
            ApplyToggles();
        }

        private void OnResetButton(object sender, RoutedEventArgs args)
        {
            ResetCameraView();
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

        private async void OnWebPreview(object sender, RoutedEventArgs args)
        {
            if (_scene == null) return;
            _webButton.IsEnabled = false;
            try
            {
                var hidden = new List<string>();
                if (!_columnGridEnabled) hidden.Add("Grid");
                if (!_columnsEnabled) hidden.Add("Columns");
                if (!_referenceEnabled) hidden.Add("Reference");
                if (!_detailEnabled) hidden.Add("Detail");
                await QuickLineWebPreview.OpenAsync(_scene,
                    "U1U · " + _model.AxisSummary, hidden);
            }
            catch (Exception ex)
            {
                Log.Warn("[U1U] WebV 预览启动失败: " + ex.Message);
                _summaryText.Text = "WebV 未打开：" + ex.Message;
            }
            finally
            {
                _webButton.IsEnabled = true;
            }
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

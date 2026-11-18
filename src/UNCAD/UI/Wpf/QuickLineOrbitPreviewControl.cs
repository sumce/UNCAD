using System;
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
    /// 3.4.0：U1U 确认窗左侧的 3D 路由预览——渲染内核从 WebView2+three.js
    /// 换回 **WPF 原生 Viewport3D**（用户指令「算了 用 Viewport3D吧」；
    /// 3.3.5~3.3.8 的浏览器内核在本机被 WebView2 用户数据目录锁同步阻塞
    /// STA 线程，「正在启动三维渲染器」白屏反复复发，浏览器整条路退役）。
    /// 分层铁律不变：场景语义全在 Core 信封（QuickLineOrbitPreviewEnvelope）
    /// 与 QuickLineOrbitSceneBuilder（three.js 页面视觉公式原样搬迁），
    /// 本控件只做宿主三件事——SetModel→同步建场景（无任何异步初始化，
    /// 「启动」这个状态不存在）、四个开关按钮（柱网/参照/结构/复位）、
    /// 图例/摘要文案。对宿主窗 API 与历版同形（SetModel/Model/柱网开关/
    /// 复位），数据级计数器仍取自信封（SetModel 时同步算好，无头测试照常
    /// 断言）。场景构建失败整块降级为提示文案，绝不炸确认窗（预览是展示
    /// 件的 3.3.0.1 降级口径）。
    /// </summary>
    public sealed class QuickLineOrbitPreviewControl : Grid
    {
        private static readonly Brush HintBrush = CreateFrozenBrush(0x8A, 0x8A, 0x8A);
        private static readonly Brush ButtonBackground =
            CreateFrozenBrush(0x16, 0x1D, 0x26);
        private static readonly Brush ButtonForeground =
            CreateFrozenBrush(0xC7, 0xD3, 0xDF);

        private readonly QuickLine3dViewportControl _viewport =
            new QuickLine3dViewportControl();
        private readonly TextBlock _hintText;
        private readonly TextBlock _legendText;
        private readonly TextBlock _summaryText;
        private readonly Button _gridButton;
        private readonly Button _refButton;
        private readonly Button _detailButton;
        private readonly Button _resetButton;

        private QuickLineOrbitPreviewModel _model;
        private QuickLineOrbitPreviewEnvelope _envelope;
        private bool _columnGridEnabled = true;
        private bool _referenceEnabled = true;
        private bool _detailEnabled;
        private bool _hasReferenceLevels;
        private bool _hasDetailStructure;
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

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0)
            };
            _gridButton = CreateSceneButton("柱网 开", OnGridButton);
            _refButton = CreateSceneButton("参照 开", OnRefButton);
            _detailButton = CreateSceneButton("结构 开", OnDetailButton);
            _resetButton = CreateSceneButton("复位", OnResetButton);
            buttons.Children.Add(_gridButton);
            buttons.Children.Add(_refButton);
            buttons.Children.Add(_detailButton);
            buttons.Children.Add(_resetButton);
            Children.Add(buttons);

            _legendText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            };
            _summaryText = new TextBlock
            {
                Foreground = HintBrush,
                FontSize = 11,
                Margin = new Thickness(10, 0, 10, 22),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            };
            Children.Add(_legendText);
            Children.Add(_summaryText);

            ShowIdleState();
        }

        /// <summary>当前预览的场景（宿主/测试可读；为 null 时显示提示）。</summary>
        public QuickLineOrbitPreviewModel Model => _model;

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

        private int _plannedColumnGridLineCount;
        private int _plannedAxisTickCount;
        private int _plannedLetterLabelCount;

        /// <summary>切换场景（含 null=隐藏为提示文字）。同步建场景，无任何异步。</summary>
        public void SetModel(QuickLineOrbitPreviewModel model)
        {
            _model = model;
            _fatal = false;
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
            }
            catch (Exception ex)
            {
                Log.Warn("[U1U] 3D 预览场景构建失败: " + ex);
                _envelope = null;
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
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(_envelope);
            _viewport.SetScene(scene);
            _hasReferenceLevels = _envelope.ReferenceLevels.Count > 0;
            _hasDetailStructure = ContainsDetail(scene);
            _hasColumnGrid = _envelope.ColumnGridLines.Count > 0;
            _columnGridEnabled = true;
            _referenceEnabled = true;
            _detailEnabled = false;
            ApplyToggles();
            _legendText.Text = QuickLineOrbitSceneBuilder.BuildLegend(_envelope);
            _summaryText.Text = QuickLineOrbitSceneBuilder.BuildSummary(_envelope);
            _legendText.Visibility = Visibility.Visible;
            _summaryText.Visibility = Visibility.Visible;
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
            _legendText.Visibility = Visibility.Collapsed;
            _summaryText.Visibility = Visibility.Collapsed;
            _gridButton.Visibility = Visibility.Collapsed;
            _refButton.Visibility = Visibility.Collapsed;
            _detailButton.Visibility = Visibility.Collapsed;
            _resetButton.Visibility = Visibility.Collapsed;
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
            _refButton.Visibility = _hasReferenceLevels
                ? Visibility.Visible
                : Visibility.Collapsed;
            _detailButton.Visibility = _hasDetailStructure
                ? Visibility.Visible
                : Visibility.Collapsed;
            _resetButton.Visibility = _envelope != null
                ? Visibility.Visible
                : Visibility.Collapsed;

            _gridButton.Content = _columnGridEnabled ? "柱网 开" : "柱网 关";
            _refButton.Content = _referenceEnabled ? "参照 开" : "参照 关";
            _detailButton.Content = _detailEnabled ? "结构 开" : "结构 关";

            _viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Grid,
                _columnGridEnabled);
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

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}

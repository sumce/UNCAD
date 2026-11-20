using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using UNCAD.Infra;
using Wpf.Ui.Controls;

namespace UNCAD.UI
{
    /// <summary>
    /// 本地只读 U13D 场景宿主（3.0 WPF 外壳，D-038 P5）。3.4.0 起渲染内核为
    /// **WPF 原生 Viewport3D**（用户指令「算了 用 Viewport3D吧」——WebView2
    /// 用户数据目录锁在本机同步阻塞 STA 线程，「正在启动三维渲染器」白屏
    /// 反复复发，浏览器内核整条路退役；D-025 的本地只读安全口径不变：
    /// 不写 CAD、不刷新工作簿、不访问网络——原生渲染反而连本地页面加载
    /// 都没有了）。行为对齐退役页面：柱网开关、路线/端点标签开关、适配
    /// 复位；右侧信息面板为只读数据。
    /// </summary>
    public sealed partial class QuickLine3dWindow : FluentWindow
    {
        private QuickLine3dBatchScene _scene;

        public QuickLine3dWindow(IDictionary<string, object> scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            InitializeComponent();
            UiShell.ApplyFluentTheme(this);
            ToolBarStyle.ApplySceneButtons(FitButton, GridButton, LabelsButton,
                DirectionButton, WebButton);
            // 同步装载（无任何异步初始化——这正是换原生内核的意义）。
            LoadScene(scene);
        }

        private void LoadScene(IDictionary<string, object> envelope)
        {
            try
            {
                QuickLine3dBatchScene batch = QuickLine3dBatchScene.Build(envelope);
                if (batch == null)
                {
                    ShowFatalError("没有可预览的路由场景（图框内未识别到带毫米标注的 U1L 路由）。");
                    return;
                }
                _scene = batch;
                var hiddenLabelKinds = new List<string>();
                if (!batch.ShowRouteLabels) hiddenLabelKinds.Add("route");
                if (!batch.ShowDirectionLabels)
                {
                    hiddenLabelKinds.Add("directionDown");
                    hiddenLabelKinds.Add("directionUp");
                }
                Viewport.SetScene(batch.Scene, hiddenLabelKinds);
                LabelsButton.Content = batch.ShowRouteLabels
                    ? "隐藏路线标签" : "显示路线标签";
                DirectionButton.Content = batch.ShowDirectionLabels
                    ? "隐藏端点标签" : "显示端点标签";
                GridButton.Visibility = Viewport.HasGroup(
                    QuickLine3dSceneGroupKind.Grid)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                StatusText.Text = batch.StatusText;
                FillInfoPanel(batch);
            }
            catch (Exception ex)
            {
                Log.Warn("U13D 场景装载失败: " + ex);
                ShowFatalError("3D 场景装载失败：" + ex.Message);
            }
        }

        private void FillInfoPanel(QuickLine3dBatchScene batch)
        {
            MachineText.Text = Text(batch.MachineId);
            DeviceText.Text = Text(batch.DeviceName);
            FrameCountText.Text = "图框数 " + Text(batch.FrameCount);
            StatsText.Text = string.Join(
                "\n",
                new[]
                {
                    "设备楼层：" + Text(batch.DeviceFloor),
                    "盘箱楼层：" + Text(batch.PanelFloor),
                    "投影模式：" + Text(batch.ProjectionMode),
                    Text(batch.DownstreamAxis) + " → " + Text(batch.UpstreamAxis),
                    batch.Spacing,
                    "段数 " + Text(batch.SegmentCount)
                        + "　路由 " + Text(batch.RouteLength)
                        + " mm（" + Text(batch.RouteMeters) + " m）"
                });
            AxisNumericText.Text = batch.AxisNumericLine;
            AxisAlphabeticText.Text = batch.AxisAlphabeticLine;

            RouteListPanel.Children.Clear();
            BridgeListPanel.Children.Clear();
            FrameListPanel.Children.Clear();
            DiagnosticsPanel.Children.Clear();
            SetSectionVisibility(RouteTitle, RouteListPanel,
                batch.Routes.Count > 0);
            SetSectionVisibility(BridgeTitle, BridgeListPanel,
                batch.Bridges.Count > 0);
            SetSectionVisibility(FrameTitle, FrameListPanel,
                batch.Frames.Count > 0);
            SetSectionVisibility(DiagnosticsTitle, DiagnosticsPanel,
                batch.Diagnostics.Count > 0);

            foreach (QuickLine3dBatchScene.RouteRow row in batch.Routes)
                AddInfoRow(RouteListPanel, row.Title, row.Detail, row.Distance);
            foreach (QuickLine3dBatchScene.BridgeRow row in batch.Bridges)
                AddInfoRow(BridgeListPanel,
                    row.Spec + " · " + (row.Associated ? "已关联" : "未关联"),
                    "", row.Length);
            foreach (QuickLine3dBatchScene.FrameRow row in batch.Frames)
                AddInfoRow(FrameListPanel, Text(row.MachineId),
                    row.Circuit, row.Axis + "　" + row.Offset);
            foreach (string diagnostic in batch.Diagnostics)
            {
                DiagnosticsPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "• " + diagnostic,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB3)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1)
                });
            }
        }

        private static void SetSectionVisibility(
            System.Windows.Controls.TextBlock title, Panel panel, bool visible)
        {
            Visibility state = visible ? Visibility.Visible : Visibility.Collapsed;
            title.Visibility = state;
            panel.Visibility = state;
        }

        private static void AddInfoRow(StackPanel panel, string title,
            string detail, string distance)
        {
            var row = new System.Windows.Controls.TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD3, 0xDF)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(title)) parts.Add(title);
            if (!string.IsNullOrEmpty(detail)) parts.Add(detail);
            if (!string.IsNullOrEmpty(distance)) parts.Add(distance);
            row.Text = string.Join("　", parts.ToArray());
            panel.Children.Add(row);
        }

        private static string Text(string value)
        {
            return string.IsNullOrEmpty(value) || value.Trim().Length == 0
                ? "-" : value;
        }

        /// <summary>致命错误路径：整块替换为错误说明（无头测试直接驱动验证）。</summary>
        internal void ShowFatalError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    (Action<string>)ShowFatalError, DispatcherPriority.Normal,
                    message);
                return;
            }
            ErrorMessage.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
            System.Windows.Controls.Panel.SetZIndex(ErrorPanel, 1);
        }

        private void OnFit(object sender, RoutedEventArgs args)
        {
            Viewport.ResetCamera();
        }

        private void OnGridToggle(object sender, RoutedEventArgs args)
        {
            bool visible = !Viewport.IsGroupVisible(
                QuickLine3dSceneGroupKind.Grid);
            Viewport.SetGroupVisible(QuickLine3dSceneGroupKind.Grid, visible);
            GridButton.Content = visible ? "柱网 开" : "柱网 关";
        }

        private void OnRouteLabelsToggle(object sender, RoutedEventArgs args)
        {
            bool visible = LabelsButton.Content as string == "显示路线标签";
            Viewport.SetLabelKindVisible("route", visible);
            LabelsButton.Content = visible ? "隐藏路线标签" : "显示路线标签";
        }

        private void OnDirectionLabelsToggle(object sender, RoutedEventArgs args)
        {
            bool visible = DirectionButton.Content as string == "显示端点标签";
            Viewport.SetLabelKindVisible("directionDown", visible);
            Viewport.SetLabelKindVisible("directionUp", visible);
            DirectionButton.Content = visible ? "隐藏端点标签" : "显示端点标签";
        }

        private void OnClose(object sender, RoutedEventArgs args)
        {
            Close();
        }

        private async void OnWebPreview(object sender, RoutedEventArgs args)
        {
            if (_scene == null) return;
            WebButton.IsEnabled = false;
            try
            {
                var hidden = new List<string> { "Detail" };
                if (!Viewport.IsGroupVisible(QuickLine3dSceneGroupKind.Grid)) hidden.Add("Grid");
                var labels = new List<string>();
                if (LabelsButton.Content as string == "显示路线标签") labels.Add("route");
                if (DirectionButton.Content as string == "显示端点标签")
                    labels.AddRange(new[] { "directionDown", "directionUp" });
                await QuickLineWebPreview.OpenAsync(_scene.Scene,
                    "U13D · " + _scene.StatusText, hidden, labels);
            }
            catch (Exception ex)
            {
                Log.Warn("[U13D] WebV 预览启动失败: " + ex.Message);
                StatusText.Text = "WebV 未打开：" + ex.Message;
            }
            finally
            {
                WebButton.IsEnabled = true;
            }
        }

        private static class ToolBarStyle
        {
            internal static void ApplySceneButtons(
                params System.Windows.Controls.Button[] buttons)
            {
                foreach (System.Windows.Controls.Button button in buttons)
                {
                    button.FontSize = 12;
                    button.Padding = new Thickness(8, 2, 8, 2);
                    button.Margin = new Thickness(4, 0, 0, 0);
                    button.Focusable = false;
                }
            }
        }
    }
}

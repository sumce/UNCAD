using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UNCAD.Core.Fill;
using UNCAD.UI;
using Wpf.Ui.Controls;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.6.0（D-057）U1SET「图框与排版」页：制图信息表六列模板 + 占位符插入条 +
    /// 实时预览 + 恢复默认；清单标题模板；XLAYOUT 排版参数。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class SettingsFramePageTests
    {
        [Fact]
        public void FramePage_EditsTemplatesWithPlaceholderInsertAndLivePreview()
        {
            RunSta(() =>
            {
                var window = new SettingsWindow(10);
                window.Show();
                Pump(3);
                SettingsFramePage page = window.FramePage;

                // 侧边栏第 11 项（下标 10）= 图框与排版，构造参数能直接落到这页。
                Assert.Equal(10, window.SelectedPageIndex);
                Assert.Equal("图框与排版", SettingsWindow.PageTitles[10]);

                // 默认模板 = 3.5.0 的行为：楼层 (楼层)、日期 (日期)，其余留空。
                Assert.Equal("", page.InfoProfession.Text);
                Assert.Equal("(楼层)", page.InfoFloor.Text);
                Assert.Equal("", page.InfoDrafter.Text);
                Assert.Equal("", page.InfoReviewer.Text);
                Assert.Equal("(日期)", page.InfoDate.Text);
                Assert.Equal("", page.InfoVersion.Text);

                // 占位符条：每个可用占位符一个按钮。
                Assert.Equal(DrawingInfoTemplateRenderer.Placeholders.Count,
                    page.PlaceholderBar.Children.Count);

                // 还没点过任何格：占位符进第一格（专业）。
                window.FrameInteraction.InsertPlaceholder("机台ID");
                Assert.Equal("(机台ID)", page.InfoProfession.Text);

                // 预览随输入刷新（样例机台 MEDPC01）；留空的列标“不改”。
                Assert.Contains("专业=MEDPC01", page.TemplatePreview.Text, StringComparison.Ordinal);
                Assert.Contains("楼层=3F/2F", page.TemplatePreview.Text, StringComparison.Ordinal);
                Assert.Contains("制图=（不改）", page.TemplatePreview.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("未识别", page.TemplatePreview.Text, StringComparison.Ordinal);

                // 拼错的占位符点名提醒（普通括号文字不受影响）。
                page.InfoVersion.Text = "V(版本号)";
                Assert.Contains("未识别的占位符", page.TemplatePreview.Text, StringComparison.Ordinal);
                Assert.Contains("(版本号)", page.TemplatePreview.Text, StringComparison.Ordinal);

                // 点过哪一格，占位符就插到那一格的光标处。
                page.InfoDate.Focus();
                Pump(1);
                Assert.Same(page.InfoDate, window.FrameInteraction.TargetBox);
                window.FrameInteraction.InsertPlaceholder("用户");
                Assert.Equal("(日期)(用户)", page.InfoDate.Text);

                // 恢复默认。
                Click(page.ResetTemplatesButton);
                Assert.Equal("", page.InfoProfession.Text);
                Assert.Equal("(日期)", page.InfoDate.Text);
                Assert.Equal("", page.InfoVersion.Text);
                Assert.True(window.FrameInteraction.CurrentTemplates().IsDefault);

                // 清单标题留空 = 用清单模板里的文字；XLAYOUT 三个数值框 + 黄框开关。
                Assert.Contains("用清单模板里的文字", page.BoqTitlePreview.Text,
                    StringComparison.Ordinal);
                Assert.Equal(4, FindAll<NumberBox>(page).Count);
                Assert.Single(FindAll<ToggleSwitch>(page));
                // 四张卡片：制图信息表 / 清单标题 / 图框排版 / 线路识别。
                Assert.Equal(4, FindAll<Card>(page).Count);
                // D-058：断口容差框读的是 U1SET 的当前值，范围 0–500（0 = 只用严格容差）。
                Assert.Equal(0, page.RouteGapTolerance.Minimum);
                Assert.Equal(500, page.RouteGapTolerance.Maximum);
                Assert.Equal(UNCAD.Infra.QuickLineRouteSettings.GapTolerance(),
                    page.RouteGapTolerance.Value);
                window.Close();
            });
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
            => button.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            void Visit(DependencyObject node)
            {
                if (node is T match) found.Add(match);
                for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                    Visit(VisualTreeHelper.GetChild(node, index));
            }
            Visit(root);
            return found;
        }
    }
}

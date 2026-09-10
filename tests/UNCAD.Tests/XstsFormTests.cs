using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Core.Stat;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// XSTS 报表窗口的布局契约。该窗体此前没有任何测试，而它的
    /// 填充区（TabControl）与页脚标签同时使用 Dock=Fill，按 WinForms 的
    /// Dock 生效顺序，页脚会吃掉全部剩余空间、把报表压成零高度。
    /// </summary>
    public class XstsFormTests
    {
        [Fact]
        public void ReportTabs_ReserveSpace_AndAreNotCollapsedByFooter()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new XstsForm(BuildReport(), @"C:\out\UNCAD_XSTS.xlsx"))
                    {
                        form.Show();
                        form.Size = form.MinimumSize;
                        Application.DoEvents();

                        TabControl tabs = FindAll<TabControl>(form).Single();
                        Label footer = FindAll<Label>(form)
                            .Single(label => label.Text.StartsWith("Excel:", StringComparison.Ordinal));

                        Assert.True(tabs.Height > 0 && tabs.Width > 0,
                            "报表区域被压成零尺寸: " + tabs.Size);
                        Assert.True(footer.Height > 0 && footer.Width > 0,
                            "页脚被压成零尺寸: " + footer.Size);

                        // 两个区域不得重叠：报表底部应在页脚顶部之上。
                        Rectangle tabsScreen = form.RectangleToScreen(tabs.Bounds);
                        Rectangle footerScreen = form.RectangleToScreen(footer.Bounds);
                        Assert.True(tabsScreen.Bottom <= footerScreen.Top,
                            "报表与页脚重叠: tabs=" + tabsScreen + " footer=" + footerScreen);

                        // 报表应占据窗口可用高度的主要部分，而不是被页脚挤扁。
                        Assert.True(tabsScreen.Height > footerScreen.Height,
                            "报表高度小于页脚高度: tabs=" + tabsScreen.Height
                                + " footer=" + footerScreen.Height);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static XstsReport BuildReport()
        {
            var report = new XstsReport { FrameCount = 3 };
            var machine = new XstsMachineSummary("M1")
            {
                SelectedCircuitCount = 2,
                ExpectedCircuitCount = 3,
                ExpectedDataAvailable = true
            };
            machine.SelectedCircuits.AddRange(new[] { "C1", "C2" });
            machine.MissingCircuits.Add("C3");
            report.Machines.Add(machine);
            report.Issues.Add(new XstsFrameIssue
            {
                FrameNumber = 1,
                MachineId = "M1",
                CircuitName = "C3",
                Description = "未在图纸中找到对应回路"
            });
            return report;
        }

        private static IEnumerable<T> FindAll<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match) yield return match;
                foreach (T nested in FindAll<T>(child)) yield return nested;
            }
        }
    }
}

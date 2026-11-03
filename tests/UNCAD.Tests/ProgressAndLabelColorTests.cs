using System;
using System.Threading;
using System.Windows.Threading;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 大批量响应性（2026-11-02）：三色标签规则（纯逻辑）与进度窗结构。
    /// 进度窗在同步命令循环里的“即时重绘”能力只能在真机 CAD 验证，
    /// 这里锁定结构契约（存在、可更新、跨线程调度、关闭不抛）。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class ProgressAndLabelColorTests
    {
        [Theory]
        // 已知 + 无缺/多/重复 → 绿
        [InlineData(true, 0, 0, 0, XLayoutLabelStatus.Complete)]
        // 已知 + 有缺少 → 红（最高优先，即使同时有重复/多出）
        [InlineData(true, 1, 0, 0, XLayoutLabelStatus.Missing)]
        [InlineData(true, 2, 1, 0, XLayoutLabelStatus.Missing)]
        [InlineData(true, 1, 0, 3, XLayoutLabelStatus.Missing)]
        // 已知 + 只多出（图纸有、Excel 没有）→ 黄：不算缺，与 XSTS“多出”口径一致
        [InlineData(true, 0, 2, 0, XLayoutLabelStatus.Warning)]
        // 已知 + 只有重复（回路齐全但同回路多框）→ 黄。旧实现 IsComplete 已含
        // “无重复”条件，重复机台掉进红色“缺回路”，与 XSTS 结论相反（2026-11-03 修复）
        [InlineData(true, 0, 0, 2, XLayoutLabelStatus.Warning)]
        // 未知（无法对照台账）→ 黄
        [InlineData(false, 0, 0, 0, XLayoutLabelStatus.Warning)]
        [InlineData(false, 3, 0, 0, XLayoutLabelStatus.Warning)]
        public void LabelStatus_FollowsFieldColors(
            bool known, int missing, int extra, int duplicates,
            XLayoutLabelStatus expected)
        {
            var summary = new XLayoutMachineSummary("M-1")
            {
                StatusKnown = known,
                MissingCircuitCount = missing,
                ExtraCircuitCount = extra,
                DuplicateCircuitCount = duplicates,
                IsComplete = known && missing == 0 && extra == 0 && duplicates == 0
            };
            Assert.Equal(expected, summary.LabelStatus);
        }

        [Fact]
        public void LabelStatus_LegacyIncompleteWithoutCounts_StaysMissing()
        {
            // 兼容旧调用（只置 IsComplete=false、未填计数）：保持红色而不是绿。
            var summary = new XLayoutMachineSummary("M-1")
            {
                StatusKnown = true,
                IsComplete = false
            };
            Assert.Equal(XLayoutLabelStatus.Missing, summary.LabelStatus);
            Assert.Equal("缺少回路", summary.StatusText);
        }

        [Fact]
        public void StatusText_And_DetailText_NameTheCause()
        {
            var duplicateOnly = new XLayoutMachineSummary("M-1")
            {
                StatusKnown = true,
                DuplicateCircuitCount = 2,
                MissingCircuitCount = 0,
                ExtraCircuitCount = 0
            };
            Assert.Equal("重复 2", duplicateOnly.StatusText);
            Assert.Equal("重复 2 处", duplicateOnly.DetailText);

            var missing = new XLayoutMachineSummary("M-2") { StatusKnown = true };
            missing.MissingCircuitNames.AddRange(new[]
                { "AP1", "AP2", "AP3", "AP4", "AP5", "AP6", "AP7" });
            missing.MissingCircuitCount = 7;
            Assert.Equal("缺少回路 7", missing.StatusText);
            // 最多列 5 个，超出折叠为“等 N 个”。
            Assert.Equal("缺 AP1、AP2、AP3、AP4、AP5 等 7 个", missing.DetailText);

            var extra = new XLayoutMachineSummary("M-3") { StatusKnown = true };
            extra.ExtraCircuitNames.Add("备用回路");
            extra.ExtraCircuitCount = 1;
            Assert.Equal("多出回路 1", extra.StatusText);
            Assert.Equal("多 备用回路", extra.DetailText);
        }

        [Fact]
        public void ProgressWindow_UpdatesValuesAndSurvivesCrossThread()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new ProgressWindow("测试进度");
                    window.Show();
                    Pump(2);

                    window.Update("正在处理", 30, 100);
                    Pump(2);
                    Assert.Equal("正在处理", window.StatusText.Text);
                    Assert.Equal(30d, window.Bar.Value);
                    Assert.Equal(100d, window.Bar.Maximum);
                    Assert.Equal("30 / 100", window.CountText.Text);

                    // 超界值必须被钳制，不得抛异常。
                    window.Update("溢出", 150, 100);
                    Assert.Equal(100d, window.Bar.Value);

                    // 跨线程调用自动调度到 UI 线程，不死锁不抛。
                    var other = new Thread(() => window.Update("跨线程", 5, 10));
                    other.Start();
                    other.Join(2000);
                    Pump(2);

                    window.Close();
                }
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
                Thread.Sleep(20);
            }
        }
    }
}

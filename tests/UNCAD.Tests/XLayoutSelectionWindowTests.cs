using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0 WPF XLAYOUT 选择窗（D-038，P2 模式）：两级树、复选框联动、
    /// 筛选只作用于可见项、空选择不得确认。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class XLayoutSelectionWindowTests
    {
        [Fact]
        public void Window_ShowsMachinesWithCircuitsAndChecksEverything()
        {
            RunSta(() =>
            {
                var window = new XLayoutSelectionWindow(Items());
                window.Show();
                Pump(3);

                Assert.Equal(5, window.ComputeSelectedItems().Count);
                Assert.Equal(3, window.Roots.Count);
                Assert.Equal(2, window.Roots[0].Children.Count);
                Assert.All(window.Roots, node => Assert.True(node.IsChecked));
                Assert.Contains("MQBAN03", window.Roots[1].Header, StringComparison.Ordinal);
                Assert.Contains("2 个回路", window.Roots[1].Header, StringComparison.Ordinal);
                Assert.True(window.Resources.MergedDictionaries.Count >= 2,
                    "Fluent 主题字典必须按窗口合并");
                Assert.True(window.ConfirmButton.IsDefault);
                Assert.True(window.CancelButton.IsCancel);
                Assert.All(window.Roots.SelectMany(MachineAndCircuits),
                    node => Assert.True(node.IsChecked));
                window.Close();
            });
        }

        [Fact]
        public void Filter_NarrowsVisibleCircuitsAndSelection()
        {
            RunSta(() =>
            {
                var window = new XLayoutSelectionWindow(Items());
                window.Show();
                Pump(2);

                window.ApplyFilter("MQBAN03");
                Assert.Equal(2, window.VisibleCircuitCount);
                Assert.Equal(2, window.ComputeSelectedItems().Count);
                Assert.Single(window.Roots);

                window.ApplyFilter("mqban03 电脑");
                // 两个电脑插座都命中（机台ID 与回路名都参与匹配）。
                Assert.Equal(2, window.VisibleCircuitCount);
                Assert.Equal(2, window.ComputeSelectedItems().Count);

                window.ApplyFilter("电脑插座1");
                Assert.Single(window.ComputeSelectedItems());

                window.ApplyFilter("不存在");
                Assert.Equal(0, window.VisibleCircuitCount);
                Assert.Empty(window.ComputeSelectedItems());
                // 空结果给一个提示节点（不可勾选），避免用户以为窗口坏了。
                XLayoutSelectionNode hint = Assert.Single(window.Roots);
                Assert.True(hint.IsPlaceholder);
                Assert.False(hint.ShowCheckBox);
                Assert.Contains("没有匹配", hint.Header, StringComparison.Ordinal);
                window.Close();
            });
        }

        [Fact]
        public void Checkboxes_OnlyAffectVisibleCircuitsAndSurviveFilterChanges()
        {
            RunSta(() =>
            {
                var window = new XLayoutSelectionWindow(Items());
                window.Show();
                Pump(2);

                // 先全部取消，再只在"电脑"筛选下勾选：清空筛选后其它回路不应回来。
                window.SetVisibleChecked(false);
                Assert.Empty(window.ComputeSelectedItems());

                window.ApplyFilter("电脑");
                Assert.Equal(2, window.VisibleCircuitCount);
                window.SetVisibleChecked(true);
                Assert.Equal(2, window.ComputeSelectedItems().Count);

                window.ApplyFilter("");
                Assert.Equal(2, window.ComputeSelectedItems().Count);
                window.Close();
            });
        }

        [Fact]
        public void MachineNodeCheckStateFollowsItsCircuits()
        {
            RunSta(() =>
            {
                var window = new XLayoutSelectionWindow(Items());
                window.Show();
                Pump(2);

                XLayoutSelectionNode machine = window.Roots[0];
                machine.Children[0].IsChecked = false;
                Assert.False(machine.IsChecked);

                machine.IsChecked = true;
                Assert.All(machine.Children, node => Assert.True(node.IsChecked));
                Assert.Equal(5, window.ComputeSelectedItems().Count);
                window.Close();
            });
        }

        [Fact]
        public void Confirm_BlocksEmptySelectionAndPublishesSelectedItems()
        {
            RunSta(() =>
            {
                var window = new XLayoutSelectionWindow(Items());
                window.Show();
                Pump(2);

                window.SetVisibleChecked(false);
                Click(window.ConfirmButton);
                Pump(2);
                Assert.False(window.Confirmed, "空选择不得确认");
                Assert.True(window.WarningBar.IsOpen);
                Assert.Contains("至少选择一个回路", window.WarningBar.Message,
                    StringComparison.Ordinal);

                window.ApplyFilter("电脑插座1");
                window.SetVisibleChecked(true);
                Click(window.ConfirmButton);
                Pump(2);
                Assert.True(window.Confirmed);
                Assert.Single(window.SelectedItems);
                Assert.Equal("电脑插座1", window.SelectedItems[0].DeviceName);
                window.Close();
            });
        }

        [Fact]
        public void DuplicateFrames_UncheckOneLeaf_LeavesTheOtherSelected()
        {
            RunSta(() =>
            {
                // 同机台同回路的重复图框（XLAYOUT 明确支持）必须各自映射各自的
                // 图框：旧实现按 MachineId+DeviceName 反查，两个叶子共享第一个
                // item，取消勾选永远清不掉第二个（2026-11-04 回归）。
                var window = new XLayoutSelectionWindow(new[]
                {
                    Item("MQBAN01", "电脑插座1", 0),
                    Item("MQBAN01", "电脑插座1", 1)
                });
                window.Show();
                Pump(2);

                Assert.Equal(2, window.ComputeSelectedItems().Count);
                var circuits = window.Roots[0].Children;
                Assert.Equal(2, circuits.Count);

                circuits[0].IsChecked = false;
                var remaining = window.ComputeSelectedItems();
                Assert.Single(remaining);
                Assert.Equal("h1", remaining[0].Handle);

                circuits[1].IsChecked = false;
                Assert.Empty(window.ComputeSelectedItems());

                // “全不选/全选”按钮走可见项集合路径，重复图框同样必须清干净。
                window.SetVisibleChecked(true);
                Assert.Equal(2, window.ComputeSelectedItems().Count);
                window.SetVisibleChecked(false);
                Assert.Empty(window.ComputeSelectedItems());

                Click(window.ConfirmButton);
                Pump(2);
                Assert.False(window.Confirmed, "空选择不得确认");
                window.Close();
            });
        }

        [Fact]
        public void UncheckingOneCircuit_KeepsItsSiblingsSelected()
        {
            RunSta(() =>
            {
                // 取消一个回路不许机台级联连带取消兄弟回路（机台分支只应响应
                // 用户直接点机台复选框；自动回同步必须抑制级联，2026-11-04）。
                var window = new XLayoutSelectionWindow(Items());
                window.Show();
                Pump(2);

                window.Roots[0].Children[0].IsChecked = false;
                var remaining = window.ComputeSelectedItems();
                Assert.Equal(4, remaining.Count);
                Assert.Contains(remaining, item => item.DeviceName == "回路B");
                Assert.False(window.Roots[0].IsChecked,
                    "机台节点应如实反映部分选择");
                window.Close();
            });
        }

        [Fact]
        public void Region_NarrowsTreeSelection_AndCombinesWithKeywordFilter()
        {
            RunSta(() =>
            {
                var region = new Dictionary<string, string>(
                    UNCAD.Core.Text.IdentityTextNormalizer.Comparer)
                {
                    ["M651"] = "AMHS",
                    ["PSPM651"] = "Litho",
                    ["OHVC-M651"] = "Metal"
                };
                var window = new XLayoutSelectionWindow(RegionItems(),
                    Core.Dwg.XLayoutArrangeMode.MachineReadiness, region);
                window.Show();
                Pump(2);

                // 全部区域：三台都在，节点标题带区域前缀。
                Assert.Equal(3, window.Roots.Count);
                Assert.Contains("AMHS · M651", window.Roots[0].Header,
                    StringComparison.Ordinal);

                // 选 AMHS：只剩 M651，其余（含同号双胞胎）整组隐藏。
                window.ApplyRegion("AMHS");
                Assert.Single(window.Roots);
                Assert.Equal("M651", window.Roots[0].MachineId);
                Assert.Equal(2, window.ComputeSelectedItems().Count);

                // 区域 + 关键词 AND：AMHS 里再按回路名筛。
                window.ApplyFilter("RCM");
                Assert.Single(window.ComputeSelectedItems());

                // 换 Litho：PSPM651 出现，M651 隐藏。
                window.ApplyFilter("");
                window.ApplyRegion("Litho");
                Assert.Single(window.Roots);
                Assert.Equal("PSPM651", window.Roots[0].MachineId);
                window.Close();
            });
        }

        [Fact]
        public void Region_WithoutRegionData_HidesComboBoxAndKeepsLegacyBehaviour()
        {
            RunSta(() =>
            {
                var window = new XLayoutSelectionWindow(RegionItems());
                window.Show();
                Pump(2);

                // 没传区域映射：下拉隐藏（不可用），全部机台可见。
                Assert.Equal(System.Windows.Visibility.Collapsed,
                    window.RegionBox.Visibility);
                Assert.Equal(0, window.RegionBox.Items.Count);
                Assert.Equal(3, window.Roots.Count);
                Assert.DoesNotContain("·", window.Roots[0].Header, StringComparison.Ordinal);
                window.Close();
            });
        }

        private static IReadOnlyList<XLayoutFrameItem> RegionItems() => new[]
        {
            Item("M651", "RCM", 0),
            Item("M651", "PUMP", 1),
            Item("PSPM651", "RCM", 2),
            Item("OHVC-M651", "RCM", 3)
        };

        private static IEnumerable<XLayoutSelectionNode> MachineAndCircuits(
            XLayoutSelectionNode machine)
        {
            yield return machine;
            foreach (XLayoutSelectionNode child in machine.Children) yield return child;
        }

        private static IReadOnlyList<XLayoutFrameItem> Items() => new[]
        {
            Item("MQBAN01", "回路A", 0),
            Item("MQBAN01", "回路B", 1),
            Item("MQBAN03", "电脑插座1", 2),
            Item("MQBAN03", "电脑插座2", 3),
            Item("MQIMA02", "主机", 4)
        };

        private static XLayoutFrameItem Item(string machineId, string circuit, int index)
            => new XLayoutFrameItem(machineId, circuit,
                new FrameRectangle("h" + index, index * 1000, 0, index * 1000 + 800, 400),
                "h" + index);

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
            thread.Join(60000);
            if (failure != null) throw failure;
        }

        private static void Click(System.Windows.Controls.Primitives.ButtonBase button)
        {
            // Wpf.Ui 的 Button 不是原生 Button，AutomationPeer 不认；
            // 直接抬 ClickEvent 路由事件，与用户点击走同一条处理链。
            Assert.NotNull(button);
            button.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Thread.Sleep(30);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>XLAYOUT 机台/回路选择窗：两级树、复选框联动、筛选只作用于可见项。</summary>
    public sealed class XLayoutSelectionFormTests
    {
        [Fact]
        public void Form_ShowsMachinesWithCircuitsAndChecksEverything()
        {
            RunSta(() =>
            {
                using (var form = new XLayoutSelectionForm(Items()))
                {
                    Assert.Equal(5, form.ComputeSelectedItems().Count);
                    Assert.Equal(3, form.Tree.Nodes.Count);
                    Assert.Equal(2, form.Tree.Nodes[0].Nodes.Count);
                    Assert.All(form.Tree.Nodes.Cast<TreeNode>(),
                        node => Assert.True(node.Checked));
                    Assert.Contains("MQBAN03", form.Tree.Nodes[1].Text);
                    Assert.Contains("2 个回路", form.Tree.Nodes[1].Text);
                }
            });
        }

        [Fact]
        public void Filter_NarrowsVisibleCircuitsAndSelection()
        {
            RunSta(() =>
            {
                using (var form = new XLayoutSelectionForm(Items()))
                {
                    form.ApplyFilter("MQBAN03");
                    Assert.Equal(2, form.VisibleCircuitCount);
                    Assert.Equal(2, form.ComputeSelectedItems().Count);
                    Assert.Single(form.Tree.Nodes.Cast<TreeNode>());

                    form.ApplyFilter("mqban03 电脑");
                    // 两个电脑插座都命中（机台ID 与回路名都参与匹配）。
                    Assert.Equal(2, form.VisibleCircuitCount);
                    Assert.Equal(2, form.ComputeSelectedItems().Count);

                    form.ApplyFilter("电脑插座1");
                    Assert.Single(form.ComputeSelectedItems());

                    form.ApplyFilter("不存在");
                    Assert.Equal(0, form.VisibleCircuitCount);
                    Assert.Empty(form.ComputeSelectedItems());
                    Assert.Empty(form.Tree.Nodes.Cast<TreeNode>());
                }
            });
        }

        [Fact]
        public void Checkboxes_OnlyAffectVisibleCircuitsAndSurviveFilterChanges()
        {
            RunSta(() =>
            {
                using (var form = new XLayoutSelectionForm(Items()))
                {
                    // 先全部取消，再只在"电脑"筛选下勾选：清空筛选后其它回路不应回来。
                    form.SetVisibleChecked(false);
                    Assert.Empty(form.ComputeSelectedItems());

                    form.ApplyFilter("电脑");
                    Assert.Equal(2, form.VisibleCircuitCount);
                    form.SetVisibleChecked(true);
                    Assert.Equal(2, form.ComputeSelectedItems().Count);

                    form.ApplyFilter("");
                    Assert.Equal(2, form.ComputeSelectedItems().Count);
                }
            });
        }

        [Fact]
        public void MachineNodeCheckStateFollowsItsCircuits()
        {
            RunSta(() =>
            {
                using (var form = new XLayoutSelectionForm(Items()))
                {
                    TreeNode machine = form.Tree.Nodes[0];
                    machine.Nodes[0].Checked = false;
                    Assert.False(machine.Checked);

                    machine.Checked = true;
                    Assert.All(machine.Nodes.Cast<TreeNode>(),
                        node => Assert.True(node.Checked));
                    Assert.Equal(5, form.ComputeSelectedItems().Count);
                }
            });
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
            var thread = new System.Threading.Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join(30000);
            if (failure != null) throw new AggregateException(failure);
        }
    }
}

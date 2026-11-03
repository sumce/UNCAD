using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// XLAYOUT 排列方案（D-045，2026-11-03）：按机台完整性 / 按机台 ID 序 /
    /// 按上游编号同行；行内回路按 Excel 行序（缺失映射退回图上位置）；
    /// 行首标签 RowLabel：机台模式=机台 ID，上游模式=上游编号。
    /// </summary>
    public sealed class XLayoutLayoutArrangeTests
    {
        private static XLayoutFrameItem Item(string machine, string circuit,
            int x, string handle)
            => new XLayoutFrameItem(machine, circuit,
                new FrameRectangle(handle, x, 0, x + 800, 400), handle);

        private static XLayoutArrangeContext Context()
        {
            var context = new XLayoutArrangeContext();
            // 机台就绪等级：MA 完整 / MB 重复 / MC 缺回路 / MD 无基准。
            context.ReadinessRank["MA"] = 0;
            context.ReadinessRank["MB"] = 1;
            context.ReadinessRank["MC"] = 2;
            context.ReadinessRank["MD"] = 3;
            return context;
        }

        [Fact]
        public void MachineReadiness_RanksCompleteBeforeDuplicatedBeforeMissing()
        {
            var context = Context();
            var items = new[]
            {
                Item("MC", "C1", 0, "h1"),   // 缺回路
                Item("MA", "A1", 0, "h2"),   // 完整
                Item("MB", "B1", 0, "h3"),   // 重复
                Item("MD", "D1", 0, "h4")    // 无基准
            };

            var placements = XLayoutLayout.Arrange(items, context,
                XLayoutArrangeMode.MachineReadiness);

            string[] rowOrder = placements
                .GroupBy(placement => placement.RowIndex)
                .OrderBy(group => group.Key)
                .Select(group => group.First().RowLabel)
                .ToArray();
            // 完整 > 重复 > 缺回路 > 无基准。
            Assert.Equal(new[] { "MA", "MB", "MC", "MD" }, rowOrder);
            // 机台模式的行首标签 = 机台 ID。
            Assert.All(placements, placement =>
                Assert.Equal(placement.Item.MachineId, placement.RowLabel));
        }

        [Fact]
        public void MachineIdOrder_IgnoresReadinessAndSortsByName()
        {
            var context = Context();
            var items = new[]
            {
                Item("MC", "C1", 0, "h1"),
                Item("MA", "A1", 0, "h2"),
                Item("MB", "B1", 0, "h3")
            };

            var placements = XLayoutLayout.Arrange(items, context,
                XLayoutArrangeMode.MachineIdOrder);

            string[] rowOrder = placements
                .GroupBy(placement => placement.RowIndex)
                .OrderBy(group => group.Key)
                .Select(group => group.First().RowLabel)
                .ToArray();
            Assert.Equal(new[] { "MA", "MB", "MC" }, rowOrder);
        }

        [Fact]
        public void UpstreamMode_GroupsSameUpstreamIntoOneRow()
        {
            var context = Context();
            context.UpstreamOf[XLayoutArrangeContext.MakeKey("MA", "A1")] = "P-01";
            context.UpstreamOf[XLayoutArrangeContext.MakeKey("MB", "B1")] = "P-01";
            context.UpstreamOf[XLayoutArrangeContext.MakeKey("MC", "C1")] = "P-02";
            // MD 无上游编号 → 归"未配上游"行，排最后。
            var items = new[]
            {
                Item("MC", "C1", 0, "h1"),
                Item("MD", "D1", 0, "h2"),
                Item("MB", "B1", 0, "h3"),
                Item("MA", "A1", 0, "h4")
            };

            var placements = XLayoutLayout.Arrange(items, context,
                XLayoutArrangeMode.Upstream);

            var rows = placements
                .GroupBy(placement => placement.RowIndex)
                .OrderBy(group => group.Key)
                .ToList();
            Assert.Equal(3, rows.Count);
            // P-01 行跨两个机台（同上游一行）；未配上游排最后且用兜底标签。
            Assert.Equal(new[] { "P-01", "P-02", "未配上游" },
                rows.Select(row => row.First().RowLabel).ToArray());
            Assert.Equal(2, rows[0].Count());
            Assert.Contains(rows[0], placement => placement.Item.MachineId == "MA");
            Assert.Contains(rows[0], placement => placement.Item.MachineId == "MB");
        }

        [Fact]
        public void RowMembers_FollowExcelRowOrderBeforeDrawingPosition()
        {
            var context = Context();
            // Excel 行序故意与图上 X 位置相反：行序应优先于位置。
            context.RowOrder[XLayoutArrangeContext.MakeKey("MA", "A2")] = 0;
            context.RowOrder[XLayoutArrangeContext.MakeKey("MA", "A1")] = 1;
            var items = new[]
            {
                Item("MA", "A1", 0, "h1"),    // Excel 第二行，图上最左
                Item("MA", "A2", 2000, "h2")  // Excel 第一行，图上在右
            };

            var placements = XLayoutLayout.Arrange(items, context,
                XLayoutArrangeMode.MachineReadiness);

            Assert.Equal("A2", placements[0].Item.DeviceName);
            Assert.Equal("A1", placements[1].Item.DeviceName);
        }

        [Fact]
        public void RowMembers_WithoutSnapshotFallbackToDrawingPosition()
        {
            var items = new[]
            {
                Item("MA", "A1", 0, "h1"),
                Item("MA", "A2", 2000, "h2")
            };

            // 空 context（快照不可用）：退回按图上位置排。
            var placements = XLayoutLayout.Arrange(items, XLayoutArrangeContext.Empty,
                XLayoutArrangeMode.MachineReadiness);

            Assert.Equal("A1", placements[0].Item.DeviceName);
            Assert.Equal("A2", placements[1].Item.DeviceName);
        }

        [Fact]
        public void ModeValues_RoundTripThroughSettingStrings()
        {
            Assert.True(XLayoutArrangeModes.TryParseValue("machine-id",
                out XLayoutArrangeMode byId));
            Assert.Equal(XLayoutArrangeMode.MachineIdOrder, byId);
            Assert.True(XLayoutArrangeModes.TryParseValue("upstream",
                out XLayoutArrangeMode upstream));
            Assert.Equal(XLayoutArrangeMode.Upstream, upstream);
            Assert.True(XLayoutArrangeModes.TryParseValue("", out _));
            Assert.Equal(XLayoutArrangeMode.MachineReadiness,
                XLayoutArrangeModes.TryParseValue("garbage", out XLayoutArrangeMode fallback)
                    ? fallback : fallback);
            Assert.Equal("readiness", XLayoutArrangeModes.Value(default));
            Assert.False(XLayoutArrangeModes.TryParseValue("garbage", out _));
        }
    }
}

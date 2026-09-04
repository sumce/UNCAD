using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class FrameInfoUpdateDiffTests
    {
        private static MachineRow Machine() => new MachineRow
        {
            MachineId = "M01",
            CircuitName = "回路1",
            Region = "ETCH",
            Cable = "3*(2*70)+1*70",
            Fr = "2F 54W",
            Detail = "N208V 3P20A",
            Dia = "32",
            Next = "I-Line盘"
        };

        [Fact]
        public void Build_FirstUpdateReportsEmptyOldValues()
        {
            // 首次更新:上次记录为空,所有有值的字段都视为"新增"变化。
            var diffs = FrameInfoUpdateDiff.Build(null, Machine(), null);
            var machineId = diffs.Single(d => d.Field == "MachineId");
            Assert.True(machineId.Changed);
            Assert.Equal("", machineId.Old);
            Assert.Equal("M01", machineId.New);
            Assert.True(diffs.Where(d => d.Changed).All(d => d.Old.Length == 0));
        }

        [Fact]
        public void Build_ChangedFieldsSortFirstAndAreFlagged()
        {
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "M01",
                DeviceName = "回路1",
                BoqCableModel = "旧电缆",
                Detail = "旧详情"
            };
            var diffs = FrameInfoUpdateDiff.Build(previous, Machine(), null);

            Assert.True(diffs[0].Changed, "变化项应排在最前");
            var detail = diffs.Single(d => d.Field == "Detail");
            Assert.True(detail.Changed);
            Assert.Equal("旧详情", detail.Old);
            Assert.Equal("N208V 3P20A", detail.New);
            // review 未给出 BOQ 替代型号时沿用上次记录的替代值,不算变化。
            var cable = diffs.Single(d => d.Field == "BoqCableModel");
            Assert.False(cable.Changed);
            Assert.Equal("旧电缆", cable.New);
            var unchanged = diffs.Single(d => d.Field == "Fr");
            Assert.True(unchanged.Changed);
        }

        [Fact]
        public void Update_RecordsLastModifiedUser()
        {
            FrameInfoJsonRecord updated = FrameInfoJsonRecordUpdater.Update(
                null, Machine(), null, "U1U",
                new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc),
                "zhangsan");
            Assert.Equal("zhangsan", updated.LastModifiedUser);
        }
    }
}

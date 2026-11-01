using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Features.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1F（新建填充）与 U1U 共用电缆预留：2.9.1 只在 U1U 路径接入了预留，
    /// U1F 既不按上游类型加盘内预留、也不复用/填写设备内预留。
    /// </summary>
    public sealed class CableReserveNewFillTests
    {
        private static CableStatResult Stats(double meters)
        {
            var statistics = new CableStatResult { CableSum = meters };
            statistics.CableFormatted.Add(TextFormatter.FormatNum(meters));
            return statistics;
        }

        [Fact]
        public void ResolveCableReserve_AppliesForNewFill_WithSavedDeviceReserve()
        {
            CableStatResult statistics = Stats(10d);
            var machine = new MachineRow
            {
                MachineId = "M1", CircuitName = "C1", Next = "I-Line盘"
            };
            var previous = new FrameInfoJsonRecord { DeviceReserveMm = "500" };

            CableReserveInfo reserve = FillFeature.ResolveCableReserve(previous, machine,
                statistics);

            Assert.NotNull(reserve);
            Assert.Equal(3400d, reserve.PanelReserveMillimeters);
            Assert.Equal(500d, reserve.DeviceReserveMillimeters);
            Assert.Equal(3000d, reserve.OwnerReserveMillimeters);
            Assert.Equal(16.9d, reserve.TotalMeters, 3);
            Assert.True(statistics.CableReserveApplied);
        }

        [Fact]
        public void ResolveCableReserve_NewFillWithoutHistory_PrefillsZeroDeviceReserve()
        {
            CableReserveInfo reserve = FillFeature.ResolveCableReserve(null,
                new MachineRow { MachineId = "M1", CircuitName = "C1", Next = "插座盘" },
                Stats(2d));

            Assert.NotNull(reserve);
            Assert.Equal(1000d, reserve.PanelReserveMillimeters);
            Assert.Equal(0d, reserve.DeviceReserveMillimeters);
            Assert.Equal(6d, reserve.TotalMeters, 3);
        }

        [Fact]
        public void ResolveCableReserve_NoMeasuredCable_ReturnsNull()
        {
            Assert.Null(FillFeature.ResolveCableReserve(null,
                new MachineRow { Next = "I-Line盘" }, new CableStatResult()));
        }

        [Fact]
        public void NewFillReview_MarksReserveEditableAndSyncsTotal()
        {
            var review = new FillReviewData();
            var reserve = new CableReserveInfo
            {
                MeasuredMeters = 10d,
                PanelReserveMillimeters = 3400d,
                PanelReserveLabel = "盘内",
                DeviceReserveMillimeters = 500d,
                OwnerReserveMillimeters = 3000d,
                TotalMeters = 16.9d
            };

            FillFeature.InitializeReserveReview(review, reserve);

            Assert.True(review.CableReserveEditable);
            Assert.Equal("16.9", review.CableMeters);
            Assert.Equal("3400", review.PanelReserveMm);
            Assert.Equal("500", review.DeviceReserveMm);
            Assert.Equal("3000", review.OwnerReserveMm);
        }

        [Fact]
        public void NewFillCableInfo_ShowsReserveBreakdownInMillimetres()
        {
            CableStatResult statistics = Stats(10d);
            CableReservePolicy.Apply(statistics, "插座盘", 500d);
            var machine = new MachineRow
            {
                MachineId = "M1", CircuitName = "C1",
                Cable = "ZB-YJV-3*70+1*35", Dia = "51"
            };

            System.Collections.Generic.Dictionary<string, string> values =
                FrameBlockFiller.BuildValues(machine, "", statistics);
            string info = values[FrameBlockFiller.TagCable];

            Assert.Contains("1000（盘内）", info);
            Assert.Contains("500（设备内）", info);
            Assert.Contains("3000（预留）", info);
            Assert.Contains("=14500mm", info);
        }
    }
}

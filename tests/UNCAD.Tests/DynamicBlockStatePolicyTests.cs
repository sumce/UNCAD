using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class DynamicBlockStatePolicyTests
    {
        [Theory]
        [InlineData("Device_Build20260716")]
        [InlineData("Device Build20260716")]
        [InlineData("device-build20260716")]
        public void DeviceBlockName_AcceptsObservedNameVariants(string name)
            => Assert.True(DynamicBlockStatePolicy.IsDeviceBlock(name));

        [Theory]
        [InlineData("插座5孔")]
        [InlineData("插座3孔")]
        [InlineData("插座")]
        public void DeviceSocketStates_EnableOutlet(string state)
        {
            Assert.True(DynamicBlockStatePolicy.TryClassifyDeviceState(state,
                out bool hasOutlet));
            Assert.True(hasOutlet);
        }

        [Fact]
        public void DeviceEquipmentState_DisablesOutlet()
        {
            Assert.True(DynamicBlockStatePolicy.TryClassifyDeviceState("设备",
                out bool hasOutlet));
            Assert.False(hasOutlet);
        }

        [Theory]
        [InlineData("I-Line盘", "I-line_Panel")]
        [InlineData("Iline盘", "I-line_Panel")]
        [InlineData("插座盘", "socket box")]
        [InlineData("母线插接口", "busbar connector")]
        public void NextPanel_MapsToObservedUpstreamVisibility(string next, string expected)
            => Assert.Equal(expected,
                DynamicBlockStatePolicy.UpstreamVisibilityState(next));

        [Fact]
        public void DeviceOutletPolicy_RemovesExcelOutletWhenDeviceIsEquipment()
        {
            List<TableFillRow> result = DeviceOutletPolicy.Apply(Rows(), false,
                new TableFillRow { Category = TableFillCategory.Outlet, Code = "8.3" });

            Assert.DoesNotContain(result, UpdateOutletPolicy.IsOutlet);
        }

        [Fact]
        public void DeviceOutletPolicy_AddsGeneratedOutletForILineWhenDeviceShowsSocket()
        {
            List<TableFillRow> input = Rows().Where(row =>
                row.Category != TableFillCategory.Outlet).ToList();
            var outlet = new TableFillRow
            {
                Category = TableFillCategory.Outlet,
                SortOrder = 800,
                Code = "8.3",
                Name = "插座",
                CatalogMatched = true
            };

            List<TableFillRow> result = DeviceOutletPolicy.Apply(input, true, outlet);

            Assert.Equal("8.3", Assert.Single(result,
                UpdateOutletPolicy.IsOutlet).Code);
        }

        private static List<TableFillRow> Rows() => new List<TableFillRow>
        {
            new TableFillRow { Category = TableFillCategory.Cable, SortOrder = 100, Code = "1.1" },
            new TableFillRow { Category = TableFillCategory.Outlet, SortOrder = 800, Code = "8.2" }
        };
    }
}

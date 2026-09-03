using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class DeviceBlockFillerTests
    {
        [Fact]
        public void BuildValue_UsesTrimmedCircuitNameOnly()
        {
            var row = new MachineRow
            {
                MachineId = "MDAPT01",
                CircuitName = "  主配电盘  "
            };

            Assert.Equal("主配电盘", DeviceBlockFiller.BuildValue(row));
        }

        [Fact]
        public void BuildValue_FallsBackToMachineId_WhenCircuitNameMissing()
        {
            Assert.Equal("MEOXT15", DeviceBlockFiller.BuildValue(new MachineRow
            {
                MachineId = "MEOXT15"
            }));
        }
    }
}

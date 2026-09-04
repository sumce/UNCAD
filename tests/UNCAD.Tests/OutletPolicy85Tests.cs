using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class OutletPolicy85Tests
    {
        private static TableFillRow Row(string code, string name, TableFillCategory category)
            => new TableFillRow { Code = code, Name = name, Category = category };

        [Fact]
        public void IsOutlet_OnlyMatchesRealOutletCodes()
        {
            Assert.True(UpdateOutletPolicy.IsOutlet(Row("8.2", "插座", TableFillCategory.Manual)));
            Assert.True(UpdateOutletPolicy.IsOutlet(Row("8.3", "插座", TableFillCategory.Manual)));
            Assert.False(UpdateOutletPolicy.IsOutlet(Row("8.5", "设备接地独立连接",
                TableFillCategory.Manual)));
            Assert.False(UpdateOutletPolicy.IsOutlet(Row("8.1", "高架地板封堵",
                TableFillCategory.Manual)));
            Assert.False(UpdateOutletPolicy.IsOutlet(Row("8.4", "变压器",
                TableFillCategory.Manual)));
            Assert.False(UpdateOutletPolicy.IsOutlet(Row("8.13", "开洞",
                TableFillCategory.Manual)));
        }

        [Fact]
        public void Apply_KeepsGroundingRow85InList()
        {
            var rows = new System.Collections.Generic.List<TableFillRow>
            {
                Row("8.5", "设备接地独立连接", TableFillCategory.Manual),
                Row("3.1", "镀锌穿线管", TableFillCategory.RigidConduit)
            };
            var result = DeviceOutletPolicy.Apply(rows, false, null);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, row => row.Code == "8.5");
        }

        [Fact]
        public void EndToEnd_PlanReviewSurvivesOutletPolicyForGrounding()
        {
            var machine = new MachineRow { MachineId = "M01", CircuitName = "c",
                Cable = "1*16", Dia = "25" };
            var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());
            var output = TableGenerationModule.Plan(new TableGenerationRequest(
                machine, catalog, new UNCAD.Core.Stat.CableStatResult { CableSum = 30 },
                FillPlanningOptions.Default));
            var survived = DeviceOutletPolicy.Apply(output.CopyDefaultRows(), false, null);
            Assert.Contains(survived, row => row.Code == "8.5");
        }
    }
}

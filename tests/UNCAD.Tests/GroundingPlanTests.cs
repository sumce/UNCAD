using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public class GroundingPlanTests
    {
        [Fact]
        public void Plan_Maps1x16CableToGroundingItem85()
        {
            var machine = new MachineRow
            {
                MachineId = "M01",
                CircuitName = "回路1",
                Cable = "1*16",
                Dia = "25"
            };
            var catalog = new BoqCatalogIndex(UNCAD.Core.Excel.ListItemReader.ReadEmbedded());
            var statistics = new CableStatResult { CableSum = 30 };
            TableGenerationOutput output = TableGenerationModule.Plan(
                new TableGenerationRequest(machine, catalog, statistics,
                    FillPlanningOptions.Default));
            var rows = output.CopyDefaultRows();
            foreach (var r in rows)
                Console.WriteLine("ROW: cat=" + r.Category + " name=" + r.Name
                    + " qty=" + r.Quantity + " code=" + r.Code + " unit=" + r.Unit);
            Assert.Contains(rows, r => r.Code == "8.5");
        }
    }
}

using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public class GroundingReviewTests
    {
        [Fact]
        public void Review_KeepsGroundingRow85Included()
        {
            var machine = new MachineRow { MachineId = "M01", CircuitName = "c",
                Cable = "1*16", Dia = "25" };
            var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());
            var output = TableGenerationModule.Plan(new TableGenerationRequest(
                machine, catalog, new CableStatResult { CableSum = 30 },
                FillPlanningOptions.Default));
            FillReviewData review = output.CreateReview(machine, FillPlanningOptions.Default);
            foreach (var item in review.Items)
                Console.WriteLine("ITEM: code=" + item.Code + " name=" + item.Name
                    + " cat=" + item.Category + " included=" + item.Included
                    + " matched=" + item.CatalogMatched);
            var g = review.Items.FirstOrDefault(item => item.Code == "8.5");
            Assert.NotNull(g);
            Assert.True(g.Included);
            var selected = review.SelectedRows();
            Assert.Contains(selected, r => r.Code == "8.5");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class FillAnomalyDetectorTests
    {
        [Fact]
        public void MissingCableReplacement_RematchesBoqWithoutChangingDeviceCable()
        {
            var original = new MachineRow { Cable = "ZB-UNKNOWN-3*2.5" };
            FillReviewData review = FillReviewData.Create(original, new[]
            {
                new TableFillRow
                {
                    Category = TableFillCategory.Cable,
                    Name = "电缆", Quantity = "12.5", CatalogMatched = false
                }
            });
            var catalog = new BoqCatalogIndex(new[]
            {
                new ListItem
                {
                    Code = "1.25", Name = "交联电缆", Feature = "清单电缆模板",
                    Unit = "m", Spec = "3*2.5"
                }
            });

            Assert.Equal("ZB-UNKNOWN-3*2.5",
                FillAnomalyDetector.MissingCable(review).Subject);
            review.SetCableModel("3*2.5", catalog);

            FillReviewItem cable = review.CableItem();
            Assert.Equal("ZB-UNKNOWN-3*2.5", review.Machine.Cable);
            Assert.Equal("ZB-UNKNOWN-3*2.5", review.OriginalCableModel);
            Assert.Equal("3*2.5", review.BoqCableModel);
            Assert.Equal("1.25", cable.Code);
            Assert.Equal("清单电缆模板", cable.Description);
            Assert.Equal("12.5", cable.Quantity);
            Assert.True(cable.CatalogMatched);
            Assert.Null(FillAnomalyDetector.MissingCable(review));

            FillReviewData baseline = review.Snapshot();
            review.SetCableModel("ANOTHER-UNKNOWN", catalog);
            Assert.Equal("3*2.5", baseline.BoqCableModel);
            Assert.Equal("1.25", baseline.CableItem().Code);
            Assert.Equal("ZB-UNKNOWN-3*2.5", baseline.Machine.Cable);
        }

        [Fact]
        public void Detector_FlagsEveryUnmatchedGeneratedCategoryButNotManualRows()
        {
            FillReviewData review = FillReviewData.Create(new MachineRow(), new[]
            {
                Row(TableFillCategory.Cable, "电缆"),
                Row(TableFillCategory.Bridge, "桥架200*100"),
                Row(TableFillCategory.Outlet, "插座")
            });
            review.AddCatalogItem(new ListItem
            {
                Code = "8.9",
                Name = "数据库辅材",
                Unit = "个"
            }, "2");

            List<FillAnomaly> anomalies = FillAnomalyDetector.DetectCatalog(review);

            Assert.Equal(3, anomalies.Count);
            Assert.All(anomalies, anomaly =>
                Assert.Equal(FillAnomalyDetector.MissingCatalogCode, anomaly.Code));
            Assert.DoesNotContain(anomalies,
                anomaly => anomaly.Category == TableFillCategory.Manual);
        }

        private static TableFillRow Row(TableFillCategory category, string name)
            => new TableFillRow
            {
                Category = category,
                Name = name,
                CatalogMatched = false
            };
    }
}

using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameInfoJsonRecordUpdaterTests
    {
        [Fact]
        public void Update_PersistsUserBusPlugBoxChoiceWithoutChangingSourceRating()
        {
            var machine = new MachineRow
            {
                MachineId = "MECTT03", CircuitName = "AC Rack 2-1",
                Next = "母线插接口", Detail = "N208 3P4W 3P350A"
            };
            var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());
            FillReviewData BuildReview() => FillReviewData.Create(machine,
                TableFillPlanner.Build(machine, catalog, new CableStatResult(),
                    FillPlanningOptions.Default));
            FillReviewData review = BuildReview();
            Assert.True(review.BusPlugBoxItem().RequiresCatalogConfirmation);

            review.ReplaceWithCatalogItem(review.BusPlugBoxItem(),
                catalog.FindBusPlugBox("400A"), catalog);
            FrameInfoJsonRecord record = FrameInfoJsonCodec.Parse(FrameInfoJsonCodec.Serialize(
                FrameInfoJsonRecordUpdater.Update(null, machine, review, "U1U", DateTime.UtcNow)));
            Assert.Equal("5.6", record.BoqBusPlugBoxCode);
            Assert.Contains("350A", record.Detail);
            Assert.Contains(record.Changes, change => change.Field == "BoqBusPlugBoxCode"
                && change.After == "5.6");

            FillReviewData restored = BuildReview();
            restored.RestoreBusPlugBoxChoice(record, catalog);
            TableFillRow row = Assert.Single(restored.SelectedRows());
            Assert.Equal("5.6", row.Code);
            Assert.Contains("400A", row.Description);
            Assert.Equal("1", row.Quantity);
            Assert.Contains("350A", restored.Machine.Detail);

            // A later catalog addition must not replace the already confirmed choice.
            var exact = new ListItem { Category = "母线插接箱", Code = "5.test",
                Name = "母线插接箱", Alias = "350A", Feature = "350A", Unit = "个" };
            var expandedCatalog = new BoqCatalogIndex(catalog.Items.Concat(new[] { exact }));
            FillReviewData expandedReview = FillReviewData.Create(machine,
                TableFillPlanner.Build(machine, expandedCatalog, new CableStatResult(),
                    FillPlanningOptions.Default));
            expandedReview.RestoreBusPlugBoxChoice(record, expandedCatalog);
            Assert.Equal("5.6", expandedReview.BusPlugBoxItem().Code);

            machine.Detail = "N208 3P4W 3P360A";
            FillReviewData changed = BuildReview();
            changed.RestoreBusPlugBoxChoice(record, catalog);
            Assert.True(changed.BusPlugBoxItem().RequiresCatalogConfirmation);

            machine.Detail = record.Detail;
            machine.CircuitName = "Different circuit";
            changed = BuildReview();
            changed.RestoreBusPlugBoxChoice(record, catalog);
            Assert.True(changed.BusPlugBoxItem().RequiresCatalogConfirmation);

            machine.CircuitName = record.DeviceName;
            record.BoqBusPlugBoxCode = "1.1";
            changed = BuildReview();
            changed.RestoreBusPlugBoxChoice(record, catalog);
            Assert.True(changed.BusPlugBoxItem().RequiresCatalogConfirmation);
        }

        [Fact]
        public void Update_PreservesConfirmedCableOverrideAndDoesNotMutatePreviousHistory()
        {
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "M1",
                DeviceName = "A",
                OriginalCableModel = "ORIGINAL",
                BoqCableModel = "CONFIRMED"
            };
            previous.Changes.Add(new FrameInfoJsonChange { Field = "Existing" });
            var machine = new MachineRow
            {
                MachineId = "M1",
                CircuitName = "A",
                Cable = "EXCEL_DEFAULT"
            };

            FrameInfoJsonRecord updated = FrameInfoJsonRecordUpdater.Update(previous,
                machine, null, "U1U", new DateTime(2026, 9, 2, 0, 0, 0,
                    DateTimeKind.Utc));

            Assert.Equal("ORIGINAL", updated.OriginalCableModel);
            Assert.Equal("CONFIRMED", updated.BoqCableModel);
            Assert.Single(previous.Changes);
            Assert.Single(updated.Changes);
            Assert.NotSame(previous.Changes[0], updated.Changes[0]);
        }

        [Fact]
        public void Update_RecordsChangedIdentityWithStableTimestamp()
        {
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "M1",
                DeviceName = "A"
            };
            var machine = new MachineRow { MachineId = "M2", CircuitName = "B" };
            DateTime now = new DateTime(2026, 9, 2, 1, 2, 3, DateTimeKind.Utc);

            FrameInfoJsonRecord updated = FrameInfoJsonRecordUpdater.Update(previous,
                machine, null, "U1U", now);

            Assert.Equal("2026-09-02T01:02:03.0000000Z", updated.LastModifiedUtc);
            Assert.Contains(updated.Changes, change => change.Field == "MachineId"
                && change.Before == "M1" && change.After == "M2");
            Assert.All(updated.Changes, change => Assert.Equal(
                updated.LastModifiedUtc, change.TimestampUtc));
        }
    }
}

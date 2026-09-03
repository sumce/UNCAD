using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameInfoJsonRecordUpdaterTests
    {
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

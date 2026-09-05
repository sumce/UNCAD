using System;
using System.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class VersionChangeLogTests
    {
        [Fact]
        public void NewestEntryMatchesTheReleasedVersion()
        {
            Assert.NotEmpty(VersionChangeLog.Entries);
            Assert.Equal(ProductMetadata.VersionLabel, VersionChangeLog.Entries[0].Version);
        }

        [Fact]
        public void EntriesAreNewestFirstWithNonEmptyChanges()
        {
            for (int index = 0; index < VersionChangeLog.Entries.Count; index++)
            {
                VersionChangeLogEntry entry = VersionChangeLog.Entries[index];
                Assert.True(Version.TryParse(entry.Version, out _), entry.Version);
                Assert.NotEmpty(entry.Changes);
                if (index == 0) continue;
                Version current = Version.Parse(entry.Version);
                Version previous = Version.Parse(VersionChangeLog.Entries[index - 1].Version);
                Assert.True(previous > current,
                    "更新日志必须按版本从新到旧排列: " + previous + " > " + current);
            }
        }

        [Fact]
        public void OlderSeenVersionShowsNewerEntriesOnly()
        {
            // 2.4.1 is a shipped entry, so an upgrade from it sees exactly 2.4.2.
            var from241 = VersionChangeLog.EntriesNewerThan("2.4.1");
            Assert.Single(from241);
            Assert.Equal(ProductMetadata.VersionLabel, from241[0].Version);
        }

        [Fact]
        public void CurrentSeenVersionShowsNothing()
        {
            Assert.Empty(VersionChangeLog.EntriesNewerThan(ProductMetadata.VersionLabel));
        }

        [Fact]
        public void FirstInstallShowsOnlyTheLatestNotes()
        {
            var firstInstall = VersionChangeLog.EntriesNewerThan("");
            Assert.Single(firstInstall);
            Assert.Equal(ProductMetadata.VersionLabel, firstInstall[0].Version);
        }

        [Fact]
        public void UnknownSeenVersionIsTreatedAsUpgradeFromIt()
        {
            // A future/corrupt stored value must not blank the dialog; anything older
            // than the newest entry still shows the newer notes.
            Assert.NotEmpty(VersionChangeLog.EntriesNewerThan("2.3.9"));
        }
    }
}

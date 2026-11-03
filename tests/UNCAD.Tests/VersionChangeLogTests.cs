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
                // D-039：开发版条目（X.Y.Z-dev）按 X.Y.Z 参与比较与解析。
                Version current = ParseWithDevSuffix(entry.Version);
                Assert.NotNull(current);
                Assert.NotEmpty(entry.Changes);
                if (index == 0) continue;
                Version previous = ParseWithDevSuffix(
                    VersionChangeLog.Entries[index - 1].Version);
                Assert.True(previous > current,
                    "更新日志必须按版本从新到旧排列: " + previous + " > " + current);
            }
        }

        private static Version ParseWithDevSuffix(string label)
        {
            string text = (label ?? "").Trim();
            int dash = text.IndexOf('-');
            if (dash >= 0) text = text.Substring(0, dash);
            return Version.TryParse(text, out Version parsed) ? parsed : null;
        }

        [Fact]
        public void StableCustomers_NeverSeeDevChannelEntries()
        {
            // D-039：正式版用户（seen=2.10.0）的更新提示不得出现 dev 条目。
            var stableSeen = VersionChangeLog.EntriesNewerThan("2.10.0");
            Assert.DoesNotContain(stableSeen,
                entry => entry.Version.IndexOf("-dev", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void PreviousSeenVersionShowsLatestEntryOnly()
        {
            // D-039：dev 条目对正式版用户不可见，所以“最新一条”要按正式版口径取。
            // 看过“次新正式版”的用户只会看到“最新正式版”的说明；dev 通道期间
            // 最新正式版可能不是 Entries[0]，未来 3.0.0 转正后本测试自动适配。
            var stableEntries = VersionChangeLog.Entries
                .Where(entry => entry.Version.IndexOf("-dev",
                    StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();
            Assert.True(stableEntries.Count >= 2, "至少要有两个正式版条目");
            var previousSeen = VersionChangeLog.EntriesNewerThan(stableEntries[1].Version);
            Assert.Single(previousSeen);
            Assert.Equal(stableEntries[0].Version, previousSeen[0].Version);
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

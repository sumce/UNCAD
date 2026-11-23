using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class ExistingFillIdentityResolverTests
    {
        [Fact]
        public void TryResolve_ReadsCurrentPowerCompositeAndDeviceValues()
        {
            bool ok = ExistingFillIdentityResolver.TryResolve(
                new[] { "MQ-BAN01-POWER" }, new[] { "MQ-BAN01-设备A" },
                new[] { "设备A" }, out ExistingFillIdentity identity, out string error);

            Assert.True(ok, error);
            Assert.Equal("MQ-BAN01", identity.MachineId);
            Assert.Equal("设备A", identity.DeviceName);
        }

        [Fact]
        public void TryResolve_ReadsLegacyCompositeWithoutPowerOrDeviceBlock()
        {
            bool ok = ExistingFillIdentityResolver.TryResolve(
                new string[0], new[] { "M01-旧设备" }, new string[0],
                out ExistingFillIdentity identity, out string error);

            Assert.True(ok, error);
            Assert.Equal("M01", identity.MachineId);
            Assert.Equal("旧设备", identity.DeviceName);
        }

        [Fact]
        public void TryResolve_UsesKnownDeviceSuffixForHyphenatedLegacyMachineId()
        {
            bool ok = ExistingFillIdentityResolver.TryResolve(
                new string[0], new[] { "MQ-BAN01-设备A" }, new[] { "设备A" },
                out ExistingFillIdentity identity, out string error);

            Assert.True(ok, error);
            Assert.Equal("MQ-BAN01", identity.MachineId);
        }

        [Fact]
        public void TryResolve_RejectsFrameAndDeviceBlockNameMismatch()
        {
            Assert.False(ExistingFillIdentityResolver.TryResolve(
                new[] { "M01-POWER" }, new[] { "M01-设备A" }, new[] { "设备B" },
                out _, out string error));
            Assert.Contains("M01-设备A", error);
            Assert.Contains("DEVICENAME", error);
            Assert.Contains("不一致", error);
        }

        [Fact]
        public void TryResolve_CompositeAloneResolvesLayoutIdentityWithoutDeviceBlocks()
        {
            // D-045 修订（2026-11-22，XLAYOUT 现场报告）：布局身份只读图框自身
            // 属性，设备块 DEVICENAME 缺席时复合属性必须独立给出完整身份——
            // 同一图框在全图/单机两种选择下读出的身份不再依赖设备块归属，
            // 「图框属性与设备块不一致」在布局路径结构性不可能发生。
            bool ok = ExistingFillIdentityResolver.TryResolve(
                new[] { "MTPWM04-POWER" }, new[] { "MTPWM04-Scrubber - 2" },
                new string[0], out ExistingFillIdentity identity, out string error);

            Assert.True(ok, error);
            Assert.Equal("", error);
            Assert.Equal("MTPWM04", identity.MachineId);
            Assert.Equal("Scrubber - 2", identity.DeviceName);
        }

        [Fact]
        public void TryResolve_IgnoresInternalWhitespaceButKeepsSourceText()
        {
            bool ok = ExistingFillIdentityResolver.TryResolve(
                new[] { "M Q-01-POWER" }, new[] { "M Q-01-设备 A" },
                new[] { "设备A" }, out ExistingFillIdentity identity, out string error);

            Assert.True(ok, error);
            Assert.Equal("M Q-01", identity.MachineId);
            Assert.Equal("设备A", identity.DeviceName);
        }

        [Fact]
        public void TryResolve_RejectsMultipleDifferentDevices()
        {
            Assert.False(ExistingFillIdentityResolver.TryResolve(
                new[] { "M01-POWER" }, new[] { "M01-A" }, new[] { "A", "B" },
                out _, out string error));
            Assert.Contains("多个不同的设备名称", error);
        }

        [Fact]
        public void MatchMachine_AutomaticallySelectsExactExistingDevice()
        {
            var identity = new ExistingFillIdentity { MachineId = "M01", DeviceName = "设备B" };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity, new[]
            {
                new MachineRow { MachineId = "M01", CircuitName = "设备A", Cable = "C-A" },
                new MachineRow { MachineId = "M01", CircuitName = "设备B", Cable = "C-B" }
            }, out string error);

            Assert.NotNull(match);
            Assert.Equal("", error);
            Assert.Equal("设备B", match.CircuitName);
            Assert.Equal("C-B", match.Cable);
        }

        [Fact]
        public void MatchMachine_IgnoresInternalWhitespaceInMachineAndDevice()
        {
            var identity = new ExistingFillIdentity { MachineId = "M Q-01", DeviceName = "设备 A" };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity, new[]
            {
                new MachineRow { MachineId = "MQ-01", CircuitName = "设备A", Cable = "C-A" }
            }, out string error);

            Assert.NotNull(match);
            Assert.Equal("", error);
            Assert.Equal("M Q-01", match.MachineId);
            Assert.Equal("设备 A", match.CircuitName);
            Assert.Equal("C-A", match.Cable);
        }

        [Fact]
        public void MatchMachine_RejectsUnknownDeviceEvenWhenMachineHasOneRow()
        {
            var identity = new ExistingFillIdentity { MachineId = "M01", DeviceName = "现场改名" };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity, new[]
            {
                new MachineRow { MachineId = "M01", CircuitName = "Excel旧名称", Cable = "C1" }
            }, out string error);

            Assert.Null(match);
            Assert.Contains("Excel", error);
            if (match == null) return;
            Assert.Equal("现场改名", match.CircuitName);
        }

        [Fact]
        public void MatchMachine_RejectsAmbiguousMachineWithoutDeviceMatch()
        {
            var identity = new ExistingFillIdentity { MachineId = "M01", DeviceName = "未知设备" };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity, new[]
            {
                new MachineRow { MachineId = "M01", CircuitName = "设备A" },
                new MachineRow { MachineId = "M01", CircuitName = "设备B" }
            }, out string error);

            Assert.Null(match);
            Assert.Contains("多个回路", error);
        }

        // ---- D-056 同名回路（同机台同名、上游编号不同） ----

        private static MachineRow[] SameNameCandidates()
        {
            return new[]
            {
                new MachineRow { MachineId = "M01", CircuitName = "ECS",
                    Fr = "3N-11B1A-PP-02", FacilitySwitch = "3P50A", Cable = "C-A" },
                new MachineRow { MachineId = "M01", CircuitName = "ECS",
                    Fr = "3N-31D2A-PP-03", FacilitySwitch = "3P63A", Cable = "C-B" }
            };
        }

        [Fact]
        public void MatchMachine_CircuitKeyDisambiguatesSameName()
        {
            string key = CircuitIdentityKey.Of(new MachineRow
                { MachineId = "M01", CircuitName = "ECS", Fr = "3N-31D2A-PP-03" });
            var identity = new ExistingFillIdentity
                { MachineId = "M01", DeviceName = "ECS", CircuitKey = key };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity,
                SameNameCandidates(),
                out System.Collections.Generic.IReadOnlyList<MachineRow> ambiguous,
                out string error);

            Assert.NotNull(match);
            Assert.Equal("", error);
            Assert.Empty(ambiguous);
            Assert.Equal("3N-31D2A-PP-03", match.Fr);
            Assert.Equal("C-B", match.Cable);
        }

        [Fact]
        public void MatchMachine_SameNameWithoutKeyReturnsCandidates()
        {
            var identity = new ExistingFillIdentity { MachineId = "M01", DeviceName = "ECS" };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity,
                SameNameCandidates(),
                out System.Collections.Generic.IReadOnlyList<MachineRow> ambiguous,
                out string error);

            Assert.Null(match);
            Assert.Equal(2, ambiguous.Count);
            Assert.Equal("3N-11B1A-PP-02", ambiguous[0].Fr);
            Assert.Equal("3N-31D2A-PP-03", ambiguous[1].Fr);
            Assert.Contains("同名回路", error);
        }

        [Fact]
        public void MatchMachine_OldOverloadReportsSameNameAsError()
        {
            // U1PDF/预览等无弹窗路径：歧义必须显式报错，绝不静默取第一条。
            var identity = new ExistingFillIdentity { MachineId = "M01", DeviceName = "ECS" };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity,
                SameNameCandidates(), out string error);

            Assert.Null(match);
            Assert.Contains("同名回路", error);
        }

        [Fact]
        public void MatchMachine_StaleKeyFallsBackToUniqueNameMatch()
        {
            // 上游改挂：帧上还是旧键，表格已换新上游——唯一同名仍应放行更新。
            string staleKey = CircuitIdentityKey.Of(new MachineRow
                { MachineId = "M01", CircuitName = "ECS", Fr = "3N-OLD-PP-01" });
            var identity = new ExistingFillIdentity
                { MachineId = "M01", DeviceName = "ECS", CircuitKey = staleKey };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity, new[]
            {
                new MachineRow { MachineId = "M01", CircuitName = "ECS", Fr = "3N-NEW-PP-01" },
                new MachineRow { MachineId = "M01", CircuitName = "Other", Fr = "3N-X-PP-01" }
            }, out string error);

            Assert.NotNull(match);
            Assert.Equal("", error);
            Assert.Equal("3N-NEW-PP-01", match.Fr);
        }

        [Fact]
        public void MatchMachine_DuplicateKeyRowsAreDataError()
        {
            MachineRow first = new MachineRow
                { MachineId = "M01", CircuitName = "ECS", Fr = "3N-11B1A-PP-02" };
            var identity = new ExistingFillIdentity
            {
                MachineId = "M01", DeviceName = "ECS",
                CircuitKey = CircuitIdentityKey.Of(first)
            };
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity, new[]
            {
                first,
                new MachineRow { MachineId = "M01", CircuitName = "ECS",
                    Fr = "3N-11B1A-PP-02（51/Ac）" } // 标注差异不影响键：同键重复行
            }, out string error);

            Assert.Null(match);
            Assert.Contains("重复行", error);
        }
    }
}

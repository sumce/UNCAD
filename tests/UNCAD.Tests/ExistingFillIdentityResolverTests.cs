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
    }
}

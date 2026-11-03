using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 身份仲裁（幽灵机台修复第二轮，2026-11-02）：图框复制/挪用后旧 JSON
    /// 不得压过用户已改新的物理标注；一致或单侧为空时维持 JSON 优先。
    /// </summary>
    public sealed class FrameIdentityArbiterTests
    {
        private static ExistingFillIdentity Identity(string machine, string device)
            => new ExistingFillIdentity { MachineId = machine, DeviceName = device };

        [Fact]
        public void Agree_OnJson()
        {
            var decided = FrameIdentityArbiter.Decide(
                Identity("MTTSA01", "注塑机A"), Identity("mttsa01", "任一描述"),
                out bool physicalWins, out string note);

            Assert.False(physicalWins);
            Assert.Equal("", note);
            Assert.Equal("MTTSA01", decided.MachineId);
        }

        [Fact]
        public void Divergence_PhysicalWinsAndExplains()
        {
            var decided = FrameIdentityArbiter.Decide(
                Identity("MTTSA01", "旧机台"), Identity("M-2F-05", "组装线"),
                out bool physicalWins, out string note);

            Assert.True(physicalWins);
            Assert.Equal("M-2F-05", decided.MachineId);
            Assert.Contains("MTTSA01", note);
            Assert.Contains("M-2F-05", note);
            Assert.Contains("按图框标注处理", note);
        }

        [Fact]
        public void JsonOnly_KeepsJsonPrecedence()
        {
            var decided = FrameIdentityArbiter.Decide(
                Identity("MTTSA01", "注塑机A"), null,
                out bool physicalWins, out _);

            Assert.False(physicalWins);
            Assert.Equal("MTTSA01", decided.MachineId);
        }

        [Fact]
        public void PhysicalOnly_UsesPhysical()
        {
            var decided = FrameIdentityArbiter.Decide(
                null, Identity("M-2F-05", "组装线"),
                out bool physicalWins, out _);

            Assert.False(physicalWins);
            Assert.Equal("M-2F-05", decided.MachineId);
        }

        [Fact]
        public void BothEmpty_ReturnsNull()
        {
            var decided = FrameIdentityArbiter.Decide(null, null, out _, out _);
            Assert.Null(decided);
        }
    }
}

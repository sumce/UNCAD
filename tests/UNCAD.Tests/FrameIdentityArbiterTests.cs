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
        private static ExistingFillIdentity Identity(string machine, string device,
            string upstream = "", string key = "")
            => new ExistingFillIdentity
            {
                MachineId = machine,
                DeviceName = device,
                UpstreamId = upstream,
                CircuitKey = key
            };

        [Fact]
        public void Agree_OnJson()
        {
            var decided = FrameIdentityArbiter.Decide(
                Identity("MTTSA01", "注塑机A"), Identity("mttsa01", "注塑机A"),
                out bool physicalWins, out string note);

            Assert.False(physicalWins);
            Assert.Equal("", note);
            Assert.Equal("MTTSA01", decided.MachineId);
        }

        [Fact]
        public void SameMachine_DifferentDevice_PhysicalWins()
        {
            // 3.8.0.11：同一机台内复制图框并改了回路名 —— 源框 JSON 的键失效。
            var json = Identity("MTTSA01", "注塑机A", "U1", "mttsa01|注塑机a|u1");
            var physical = Identity("MTTSA01", "注塑机B");
            var decided = FrameIdentityArbiter.Decide(json, physical,
                out bool physicalWins, out string note);

            Assert.True(physicalWins);
            Assert.Equal("注塑机B", decided.DeviceName);
            Assert.Equal("", decided.CircuitKey);
            Assert.Contains("回路 注塑机A", note);
            Assert.Contains("回路 注塑机B", note);
        }

        [Fact]
        public void SameMachineSameDevice_DifferentUpstream_PhysicalWinsAndDropsKey()
        {
            // 现场（2026-11-27）：同机台两条同名回路、上游不同；复制框的
            // 物理上游已改成第二条，但 frameinfo_json 还是第一条的身份键。
            var json = Identity("MTTSA01", "ECS", "3N-11B1A-PP-02",
                "mttsa01|ecs|3n-11b1a-pp-02");
            var physical = Identity("MTTSA01", "ECS", "2U-UPS5D1-PP-02");
            var decided = FrameIdentityArbiter.Decide(json, physical,
                out bool physicalWins, out string note);

            Assert.True(physicalWins);
            Assert.Equal("2U-UPS5D1-PP-02", decided.UpstreamId);
            Assert.Equal("", decided.CircuitKey);
            Assert.Contains("上游 3N-11B1A-PP-02", note);
            Assert.Contains("上游 2U-UPS5D1-PP-02", note);
        }

        [Fact]
        public void SameMachine_PhysicalUpstreamMissing_KeepsJsonKey()
        {
            // 上游块未被选中/老图没有上游块：无法判定，维持 JSON 优先。
            var json = Identity("MTTSA01", "ECS", "3N-11B1A-PP-02",
                "mttsa01|ecs|3n-11b1a-pp-02");
            var physical = Identity("MTTSA01", "ECS");
            var decided = FrameIdentityArbiter.Decide(json, physical,
                out bool physicalWins, out _);

            Assert.False(physicalWins);
            Assert.Equal("mttsa01|ecs|3n-11b1a-pp-02", decided.CircuitKey);
        }

        [Fact]
        public void SameUpstreamCode_DifferentAnnotations_KeepsJsonKey()
        {
            // 上游编号按 D-056 口径比较：槽位/轴位注记不同但编号相同 = 同一条回路。
            var json = Identity("MTTSA01", "ECS", "3N-11B1A-PP-02 40-42(41/Ab)",
                "mttsa01|ecs|3n-11b1a-pp-02");
            var physical = Identity("MTTSA01", "ECS", "3N-11B1A-PP-02 36(43/AC)");
            var decided = FrameIdentityArbiter.Decide(json, physical,
                out bool physicalWins, out _);

            Assert.False(physicalWins);
            Assert.Equal("mttsa01|ecs|3n-11b1a-pp-02", decided.CircuitKey);
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

        [Fact]
        public void CopiedSameNameCircuit_AfterArbitration_BecomesUnresolvedInCircuitMatcher()
        {
            string upA = "3N-11B1A-PP-02";
            string upB = "2U-UPS5D1-PP-02";
            var expected = new[]
            {
                new CircuitSelection
                {
                    MachineId = "MTTSA01", CircuitName = "ECS", UpstreamId = upA,
                    CircuitKey = CircuitIdentityKey.Of("MTTSA01", "ECS", upA)
                },
                new CircuitSelection
                {
                    MachineId = "MTTSA01", CircuitName = "ECS", UpstreamId = upB,
                    CircuitKey = CircuitIdentityKey.Of("MTTSA01", "ECS", upB)
                }
            };
            var decided = FrameIdentityArbiter.Decide(
                Identity("MTTSA01", "ECS", upA, expected[0].CircuitKey),
                Identity("MTTSA01", "ECS", upB),
                out bool physicalWins, out _);

            Assert.True(physicalWins);
            Assert.Equal("", decided.CircuitKey);

            CircuitMatchResult result = CircuitMatcher.Pair("MTTSA01", expected,
                new[]
                {
                    expected[0],
                    new CircuitSelection
                    {
                        MachineId = decided.MachineId,
                        CircuitName = decided.DeviceName,
                        CircuitKey = decided.CircuitKey,
                        UpstreamId = decided.UpstreamId
                    }
                },
                hasBaseline: true);

            Assert.False(result.Complete);
            Assert.Single(result.Unresolved);
            Assert.Empty(result.Duplicates);
        }
    }
}

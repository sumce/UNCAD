using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.8.0.11（D-056 Amendment）：复制图框的旧身份键不能压过图框物理标注。
    /// 同一机台内同名回路的上游编号不同就是另一条回路——物理上游从上游块的
    /// UPSTREAM_INFO 首行读，身份仲裁与搬场重绑都要比对。
    /// </summary>
    public sealed class CopiedFrameIdentityContractTests
    {
        [Fact]
        public void PhysicalUpstream_IsReadAndCompared()
        {
            string collector = Source("src", "UNCAD", "Cad", "Frame",
                "FillSelectionCollector.cs");
            Assert.Contains("ConnectionBlockFiller.TagUpstreamInfo", collector);
            Assert.Contains("identity.UpstreamId = ReadPhysicalUpstream", collector);
            // 3.8.0.11（XLAYOUT 口径对齐）：上游信息读取不得被 includeDeviceBlocks 门控挡掉；
            // 且 TryReadExistingIdentity 必须先读物理身份并传入 FrameInfoJsonBlockReader.Read，
            // 以支持 D-052-C 搬场保留 CircuitKey。
            int deviceGate = collector.IndexOf("if (includeDeviceBlocks)",
                StringComparison.Ordinal);
            int upstreamAssign = collector.IndexOf("identity.UpstreamId = ReadPhysicalUpstream",
                StringComparison.Ordinal);
            Assert.True(deviceGate >= 0 && upstreamAssign > deviceGate);
            int resolveIdx = collector.IndexOf("bool resolved = ExistingFillIdentityResolver.TryResolve(",
                StringComparison.Ordinal);
            Assert.True(resolveIdx >= 0 && upstreamAssign > resolveIdx);
            int physicalIdx = collector.IndexOf("TryReadPhysicalIdentity(",
                StringComparison.Ordinal);
            int jsonReadIdx = collector.IndexOf("FrameInfoJsonBlockReader.Read(transaction,",
                StringComparison.Ordinal);
            Assert.True(physicalIdx >= 0 && jsonReadIdx > physicalIdx);

            string arbiter = Source("src", "UNCAD", "Core", "Fill",
                "FrameIdentityArbiter.cs");
            Assert.Contains("upstreamDiffers", arbiter);
            Assert.Contains("physicalIdentity.CircuitKey = \"\"", arbiter);

            string reader = Source("src", "UNCAD", "Cad", "Frame",
                "FrameInfoJsonBlockReader.cs");
            Assert.Contains("expectedIdentity?.UpstreamId", reader);

            string identityReader = Source("src", "UNCAD", "Cad", "Frame",
                "FrameIdentityReader.cs");
            Assert.Contains("ReadArbitratedIdentity", identityReader);
            Assert.Contains("TryReadExistingIdentity", identityReader);

            string policy = Source("src", "UNCAD", "Core", "Fill",
                "FrameMetadataRebindPolicy.cs");
            Assert.Contains("recordedUpstream", policy);
            Assert.Contains("CircuitIdentityKey.UpstreamKey(record.Fr)", policy);
            Assert.Contains("CircuitIdentityKey.UpstreamKey", arbiter);
        }

        [Fact]
        public void CrossFileCopyRebind_FixesMissingHandleAndCollisionAndCableStage()
        {
            string reader = Source("src", "UNCAD", "Cad", "Frame",
                "FrameInfoJsonBlockReader.cs");
            int tryGet = reader.IndexOf("TryGetObjectId(new Handle(value)",
                StringComparison.Ordinal);
            Assert.True(tryGet >= 0);
            int notFoundReturn = reader.IndexOf("return false;", tryGet,
                StringComparison.Ordinal);
            int foundCheck = reader.IndexOf("found == ObjectId.Null",
                StringComparison.Ordinal);
            Assert.True(notFoundReturn > tryGet && notFoundReturn < foundCheck,
                "目标 DWG 不存在原句柄时 TryGetObjectId 返回 false，必须直接判为已不在本图（false）");
            Assert.Contains("ClearlyDifferentFrameIdentity(", reader);
            Assert.Contains("TryReadPhysicalIdentity(transaction, selection", reader);

            string pickStage = Source("src", "UNCAD", "Features", "Fill",
                "FillMachinePickStage.cs");
            Assert.Contains("expectedIdentity, out _, out _);", pickStage);
        }

        private static string Source(params string[] parts)
            => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
    }
}

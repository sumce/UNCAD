using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-052-C（2026-11-10）搬场重绑判定的纯逻辑单测：句柄失配时，
    /// 「身份一致 + 原句柄已不在本图」= 整机台搬场（跨文件复制/移动）→ 收下；
    /// 其余（同文件复制、复制后改号、身份残缺、幽灵）→ 维持外来拒绝。
    /// </summary>
    public sealed class FrameMetadataRebindPolicyTests
    {
        private static FrameInfoJsonRecord Record(string machine, string device,
            string frameHandle)
            => new FrameInfoJsonRecord
            {
                MachineId = machine,
                DeviceName = device,
                FrameHandle = frameHandle,
                DeviceReserveMm = "3000"
            };

        [Fact]
        public void IdentityMatchAndHandleGone_IsMoved()
        {
            Assert.True(FrameMetadataRebindPolicy.IsMovedFrame(
                Record("MMPTC10", "注塑机L3", "1A2B"),
                "MMPTC10", "注塑机L3", handle => false));
        }

        [Theory]
        [InlineData("MMPTC11", "注塑机L3")]   // 机台号不同（复制后改号/幽灵）
        [InlineData("mmptc10 ", "注塑机L4")]  // 设备名不同
        public void IdentityMismatch_NotMoved(string machine, string device)
        {
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                Record("MMPTC10", "注塑机L3", "1A2B"),
                machine, device, handle => false));
        }

        [Fact]
        public void IdentityNormalization_IgnoresCaseAndWhitespace()
        {
            // 空白/大小写差异不阻断搬场判定（与机台匹配同一套规范化口径）。
            Assert.True(FrameMetadataRebindPolicy.IsMovedFrame(
                Record(" mmptc10 ", "注塑机 L3", "1A2B"),
                "MMPTC10", "注塑机L3", handle => false));
        }

        [Fact]
        public void SameMachineDevice_DifferentUpstream_NotMoved()
        {
            // 3.8.0.11：同机台同名回路、上游不同 = 另一条回路；复制框不能
            // 按「整机台搬场」收下源框记录（否则会沿用源框的身份键）。
            var record = Record("MMPTC10", "注塑机L3", "1A2B");
            record.Fr = "3N-11B1A-PP-02";
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                record, "MMPTC10", "注塑机L3", "2U-UPS5D1-PP-02",
                handle => false));
        }

        [Fact]
        public void SameUpstream_IsMoved()
        {
            var record = Record("MMPTC10", "注塑机L3", "1A2B");
            record.Fr = "3N-11B1A-PP-02";
            Assert.True(FrameMetadataRebindPolicy.IsMovedFrame(
                record, "MMPTC10", "注塑机L3", "3n-11b1a-pp-02 ",
                handle => false));
        }

        [Theory]
        [InlineData("", "2U-UPS5D1-PP-02")]   // 记录没有上游
        [InlineData("3N-11B1A-PP-02", "")]    // 本框没有物理上游（上游块未选中）
        public void MissingUpstream_KeepsLegacyMovedSemantics(string recorded,
            string expected)
        {
            // 任一侧上游未知时维持旧口径：只比机台+回路名，不阻断搬场。
            var record = Record("MMPTC10", "注塑机L3", "1A2B");
            record.Fr = recorded;
            Assert.True(FrameMetadataRebindPolicy.IsMovedFrame(
                record, "MMPTC10", "注塑机L3", expected, handle => false));
        }

        [Fact]
        public void SourceFrameStillInDrawing_NotMoved()
        {
            // 原句柄仍在本图 = 源框还在，这是同文件复制出的新图框，不是搬场。
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                Record("MMPTC10", "注塑机L3", "1A2B"),
                "MMPTC10", "注塑机L3", handle => true));
        }

        [Theory]
        [InlineData("", "注塑机L3", "1A2B")]  // 记录身份残缺
        [InlineData("MMPTC10", "", "1A2B")]
        [InlineData("MMPTC10", "注塑机L3", "")] // 记录无句柄绑定（不该走到这里，防御）
        public void IncompleteRecord_NotMoved(string machine, string device,
            string handle)
        {
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                Record(machine, device, handle),
                "MMPTC10", "注塑机L3", _ => false));
        }

        [Fact]
        public void MissingExpectedIdentityOrRecord_NotMoved()
        {
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                null, "MMPTC10", "注塑机L3", _ => false));
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                Record("MMPTC10", "注塑机L3", "1A2B"),
                "", "注塑机L3", _ => false));
            Assert.False(FrameMetadataRebindPolicy.IsMovedFrame(
                Record("MMPTC10", "注塑机L3", "1A2B"),
                "MMPTC10", "", _ => false));
        }

        [Fact]
        public void IsDifferentFrameIdentity_DetectsCollidingDifferentFramesAcrossFiles()
        {
            var record = Record("MMPTC10", "注塑机L3", "1A2B");
            record.Fr = "3N-11B1A-PP-02";

            // 目标文件同一十六进制句柄恰巧属于另一机台/回路/上游 → 撞车不算原框仍在。
            Assert.True(FrameMetadataRebindPolicy.IsDifferentFrameIdentity(
                record, "MMPTC99", "注塑机L3", ""));
            Assert.True(FrameMetadataRebindPolicy.IsDifferentFrameIdentity(
                record, "MMPTC10", "注塑机L4", ""));
            Assert.True(FrameMetadataRebindPolicy.IsDifferentFrameIdentity(
                record, "MMPTC10", "注塑机L3", "2U-UPS5D1-PP-02"));

            // 同机台同回路同上游（或缺省空标签）→ 保守判定为原框仍在（同文件复制仍拦）。
            Assert.False(FrameMetadataRebindPolicy.IsDifferentFrameIdentity(
                record, "mmptc10", "注塑机 L3", "3n-11b1a-pp-02(1/A)"));
            Assert.False(FrameMetadataRebindPolicy.IsDifferentFrameIdentity(
                record, "", "", ""));
        }
    }
}

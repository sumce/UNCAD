using UNCAD.Core.Fill;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 设备内预留恢复（2026-11-02 现场需求）：U1U 在 JSON 没有记录时，
    /// 默认采用图框 CABLE_INFO 文本里已有的「数值（设备内）」——数值任意。
    /// </summary>
    public sealed class CableInfoDeviceReserveRecoveryTests
    {
        [Theory]
        [InlineData("电缆ZB-YJVR-5*4mm²: 1000+700+3400（盘内）+800（设备内）+3000（预留）=8900mm", 800d)]
        [InlineData("电缆ZB-YJVR-5*4mm²: 1000+1500.5（设备内）=2500.5mm", 1500.5d)]
        [InlineData("1000(设备内)", 1000d)]                                 // 半角括号
        [InlineData("…+950 （设备内）+…", 950d)]                             // 括号前空白
        [InlineData("2000（设备内预留说明）之后又出现 650（设备内）", 650d)]  // 取第一个合法段
        [InlineData("0（设备内）", 0d)]
        public void Parses_AnyDeviceReserveValue(string cableInfo, double expected)
        {
            Assert.True(CableReserveReview.TryParseDeviceReserveMillimeters(
                cableInfo, out double millimeters));
            Assert.Equal(expected, millimeters, 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("电缆ZB-YJVR-5*4mm²: 1000+700+3400（盘内）+3000（预留）=7100mm")] // 无设备内段
        [InlineData("设备内预留说明文字")]          // 无「数值（设备内）」结构
        [InlineData("120000（设备内）")]            // 超上限，视为无效
        [InlineData("abc（设备内）")]               // 括号前不是数字
        public void Rejects_TextWithoutValidDeviceSegment(string cableInfo)
        {
            Assert.False(CableReserveReview.TryParseDeviceReserveMillimeters(
                cableInfo, out _));
        }

        [Fact]
        public void Recovery_WiredIntoBothU1uPaths()
        {
            // 源码契约：批量与单框两条 U1U 路径都必须先尝试 CABLE_INFO 恢复，
            // 再决定是否询问/预填设备内预留。
            // 2026-11-05：单框的恢复接线在 FillReserveStage（预留恢复阶段，
            // 先于统计执行）。
            // 2026-11-09：批量恢复接线随 Preflight 拆到 BatchFillUpdatePreflight。
            string batch = System.IO.File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "BatchFillUpdatePreflight.cs"));
            string single = System.IO.File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FillReserveStage.cs"));

            Assert.Contains("TryParseDeviceReserveMillimeters", batch);
            Assert.Contains("TryParseDeviceReserveMillimeters", single);
            // 不得误用其他段（预留/盘内）做恢复来源。
            Assert.Contains("FrameBlockFiller.TagCable", batch);
            Assert.Contains("FrameBlockFiller.TagCable", single);
        }

        [Fact]
        public void BothFillPaths_AbsorbLegacyHorizontalMarkers()
        {
            // D-043（2026-11-03）：单框与批量 U1U 都接横排毫米标自动识别。
            // 2026-11-05：单框的识别/决策在 FillReserveStage（先于统计，
            // 吸收文本不计入米数），删除仍在 FillFeature 写事务。
            string single = File.ReadAllText(RepoFile("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            Assert.Contains("DeviceReserveMarkerReader.CollectInSelection(", single);
            Assert.Contains("DeviceReserveMarker.Decide(", single);
            string fillFeature = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));
            Assert.Contains("DeviceReserveMarkerReader.EraseAbsorbed(", fillFeature);

            // 2026-11-09：批量 Collect/Decide 在预检（BatchFillUpdatePreflight），
            // EraseAbsorbed 仍在协调器写事务。
            string batchPreflight = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdatePreflight.cs"));
            string batch = File.ReadAllText(RepoFile("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            Assert.Contains("DeviceReserveMarkerReader.Collect(", batchPreflight);
            Assert.Contains("DeviceReserveMarker.Decide(", batchPreflight);
            Assert.Contains("DeviceReserveMarkerReader.EraseAbsorbed(", batch);

            // 默认开关值与 U1SET 开关键名。
            string keys = File.ReadAllText(RepoFile("src", "UNCAD", "Infra", "ConfigKeys.cs"));
            Assert.Contains("UNC_FILL_DEVICE_RESERVE_AUTO", keys);
        }

        [Fact]
        public void MovedFrameRebind_WiredIntoJsonReadAndBothU1uPaths()
        {
            // D-052-C（2026-11-10，现场报告“图纸跨文件复制后 U1U 提示输入预留、
            // 默认 0”）：句柄失配不再一律拒绝——身份一致且原句柄已不在本图
            // （= 整机台跨文件搬场）时收下记录；单框与批量两条读取路径都带身份。
            string writer = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FrameInfoJsonBlockWriter.cs"));
            Assert.Contains("FrameMetadataRebindPolicy.IsMovedFrame", writer);
            Assert.Contains("movedJsonNote", writer);
            Assert.Contains("HandleAliveInDatabase", writer);
            // 外来拒绝文案保持不变（3.0.0.6 语义不回归）。
            Assert.Contains("多为图框移动后遗留，建议删除该块", writer);

            string batch = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "BatchFillUpdatePreflight.cs"));
            Assert.Contains("out string foreignJsonNote, out string movedJsonNote", batch);
            Assert.Contains("identity,", batch);

            string single = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FillReserveStage.cs"));
            Assert.Contains("FillSelectionCollector.TryReadExistingIdentity(previousTransaction,",
                single);
            Assert.Contains("ownIdentity,", single);
            Assert.Contains("out double? deviceReserveHintMm", single);
            // 复制框建议值只做预填（用户核对确认），不静默写回记录。
            Assert.Contains("清单确认窗已按该值预填（复制源），请核对后确认", single);

            string feature = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FillFeature.cs"));
            Assert.Contains("out double? copiedDeviceReserveHint", feature);
            Assert.Contains("ApplyDeviceReserve(statistics, cableReserve, copiedDeviceReserveHint.Value)",
                feature);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = System.IO.Path.GetFullPath(System.AppContext.BaseDirectory
                + "\\..\\..\\..\\..\\..");
            return System.IO.Path.Combine(root, System.IO.Path.Combine(parts));
        }
    }
}

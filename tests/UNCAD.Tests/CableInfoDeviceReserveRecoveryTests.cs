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
            string batch = System.IO.File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "BatchFillUpdateCoordinator.cs"));
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

            string batch = File.ReadAllText(RepoFile("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            Assert.Contains("DeviceReserveMarkerReader.Collect(", batch);
            Assert.Contains("DeviceReserveMarker.Decide(", batch);
            Assert.Contains("DeviceReserveMarkerReader.EraseAbsorbed(", batch);

            // 默认开关值与 U1SET 开关键名。
            string keys = File.ReadAllText(RepoFile("src", "UNCAD", "Infra", "ConfigKeys.cs"));
            Assert.Contains("UNC_FILL_DEVICE_RESERVE_AUTO", keys);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = System.IO.Path.GetFullPath(System.AppContext.BaseDirectory
                + "\\..\\..\\..\\..\\..");
            return System.IO.Path.Combine(root, System.IO.Path.Combine(parts));
        }
    }
}

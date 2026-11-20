using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-050 Amendment（2026-11-15，用户："有线管的线不会有桥架"）——线管与
    /// 桥架在一条基准线上互斥，U1C 的独立 UNC_CONDUIT_* 键保留，但默认车道
    /// 改为与 U1Q 完全一致：上侧、偏移 15。旧默认（下侧、外圈 210）是
    /// D-050 为"可能与桥架共存"预留的分离车道，共存不存在，默认随之作废。
    /// 源级契约锁死三个默认值落点：
    /// ① ConduitFeature 命令默认（GetDouble 15.0 / Get "1"），旧的 210.0 默认
    ///    不得复活；
    /// ② U1SET 设置窗加载默认与命令默认一致（两处漂移会让显示值 ≠ 生效值）；
    /// ③ 保存路径的空值兜底同为上侧（与 UnqSide 既有兜底口径一致）。
    /// </summary>
    public sealed class ConduitLaneDefaultContractTests
    {
        [Fact]
        public void ConduitFeature_Defaults_To_Tray_Lane()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Features",
                "Conduit", "ConduitFeature.cs"));
            Assert.Contains("Settings.GetNonNegativeDouble(ConfigKeys.ConduitLineOff, 15.0)",
                source, StringComparison.Ordinal);
            Assert.Contains("Settings.Get(ConfigKeys.ConduitSide, \"1\")",
                source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConfigKeys.ConduitLineOff, 210.0",
                source, StringComparison.Ordinal);
        }

        [Fact]
        public void SettingsWindow_Load_And_Save_Defaults_Match_Command_Defaults()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "UI", "Wpf",
                "SettingsWindow.xaml.cs"));
            // 加载：默认 15 / 上侧，与 ConduitFeature 一致。
            Assert.Contains("Settings.GetDouble(ConfigKeys.ConduitLineOff, 15.0)",
                source, StringComparison.Ordinal);
            Assert.Contains("Settings.Get(ConfigKeys.ConduitSide, \"1\")",
                source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConfigKeys.ConduitLineOff, 210.0",
                source, StringComparison.Ordinal);
            // 保存：空值兜底同为上侧。
            Assert.Contains(
                "Convert.ToString(DrawPage.ConduitSide.SelectedValue ?? \"1\")",
                source, StringComparison.Ordinal);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}

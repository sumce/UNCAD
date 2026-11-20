using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Cad;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 分层守卫（D-037）：把 DEVELOPMENT.md 的模块边界从纸面规则变成失败即红的测试。
    /// 规则按“当前干净的状态”冻结——新增违规直接红；存量例外必须写进白名单并注明原因。
    /// 只检查 using 指令（真实依赖），注释里提到 AutoCAD/Excel 不算违规。
    /// </summary>
    public sealed class LayerGuardTests
    {
        /// <summary>
        /// RibbonBuilder 是 AutoCAD 侧 Ribbon 的构造器，唯一允许 UI 层触碰 Autodesk API
        /// 的文件；属已知债务，3.0 UI 迁移时再决定去处（见 D-037）。
        /// </summary>
        private static readonly string[] UiAutoCadWhitelist =
        {
            Path.Combine("src", "UNCAD", "UI", "RibbonBuilder.cs")
        };

        [Fact]
        public void Core_StaysFreeOfAutoCadAndWinFormsAndWpf()
        {
            string[] offenders = SourceFilesUnder("src", "UNCAD", "Core")
                .Where(f => UsesAnyOf(f,
                    "using Autodesk", "using System.Windows.Forms", "using System.Windows;"))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void Npoi_StaysConfinedToCore()
        {
            // NPOI 是 Excel 读写 SDK；Core/Excel、Core/Report、Core/Submission 是它的合法住处，
            // 出现在 Cad/Features/UI/Infra 就意味着业务数据解析越层。
            string[] offenders = SourceFilesUnder("src", "UNCAD")
                .Where(f => !IsUnder(f, "src", "UNCAD", "Core"))
                .Where(f => UsesAnyOf(f, "using NPOI"))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void Ui_StaysFreeOfAutoCadExceptRibbonBuilder()
        {
            string[] offenders = SourceFilesUnder("src", "UNCAD", "UI")
                .Where(f => !UiAutoCadWhitelist.Any(w =>
                    f.EndsWith(w, StringComparison.OrdinalIgnoreCase)))
                .Where(f => UsesAnyOf(f, "using Autodesk"))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void Cad_StaysFreeOfExcelAndDesktopUi()
        {
            // Cad 只做 AutoCAD 适配：Excel 解析/写入在 Core，WinForms/WPF 在 UI。
            string[] offenders = SourceFilesUnder("src", "UNCAD", "Cad")
                .Where(f => UsesAnyOf(f, "using NPOI", "using UNCAD.Core.Excel",
                    "using System.Windows.Forms", "using System.Windows;"))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void FrameBlockNames_AreFrozenForLegacyDrawings()
        {
            // 图框块名是对客户旧图纸的兼容承诺；上一代迁移到当前代。
            // 2026-11-19：FRAME_20261102 降为上一代，FRAME_20261119 成为当前代。
            // 改名或删除任何一代都等于放弃一批现场图纸，必须先立新决策。
            Assert.Equal("frame_20260812", FrameRegionCollector.SupportedFrameName);
            Assert.Equal("frame", FrameRegionCollector.LegacyFrameName);
            Assert.Equal("xframe", FrameRegionCollector.XFrameName);
            Assert.Equal("xframe_v2", FrameRegionCollector.XFrameV2Name);
            Assert.Equal("FRAME_20261102", FrameRegionCollector.PreviousFrameName);
            Assert.Equal("FRAME_20261119", FrameRegionCollector.CurrentFrameName);
        }

        private static IEnumerable<string> SourceFilesUnder(params string[] parts)
            => Directory.GetFiles(RepoDir(parts), "*.cs", SearchOption.AllDirectories);

        private static bool UsesAnyOf(string file, params string[] forbiddenUsings)
        {
            return File.ReadAllLines(file).Any(line =>
            {
                string trimmed = line.TrimStart();
                return forbiddenUsings.Any(prefix =>
                    trimmed.StartsWith(prefix, StringComparison.Ordinal));
            });
        }

        private static bool IsUnder(string file, params string[] parts)
        {
            string dir = RepoDir(parts);
            return file.StartsWith(dir + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string RepoDir(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

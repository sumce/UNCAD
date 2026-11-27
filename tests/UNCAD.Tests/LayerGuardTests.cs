using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        /// <summary>
        /// 当前存在的跨 Feature 引用（结构债）。新引用不在表内即失败；
        /// 修掉一条后必须同步删条目，否则 ArchitectureWhitelistEntries_AreStillPresent 会红。
        /// 目标见 docs/ARCHITECTURE.md：Feature 之间不得互相引用。
        /// </summary>
        private static readonly HashSet<string> CrossFeatureWhitelist =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "src/UNCAD/Features/Fill/BatchFillUpdateCoordinator.cs -> UNCAD.Features.Submit",
                "src/UNCAD/Features/Fill/FillFeature.cs -> UNCAD.Features.Submit",
                "src/UNCAD/Features/Conduit/ConduitFeature.cs -> UNCAD.Features.Fill"
            };

        /// <summary>
        /// UI 反向引用 Features 的存量（已清零；新引用立即失败）。
        /// </summary>
        private static readonly HashSet<string> UiFeatureWhitelist =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        public void FeatureToFeatureReferences_DoNotGrow()
        {
            // 目标：Feature 之间不互相引用；共享能力下沉 Core/Cad/Features.Shared。
            // 存量在 CrossFeatureWhitelist 中逐条登记，新增即红。
            var offenders = new List<string>();
            foreach (string file in SourceFilesUnder("src", "UNCAD", "Features"))
            {
                string own = FeatureNamespaceOf(file);
                foreach (string target in FeatureNamespacesUsed(file))
                {
                    if (target == own
                        || string.Equals(target, "Shared", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string key = RepoRelative(file) + " -> UNCAD.Features." + target;
                    if (!CrossFeatureWhitelist.Contains(key)) offenders.Add(key);
                }
            }
            Assert.True(offenders.Count == 0,
                "新增跨 Feature 引用（共享能力应下沉 Core/Cad/Features.Shared）: "
                + string.Join("; ", offenders));
        }

        [Fact]
        public void UiDoesNotReferenceFeatures_ExceptKnownResultWindows()
        {
            var offenders = new List<string>();
            foreach (string file in SourceFilesUnder("src", "UNCAD", "UI"))
            {
                foreach (string target in FeatureNamespacesUsed(file))
                {
                    string key = RepoRelative(file) + " -> UNCAD.Features." + target;
                    if (!UiFeatureWhitelist.Contains(key)) offenders.Add(key);
                }
            }
            Assert.True(offenders.Count == 0,
                "UI 反向引用 Features（结果 DTO 应下沉 Core）: "
                + string.Join("; ", offenders));
        }

        [Fact]
        public void Infra_DoesNotReferenceCadFeaturesOrUi()
        {
            string[] offenders = SourceFilesUnder("src", "UNCAD", "Infra")
                .Where(f => UsesAnyOf(f, "using UNCAD.Cad", "using UNCAD.Features",
                    "using UNCAD.UI"))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void Cad_DoesNotReferenceFeaturesOrUi()
        {
            string[] offenders = SourceFilesUnder("src", "UNCAD", "Cad")
                .Where(f => UsesAnyOf(f, "using UNCAD.Features", "using UNCAD.UI"))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void ArchitectureWhitelistEntries_AreStillPresent()
        {
            // 白名单条目必须随修复收敛：文件改名、搬走或引用删除后，条目要一起删。
            foreach (string key in CrossFeatureWhitelist.Concat(UiFeatureWhitelist))
            {
                string[] parts = key.Split(new[] { " -> " }, StringSplitOptions.None);
                string file = Path.Combine(RepoDir(), parts[0].Replace('/', '\\'));
                Assert.True(File.Exists(file), "白名单文件已不存在，请删除条目: " + key);
                Assert.True(UsesAnyOf(file, "using " + parts[1]),
                    "白名单引用已不存在，请删除条目: " + key);
            }
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
            => Directory.GetFiles(RepoDir(parts), "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains(
                        Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    && !file.Contains(
                        Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

        private static string FeatureNamespaceOf(string file)
        {
            string marker = Path.DirectorySeparatorChar + "Features"
                + Path.DirectorySeparatorChar;
            int index = file.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return "";
            string rest = file.Substring(index + marker.Length);
            int slash = rest.IndexOf(Path.DirectorySeparatorChar);
            return slash < 0 ? "" : rest.Substring(0, slash);
        }

        private static IEnumerable<string> FeatureNamespacesUsed(string file)
        {
            foreach (string line in File.ReadAllLines(file))
            {
                Match match = Regex.Match(line.Trim(),
                    @"^using\s+UNCAD\.Features\.([A-Za-z0-9_]+)\s*;");
                if (match.Success) yield return match.Groups[1].Value;
            }
        }

        private static string RepoRelative(string file)
        {
            string root = RepoDir();
            return file.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

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

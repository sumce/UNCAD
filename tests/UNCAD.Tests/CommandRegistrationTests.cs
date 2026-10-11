using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class CommandRegistrationTests
    {
        [Fact]
        public void Catalog_ContainsOnlyU1AndRetainedTraditionalCommands()
        {
            Assert.Equal(new[]
            {
                "U1L", "U1LX", "U1L3D", "U13D", "U1DT", "U1R", "U1Q1", "U1Q2", "U1Q4", "U1F", "U1U",
                "U1S", "U1C", "U1A", "U1SET", "U1DATA", "U1DWG", "XLAYOUT", "XSTS", "Xmerge", "U1HELP"
            }, CommandIds.Canonical);
            Assert.Equal(new[] { "U1D" }, CommandIds.Aliases);
            Assert.Equal(new[] { "UNL", "UNLX", "UNR", "UNQ1", "UNQ2", "UNQ4", "UNADD" },
                CommandIds.Legacy);
            Assert.Equal("U1LX;UNLX", CommandIds.QuickLineFeatureCommands);
            Assert.Equal("U1L3D;U13D", CommandIds.QuickLine3dPreviewFeatureCommands);
            Assert.Equal("U1C;U1C20;U1C25;U1C38;U1C51",
                CommandIds.ConduitFeatureCommands);
            Assert.Equal("U1DWG;U1D", CommandIds.DwgExportFeatureCommands);
            Assert.Equal("U1PDF", CommandIds.PdfExportFeatureCommands);
            Assert.Equal(35, CommandIds.Registered.Count);
            Assert.Equal(35, CommandIds.Registered.Distinct(
                System.StringComparer.OrdinalIgnoreCase).Count());
            // UNC_ 前缀只允许在线登录命令使用（它是在线授权的恢复入口）。
            Assert.Equal(new[] { CommandIds.ClientLogin }, CommandIds.Registered.Where(
                command => command.StartsWith("UNC_", System.StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(CommandIds.Registered, command =>
                command.StartsWith("OPUN", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("UNADDX", CommandIds.Registered);
        }

        [Fact]
        public void FeatureCommandLists_CoverEveryRegisteredCompatibilityAlias()
        {
            // FeatureRegistry.Summary 直接拼接这些字符串并打印"已加载：…"，
            // 漏掉兼容旧名等于对用户隐瞒命令（曾经漏 UNL/UNR/UNQ1/UNQ2/UNQ4）。
            Assert.Equal("U1Q1;U1Q2;U1Q4;UNQ1;UNQ2;UNQ4", CommandIds.TrayFeatureCommands);
            Assert.Equal("U1L;UNL", CommandIds.LineFeatureCommands);
            Assert.Equal("U1R;UNR", CommandIds.ArchFeatureCommands);
        }

        [Fact]
        public void UnlxAlias_IsRegisteredAndForwardsToCanonicalQuickLineHandler()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineAliasCommand.cs"));

            Assert.Contains("[CommandMethod(CommandIds.LegacyLineQuick)]", source);
            Assert.Contains("new QuickLineFeature().QuickLine()", source);
        }

        [Fact]
        public void U1D_IsRegisteredOnlyAsTheU1DwgShortcut()
        {
            string export = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "DwgExport", "DwgExportFeature.cs"));
            string dimension = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Dimension", "DimensionTextFeature.cs"));

            Assert.Contains("[CommandMethod(CommandIds.DwgExportShort", export);
            Assert.Contains("public void ExportDwgShort() => Run()", export);
            Assert.Contains("[CommandMethod(CommandIds.DimensionText", dimension);
            Assert.DoesNotContain("CommandIds.DwgExportShort", dimension);
        }

        [Fact]
        public void SourceCommandMethods_RegisterEveryPublicCommandExactlyOnce()
        {
            // 扫描整个 src/UNCAD（不只 Features），并同时接受 CommandIds.X 与字面量两种写法：
            // 只认一种写法、只扫一个目录时，新命令漏注册或写到别处都不会让断言变红。
            string sourceRoot = RepoFile("src", "UNCAD");
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(sourceRoot, "*.cs",
                SearchOption.AllDirectories))
            {
                if (file.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0
                    || file.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                string source = File.ReadAllText(file);
                foreach (Match attribute in Regex.Matches(source,
                    @"\[CommandMethod\(\s*([^)]*)"))
                {
                    string arguments = attribute.Groups[1].Value;
                    Match constant = Regex.Match(arguments, @"CommandIds\.(\w+)");
                    string command;
                    if (constant.Success)
                    {
                        command = ResolveCommandConstant(constant.Groups[1].Value);
                    }
                    else
                    {
                        int quote = arguments.IndexOf('"');
                        int end = quote < 0 ? -1 : arguments.IndexOf('"', quote + 1);
                        if (end <= quote) continue;
                        command = arguments.Substring(quote + 1, end - quote - 1);
                    }
                    counts[command] = counts.TryGetValue(command, out int count)
                        ? count + 1 : 1;
                }
            }

            // 注册集合必须与公开契约逐一相等：少注册=用户敲了没反应，
            // 多注册=Bundle/帮助/Ribbon 都没同步。
            Assert.Equal(
                CommandIds.Registered.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
                counts.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            foreach (KeyValuePair<string, int> entry in counts)
                Assert.True(entry.Value == 1, entry.Key
                    + " must have exactly one [CommandMethod] registration.");
        }

        private static string ResolveCommandConstant(string constant)
        {
            System.Reflection.FieldInfo field = typeof(CommandIds).GetField(constant,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.True(field != null, "CommandIds." + constant + " 不存在");
            return (string)field.GetValue(null);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
    }
}

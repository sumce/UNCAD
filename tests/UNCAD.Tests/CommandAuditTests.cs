using System;
using System.IO;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 锁定 2.10.0 命令审计结论：审计文档必须覆盖 CommandIds.Registered 全部命令，
    /// 已删除的旧 SET/OP 命令入口不得回流（否则帮助/Ribbon/注册表会再次漂移）。
    /// </summary>
    public sealed class CommandAuditTests
    {
        [Fact]
        public void AuditDocument_CoversEveryRegisteredCommand()
        {
            string audit = File.ReadAllText(RepoFile("docs", "COMMAND-AUDIT.md"));

            Assert.Contains("`CommandIds.Registered`", audit);
            Assert.All(CommandIds.Registered, command =>
                Assert.Contains("`" + command + "`", audit));
        }

        [Fact]
        public void DeprecatedCommandEntryPoints_AreNotReintroduced()
        {
            string[] forbidden =
            {
                "OpUnl(", "OpUnr(", "OpUnq("
            };
            Assert.DoesNotContain("UncadLineSet", File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Unl", "UnlFeature.cs")));
            Assert.DoesNotContain("UncadTraySet", File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Unq", "UnqFeature.cs")));
            Assert.DoesNotContain("UncadConduitSet", File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Conduit", "ConduitFeature.cs")));
            Assert.DoesNotContain("UncadArchSet", File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Unr", "UnrFeature.cs")));
            Assert.DoesNotContain("UncadRibbon", File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "About", "AboutFeature.cs")));

            // U1Q 系列只接受固定规格；旧的“读 UNQ_TEXT 的无参桥架命令”不得复活。
            Assert.DoesNotContain("public void UncadTray()", File.ReadAllText(
                RepoFile("src", "UNCAD", "Features", "Unq", "UnqFeature.cs")));

            foreach (string name in forbidden)
            {
                foreach (string file in Directory.GetFiles(RepoDir(
                    "src", "UNCAD", "Features"), "*.cs", SearchOption.AllDirectories))
                    Assert.DoesNotContain(name, File.ReadAllText(file));
            }
        }

        [Fact]
        public void SettingsFeature_HasOneParameterlessEntryPoint()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "ConfigCenter", "SettingsFeature.cs"));

            Assert.Contains("internal static bool Show()", source);
            Assert.DoesNotContain("Show(int tabIndex)", source);
        }

        private static string RepoDir(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }

        private static string RepoFile(params string[] parts)
            => RepoDir(parts);
    }
}

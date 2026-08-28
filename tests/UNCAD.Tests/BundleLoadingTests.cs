using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class BundleLoadingTests
    {
        private const string PreviousProductCode =
            "{EC9A3EDE-1958-48A2-8CE9-75BEB058E1FB}";

        [Fact]
        public void Manifest_HasVersionSpecificProductAndStableUpgradeIdentity()
        {
            XElement package = LoadManifest();
            Version version = typeof(CommandIds).Assembly.GetName().Version;
            string expected = version.Major + "." + version.Minor + "." + version.Build;
            Assert.Equal(expected, (string)package.Attribute("AppVersion"));
            string productCode = (string)package.Attribute("ProductCode");
            string upgradeCode = (string)package.Attribute("UpgradeCode");
            Assert.True(Guid.TryParse(productCode, out _));
            Assert.True(Guid.TryParse(upgradeCode, out _));
            Assert.False(string.Equals(PreviousProductCode, productCode,
                StringComparison.OrdinalIgnoreCase));
            Assert.False(string.Equals(productCode, upgradeCode,
                StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Manifest_DeclaresEveryCanonicalCommandForOnDemandLoading()
        {
            XElement entry = LoadManifest().Element("Components")
                ?.Element("ComponentEntry");
            Assert.NotNull(entry);
            Assert.Equal("True", (string)entry.Attribute("LoadOnAutoCADStartup"));
            Assert.Equal("True", (string)entry.Attribute("LoadOnCommandInvocation"));
            var declared = new HashSet<string>(entry.Element("Commands")
                ?.Elements("Command").Select(item => (string)item.Attribute("Global"))
                ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(CommandIds.Registered.OrderBy(command => command),
                declared.OrderBy(command => command), StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Installer_UnblocksDownloadedBundleAndExplainsUserScope()
        {
            string script = File.ReadAllText(RepoFile("installer.ps1"));
            Assert.Contains("Unblock-BundleFiles $SourceBundle", script);
            Assert.Contains("Unblock-BundleFiles $stage", script);
            Assert.Contains("Unblock-BundleFiles $destination", script);
            Assert.Contains("current Windows user only", script);
            Assert.Contains("use AutoCAD RIBBON if hidden", script);
            Assert.Contains("$unexpectedCommands", script);
        }

        [Fact]
        public void Installer_RecoversInterruptedInstallAndReportsRollbackFailure()
        {
            string script = File.ReadAllText(RepoFile("installer.ps1"));

            Assert.Contains("Recover-InterruptedInstall $parent $destination", script);
            Assert.Contains("Previous installation was restored and verified.", script);
            Assert.Contains("安装失败且回滚未完成", script);
            Assert.DoesNotContain("Move-Item -LiteralPath $backup -Destination $destination -ErrorAction SilentlyContinue", script);
        }

        [Fact]
        public void Release_RejectsStaleOrMismatchedBuildOutput()
        {
            string script = File.ReadAllText(RepoFile("release.ps1"));

            Assert.Contains("-NoBuild rejected: source is newer than UNCAD.dll", script);
            Assert.Contains("Bundle UNCAD.dll does not match the verified build output", script);
            Assert.Contains("Get-FileHash $buildOutput -Algorithm SHA256", script);
        }

        private static XElement LoadManifest()
            => XDocument.Load(RepoFile("bundle", "UNCAD.bundle",
                "PackageContents.xml")).Root;

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
    }
}

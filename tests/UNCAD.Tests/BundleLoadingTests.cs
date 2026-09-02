using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class BundleLoadingTests
    {
        private const string PreviousProductCode =
            "{2AC56E88-DA40-4E57-B625-AA8347C2CA96}";

        [Fact]
        public void Manifest_HasVersionSpecificProductAndStableUpgradeIdentity()
        {
            XElement package = LoadManifest();
            Version version = typeof(CommandIds).Assembly.GetName().Version;
            string expected = version.ToString();
            Assert.Equal(expected, (string)package.Attribute("AppVersion"));
            Assert.Equal("UNCAD", (string)package.Attribute("Name"));
            Assert.Equal("Perpetual", (string)package.Attribute("LicenseMode"));
            Assert.Equal("", (string)package.Attribute("LicenseExpiresUtc"));
            Assert.Equal("https://www.unsiao.com", (string)package.Attribute("Website"));
            Assert.Equal("UNSIAO.Ltd", (string)package.Element("CompanyDetails")?.Attribute("Name"));
            Assert.Equal("https://www.unsiao.com", (string)package.Element("CompanyDetails")?.Attribute("Website"));
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
            Assert.Contains("\"U1DWG\"", script);
        }

        [Fact]
        public void Installer_PublicCommandAllowListMatchesRegisteredCommands()
        {
            string script = File.ReadAllText(RepoFile("installer.ps1"));
            Match block = Regex.Match(script,
                @"\$expectedCommands\s*=\s*@\((?<body>[\s\S]*?)\)\s*foreach\s*\(\$requiredCommand",
                RegexOptions.CultureInvariant);
            Assert.True(block.Success, "installer.ps1 expected-command block was not found.");
            string[] commands = Regex.Matches(block.Groups["body"].Value,
                    "\"(?<command>[^\"]+)\"", RegexOptions.CultureInvariant)
                .Cast<Match>().Select(item => item.Groups["command"].Value)
                .ToArray();

            Assert.Equal(CommandIds.Registered.OrderBy(item => item),
                commands.OrderBy(item => item), StringComparer.OrdinalIgnoreCase);
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
        public void Installer_ClearsStaleCommandRegistrationCacheOnInstallAndUninstall()
        {
            string script = File.ReadAllText(RepoFile("installer.ps1"));

            // 缓存清理函数必须存在,并在安装成功与卸载时都被调用;
            // 覆盖 HKCU 与 HKLM 两个注册表分支。
            Assert.Contains("function Clear-CommandRegistrationCache", script);
            Assert.Contains("Applications\\UNCAD", script);
            int install = script.IndexOf("Restart AutoCAD 2022.", StringComparison.Ordinal);
            int installCall = script.IndexOf("Clear-CommandRegistrationCache", install,
                StringComparison.Ordinal);
            Assert.True(installCall > install,
                "Install path must clear the stale command registration cache.");
            int uninstall = script.IndexOf("function Uninstall-Bundle", StringComparison.Ordinal);
            int uninstallCall = script.IndexOf("Clear-CommandRegistrationCache", uninstall,
                StringComparison.Ordinal);
            Assert.True(uninstallCall > uninstall,
                "Uninstall path must clear the stale command registration cache.");
            Assert.Contains("\"HKCU:\"", script);
            Assert.Contains("\"HKLM:\"", script);
        }

        [Fact]
        public void Release_RejectsStaleOrMismatchedBuildOutput()
        {
            string script = File.ReadAllText(RepoFile("release.ps1"));

            Assert.Contains("-NoBuild rejected: source is newer than UNCAD.dll", script);
            Assert.Contains("Bundle UNCAD.dll does not match the verified build output", script);
            Assert.Contains("Get-FileHash $buildOutput -Algorithm SHA256", script);
            Assert.DoesNotContain("Web\\QuickLine3D", script);
        }

        [Fact]
        public void PackagePipeline_ShipsNativeEditorWithoutWebPayload()
        {
            string build = File.ReadAllText(RepoFile("build.ps1"));
            string temporary = File.ReadAllText(RepoFile("release-temp.ps1"));
            string installer = File.ReadAllText(RepoFile("installer.ps1"));
            string csproj = File.ReadAllText(RepoFile("src", "UNCAD", "UNCAD.csproj"));

            // WebView2 与 Web 资源已随原生 OpenTK 编辑器移除。
            Assert.DoesNotContain("WebView2", build);
            Assert.DoesNotContain("Web\\QuickLine3D", build);
            Assert.DoesNotContain("WebView2", temporary);
            Assert.DoesNotContain("WebView2", installer);
            Assert.DoesNotContain("Web\\QuickLine3D", installer);
            Assert.DoesNotContain("Microsoft.Web.WebView2", csproj);
            Assert.DoesNotContain("OpenTK", csproj);
            Assert.DoesNotContain("OpenTK", installer);
            Assert.Contains("temporaryDllNames", temporary);
            Assert.Contains("Remove-Item -Force", temporary);
        }

        [Fact]
        public void TemporaryRelease_UsesUtcPlus8ExpiryAndIsolatedBundle()
        {
            string script = File.ReadAllText(RepoFile("release-temp.ps1"));

            Assert.Contains("$configuration = \"Temporary\"", script);
            Assert.Contains("$licenseMode = \"Trial\"", script);
            Assert.Contains("2026-09-03T00:00:00+08:00", script);
            Assert.Contains("temp-20260903", script);
            Assert.Contains("SkipBundle = $true", script);
            Assert.Contains("$stageBundle", script);
            Assert.Contains("-Mode VerifyPackage", script);
        }

        [Fact]
        public void CustomerRelease_DeclaresJiangsuWenyanLicenseMetadata()
        {
            string script = File.ReadAllText(RepoFile("release-jswy.ps1"));

            Assert.Contains("$configuration = \"JSWY\"", script);
            Assert.Contains("$licenseMode = \"Project\"", script);
            Assert.Contains("2026-10-01T00:00:00+08:00", script);
            Assert.Contains("UNCAD-JSWY", script);
            Assert.Contains("0x6C5F", script);
            Assert.Contains("0x674E", script);
            Assert.Contains("$expectedAuthorizationYears = \"10\"", script);
            Assert.Contains("-Mode VerifyPackage", script);
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

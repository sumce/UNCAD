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
            Assert.Contains(".html", script);
            Assert.Contains(".css", script);
            Assert.Contains(".js", script);
            Assert.Contains(".txt", script);
            Assert.Contains("Assert-TreeMatches", script);
            Assert.Contains("Web\\QuickLine3D", script);
        }

        [Fact]
        public void PackagePipeline_PreservesAndValidatesOfflineThreeAndWebView2Payload()
        {
            string build = File.ReadAllText(RepoFile("build.ps1"));
            string temporary = File.ReadAllText(RepoFile("release-temp.ps1"));
            string installer = File.ReadAllText(RepoFile("installer.ps1"));

            Assert.Contains("Microsoft.Web.WebView2.Core.dll", build);
            Assert.Contains("Microsoft.Web.WebView2.WinForms.dll", build);
            Assert.Contains("Microsoft.Web.WebView2.Wpf.dll", build);
            Assert.Contains("runtimes\\win-x64\\native\\WebView2Loader.dll", build);
            Assert.Contains("Web\\QuickLine3D", build);
            Assert.Contains("Copy-Item -LiteralPath $webSource", build);

            Assert.Contains("runtimes\\win-x64\\native\\WebView2Loader.dll", temporary);
            Assert.Contains("Web\\QuickLine3D", temporary);
            Assert.Contains("Microsoft.Web.WebView2.Core.dll", installer);
            Assert.Contains("runtimes\\win-x64\\native\\WebView2Loader.dll", installer);
            Assert.Contains("Web\\QuickLine3D\\vendor\\three\\three.core.min.js", installer);
            Assert.Contains("Web\\QuickLine3D\\vendor\\three\\three.module.min.js", installer);
            Assert.Contains("Web\\QuickLine3D\\vendor\\three\\LICENSE.txt", installer);
        }

        [Fact]
        public void TemporaryRelease_UsesUtcPlus8ExpiryAndIsolatedBundle()
        {
            string script = File.ReadAllText(RepoFile("release-temp.ps1"));

            Assert.Contains("$configuration = \"Temporary\"", script);
            Assert.Contains("$licenseMode = \"Trial\"", script);
            Assert.Contains("2026-09-08T23:59:59+08:00", script);
            Assert.Contains("temp-20260908", script);
            Assert.Contains("SkipBundle = $true", script);
            Assert.Contains("$stageBundle", script);
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

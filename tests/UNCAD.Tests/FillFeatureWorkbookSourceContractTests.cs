using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FillFeatureWorkbookSourceContractTests
    {
        [Fact]
        public void RemoteWorkbookResolution_UsesManualCacheOnly()
        {
            string source = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));

            Assert.Contains("TryGetCachedPath(path", source);
            Assert.Contains("网络 Excel 尚未缓存，请在 U1SET 中点击“刷新”。", source);
            Assert.DoesNotContain("MachineWorkbookSource.Refresh(path)", source);
        }

        [Fact]
        public void Xsts_RemoteWorkbookResolution_UsesManualCacheOnly()
        {
            string source = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Stat", "XstsFeature.cs"));

            Assert.Contains("TryGetCachedPath(configuredPath", source);
            Assert.Contains("网络 Excel 尚未缓存，请在 U1SET 中点击“刷新”", source);
            Assert.DoesNotContain("MachineWorkbookSource.Refresh", source);
        }

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}

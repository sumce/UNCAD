using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FillColorFeatureContractTests
    {
        [Fact]
        public void FillAndBatchUpdate_ColorEveryDeviceAndUpstreamBlockGroup()
        {
            string fill = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            string batch = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string writer = Read("src", "UNCAD", "Features", "Fill",
                "CadBlockColorWriter.cs");

            foreach (string source in new[] { fill, batch })
            {
                Assert.Contains("CadBlockColorWriter.Apply", source);
                Assert.Contains("DeviceBlockIds", source);
                Assert.Contains("DownstreamAxisBlockIds", source);
                Assert.Contains("DeviceColorBlockIds", source);
                Assert.Contains("UpstreamStateBlockIds", source);
                Assert.Contains("UpstreamInfoBlockIds", source);
                Assert.Contains("UpstreamAxisBlockIds", source);
                Assert.Contains("UpstreamColorBlockIds", source);
                Assert.DoesNotContain("UpstreamConnectionLineWriter", source);
            }
            Assert.Contains("block.RecordGraphicsModified(true)", writer);
            Assert.Contains("AttributeReference", writer);
        }

        [Fact]
        public void RemovedAutomaticConnectionImplementationDoesNotExist()
        {
            Assert.False(File.Exists(PathOf("src", "UNCAD", "Features", "Fill",
                "UpstreamConnectionLineWriter.cs")));
            Assert.False(File.Exists(PathOf("src", "UNCAD", "Core", "Geometry",
                "UpstreamConnectionGeometry.cs")));
        }

        private static string Read(params string[] parts)
            => File.ReadAllText(PathOf(parts));

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

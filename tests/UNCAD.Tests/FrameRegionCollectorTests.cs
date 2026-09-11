using System;
using System.IO;
using UNCAD.Cad;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameRegionCollectorTests
    {
        [Theory]
        [InlineData("frame")]
        [InlineData("frame_20260812")]
        [InlineData("xframe")]
        [InlineData("xframe$0$")]
        [InlineData("FRAME_20260812$12$$3$")]
        public void SupportedFrameNamesIncludeLegacyAndMangledDefinitions(string name)
        {
            Assert.True(FrameRegionCollector.IsSupportedFrameName(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("*U42")]
        [InlineData("frameinfo_json")]
        [InlineData("xframe_backup")]
        public void UnsupportedFrameNamesAreNotAccepted(string name)
        {
            Assert.False(FrameRegionCollector.IsSupportedFrameName(name));
        }

        [Fact]
        public void LegacyFrameClassificationExcludesCurrentXframe()
        {
            Assert.True(FrameRegionCollector.IsLegacyFrameName("frame"));
            Assert.True(FrameRegionCollector.IsLegacyFrameName("frame_20260812"));
            Assert.False(FrameRegionCollector.IsLegacyFrameName("xframe"));
        }

        [Fact]
        public void AnnotationOwnership_UsesTextAnchorBeforeGeometricExtents()
        {
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "UNCAD", "Cad", "FrameRegionCollector.cs")));
            int annotationAnchor = source.IndexOf(
                "TryAnnotationAnchor(entity, out anchor)", StringComparison.Ordinal);
            int extentsFallback = source.IndexOf(
                "return TryExtentsCenter(entity, out anchor);", annotationAnchor,
                StringComparison.Ordinal);

            Assert.True(annotationAnchor >= 0);
            Assert.True(extentsFallback > annotationAnchor);
            Assert.Contains("anchor = mtext.Location;", source);
            Assert.Contains("text.AlignmentPoint", source);
        }
    }
}

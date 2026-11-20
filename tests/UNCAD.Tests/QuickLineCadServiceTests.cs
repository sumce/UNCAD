using System.Collections.Generic;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineCadServiceTests
    {
        [Theory]
        [InlineData("2000mm", 2000)]
        [InlineData("  1250.5 MM ", 1250.5)]
        [InlineData(".5mm", 0.5)]
        [InlineData("0mm", 0)]
        public void TryParseMillimetres_AcceptsCanonicalLabels(string value, double expected)
        {
            Assert.True(QuickLineCadService.TryParseMillimetres(value, out double actual));
            Assert.Equal(expected, actual, 8);
        }

        [Theory]
        [InlineData("2000")]
        [InlineData("length 2000mm")]
        [InlineData("2000.5.5mm")]
        public void TryParseMillimetres_RejectsNonLabels(string value)
        {
            Assert.False(QuickLineCadService.TryParseMillimetres(value, out _));
        }

        [Fact]
        public void FormatMillimetres_UsesInvariantCompactValue()
        {
            Assert.Equal("1250.5mm", QuickLineCadService.FormatMillimetres(1250.5));
            Assert.Equal("2000mm", QuickLineCadService.FormatMillimetres(2000));
        }

        [Fact]
        public void StaleLabelMatcher_RestoresOnlyUniqueNearbyPath()
        {
            var paths = new IReadOnlyList<QuickLinePoint>[]
            {
                new[] { new QuickLinePoint(0, 0), new QuickLinePoint(100, 0) },
                new[] { new QuickLinePoint(0, 40), new QuickLinePoint(100, 40) }
            };

            Assert.Equal(0, QuickLineStaleLabelMatcher.FindUniqueNearestPath(
                new QuickLinePoint(50, 3), paths, 10, 1));
        }

        [Fact]
        public void StaleLabelRecovery_IsDisabledOutsideFillScan()
        {
            Assert.Null(new QuickLineScanOptions()
                .StaleAssociationMaximumDistance);
        }

        [Fact]
        public void StaleLabelMatcher_RejectsUnrelatedOrAmbiguousPaths()
        {
            var unrelated = new IReadOnlyList<QuickLinePoint>[]
            {
                new[] { new QuickLinePoint(0, 40), new QuickLinePoint(100, 40) }
            };
            var ambiguous = new IReadOnlyList<QuickLinePoint>[]
            {
                new[] { new QuickLinePoint(0, 0), new QuickLinePoint(100, 0) },
                new[] { new QuickLinePoint(0, 10), new QuickLinePoint(100, 10) }
            };

            Assert.Equal(-1, QuickLineStaleLabelMatcher.FindUniqueNearestPath(
                new QuickLinePoint(50, 3), unrelated, 10, 1));
            Assert.Equal(-1, QuickLineStaleLabelMatcher.FindUniqueNearestPath(
                new QuickLinePoint(50, 5), ambiguous, 10, 1));
        }
    }
}

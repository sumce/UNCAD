using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class DimensionTextFeatureContractTests
    {
        [Fact]
        public void U1DT_UsesCableHeightAndOriginalPlacementWithoutErasingGeometry()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            string feature = File.ReadAllText(Path.Combine(root, "src", "UNCAD",
                "Features", "Dimension", "DimensionTextFeature.cs"));
            string converter = File.ReadAllText(Path.Combine(root, "src", "UNCAD",
                "Cad", "AlignedDimensionTextConverter.cs"));
            string fillSelection = File.ReadAllText(Path.Combine(root, "src", "UNCAD",
                "Features", "Fill", "FillSelectionCollector.cs"));
            string statisticsReader = File.ReadAllText(Path.Combine(root, "src",
                "UNCAD", "Cad", "StatisticsTextReader.cs"));

            Assert.Contains("Settings.GetDouble(ConfigKeys.UnlHeight, 180.0)", feature);
            Assert.Contains("dimension.TextPosition", converter);
            Assert.Contains("dimension.TextRotation", converter);
            Assert.Contains("AttachmentPoint.MiddleCenter", converter);
            Assert.Contains("dimension.DimensionText = DimensionTextFormatter.SuppressedDimensionText",
                converter);
            Assert.DoesNotContain("dimension.Erase", converter);
            // U1F/U1U statistics read aligned/rotated dimension text overrides through the
            // shared reader; auto-measured values must never enter the statistics input.
            Assert.Contains("TEXT,MTEXT,DIMENSION,ACAD_TABLE,INSERT", fillSelection);
            Assert.Contains("StatisticsTextReader", fillSelection);
            Assert.Contains("entity is AlignedDimension || entity is RotatedDimension",
                statisticsReader);
            Assert.Contains("IndexOf(\"<>\"", statisticsReader);
        }
    }
}

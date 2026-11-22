using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class FillDrawingSettingsCorrectionContractTests
    {
        [Fact]
        public void U1FAndU1U_SkipCableTrayAndConduitStyleCorrection()
        {
            string fill = Source("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs");
            string batch = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string corrector = Source("src", "UNCAD", "Cad",
                "FillDrawingSettingsCorrector.cs");

            Assert.Contains("correctCableTrayConduitStyles: false", fill);
            Assert.Contains("correctCableTrayConduitStyles: false", batch);
            Assert.Contains("correctCableLabelPlacement:", fill);
            Assert.Contains("correctCableLabelPlacement:", batch);
            Assert.DoesNotContain("BridgeLabelMigrationWriter.Migrate(", fill);

            int applyStart = corrector.IndexOf(
                "internal static FillDrawingSettingsCorrectionResult Apply(",
                StringComparison.Ordinal);
            int cableMethod = corrector.IndexOf(
                "private static int CorrectCableLabels(", applyStart,
                StringComparison.Ordinal);
            int hoseMethod = corrector.IndexOf(
                "private static int CorrectHoseLabels(", cableMethod,
                StringComparison.Ordinal);
            Assert.True(applyStart >= 0 && cableMethod > applyStart
                && hoseMethod > cableMethod);
            string apply = corrector.Substring(applyStart,
                cableMethod - applyStart);

            Assert.Contains(
                "if (correctCableLabelPlacement || correctCableTrayConduitStyles)",
                apply);
            Assert.Contains("result.CableLabels = CorrectCableLabels(", apply);
            Assert.Contains("if (correctCableTrayConduitStyles)", apply);
            Assert.Contains("result.TrayRuns = CorrectParallelRuns(", apply);
            Assert.Contains("result.ConduitRuns = CorrectParallelRuns(", apply);

            string cableCorrection = corrector.Substring(cableMethod);
            Assert.Contains("correctHeight && Math.Abs(dbText.Height", cableCorrection);
            Assert.Contains("if (correctHeight) dbText.Height", cableCorrection);
            Assert.Contains("correctHeight && Math.Abs(multiText.TextHeight",
                cableCorrection);
            Assert.Contains("if (correctHeight)", cableCorrection);
            Assert.DoesNotContain("TextString =", cableCorrection);

            string stage = Source("src", "UNCAD", "Features", "Fill",
                "FillRouteInspectionStage.cs");
            Assert.Contains(
                "styleSettings.AdjacentLabelAngleToleranceRadians", stage);
        }

        private static string Source(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
        }
    }
}

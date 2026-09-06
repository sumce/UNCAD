using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineFeatureContractTests
    {
        [Fact]
        public void FastAnnotate_IsCommandLineOnlyAndTraversalDriven()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineFeature.cs"));

            // 无 GUI:不允许任何窗体/Web 依赖;逐段填写走遍历计划。
            Assert.DoesNotContain("ShowDialog", source);
            Assert.DoesNotContain("QuickLine3dEditorForm", source);
            Assert.DoesNotContain("WebView2", source);
            Assert.DoesNotContain("OpenTK", source);
            Assert.Contains("QuickLineTraversal.CreatePlan", source);
            Assert.Contains("QuickLineCadService.TryWriteSegmentMillimetre", source);
            Assert.Contains("QuickLineCadService.TryCenterView", source);
            Assert.Contains("GetEntity(", source);
            Assert.Contains("GetDouble(", source);

            // 顺序契约:先选实体,再写标注(计划经 BuildPlan 构建)。
            int pick = source.IndexOf("ctx.Ed.GetEntity(", StringComparison.Ordinal);
            int plan = source.IndexOf("BuildPlan(graph, startId", StringComparison.Ordinal);
            int create = source.IndexOf("QuickLineTraversal.CreatePlan", StringComparison.Ordinal);
            int write = source.IndexOf("TryWriteSegmentMillimetre", StringComparison.Ordinal);
            Assert.True(pick >= 0 && plan > pick);
            Assert.True(create >= 0 && write > 0);
        }

        [Fact]
        public void InteriorClick_AsksUserWhichDirectionBeforeTraversal()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineFeature.cs"));
            int interior = source.IndexOf("ClickRegion != QuickLineClickRegion.Interior",
                StringComparison.Ordinal);
            int keywords = source.IndexOf("GetKeywords(", StringComparison.Ordinal);
            Assert.True(interior >= 0 && keywords > interior,
                "An interior click must prompt for the direction before building the plan.");
        }

        [Fact]
        public void NoThreeDimensionalQuickLineRemnantsInProject()
        {
            string project = File.ReadAllText(RepoFile("src", "UNCAD", "UNCAD.csproj"));
            Assert.DoesNotContain("OpenTK", project);
            string feature = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineFeature.cs"));
            Assert.DoesNotContain("WebView2", feature);
            Assert.DoesNotContain("Line3d", feature);
            Assert.DoesNotContain("IsometricScene", feature);
            Assert.DoesNotContain("SchematicLayout", feature);
        }

        [Fact]
        public void CenterView_UsesTargetTwistAndInverseDisplayTransform()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "QuickLine", "QuickLineCadService.cs"));
            int methodStart = source.IndexOf(
                "private static Point3d ToDisplayCoordinates(",
                StringComparison.Ordinal);
            Assert.True(methodStart >= 0,
                "The WCS-to-DCS view conversion helper was not found.");

            string method = source.Substring(methodStart);
            int targetTranslation = method.IndexOf(
                "Matrix3d.Displacement(target - Point3d.Origin)",
                StringComparison.Ordinal);
            int twist = method.IndexOf(
                "Matrix3d.Rotation(-viewTwist, viewDirection, target)",
                StringComparison.Ordinal);
            int inverse = method.IndexOf("displayToWorld.Inverse()",
                StringComparison.Ordinal);

            Assert.True(targetTranslation >= 0,
                "View conversion must use the non-zero WCS target.");
            Assert.True(twist > targetTranslation,
                "View twist must be applied after target translation.");
            Assert.True(inverse > twist,
                "The assembled DCS-to-WCS transform must be inverted.");
        }

        [Fact]
        public void LabelFallback_OnlyEnumeratesLinesPresentInCandidateMap()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "QuickLine", "QuickLineCadService.cs"));
            int methodStart = source.IndexOf(
                "private static void AssignRemainingLabels(",
                StringComparison.Ordinal);
            int methodEnd = source.IndexOf(
                "private static bool TryAssignLine(", methodStart,
                StringComparison.Ordinal);
            Assert.True(methodStart >= 0 && methodEnd > methodStart,
                "AssignRemainingLabels was not found.");

            string method = source.Substring(methodStart, methodEnd - methodStart);
            Assert.Contains(
                "foreach (KeyValuePair<ObjectId, List<LabelMatch>> candidate in candidates",
                method);
            Assert.DoesNotContain("candidates[item.Id]", method);
        }

        [Fact]
        public void BatchWrite_ValidatesAndOpensEveryLabelBeforeMutation()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "QuickLine", "QuickLineCadService.cs"));
            int methodStart = source.IndexOf(
                "public static bool TryUpdateMillimetreLabels(",
                StringComparison.Ordinal);
            int methodEnd = source.IndexOf(
                "public static string FormatMillimetres(", methodStart,
                StringComparison.Ordinal);
            Assert.True(methodStart >= 0 && methodEnd > methodStart,
                "The batch update method was not found.");

            string method = source.Substring(methodStart, methodEnd - methodStart);
            int valueValidation = method.IndexOf("double.IsNaN(update.Millimetres)",
                StringComparison.Ordinal);
            int transaction = method.IndexOf("StartTransaction()",
                StringComparison.Ordinal);
            int collected = method.IndexOf("mutations.Add(mutation)",
                StringComparison.Ordinal);
            int mutation = method.IndexOf("mutation.Apply()",
                StringComparison.Ordinal);
            int commit = method.IndexOf("tr.Commit()", StringComparison.Ordinal);

            Assert.True(valueValidation >= 0 && valueValidation < transaction);
            Assert.True(collected > transaction && mutation > collected);
            Assert.True(commit > mutation);
        }

        [Fact]
        public void ScannerSupportsNativeDimensionsMTextAndBlockAttributes()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "QuickLine", "QuickLineCadService.cs"));

            Assert.Contains("entity as MText", source);
            Assert.Contains("entity as Dimension", source);
            Assert.Contains("entity as BlockReference", source);
            Assert.Contains("block.AttributeCollection", source);
            Assert.Contains("dimension.Measurement", source);
            Assert.Contains("label.WithObservation(dimension)", source);
        }

        [Fact]
        public void BatchWriteSupportsEveryScannedLabelKind()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "QuickLine", "QuickLineCadService.cs"));
            int start = source.IndexOf("private static bool TryPrepareLabelUpdate(",
                StringComparison.Ordinal);
            Assert.True(start >= 0);
            string method = source.Substring(start);

            Assert.Contains("entity as AttributeReference", method);
            Assert.Contains("entity as DBText", method);
            Assert.Contains("entity as MText", method);
            Assert.Contains("entity as Dimension", method);
            Assert.Contains("dimension.DimensionText = formatted", method);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

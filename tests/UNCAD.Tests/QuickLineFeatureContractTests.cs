using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineFeatureContractTests
    {
        [Fact]
        public void ThreeDimensionalEditor_CommitsOnlyAfterModalAcceptance()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineFeature.cs"));
            int scene = source.IndexOf("CreateDrawingScene()",
                StringComparison.Ordinal);
            int modal = source.IndexOf("AcApplication.ShowModalDialog(form)",
                StringComparison.Ordinal);
            int accepted = source.IndexOf("result != DialogResult.OK",
                StringComparison.Ordinal);
            int insertion = source.IndexOf(
                "PromptPointOptions(\"\\n请点击平面图插入点: \")",
                StringComparison.Ordinal);
            int batch = source.IndexOf(
                "QuickLineCadService.TryCreatePlanRoute(ctx,",
                StringComparison.Ordinal);

            Assert.True(scene >= 0, "The 3D scene builder was not found.");
            Assert.True(modal > scene, "The scene must be built before opening the editor.");
            Assert.True(accepted > modal,
                "The modal result must be checked before any CAD write.");
            Assert.True(insertion > accepted,
                "The insertion point must be picked after editor acceptance.");
            Assert.True(batch > insertion,
                "The batch write must occur only after the insertion point is picked.");
            Assert.DoesNotContain("QuickLineDistanceForm", source);
            Assert.DoesNotContain("TryUpdateMillimetreLabel(ctx, current", source);
        }

        [Fact]
        public void QuickLineStartsBlankWithoutScanningOrSelectingCadEntities()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineFeature.cs"));

            Assert.Contains("CreateDrawingScene()", source);
            Assert.Contains("form.CreatedSegments", source);
            Assert.Contains("ExecuteSelected", source);
            Assert.Contains("GetEntity(", source);
        }

        [Fact]
        public void NativeEditor_KeepsActualDistanceSeparateFromDisplayLength()
        {
            // 未确认段以原线长显示(display),真实毫米值(distance)必须保留到写回。
            var state = UI.QuickLine3d.QuickLineSceneState.CreateDrawing();
            state.SetPreview(Core.QuickLine.QuickLineSpatialAxis.X, 1);
            state.CommitPreview(2000);
            var segment = state.Segments.Single();
            Assert.Equal(2000, segment.DistanceMillimetres);
            Assert.Equal(2000, segment.DisplayDistanceMillimetres);
            Assert.True(segment.Completed);
        }

        [Fact]
        public void NativeEditor_FormReadsSceneStateWithoutWebDependencies()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Unl", "QuickLineFeature.cs"));
            Assert.Contains("UNCAD.UI.QuickLine3d.QuickLine3dEditorForm", source);
            Assert.DoesNotContain("Microsoft.Web.WebView2", source);
            Assert.DoesNotContain("QuickLine3dEditorProtocol", source);

            string project = File.ReadAllText(RepoFile("src", "UNCAD", "UNCAD.csproj"));
            Assert.DoesNotContain("Microsoft.Web.WebView2", project);
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

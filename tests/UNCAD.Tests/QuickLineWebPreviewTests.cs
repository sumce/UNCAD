using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Media;
using UNCAD.Core.QuickLine;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>Both renderers must receive identical metric meshes and label anchors.</summary>
    public sealed class QuickLineWebPreviewTests
    {
        [Fact]
        public void Export_ColumnAxesStayCombinedAndVisibleWhenColumnsAreHidden()
        {
            var model = new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("route", new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(0, 9600, 0), QuickLineSpatialAxis.Y, 9600, true)
            }, "", null, "48/AA", "46/AA", new QuickLineSpatialPoint(0, 0, 0),
                new QuickLineSpatialPoint(0, 9600, 0), directionResolved: true);
            var envelope = QuickLineOrbitPreviewEnvelope.Build(model,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList), "2F地面");
            var scene = QuickLineOrbitSceneBuilder.Build(envelope);
            var payload = Parse(QuickLineWebPreview.Serialize(scene, "U1U", new[] { "Columns" }));
            var labels = ((IEnumerable)payload["labels"]).Cast<Dictionary<string, object>>()
                .Where(label => (string)label["kind"] == "axis").ToArray();
            Assert.NotEmpty(labels);
            Assert.All(labels, label =>
            {
                Assert.Matches(@"^\d+/[A-Z]+$", (string)label["text"]);
                Assert.Equal("Grid", label["group"]);
                double[] point = Numbers(label["position"]).ToArray();
                Assert.Contains(scene.Primitives, column => column.Kind == QuickLine3dPrimitiveKind.Box
                    && column.Group == QuickLine3dSceneGroupKind.Columns
                    && column.A1x == point[0] && column.A1y == point[1]);
            });
            Assert.Contains(labels, label => (string)label["text"] == "48/AA");
            Assert.Equal(new object[] { "Columns" },
                ((IEnumerable)payload["hiddenGroups"]).Cast<object>());
        }

        [Fact]
        public void Export_PreservesEveryPrimitiveMeshAndCamera()
        {
            var scene = new QuickLine3dScene
            {
                Orthographic = true, Centerx = 17, Centery = -20, Centerz = 1000,
                CameraDistance = 12000, OrthoViewHeight = 7800
            };
            var group = QuickLine3dSceneGroupKind.Reference;
            scene.AddTube(group, 0, 0, 0, 0, 0, 2500, 20, Colors.Red);
            scene.AddBox(group, 1000, 1000, 500, 200, 400, 1000,
                Colors.Red, 1, Colors.Transparent);
            scene.AddQuad(group, 0, 0, 0, 100, 0, 0, 100, 100, 0, 0, 100, 0, Colors.Red, 1);
            scene.AddWaffleCell(group, 0, 0, 1600, 1600, 7200, 576, Colors.Red, 1);
            scene.AddSphere(group, 500, 500, 2500, 30, Colors.Red);
            scene.AddLabel(group, "mm", "2500mm <text>", 0, 0, 1250);

            var expected = new QuickLine3dViewportControl.MeshBuilder();
            foreach (var primitive in scene.Primitives) expected.Append(primitive);
            var mesh = expected.ToBuilt(
                new QuickLine3dViewportControl.MaterialKey(Colors.Red, 1), group).Mesh;
            var payload = Parse(QuickLineWebPreview.Serialize(scene, "U1U"));
            var raw = (Dictionary<string, object>)Assert.Single(((IEnumerable)payload["meshes"]).Cast<object>());
            AssertDoubles(mesh.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }),
                Numbers(raw["positions"]));
            AssertDoubles(mesh.Normals.SelectMany(n => new[] { n.X, n.Y, n.Z }),
                Numbers(raw["normals"]));
            Assert.Equal(mesh.TriangleIndices.Select(i => (double)i).ToArray(),
                Numbers(raw["indices"]).ToArray());
            var camera = (Dictionary<string, object>)payload["camera"];
            Assert.True((bool)camera["orthographic"]);
            Assert.Equal(new[] { 17d, -20d, 1000d }, Numbers(camera["center"]));
            var label = (Dictionary<string, object>)Assert.Single(((IEnumerable)payload["labels"]).Cast<object>());
            Assert.Equal("2500mm <text>", label["text"]);
            Assert.Equal(new[] { 0d, 0d, 1250d }, Numbers(label["position"]));
        }

        [Fact]
        public void Export_PreservesTogglesAndUsesDefaultHiddenStructure()
        {
            var scene = new QuickLine3dScene();
            var defaults = Parse(QuickLineWebPreview.Serialize(scene, "U13D"));
            Assert.Equal(new object[] { "Detail" }, ((IEnumerable)defaults["hiddenGroups"]).Cast<object>());
            var current = Parse(QuickLineWebPreview.Serialize(scene, "U13D",
                new[] { "Grid", "Reference" }, new[] { "route", "directionUp" }));
            Assert.Equal(new object[] { "Grid", "Reference" }, ((IEnumerable)current["hiddenGroups"]).Cast<object>());
            Assert.Equal(new object[] { "route", "directionUp" }, ((IEnumerable)current["hiddenLabelKinds"]).Cast<object>());
        }

        private static Dictionary<string, object> Parse(string json)
            => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);

        private static IEnumerable<double> Numbers(object values)
            => ((IEnumerable)values).Cast<object>().Select(Convert.ToDouble);

        private static void AssertDoubles(IEnumerable<double> expected,
            IEnumerable<double> actual)
        {
            double[] left = expected.ToArray();
            double[] right = actual.ToArray();
            Assert.Equal(left.Length, right.Length);
            for (int index = 0; index < left.Length; index++)
                Assert.Equal(left[index], right[index], 9);
        }
    }
}

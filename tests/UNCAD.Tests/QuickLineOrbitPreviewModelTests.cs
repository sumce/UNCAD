using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.0（D-054 特性③）引入、3.3.2（D-054 Amendment）随渲染内核切换保留的
    /// 数据模型锁死——FromScene 地面归零（minZ=0、高度差不变、原场景不改）与
    /// QuickLineAxisSpan 轴位解析/摘要文案。正交轨道相机 QuickLineOrbitCamera
    /// 的 7 条投影数学测试随 Canvas 内核退役（3.3.2 换 Helix、3.3.5 换 WebView2 透视
    /// 相机，投影由 WPF 3D 管线承担，不再自维护相机数学）。
    /// </summary>
    public sealed class QuickLineOrbitPreviewModelTests
    {
        [Fact]
        public void FromScene_GroundNormalizesSoMinimumZIsZero()
        {
            // 连通链：水平 1000mm + 竖直向下 4000mm——重建后末端 Z=-4000，
            // 地面归零应把最低端点抬到 0（整体平移，段内高度差不变）。
            var graph = QuickLineGraph.Build(new List<QuickLineSegment>
            {
                new QuickLineSegment("h", new QuickLinePoint(0, 0),
                    new QuickLinePoint(1000, 0)),
                new QuickLineSegment("v", new QuickLinePoint(1000, 0),
                    new QuickLinePoint(1000, -4000))
            }, 1.0);
            var distances = new Dictionary<string, double>
            {
                ["h"] = 1000.0,
                ["v"] = 4000.0
            };
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(-4000.0, scene.Nodes.Min(node => node.Position.Z), 6);

            QuickLineOrbitPreviewModel model =
                QuickLineOrbitPreviewModel.FromScene(scene, "轴位摘要");
            Assert.NotNull(model);
            Assert.Equal(2, model.Segments.Count);
            double minZ = model.Segments.Min(segment =>
                Math.Min(segment.Start.Z, segment.End.Z));
            Assert.Equal(0.0, minZ, 6);
            Assert.Equal("轴位摘要", model.AxisSummary);
            // 地面归零是整体平移：竖直段的 4000mm 高差保持不变。
            QuickLineOrbitPreviewSegment vertical = model.Segments.Single(item =>
                item.Axis == QuickLineSpatialAxis.Z);
            Assert.Equal(4000.0, Math.Abs(vertical.Start.Z - vertical.End.Z), 6);
            // 原场景不被修改。
            Assert.Equal(-4000.0, scene.Nodes.Min(node => node.Position.Z), 6);
        }

        [Fact]
        public void FromScene_EmptySceneReturnsNull()
        {
            Assert.Null(QuickLineOrbitPreviewModel.FromScene(null, ""));
            var empty = new QuickLineIsometricScene("N1",
                QuickLineProjectionMode.Isometric,
                new List<QuickLineIsometricNode>(),
                new List<QuickLineIsometricSegment>(), new List<string>());
            Assert.Null(QuickLineOrbitPreviewModel.FromScene(empty, ""));
        }

        [Fact]
        public void AxisSpan_ParsesTolerantFormsAndDescribesSpan()
        {
            Assert.True(QuickLineAxisSpan.TryParse("48/V",
                out QuickLineAxisReference full));
            Assert.Equal(48, full.Number);
            Assert.Equal("V", full.Letter);
            Assert.True(QuickLineAxisSpan.TryParse("48-V",
                out QuickLineAxisReference dashed));
            Assert.Equal(48, dashed.Number);
            Assert.Equal("V", dashed.Letter);
            Assert.True(QuickLineAxisSpan.TryParse("48", out QuickLineAxisReference numeric));
            Assert.True(numeric.HasNumber);
            Assert.False(numeric.HasLetter);
            Assert.True(QuickLineAxisSpan.TryParse(" V ", out QuickLineAxisReference letters));
            Assert.False(letters.HasNumber);
            Assert.Equal("V", letters.Letter);

            Assert.False(QuickLineAxisSpan.TryParse("", out _));
            Assert.False(QuickLineAxisSpan.TryParse("48V", out _),
                "数字+字母无分隔符不猜");
            Assert.False(QuickLineAxisSpan.TryParse("48/V/x", out _));
            Assert.False(QuickLineAxisSpan.TryParse("轴A", out _));
            Assert.True(QuickLineAxisSpan.TryParse("ABC",
                out QuickLineAxisReference multiLetter));
            Assert.Equal("ABC", multiLetter.Letter);

            // 展示文案：两端数字轴可算跨度（48−37=11 跨 × 4.8m = 52.8m）。
            string described = QuickLineAxisSpan.Describe("48/V", "37/V");
            Assert.StartsWith("上游 48/V ↔ 下游 37/V", described, StringComparison.Ordinal);
            Assert.Contains("Δ11 跨 × 4.8m ≈ 52.8m", described, StringComparison.Ordinal);
            // 解析失败：显示原文，不算跨度（不猜测）。
            string raw = QuickLineAxisSpan.Describe("轴A", "37/V");
            Assert.Equal("上游 轴A ↔ 下游 37/V", raw);
            // 字母轴也能给跨度（V=22、P=16，差 6 跨）。
            string letterOnly = QuickLineAxisSpan.Describe("V", "P");
            Assert.Contains("Δ6 跨", letterOnly, StringComparison.Ordinal);
            // 半边值（一端只有数字、另一端只有字母）不算跨度。
            Assert.DoesNotContain("跨", QuickLineAxisSpan.Describe("48", "V"),
                StringComparison.Ordinal);
        }
    }
}

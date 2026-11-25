using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.5（D-054 Amendment）：U1U 3D 预览**渲染信封**（QuickLineOrbitPreviewEnvelope）
    /// 的几何与序列化锁死。信封是 WebView2+three.js 内核的唯一数据源——场景语义
    /// （柱网规划/通道双线偏移/标签锚点）全部在 Core 算死，页面只画；几何口径与
    /// 3.3.2 Helix 控件逐条对齐（那套公式被窗口数据级测试锁定过，此处逐值再锁）。
    /// 序列化键名（JavaScriptSerializer 的 PascalCase 属性名）是 C#/JS 两端的
    /// 契约——Web/QuickLineOrbit/app.js 按这些键取数，改名必须两侧同步。
    /// </summary>
    public sealed class QuickLineOrbitPreviewEnvelopeTests
    {
        private static QuickLineOrbitPreviewModel FullModel()
            => new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 0),
                    QuickLineSpatialAxis.X, 9600, true, "9600mm",
                    new QuickLineOrbitPreviewChannel(400.0, false, false)),
                new QuickLineOrbitPreviewSegment("s2",
                    new QuickLineSpatialPoint(9600, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 2500),
                    QuickLineSpatialAxis.Z, 2500, true, "2500mm",
                    new QuickLineOrbitPreviewChannel(null, false, true))
            }, "上游 48/V ↔ 下游 37/V", new List<string>(), " 48/V ", " 37/V ",
                new QuickLineSpatialPoint(0, 0, 0),
                new QuickLineSpatialPoint(9600, 0, 2500),
                directionResolved: true);

        [Fact]
        public void Build_NullOrEmptyModel_ReturnsNull()
        {
            Assert.Null(QuickLineOrbitPreviewEnvelope.Build(null));
            Assert.Null(QuickLineOrbitPreviewEnvelope.Build(
                new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>(),
                    "", null)));
        }

        [Fact]
        public void Build_SegmentsAndChannels_MappedWithRailGeometry()
        {
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(FullModel());
            Assert.NotNull(envelope);
            Assert.Equal(2, envelope.Segments.Count);
            Assert.Equal("X", envelope.Segments[0].Axis);
            Assert.Equal("Z", envelope.Segments[1].Axis);

            // 桥架段（X 向，宽 400 → 偏移=200 > 对角线 0.0012≈11.9）：
            // 法向 = dir×ẑ = (0,-1)（世界 Y 负向一侧为 a）。
            QuickLineOrbitPreviewEnvelope.EnvelopeChannel tray =
                envelope.Segments[0].Channel;
            Assert.NotNull(tray);
            Assert.Equal("tray", tray.Kind);
            Assert.Equal(0.0, tray.A1.X, 6);
            Assert.Equal(-200.0, tray.A1.Y, 6);
            Assert.Equal(9600.0, tray.A2.X, 6);
            Assert.Equal(-200.0, tray.A2.Y, 6);
            Assert.Equal(0.0, tray.B1.X, 6);
            Assert.Equal(200.0, tray.B1.Y, 6);
            Assert.Equal(9600.0, tray.B2.X, 6);
            Assert.Equal(200.0, tray.B2.Y, 6);

            // 线管段（Z 向竖直：水平法向退化回 ŷ）；偏移=对角线 0.0006≈5.95。
            QuickLineOrbitPreviewEnvelope.EnvelopeChannel conduit =
                envelope.Segments[1].Channel;
            Assert.NotNull(conduit);
            Assert.Equal("conduit", conduit.Kind);
            Assert.Equal(9600.0, conduit.A1.X, 4);
            Assert.True(conduit.A1.Y > 5.0 && conduit.A1.Y < 7.0,
                "线管偏移=对角线 0.0006（≈5.95）");
            Assert.Equal(2500.0, conduit.A2.Z, 4);

            // 缺省宽度旗标：FullModel 的 400 是显式宽度 → false。
            Assert.False(envelope.AnyTrayWidthAssumedDefault);
        }

        [Fact]
        public void Build_AssumedDefaultTrayWidth_Flagged()
        {
            var model = new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(4000, 0, 0),
                    QuickLineSpatialAxis.X, 4000, true, null,
                    new QuickLineOrbitPreviewChannel(200.0, true, false))
            }, "", null);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model);
            Assert.True(envelope.AnyTrayWidthAssumedDefault);
        }

        [Fact]
        public void Build_LabelsAndAxisText_MappedAndTrimmed()
        {
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(FullModel());
            // 特性B：两条段毫米标。3.8.0.2 起文字贴在线上——锚点就是段中点，
            // 并带上这一段的方向（叠加层沿线排字、固定在线旁，不再抬到管子上方）。
            Assert.Equal(2, envelope.SegmentLabels.Count);
            Assert.Equal("9600mm", envelope.SegmentLabels[0].Text);
            Assert.Equal("2500mm", envelope.SegmentLabels[1].Text);
            QuickLineOrbitPreviewEnvelope.EnvelopeLabel horizontal = envelope.SegmentLabels[0];
            Assert.Equal(4800.0, horizontal.X, 6);
            Assert.Equal(0.0, horizontal.Y, 6);
            Assert.Equal(0.0, horizontal.Z, 6);
            Assert.True(horizontal.HasDirection);
            Assert.Equal(9600.0, horizontal.DirectionX, 6);
            Assert.Equal(0.0, horizontal.DirectionY, 6);
            QuickLineOrbitPreviewEnvelope.EnvelopeLabel riser = envelope.SegmentLabels[1];
            Assert.Equal(1250.0, riser.Z, 6);
            Assert.True(riser.HasDirection);
            Assert.Equal(0.0, riser.DirectionX, 6);
            Assert.Equal(2500.0, riser.DirectionZ, 6);
            // 轴位原文透传（首尾空白剥掉）。
            Assert.Equal("48/V", envelope.UpstreamAxisLabel);
            Assert.Equal("37/V", envelope.DownstreamAxisLabel);
            Assert.Equal("上游 48/V ↔ 下游 37/V", envelope.AxisSummary);
        }

        [Fact]
        public void Build_ColumnGridAndEndpointPins_PlannedIntoEnvelope()
        {
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(FullModel());
            // 特性C（3.3.2 两向柱网）：网格线端点对 + 数字刻度（48→37 沿 X）+
            // 字母标签（V 锚定）+ 两端端点钉（引线+标签）。
            Assert.True(envelope.ColumnGridLines.Count >= 6,
                "柱网线端点对至少覆盖包络（X/Y 两向）");
            Assert.True(envelope.NumericTicks.Count >= 2,
                "两端数字轴可解析时数字刻度要带");
            Assert.Contains("48", envelope.NumericTicks[0].Text);
            Assert.True(envelope.LetterLabels.Count >= 1,
                "两端字母轴可解析时字母标签要带");
            Assert.Equal(2, envelope.EndpointPins.Count);
            Assert.Contains(envelope.EndpointPins, pin => pin.Text == "48/V");
            Assert.Contains(envelope.EndpointPins, pin => pin.Text == "37/V");
            foreach (QuickLineOrbitPreviewEnvelope.EnvelopePin pin
                in envelope.EndpointPins)
            {
                Assert.True(pin.TipZ > pin.Z, "端点钉是竖直向上的引线");
                Assert.True(pin.LabelZ > pin.TipZ, "标签在引线顶端之上");
            }
        }

        [Fact]
        public void Build_AxisLabelsArePlacedOnCenterLinesOfGrid()
        {
            var model = new QuickLineOrbitPreviewModel(
                new List<QuickLineOrbitPreviewSegment>
                {
                    new QuickLineOrbitPreviewSegment("lower",
                        new QuickLineSpatialPoint(0, 0, 0),
                        new QuickLineSpatialPoint(9600, 0, 0),
                        QuickLineSpatialAxis.X, 9600, true),
                    new QuickLineOrbitPreviewSegment("upper",
                        new QuickLineSpatialPoint(0, 9600, 0),
                        new QuickLineSpatialPoint(9600, 9600, 0),
                        QuickLineSpatialAxis.X, 9600, true)
                }, "", null, "48/V", "37/T",
                new QuickLineSpatialPoint(0, 0, 0),
                new QuickLineSpatialPoint(9600, 9600, 0),
                directionResolved: true);

            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model);

            Assert.All(envelope.NumericTicks,
                label => Assert.Equal(4800.0, label.Y));
            Assert.All(envelope.LetterLabels,
                label => Assert.Equal(4800.0, label.X));
        }

        [Fact]
        public void Build_DegenerateZeroDirectionSegment_ChannelDropped()
        {
            var model = new QuickLineOrbitPreviewModel(new List<QuickLineOrbitPreviewSegment>
            {
                new QuickLineOrbitPreviewSegment("dot",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(0, 0, 0),
                    QuickLineSpatialAxis.X, 0, false, null,
                    new QuickLineOrbitPreviewChannel(400.0, false, false))
            }, "", null);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model);
            Assert.NotNull(envelope);
            // 零长度段：段保留（线渲染端会跳过），通道双线无意义 → null。
            Assert.Single(envelope.Segments);
            Assert.Null(envelope.Segments[0].Channel);
        }

        [Fact]
        public void Build_UnresolvedDirectionKeepsGridButHidesAxisPinsAndTicks()
        {
            var model = new QuickLineOrbitPreviewModel(
                new List<QuickLineOrbitPreviewSegment>
                {
                    new QuickLineOrbitPreviewSegment("s1",
                        new QuickLineSpatialPoint(9600, 0, 0),
                        new QuickLineSpatialPoint(0, 9600, 0),
                        QuickLineSpatialAxis.X, 13576, true)
                }, "上游 bad ↔ 下游 47/U", new List<string>(),
                "bad", "47/U", directionDiagnostics: new[]
                {
                    "轴位格式无效，无法确认上下游。"
                });

            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model);

            Assert.False(envelope.DirectionResolved);
            Assert.NotNull(envelope.GridAnchor);
            Assert.NotEmpty(envelope.ColumnGridLines);
            Assert.Empty(envelope.EndpointPins);
            Assert.Empty(envelope.NumericTicks);
            Assert.Empty(envelope.LetterLabels);
            Assert.Contains("轴位格式无效", envelope.DirectionDiagnostics[0],
                StringComparison.Ordinal);
        }

        [Fact]
        public void Serialize_PascalCaseKeys_AreTheJsContract()
        {
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(FullModel());
            string json = new JavaScriptSerializer
            {
                MaxJsonLength = 16 * 1024 * 1024
            }.Serialize(envelope);
            // C#/JS 契约：JavaScriptSerializer 按 C# 属性名输出（PascalCase），
            // Web/QuickLineOrbit/app.js 按这些键取数——改名必须两侧同步。
            foreach (string key in new[]
            {
                "\"Segments\"", "\"ColumnGridLines\"", "\"NumericTicks\"",
                "\"LetterLabels\"", "\"SegmentLabels\"", "\"EndpointPins\"",
                "\"Diagonal\"", "\"AnyTrayWidthAssumedDefault\"",
                "\"AxisSummary\"", "\"tray\""
            })
            {
                Assert.Contains(key, json, StringComparison.Ordinal);
            }
            // 序列化结果可整体解析回字典（宿主发给页面的就是这串）。
            var payload = new Dictionary<string, object>
            {
                { "type", "initialize" }, { "schemaVersion", 1 },
                { "envelope", envelope }
            };
            string payloadJson = new JavaScriptSerializer().Serialize(payload);
            var decoded = new JavaScriptSerializer().DeserializeObject(payloadJson)
                as Dictionary<string, object>;
            Assert.NotNull(decoded);
            Assert.Equal("initialize", Convert.ToString(decoded["type"]));
            Assert.True(decoded["envelope"] is Dictionary<string, object>);
        }
    }
}

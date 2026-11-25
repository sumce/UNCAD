using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Core.Contracts;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Features.Unl;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-058 锁死：U1UL 的归位规划（只改距离和角度，所在侧与沿线位置保持）、
    /// 预览信封/场景里的软管与两端标记、窗格的问题提示、识别摘要与问题文案、
    /// U1UL 的源码契约（只动带关联元数据的毫米标，一个事务，不碰文字和字高）。
    /// </summary>
    public sealed class RoutePreviewRecognitionTests
    {
        private static QuickLinePoint P(double x, double y) => new QuickLinePoint(x, y);

        // ---- U1UL 归位规划 ------------------------------------------------------

        [Fact]
        public void Label_AboveTheLine_StaysAboveAtTheConfiguredOffset()
        {
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, 300), 0.0, 50.0, true);
            Assert.Equal(2000.0, placement.AnchorX, 6);
            Assert.Equal(50.0, placement.AnchorY, 6);
            Assert.Equal(0.0, placement.Rotation, 9);
            Assert.Equal(QuickLineLabelSide.Left, placement.Side);
            Assert.False(placement.ReadsReversed);
        }

        [Fact]
        public void Label_MovedToTheOtherSide_StaysOnThatSide()
        {
            // 用户把毫米标挪到线下方：归位后还在下方，只是距离变成偏移值。
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, -300), 0.0, 50.0, true);
            Assert.Equal(2000.0, placement.AnchorX, 6);
            Assert.Equal(-50.0, placement.AnchorY, 6);
            Assert.Equal(QuickLineLabelSide.Right, placement.Side);
            Assert.Equal(0.0, placement.Rotation, 9);
        }

        [Fact]
        public void Label_DraggedAlongTheLine_KeepsItsAlongPosition()
        {
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(3500, 300), 0.0, 50.0, true);
            Assert.Equal(3500.0, placement.AnchorX, 6);
            Assert.Equal(50.0, placement.AnchorY, 6);
        }

        [Fact]
        public void Label_BeyondTheEnd_IsClampedToTheSegment()
        {
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(4500, 300), 0.0, 50.0, true);
            Assert.Equal(4000.0, placement.AnchorX, 6);
        }

        [Fact]
        public void Label_Angle_BecomesParallelToTheLine()
        {
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, 300), 0.17, 50.0, true);
            Assert.Equal(0.0, placement.Rotation, 9);
            Assert.False(placement.ReadsReversed);
        }

        [Fact]
        public void Label_ReadingUpsideDown_KeepsReadingThatWay()
        {
            // 文字原本倒着读（180°）：归位后仍倒读，且留在原来那一侧（线下方）。
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, -300), Math.PI, 50.0, true);
            Assert.True(placement.ReadsReversed);
            Assert.Equal(Math.PI, Math.Abs(placement.Rotation), 9);
            Assert.Equal(2000.0, placement.AnchorX, 6);
            Assert.Equal(-50.0, placement.AnchorY, 6);
        }

        [Fact]
        public void Label_RotatedPastNinetyDegrees_IsFlippedToTheNearestParallelReading()
        {
            // 旋转 114.6°：离「倒读」更近，按倒读平行于线段；文字原在线上方，仍在线上方。
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, 300), 2.0, 50.0, true);
            Assert.True(placement.ReadsReversed);
            Assert.Equal(2000.0, placement.AnchorX, 6);
            Assert.Equal(50.0, placement.AnchorY, 6);
        }

        [Fact]
        public void LineDrawnRightToLeft_StillReadsLeftToRight()
        {
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(4000, 0), P(0, 0), P(2000, 300), 0.0, 50.0, true);
            Assert.Equal(0.0, placement.Rotation, 9);
            Assert.Equal(50.0, placement.AnchorY, 6);
        }

        [Fact]
        public void VerticalLine_KeepsTheSideItIsOn()
        {
            QuickLineLabelPlacement left = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(0, 4000), P(-300, 2000), Math.PI / 2, 50.0, true);
            Assert.Equal(-50.0, left.AnchorX, 6);
            Assert.Equal(2000.0, left.AnchorY, 6);
            Assert.Equal(Math.PI / 2, left.Rotation, 9);

            QuickLineLabelPlacement right = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(0, 4000), P(300, 2000), Math.PI / 2, 50.0, true);
            Assert.Equal(50.0, right.AnchorX, 6);
            Assert.Equal(2000.0, right.AnchorY, 6);
        }

        [Fact]
        public void Label_OnTheLine_UsesTheDefaultSide()
        {
            QuickLineLabelPlacement above = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, 0), 0.0, 60.0, true);
            Assert.Equal(QuickLineLabelSide.OnLine, above.Side);
            Assert.Equal(60.0, above.AnchorY, 6);

            QuickLineLabelPlacement below = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(2000, 0), 0.0, 60.0, false);
            Assert.Equal(-60.0, below.AnchorY, 6);
        }

        [Fact]
        public void Centered_ZeroOffset_LandsOnTheLine()
        {
            QuickLineLabelPlacement placement = QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(4000, 0), P(1200, 300), 0.0, 0.0, true);
            Assert.Equal(1200.0, placement.AnchorX, 6);
            Assert.Equal(0.0, placement.AnchorY, 6);
        }

        [Theory]
        [InlineData(-100, 0, 0)]
        [InlineData(0, 100, 90)]
        [InlineData(100, -100, -45)]
        public void ReadableAngle_MatchesTheU1lConvention(double dx, double dy,
            double degrees)
        {
            Assert.Equal(degrees * Math.PI / 180.0,
                QuickLineLabelPlacementPlanner.ReadableAngle(P(0, 0), P(dx, dy)), 9);
        }

        [Fact]
        public void InvalidInput_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => QuickLineLabelPlacementPlanner.Plan(
                P(0, 0), P(0, 0), P(1, 1), 0.0, 50.0, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuickLineLabelPlacementPlanner.Plan(P(0, 0), P(100, 0), P(50, 10), 0.0, -1.0, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuickLineLabelPlacementPlanner.Plan(P(0, 0), P(100, 0), P(50, 10),
                    double.NaN, 10.0, true));
        }

        // ---- 预览信封 / 场景 ------------------------------------------------------

        private static QuickLineOrbitPreviewModel ResolvedModel(bool resolved = true,
            QuickLineOrbitPreviewHose hose = null, string summary = "",
            IReadOnlyList<string> issues = null)
            => new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(3000, 0, 0),
                    QuickLineSpatialAxis.X, 3000, true, "3000mm"),
                new QuickLineOrbitPreviewSegment("s2",
                    new QuickLineSpatialPoint(3000, 0, 0),
                    new QuickLineSpatialPoint(6000, 0, 0),
                    QuickLineSpatialAxis.X, 3000, true, "3000mm",
                    new QuickLineOrbitPreviewChannel(200.0, false, false))
            }, "上游 48/V ↔ 下游 47/V", new List<string>(), "48/V", "47/V",
                resolved ? new QuickLineSpatialPoint?(new QuickLineSpatialPoint(0, 0, 0)) : null,
                resolved ? new QuickLineSpatialPoint?(new QuickLineSpatialPoint(6000, 0, 0)) : null,
                directionResolved: resolved,
                hose: hose, recognitionSummary: summary, routeIssues: issues);

        [Fact]
        public void Envelope_HoseContinuesFromTheDeviceEndAlongTheLastSegment()
        {
            QuickLineOrbitPreviewEnvelope envelope = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(hose: new QuickLineOrbitPreviewHose(2500.0, 100.0, "Ruanguan 块", false)));

            Assert.NotNull(envelope.Hose);
            Assert.Equal(6000.0, envelope.Hose.Start.X, 6);
            Assert.Equal(8500.0, envelope.Hose.End.X, 6);
            Assert.Equal(2500.0, envelope.Hose.LengthMillimetres, 6);
            Assert.Equal("软管 ⌀100 · 2.5m", envelope.Hose.Label);
        }

        [Fact]
        public void Envelope_HoseLabelFlagsU1BText_AndIsHiddenWhenDirectionIsUnknown()
        {
            QuickLineOrbitPreviewEnvelope byLabel = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(hose: new QuickLineOrbitPreviewHose(2500.0, null, "软管:2500mm", true)));
            Assert.Equal("软管 · 2.5m（U1B 标注）", byLabel.Hose.Label);

            QuickLineOrbitPreviewEnvelope unresolved = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(resolved: false,
                    hose: new QuickLineOrbitPreviewHose(2500.0, 100.0, "Ruanguan 块", false)));
            // 设备端未确认：软管放不出来，但摘要里仍报长度。
            Assert.Null(unresolved.Hose);
        }

        [Fact]
        public void Envelope_CarriesRecognitionSummaryAndIssues()
        {
            QuickLineOrbitPreviewEnvelope envelope = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(summary: "识别 2 段带毫米标线段 · 桥架 1 段",
                    issues: new[] { "1 段线段没有毫米标：h9" }));
            Assert.Equal("识别 2 段带毫米标线段 · 桥架 1 段", envelope.RecognitionSummary);
            Assert.Equal(new[] { "1 段线段没有毫米标：h9" }, envelope.RouteIssues);
        }

        [Fact]
        public void Scene_DrawsHoseTubeAndEndMarkersOnlyWhenResolved()
        {
            QuickLineOrbitPreviewEnvelope resolved = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(hose: new QuickLineOrbitPreviewHose(2500.0, 100.0, "Ruanguan 块", false)));
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(resolved);

            QuickLine3dPrimitive hose = Assert.Single(scene.Primitives,
                primitive => primitive.Kind == QuickLine3dPrimitiveKind.Tube
                    && Math.Abs(primitive.A1x - 6000) < 1e-6
                    && Math.Abs(primitive.B1x - 8500) < 1e-6);
            Assert.Equal(QuickLine3dSceneGroupKind.Routes, hose.Group);
            Assert.Equal(2, scene.Primitives.Count(primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.Sphere
                && primitive.Group == QuickLine3dSceneGroupKind.Routes));
            Assert.Contains(scene.Labels, label => label.Text == "上游端  48/V"
                && label.Kind == "directionUp");
            Assert.Contains(scene.Labels, label => label.Text == "设备端  47/V"
                && label.Kind == "directionDown");
            Assert.Contains(scene.Labels, label => label.Text == "软管 ⌀100 · 2.5m"
                && label.Kind == "hose");

            QuickLine3dScene unknown = QuickLineOrbitSceneBuilder.Build(
                QuickLineOrbitPreviewEnvelope.Build(ResolvedModel(resolved: false,
                    hose: new QuickLineOrbitPreviewHose(2500.0, 100.0, "Ruanguan 块", false))));
            Assert.DoesNotContain(unknown.Primitives, primitive =>
                primitive.Kind == QuickLine3dPrimitiveKind.Sphere
                && primitive.Group == QuickLine3dSceneGroupKind.Routes);
            Assert.DoesNotContain(unknown.Labels, label =>
                label.Kind == "directionUp" || label.Kind == "directionDown");
            Assert.DoesNotContain(unknown.Labels, label => label.Kind == "hose");
        }

        [Fact]
        public void SceneTexts_MentionHoseAndEnds()
        {
            QuickLineOrbitPreviewEnvelope resolved = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(true, new QuickLineOrbitPreviewHose(2500.0, 100.0, "x", false),
                    "识别 2 段带毫米标线段", new string[0]));
            string legend = QuickLineOrbitSceneBuilder.BuildLegend(resolved);
            Assert.Contains("绿管=软管(示意)", legend, StringComparison.Ordinal);
            Assert.Contains("琥珀=上游端 绿=设备端", legend, StringComparison.Ordinal);
            Assert.StartsWith("识别 2 段带毫米标线段", QuickLineOrbitSceneBuilder.BuildSummary(resolved),
                StringComparison.Ordinal);
            Assert.DoesNotContain("左键拖动", QuickLineOrbitSceneBuilder.BuildSummary(resolved),
                StringComparison.Ordinal);

            QuickLineOrbitPreviewEnvelope plain = QuickLineOrbitPreviewEnvelope.Build(
                ResolvedModel(false));
            Assert.DoesNotContain("软管", QuickLineOrbitSceneBuilder.BuildLegend(plain),
                StringComparison.Ordinal);
            Assert.Contains("上下游未确认：", QuickLineOrbitSceneBuilder.BuildSummary(plain),
                StringComparison.Ordinal);
        }

        // ---- 识别摘要与问题文案 ------------------------------------------------------

        [Fact]
        public void RecognitionSummary_CountsTrayConduitHoseAndGaps()
        {
            var inspection = new FillRouteInspection();
            inspection.Channels["s1"] = new QuickLineOrbitPreviewChannel(200.0, false, false);
            inspection.Channels["s2"] = new QuickLineOrbitPreviewChannel(null, false, true);
            inspection.Connectivity = UNCAD.Core.QuickLine.CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    new CircuitConnectivityAnalyzer.InputSegment("s1",
                        new QuickLinePoint(0, 0), new QuickLinePoint(3000, 0)),
                    new CircuitConnectivityAnalyzer.InputSegment("s2",
                        new QuickLinePoint(3030, 0), new QuickLinePoint(6000, 0))
                }, 1.0, id => true, 50.0);

            // D-059：桥架/线管按「趟」计，长度取各趟标注上的整趟总长（不是逐段相加）。
            inspection.AnnotationRuns.Add(new FillAnnotationRun
            {
                Kind = RouteAnnotationTextKind.Tray, Label = "梯形桥架400Wx200H",
                LengthMillimetres = 19200.0
            });
            inspection.AnnotationRuns.Add(new FillAnnotationRun
            {
                Kind = RouteAnnotationTextKind.Conduit, Label = "镀锌穿线管38mm",
                LengthMillimetres = 9600.0
            });
            inspection.AnnotationRuns.Add(new FillAnnotationRun
            {
                Kind = RouteAnnotationTextKind.Tray, Label = "桥架 未标长度"
            });
            string summary = FillRouteRecognitionStage.BuildSummary(inspection, 2,
                "上游端/设备端＝离上游块/设备块最近的路线末端",
                new QuickLineOrbitPreviewHose(2500.0, 100.0, "x", false));
            Assert.Equal("识别 2 段带毫米标线段（容错接上 1 处断口） · 桥架 2 趟 共 19.2m（1 趟未标长度）"
                + " · 线管 1 趟 共 9.6m · 软管 2.5m · 上游端/设备端＝离上游块/设备块最近的路线末端",
                summary);
            Assert.EndsWith(" · 软管 — · x",
                FillRouteRecognitionStage.BuildSummary(inspection, 2, "x", null),
                StringComparison.Ordinal);
        }

        [Fact]
        public void DrawingIssues_NameUnlabelledLinesGapsTextBindingsAndOrphans()
        {
            var inspection = new FillRouteInspection();
            inspection.UnlabelledSegmentHandles.AddRange(new[] { "h1", "h2" });
            inspection.OrphanAnnotations.Add("桥架200*100 2500mm");
            inspection.Connectivity = CircuitConnectivityAnalyzer.Analyze(
                new[]
                {
                    new CircuitConnectivityAnalyzer.InputSegment("a",
                        new QuickLinePoint(0, 0), new QuickLinePoint(3000, 0)),
                    new CircuitConnectivityAnalyzer.InputSegment("b",
                        new QuickLinePoint(3030, 0), new QuickLinePoint(6000, 0))
                }, 1.0, id => true, 50.0);
            inspection.TextBoundAnnotations.AddRange(RouteAnnotationTextPlanner.Bind(
                new[]
                {
                    new RouteAnnotationTextStamp("t1", "⌀20线管 2000mm",
                        new QuickLinePoint(2000, 30), 0.0, false, RouteAnnotationTextKind.Conduit)
                }, new[]
                {
                    new QuickLineSegment("s1", new QuickLinePoint(0, 0), new QuickLinePoint(4000, 0))
                }));

            List<string> issues = FillRouteRecognitionStage.BuildDrawingIssues(inspection);
            Assert.Equal(new[]
            {
                "2 段线段没有毫米标（是电缆线请用 U1LX 补标；不是电缆线请忽略）：h1、h2",
                "断口容差接上 1 处（看着连着、放大有缝）：a–b 间隙 30",
                "线管标注「⌀20线管 2000mm」没有归属元数据，已就近绑定到线段 s1（只算这一段）",
                "桥架/线管标注「桥架200*100 2500mm」找不到宿主线段（旁边没有红线，或红线旁边没有带毫米标的电缆线），未显示"
            }, issues);
        }

        // ---- U1UL 源码契约 --------------------------------------------------------

        [Fact]
        public void U1UL_OnlyMovesExplicitlyAssociatedLabelsInOneTransaction()
        {
            string service = Source("src", "UNCAD", "Cad", "QuickLine",
                "QuickLineLabelAlignService.cs");
            // 只认带关联元数据的毫米标（扫描器只按位置匹配到的不动）。
            Assert.Contains("QuickLineMetadataStore", service);
            Assert.Contains(".ReadAssociatedLineHandle(entity)", service);
            Assert.Contains("string.Equals(owner, handle,", service);
            Assert.Contains("QuickLineLabelPlacementPlanner.Plan(", service);
            // 一个事务；任一目标算不出来就整体放弃。
            Assert.Equal(1, Count(service, "StartTransaction()"));
            Assert.Contains("if (edit == null) return null;", service);
            // 只动位置/角度/对齐：不改文字、不改字高。
            Assert.DoesNotContain("TextString =", service);
            Assert.DoesNotContain("Contents =", service);
            Assert.DoesNotContain(".Height =", service);
            Assert.DoesNotContain("TextHeight =", service);
            Assert.Contains("AdjustAlignment(ctx.Db)", service);
        }

        [Fact]
        public void U1UL_CommandIsRegisteredDocumentedAndOnTheRibbon()
        {
            Assert.Equal("U1UL", CommandIds.LineLabelAlign);
            Assert.Equal("U1UL", CommandIds.LineLabelAlignFeatureCommands);
            FeatureAttribute feature = (FeatureAttribute)Attribute.GetCustomAttribute(
                typeof(QuickLineLabelAlignFeature), typeof(FeatureAttribute), false);
            Assert.NotNull(feature);
            Assert.Equal("U1UL", feature.Commands);

            CommandHelpEntry help = CommandHelpCatalog.Find("U1UL");
            Assert.NotNull(help);
            Assert.Equal("毫米标归位", help.Name);
            Assert.Contains("哪一侧就留在哪一侧", help.Notes, StringComparison.Ordinal);

            string align = Source("src", "UNCAD", "Features", "Unl",
                "QuickLineLabelAlignFeature.cs");
            Assert.Contains("[CommandMethod(CommandIds.LineLabelAlign)]", align);
            Assert.Contains("ProductMetadata.EnsureCommandAllowed(CommandIds.LineLabelAlign)", align);
            Assert.Contains("QuickLineLabelAlignService.Apply(", align);
            // 选图框＝处理整框。
            Assert.Contains("FrameRegionCollector.Collect(ctx, selected)", align);

            Assert.Contains(RibbonCatalog.CommandItems,
                item => item.Command == CommandIds.LineLabelAlign);
            Assert.Contains("<Command Global=\"U1UL\" />",
                Source("bundle", "UNCAD.bundle", "PackageContents.xml"));
            Assert.Contains("\"U1UL\"", Source("installer.ps1"));
        }

        private static int Count(string source, string fragment)
        {
            int count = 0;
            for (int index = source.IndexOf(fragment, StringComparison.Ordinal);
                index >= 0;
                index = source.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static string Source(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
        }
    }
}

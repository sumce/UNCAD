using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.Frame;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_ROUTE_PREVIEW_SELFTEST（3.7.0，D-058 U1U 3D 预览数据链真机自检）：
    /// 在真图纸里画三段带毫米标的正交路线（其中一处拐角差 30 没碰上）、一条没有
    /// 毫米标的线、一趟带元数据的桥架输出、一条 U1B 软管标注，再放两个块引用当
    /// 上游块/设备块——走真实的体检 + 预览构建链，断言：断口被容差接上、连通性
    /// 报告注明接缝、桥架通道归属到电缆段、软管取到 U1B 标注长度、上游端/设备端
    /// 落在离各自连接块最近的末端、识别摘要与问题清单点名。只读（只加临时实体）。
    /// </summary>
    public sealed class RoutePreviewSelfTestCommand
    {
        [CommandMethod("UNCAD_ROUTE_PREVIEW_SELFTEST")]
        public void Run()
        {
            string result = "PASS";
            try { RunCore(); }
            catch (System.Exception ex) { result = ex.ToString(); }
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, result);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + result + "\n");
        }

        private static void RunCore()
        {
            var ctx = new CadContext(Application.DocumentManager.MdiActiveDocument);
            const double o = 500000000;
            double tolerance = QuickLineRouteSettings.GapTolerance();
            ObjectId s1, s2, s3, stray, device, upstream;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                Require(QuickLineMetadataStore.EnsureApplication(ctx.Db, tr), "U1LX metadata app");
                Require(ParallelAnnotationMetadata.EnsureApplication(ctx.Db, tr),
                    "parallel metadata app");
                s1 = Line(ctx, tr, o, o, o + 3000, o);
                s2 = Line(ctx, tr, o + 3000, o, o + 3000, o + 2000);
                // 拐角差 30 没碰上：看着连着，放大有缝。
                s3 = Line(ctx, tr, o + 3030, o + 2000, o + 6030, o + 2000);
                Link(ctx, tr, s1, "4800mm", o + 1500, o + 600);
                Link(ctx, tr, s2, "3000mm", o + 3600, o + 1000);
                Link(ctx, tr, s3, "6000mm", o + 4530, o + 2600);
                // 没有毫米标的线段。
                stray = Line(ctx, tr, o, o - 3000, o + 1000, o - 3000);

                // 桥架输出：一根平行线 + 一段标注文字，元数据 SOURCE = s1。
                var trayLine = new Line(new Point3d(o, o + 15, 0), new Point3d(o + 3000, o + 15, 0));
                ctx.AddToCurrentSpace(tr, trayLine);
                ParallelAnnotationMetadata.Set(trayLine, UncadLayerStandard.TrayAnnotationKind,
                    s1.Handle.ToString());
                var trayText = new DBText
                {
                    TextString = "桥架200*100 2500mm",
                    Height = 180,
                    Position = new Point3d(o + 1500, o + 60, 0)
                };
                ctx.AddToCurrentSpace(tr, trayText);
                ParallelAnnotationMetadata.Set(trayText, UncadLayerStandard.TrayAnnotationKind,
                    s1.Handle.ToString());

                // U1B 软管标注（没有 Ruanguan 块）。
                var hoseText = new DBText
                {
                    TextString = "100mm软管:2500mm",
                    Height = 180,
                    Position = new Point3d(o + 5000, o - 1000, 0)
                };
                ctx.AddToCurrentSpace(tr, hoseText);
                ParallelAnnotationMetadata.Set(hoseText, UncadLayerStandard.HoseAnnotationKind,
                    hoseText.Handle.ToString());

                // 两个块引用当上游块（靠近路线起点）和设备块（靠近路线终点）。
                ObjectId definition = PickBlockDefinition(ctx, tr);
                var upstreamReference = new BlockReference(new Point3d(o - 100, o + 100, 0),
                    definition);
                var deviceReference = new BlockReference(new Point3d(o + 6100, o + 2100, 0),
                    definition);
                upstream = ctx.AddToCurrentSpace(tr, upstreamReference);
                device = ctx.AddToCurrentSpace(tr, deviceReference);

                // D-059：一个没有归属线段的毫米标（竖排、附近没有线）必须点名；
                // 一个设备预留标记形态的横排毫米文字（3000mm）不算。
                var orphan = new DBText
                {
                    TextString = "2000mm",
                    Height = 180,
                    Rotation = Math.PI / 2,
                    Position = new Point3d(o + 9000, o + 4000, 0)
                };
                ctx.AddToCurrentSpace(tr, orphan);
                var reserve = new DBText
                {
                    TextString = "3000mm",
                    Height = 180,
                    Position = new Point3d(o + 9000, o + 4500, 0)
                };
                ctx.AddToCurrentSpace(tr, reserve);
                tr.Commit();
            }

            var boundary = new FrameRectangle("preview", o - 5000, o - 5000, o + 12000, o + 5000);
            QuickLineScanResult scan = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = new[] { boundary }
                });
            IReadOnlyList<QuickLineCadSegment> scanned = scan.Segments;
            FillRouteInspection inspection;
            var machine = new MachineRow
            {
                MachineId = "SELFTEST-PREVIEW",
                CircuitName = "回路",
                PanelFloor = "2F",
                DeviceFloor = "3F",
                Next = "I-Line盘"
            };
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var selection = new FillSelection
                {
                    DeviceBlockIds = new[] { device },
                    UpstreamInfoBlockIds = new[] { upstream }
                };
                inspection = FillRouteInspectionStage.Inspect(ctx, tr, new ObjectId[0], scanned,
                    boundary, null, null, scan.UnboundLabels, selection);
                FillRouteInspectionStage.AttachPreview(ctx, tr, inspection, selection, machine);
                tr.Commit();
            }

            Require(inspection.PreviewFailure == null, "preview built: " + inspection.PreviewFailure);
            Require(inspection.Preview != null, "preview model present");
            Require(inspection.Segments.Count == 4, "three labelled lines and one stray in scope");
            Require(inspection.Preview.Segments.Count == 3, "three segments in the scene");
            Require(inspection.Connectivity != null, "connectivity report present");

            string s1Handle = Handle(s1);
            // 断口容差：默认 50 时 s2–s3 的 30 缝被接上；容差调到 30 以下则保持断开。
            if (tolerance >= 30)
            {
                Require(inspection.Connectivity.GapJoins.Count == 1,
                    "one tolerated gap, got " + inspection.Connectivity.GapJoins.Count);
                Require(inspection.ConnectivityText.IndexOf("容错接上 1 处断口", StringComparison.Ordinal) >= 0,
                    "connectivity text names the gap: " + inspection.ConnectivityText);
            }
            else
                Require(inspection.Connectivity.GapJoins.Count == 0, "no tolerated gap below tolerance");

            // 没有毫米标的线段被点名；带桥架输出宿主的电缆段拿到通道。
            Require(inspection.UnlabelledSegmentHandles.Count == 1
                && inspection.UnlabelledSegmentHandles[0] == Handle(stray),
                "the stray line is reported as unlabelled");
            QuickLineOrbitPreviewChannel channel = inspection.Channels[s1Handle];
            Require(channel != null && channel.TrayWidthMillimetres == 200.0
                && !channel.TrayWidthAssumedDefault, "tray channel on s1 with width 200");

            // 两端：上游块靠近起点，设备块靠近终点。
            Require(inspection.Preview.DirectionResolved, "direction resolved from blocks");
            Near(inspection.Preview.UpstreamEndpoint.Value.X, 0.0, "upstream end X");
            Near(inspection.Preview.UpstreamEndpoint.Value.Z, 0.0, "upstream end Z");
            Near(inspection.Preview.DownstreamEndpoint.Value.X, 10800.0, "device end X");
            Near(inspection.Preview.DownstreamEndpoint.Value.Z, 3000.0, "device end Z");

            // 软管取 U1B 标注长度；从设备端沿末段方向伸出。
            Require(inspection.Preview.Hose != null && inspection.Preview.Hose.ByLabelOnly
                && inspection.Preview.Hose.LengthMillimetres == 2500.0, "hose from the U1B label");
            QuickLineOrbitPreviewEnvelope envelope = QuickLineOrbitPreviewEnvelope.Build(
                inspection.Preview);
            Require(envelope != null && envelope.Hose != null, "hose geometry placed");
            Near(envelope.Hose.End.X, 13300.0, "hose continues from the device end");

            // 识别摘要与问题清单（D-059：桥架/线管按「趟」计）。
            Require(inspection.RecognitionText.IndexOf("识别 3 段", StringComparison.Ordinal) >= 0
                && inspection.RecognitionText.IndexOf("桥架 1 趟 共 2.5m", StringComparison.Ordinal) >= 0
                && inspection.RecognitionText.IndexOf("软管 2.5m", StringComparison.Ordinal) >= 0,
                "recognition summary: " + inspection.RecognitionText);
            Require(inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("1 段线段没有毫米标", StringComparison.Ordinal) >= 0
                    && issue.IndexOf(Handle(stray), StringComparison.Ordinal) >= 0),
                "unlabelled line named in the issues");

            // D-059：没有归属线段的毫米标点名；设备预留标记形态的横排文字不点。
            Require(inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("1 个毫米标没有归属线段", StringComparison.Ordinal) >= 0
                    && issue.IndexOf("2000mm", StringComparison.Ordinal) >= 0),
                "orphan millimetre label named in the issues");
            Require(!inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("3000mm", StringComparison.Ordinal) >= 0),
                "reserve-marker text is not reported as an orphan label");

            // D-059：上游/下游轴位块缺一不可——本选集没有给轴位块，两条都要报。
            Require(inspection.RouteIssues.Count(issue =>
                    issue.IndexOf("轴位块", StringComparison.Ordinal) >= 0) == 2,
                "both missing axis blocks are reported");

            // D-059：楼层平面按台账（2F 上游 → 3F 设备），跨层高度不符要点名。
            Require(inspection.Preview.Floors != null
                && inspection.Preview.Floors.Floors.Count == 2,
                "two floor planes from the workbook floors");
            Require(inspection.FloorSummary == "台账 2F→3F",
                "floor summary: " + inspection.FloorSummary);
            // 3.8.0.3：量的是「上游楼层地面到设备端」；I-Line 盘不抬，线路竖向
            // 3 m 就是 3 m。文案点名区间、标高清单来源和实际值。
            string iLineRise = inspection.RouteIssues.Single(issue =>
                issue.IndexOf("跨层高度不符", StringComparison.Ordinal) >= 0);
            Require(iLineRise.IndexOf("2F地面到设备端应在 7.6~7.8 m",
                    StringComparison.Ordinal) >= 0
                && iLineRise.IndexOf("按 U1SET 标高清单 2F地面 → 3F高架地板面 差 7.8 m",
                    StringComparison.Ordinal) >= 0
                && iLineRise.IndexOf("实际 3 m（线路竖向 3 m）", StringComparison.Ordinal) >= 0,
                "cross-floor rise warning measures floor-to-device: " + iLineRise);

            // D-059：上游柜按台账「盘柜类型」——I-Line 盘落在 2F 地面上。
            Require(inspection.Preview.CabinetKind == PreviewCabinetKind.ILinePanel,
                "cabinet kind from the workbook");
            Require(envelope.Cabinet != null, "cabinet geometry placed");
            Near(envelope.Cabinet.BaseZ, 0.0, "I-Line panel stands on the 2F floor");
            Near(envelope.Cabinet.Height, 2200.0, "I-Line panel height");

            // 3.8.0.2：插座盘——抬的是整条线路（方块只是占位）：上游末端（设备
            // 基础点）落在 2F 地面 + 1 m，设备端跟着高 1 m，柜底与末端同高。
            FillRouteInspection socketInspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var socketSelection = new FillSelection
                {
                    DeviceBlockIds = new[] { device },
                    UpstreamInfoBlockIds = new[] { upstream }
                };
                socketInspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scanned, boundary, null, null,
                    scan.UnboundLabels, socketSelection);
                FillRouteInspectionStage.AttachPreview(ctx, tr, socketInspection,
                    socketSelection, new MachineRow
                    {
                        MachineId = "SELFTEST-PREVIEW",
                        CircuitName = "回路",
                        PanelFloor = "2F",
                        DeviceFloor = "3F",
                        Next = "插座盘"
                    });
                tr.Commit();
            }
            Require(socketInspection.PreviewFailure == null
                && socketInspection.Preview != null,
                "socket preview built: " + socketInspection.PreviewFailure);
            Require(socketInspection.Preview.CabinetKind == PreviewCabinetKind.SocketPanel,
                "socket cabinet kind from the workbook");
            Near(socketInspection.Preview.UpstreamEndpoint.Value.Z, 1000.0,
                "socket panel: upstream end 1 m above the 2F floor");
            Require(socketInspection.Preview.Segments.All(segment =>
                    Math.Min(segment.Start.Z, segment.End.Z) >= 1000.0 - 1e-6),
                "socket panel: the whole route is lifted 1 m");
            Near(socketInspection.Preview.DownstreamEndpoint.Value.Z, 4000.0,
                "socket panel: device end lifted 1 m too (3000 + 1000)");
            QuickLineOrbitPreviewEnvelope socketEnvelope =
                QuickLineOrbitPreviewEnvelope.Build(socketInspection.Preview);
            Require(socketEnvelope.Cabinet != null, "socket cabinet placed");
            Near(socketEnvelope.Cabinet.BaseZ, 1000.0,
                "socket panel bottom = upstream end (floor + 1 m)");
            Near(socketEnvelope.Cabinet.MountingFloorZ.Value, 0.0,
                "socket panel stands on the 2F floor plane");
            QuickLine3dScene socketScene = QuickLineOrbitSceneBuilder.Build(socketEnvelope);
            Require(socketScene.Primitives.Any(primitive =>
                    primitive.Kind == QuickLine3dPrimitiveKind.Tube
                    && primitive.Group == QuickLine3dSceneGroupKind.Reference
                    && Math.Abs(primitive.A1z) < 1e-6
                    && Math.Abs(primitive.B1z - 1000.0) < 1e-6),
                "socket panel: stand from the floor to the panel bottom");
            // 3.8.0.3：跨层校验从 2F 地面量到设备端——插座盘离地那 1 m 要加回来
            // （线路竖向 3 m + 离地 1 m = 4 m；以前报的是 3 m）。
            string socketRise = socketInspection.RouteIssues.Single(issue =>
                issue.IndexOf("跨层高度不符", StringComparison.Ordinal) >= 0);
            Require(socketRise.IndexOf("实际 4 m（上游端离地 1 m + 线路竖向 3 m）",
                    StringComparison.Ordinal) >= 0,
                "socket panel: rise measured from the 2F floor: " + socketRise);

            // 3.8.0.4（用户口径「一个轴位是 4.8 m；算出来的网格轴位和设备/上游
            // 轴位对不上要提示」）：轴位网格按 4.8 m 铺，每根柱网线一个轴号；
            // 图面距离与台账轴位（48/V→37/V = 11 跨）对不上时问题清单点名，
            // 不再线性插值把 11 跨压进 9 m 的图面。
            FillRouteInspection axisInspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var axisSelection = new FillSelection
                {
                    DeviceBlockIds = new[] { device },
                    UpstreamInfoBlockIds = new[] { upstream }
                };
                axisInspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scanned, boundary, null, null,
                    scan.UnboundLabels, axisSelection);
                FillRouteInspectionStage.AttachPreview(ctx, tr, axisInspection,
                    axisSelection, new MachineRow
                    {
                        MachineId = "SELFTEST-PREVIEW",
                        CircuitName = "回路",
                        PanelFloor = "2F",
                        DeviceFloor = "3F",
                        Next = "I-Line盘",
                        UpstreamAxis = "48/V",
                        DownstreamAxis = "37/V"
                    });
                tr.Commit();
            }
            Require(axisInspection.PreviewFailure == null
                && axisInspection.Preview != null,
                "axis preview built: " + axisInspection.PreviewFailure);
            QuickLineOrbitPreviewEnvelope axisEnvelope =
                QuickLineOrbitPreviewEnvelope.Build(axisInspection.Preview);
            List<int> axisNumbers = axisEnvelope.NumericTicks
                .Select(label => int.Parse(label.Text,
                    System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Require(axisNumbers.Count >= 2, "axis ticks at the 4.8 m pitch");
            for (int index = 1; index < axisNumbers.Count; index++)
                Require(Math.Abs(axisNumbers[index] - axisNumbers[index - 1]) == 1,
                    "one axis number per grid line (adjacent numbers differ by 1)");
            Require(axisInspection.RouteIssues.Any(issue => issue.IndexOf(
                    "轴位与图面距离对不上", StringComparison.Ordinal) >= 0),
                "axis/grid mismatch named in the issues");

            // D-059/3.8.0.1：场景只有楼层线框、轴网、线路、柜子——没有参照建筑；
            // 楼层不画半透明面（会挡住跨层线路），只画边框和柱网。
            QuickLine3dScene scene = QuickLineOrbitSceneBuilder.Build(envelope);
            Require(scene != null, "scene builds");
            Require(scene.Primitives.Count(primitive =>
                    primitive.Group == QuickLine3dSceneGroupKind.Reference
                    && primitive.Kind == QuickLine3dPrimitiveKind.Quad) == 0,
                "floor planes are wireframe, not filled quads");
            Require(scene.Labels.Any(label => label.Kind == "floor"
                    && label.Text.StartsWith("2F", StringComparison.Ordinal))
                && scene.Labels.Any(label => label.Kind == "floor"
                    && label.Text.StartsWith("3F", StringComparison.Ordinal)),
                "both floor labels present");
            Require(scene.Primitives.Count(primitive =>
                    primitive.Kind == QuickLine3dPrimitiveKind.WaffleCell) == 0,
                "no waffle plate in the preview");
        }

        private static ObjectId Line(CadContext ctx, Transaction tr, double x1, double y1,
            double x2, double y2)
            => ctx.AddToCurrentSpace(tr, new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0)));

        private static void Link(CadContext ctx, Transaction tr, ObjectId line, string text,
            double x, double y)
        {
            ObjectId label = ctx.AddToCurrentSpace(tr, new DBText
            {
                TextString = text,
                Height = 180,
                Position = new Point3d(x, y, 0)
            });
            Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, line, label),
                "link " + text);
        }

        private static ObjectId PickBlockDefinition(CadContext ctx, Transaction tr)
        {
            var table = (BlockTable)tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var definition = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (definition.IsLayout || definition.IsAnonymous) continue;
                return id;
            }
            throw new InvalidOperationException("FAILED: no block definition available");
        }

        private static string Handle(ObjectId id) => id.Handle.ToString();

        private static void Near(double actual, double expected, string what)
        {
            if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-3)
                throw new InvalidOperationException(what + ": expected " + expected
                    + ", got " + actual);
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + what);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Infra;

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
                tr.Commit();
            }

            var boundary = new FrameRectangle("preview", o - 5000, o - 5000, o + 12000, o + 5000);
            var scanned = QuickLineCadService.Scan(ctx, new QuickLineScanOptions
            {
                IncludeUnlabelled = true,
                FrameBoundaries = new[] { boundary }
            });
            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr, new ObjectId[0], scanned,
                    boundary, null, null);
                var selection = new FillSelection
                {
                    DeviceBlockIds = new[] { device },
                    UpstreamInfoBlockIds = new[] { upstream }
                };
                FillRouteInspectionStage.AttachPreview(ctx, tr, inspection, selection, null);
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

            // 识别摘要与问题清单。
            Require(inspection.RecognitionText.IndexOf("识别 3 段", StringComparison.Ordinal) >= 0
                && inspection.RecognitionText.IndexOf("桥架 1 段", StringComparison.Ordinal) >= 0
                && inspection.RecognitionText.IndexOf("软管 2500mm", StringComparison.Ordinal) >= 0,
                "recognition summary: " + inspection.RecognitionText);
            Require(inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("1 段线段没有毫米标", StringComparison.Ordinal) >= 0
                    && issue.IndexOf(Handle(stray), StringComparison.Ordinal) >= 0),
                "unlabelled line named in the issues");
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

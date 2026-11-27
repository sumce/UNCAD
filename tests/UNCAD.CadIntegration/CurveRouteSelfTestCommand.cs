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
using UNCAD.Core.Excel;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_CURVE_ROUTE_SELFTEST（3.8.0.1）：旧图框里电缆段不是普通 LINE 的
    /// 几种画法——闭合但退化成一根线的多段线、三维多段线、样条曲线、以及用
    /// 线性标注代替的 300mm 短线（标注旁边另放一个「300mm」文字）。断言扫描器
    /// 都能认出线段、毫米标都归属到线段；线性标注只在填充/U1U 扫描里当线段，
    /// U1LX 默认口径不变。只读（只加临时实体）。
    /// </summary>
    public sealed class CurveRouteSelfTestCommand
    {
        [CommandMethod("UNCAD_CURVE_ROUTE_SELFTEST")]
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
            const double o = 700000000;
            ObjectId closedPolyline, polyline3d, spline, dimension;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                Require(QuickLineMetadataStore.EnsureApplication(ctx.Db, tr), "U1LX metadata app");
                Require(ParallelAnnotationMetadata.EnsureApplication(ctx.Db, tr),
                    "parallel metadata app");

                // ① 闭合但三个顶点共线的多段线（视觉上就是一根线）。
                var closed = new Polyline(3);
                closed.AddVertexAt(0, new Point2d(o, o), 0, 0, 0);
                closed.AddVertexAt(1, new Point2d(o + 500, o), 0, 0, 0);
                closed.AddVertexAt(2, new Point2d(o + 1000, o), 0, 0, 0);
                closed.Closed = true;
                closedPolyline = ctx.AddToCurrentSpace(tr, closed);
                Label(ctx, tr, "700mm", o + 500, o + 250);

                // ② 三维多段线。
                var poly3d = new Polyline3d(Poly3dType.SimplePoly,
                    new Point3dCollection
                    {
                        new Point3d(o, o + 2000, 0),
                        new Point3d(o + 1500, o + 2000, 0)
                    }, false);
                polyline3d = ctx.AddToCurrentSpace(tr, poly3d);
                Label(ctx, tr, "1500mm", o + 750, o + 2250);

                // ③ 样条曲线（四个拟合点共线）。
                var fit = new Point3dCollection
                {
                    new Point3d(o, o + 4000, 0),
                    new Point3d(o + 500, o + 4000, 0),
                    new Point3d(o + 1100, o + 4000, 0),
                    new Point3d(o + 1600, o + 4000, 0)
                };
                var splineEntity = new Spline(fit, 3, 0.0);
                spline = ctx.AddToCurrentSpace(tr, splineEntity);
                Label(ctx, tr, "1600mm", o + 800, o + 4250);

                // ④ 线性标注代替 300mm 短线：标注本身没有替代文字（量出来
                // 300），旁边另放一个「300mm」文字——现场就是这种画法。
                var dim = new AlignedDimension(
                    new Point3d(o, o + 6000, 0), new Point3d(o + 300, o + 6000, 0),
                    new Point3d(o + 150, o + 6500, 0), "",
                    ctx.Db.Dimstyle);
                dimension = ctx.AddToCurrentSpace(tr, dim);
                Label(ctx, tr, "300mm", o + 150, o + 6250);
                tr.Commit();
            }

            var boundary = new FrameRectangle("curve-routes", o - 1000, o - 1000,
                o + 4000, o + 8000);
            QuickLineScanResult scan = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    IncludeDimensionSegments = true,
                    FrameBoundaries = new[] { boundary }
                });

            QuickLineCadSegment closedSegment = Find(scan, closedPolyline);
            Require(closedSegment != null && closedSegment.LabelText == "700mm",
                "closed collinear polyline is a 700mm segment");
            QuickLineCadSegment p3dSegment = Find(scan, polyline3d);
            Require(p3dSegment != null && p3dSegment.LabelText == "1500mm",
                "3D polyline is a 1500mm segment");
            QuickLineCadSegment splineSegment = Find(scan, spline);
            Require(splineSegment != null && splineSegment.LabelText == "1600mm",
                "spline is a 1600mm segment");
            QuickLineCadSegment dimensionSegment = Find(scan, dimension);
            Require(dimensionSegment != null && dimensionSegment.IsDimension,
                "the linear dimension becomes a synthetic segment");
            Require(dimensionSegment.LabelText == "300mm"
                && dimensionSegment.LabelMillimetres == 300.0,
                "the 300mm text binds to the dimension segment");
            Require(!scan.UnboundLabels.Any(item =>
                    item.Text.IndexOf("300mm", StringComparison.Ordinal) >= 0),
                "300mm is not reported as an unbound label");

            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scan.Segments, boundary, null, null,
                    scan.UnboundLabels, new FillSelection());
                tr.Commit();
            }
            Require(inspection.Segments.Count == 4,
                "four curve segments in scope, got " + inspection.Segments.Count);
            Require(!inspection.UnlabelledSegmentHandles.Any(handle =>
                    IsHandleOf(handle, closedPolyline) || IsHandleOf(handle, polyline3d)
                    || IsHandleOf(handle, spline) || IsHandleOf(handle, dimension)),
                "no curve segment is reported as unlabelled");

            // U1LX 默认口径：不把标注当线段（填充扫描才打开）。
            QuickLineScanResult legacy = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = new[] { boundary }
                });
            Require(Find(legacy, dimension) == null,
                "dimension segments stay off for the default U1LX scan");
        }

        private static QuickLineCadSegment Find(QuickLineScanResult scan, ObjectId id)
            => scan.Segments.FirstOrDefault(segment => segment.LineId == id);

        private static bool IsHandleOf(string handle, ObjectId id)
            => string.Equals(handle, id.Handle.ToString(),
                StringComparison.OrdinalIgnoreCase);

        private static void Label(CadContext ctx, Transaction tr, string text,
            double x, double y)
        {
            ctx.AddToCurrentSpace(tr, new DBText
            {
                TextString = text,
                Height = 180,
                Position = new Point3d(x, y, 0)
            });
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + what);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_BENT_POLYLINE_SELFTEST（3.8.0.2）：带拐角的多段线——现场把一条路线
    /// 画成一笔（斜 3400 / 竖 2000 / 平 3000），其中两个毫米标还带着指向这根
    /// 多段线的 U1L 关联。以前扫描器把它当成「首点到末点」的一条斜线：只挂得上
    /// 一个标注，其余标注报「没有归属线段」，3D 里整根因为方向不是等测轴被丢掉。
    /// 断言：填充/U1U 扫描按段拆开，各段各有自己的毫米标（键＝句柄/序号），无归属
    /// 为 0，3D 里轴线方向的两段都在；默认 U1LX 口径不拆；带斜腿的绕行多段线不拆。
    /// 只读（只加临时实体）。
    /// </summary>
    public sealed class BentPolylineSelfTestCommand
    {
        [CommandMethod("UNCAD_BENT_POLYLINE_SELFTEST")]
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
            const double o = 900000000;
            ObjectId bent, detourId;
            string bentHandle;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                Require(QuickLineMetadataStore.EnsureApplication(ctx.Db, tr), "U1LX metadata app");

                // ① 带拐角的路线（现场形态）：斜 30° 3400 → 竖 2000 → 斜 150° 3000。
                Point3d p0 = new Point3d(o, o, 0);
                Point3d p1 = new Point3d(o + 3400 * Math.Cos(Math.PI / 6.0),
                    o + 3400 * Math.Sin(Math.PI / 6.0), 0);
                Point3d p2 = new Point3d(p1.X, p1.Y + 2000, 0);
                Point3d p3 = new Point3d(p2.X + 3000 * Math.Cos(5 * Math.PI / 6.0),
                    p2.Y + 3000 * Math.Sin(5 * Math.PI / 6.0), 0);
                var route = new Polyline(4);
                route.AddVertexAt(0, new Point2d(p0.X, p0.Y), 0, 0, 0);
                route.AddVertexAt(1, new Point2d(p1.X, p1.Y), 0, 0, 0);
                route.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
                route.AddVertexAt(3, new Point2d(p3.X, p3.Y), 0, 0, 0);
                bent = ctx.AddToCurrentSpace(tr, route);
                bentHandle = bent.Handle.ToString();
                // 两个标注带着指向这根多段线的关联（合并/连接前的旧关联），一个是自由文字。
                ObjectId first = Text(ctx, tr, "3400mm",
                    (p0.X + p1.X) / 2.0 - 300, (p0.Y + p1.Y) / 2.0 + 300);
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, bent, first),
                    "link the first label to the polyline");
                Text(ctx, tr, "2000mm", p1.X + 300, p1.Y + 1000);
                ObjectId last = Text(ctx, tr, "3000mm",
                    (p2.X + p3.X) / 2.0 - 300, (p2.Y + p3.Y) / 2.0 + 300);
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, bent, last),
                    "link the last label to the polyline");

                // ② 带斜腿的绕行多段线（开洞绕行的样子）：不拆。
                const double q = 7000;
                var detour = new Polyline(4);
                detour.AddVertexAt(0, new Point2d(o + 20000, o + 20000), 0, 0, 0);
                detour.AddVertexAt(1, new Point2d(o + 20000 + q, o + 20000), 0, 0, 0);
                detour.AddVertexAt(2, new Point2d(o + 20000 + q + 400, o + 20000 + 400), 0, 0, 0);
                detour.AddVertexAt(3, new Point2d(o + 20000 + 2 * q + 400, o + 20000 + 400), 0, 0, 0);
                detourId = ctx.AddToCurrentSpace(tr, detour);
                tr.Commit();
            }

            var boundary = new FrameRectangle("bent-polyline", o - 2000, o - 2000,
                o + 40000, o + 30000);
            // 默认 U1LX 口径：不拆，仍是首点到末点的一段。
            QuickLineScanResult legacy = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = new[] { boundary }
                });
            QuickLineCadSegment[] legacyParts = legacy.Segments
                .Where(item => item.LineId == bent).ToArray();
            Require(legacyParts.Length == 1 && legacyParts[0].PartIndex == 0,
                "the default scan keeps the bent polyline as one chord");

            // 填充/U1U 口径：按段拆开，各段各有自己的毫米标。
            QuickLineScanResult scan = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    SplitBentPolylines = true,
                    FrameBoundaries = new[] { boundary }
                });
            QuickLineCadSegment[] parts = scan.Segments
                .Where(item => item.LineId == bent).OrderBy(item => item.PartIndex)
                .ToArray();
            Require(parts.Length == 3, "the bent polyline splits into 3 legs, got "
                + parts.Length);
            Require(parts[0].Key == bentHandle + "/1"
                && parts[1].Key == bentHandle + "/2"
                && parts[2].Key == bentHandle + "/3",
                "leg keys are handle/seq: " + string.Join(",", parts.Select(item => item.Key)));
            Require(parts[0].LabelText == "3400mm" && parts[0].LabelMillimetres == 3400.0,
                "leg 1 carries 3400mm, got [" + parts[0].LabelText + "]");
            Require(parts[1].LabelText == "2000mm", "leg 2 carries 2000mm, got ["
                + parts[1].LabelText + "]");
            Require(parts[2].LabelText == "3000mm", "leg 3 carries 3000mm, got ["
                + parts[2].LabelText + "]");
            Require(!scan.UnboundLabels.Any(),
                "no unbound label, got " + scan.UnboundLabels.Count);
            // 绕行多段线不拆。
            Require(scan.Segments.Count(item => item.LineId == detourId) == 1,
                "a detour polyline with an off-axis leg is not split");

            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scan.Segments, boundary, null, null,
                    scan.UnboundLabels, new FillSelection());
                FillRouteInspectionStage.AttachPreview(ctx, tr, inspection,
                    new FillSelection(), new MachineRow
                    {
                        MachineId = "SELFTEST-BENT",
                        CircuitName = "回路",
                        Next = "I-Line盘"
                    });
                tr.Commit();
            }
            Require(inspection.PreviewFailure == null,
                "preview built: " + inspection.PreviewFailure);
            // 拆出的三段各有标注；唯一没有毫米标的是那根故意不放标注的绕行多段线。
            Require(inspection.UnlabelledSegmentHandles.Count == 1
                && inspection.UnlabelledSegmentHandles[0] == detourId.Handle.ToString(),
                "only the unlabelled detour polyline is reported: "
                + string.Join(",", inspection.UnlabelledSegmentHandles));
            Require(inspection.RouteIssues.Count(issue =>
                    issue.IndexOf("没有归属线段", StringComparison.Ordinal) >= 0) == 0,
                "no unbound-label issue");
            // 3D：以前整根（首末点连线、方向不在轴上）被丢掉；现在三段都在。
            Require(inspection.Preview != null && inspection.Preview.Segments.Count == 3,
                "three legs reach the 3D scene, got "
                + (inspection.Preview?.Segments.Count ?? -1));
            Require(inspection.Preview.Segments.Any(segment =>
                    segment.LabelText == "3400mm")
                && inspection.Preview.Segments.Any(segment =>
                    segment.LabelText == "2000mm")
                && inspection.Preview.Segments.Any(segment =>
                    segment.LabelText == "3000mm"),
                "the legs keep their own millimetre labels");
            Require(inspection.Connectivity != null
                && inspection.ConnectivityText.Length > 0,
                "connectivity report present");
        }

        private static ObjectId Text(CadContext ctx, Transaction tr, string text,
            double x, double y)
        {
            var entity = new DBText
            {
                TextString = text,
                Height = 180,
                Position = new Point3d(x, y, 0)
            };
            return ctx.AddToCurrentSpace(tr, entity);
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + what);
        }
    }
}

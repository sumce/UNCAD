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
    /// UNCAD_LEGACY_TRAY_SELFTEST（3.8.0.1）：旧图框的桥架标注——多行文字
    /// 「梯形桥架200Wx100H / 3500mm」竖排（和红线不平行）、红线被缩短、没有
    /// 归属元数据。断言：扫描器不把标注第二行的 3500mm 当电缆毫米标；红线按
    /// 几何找到旁边带毫米标的电缆、成为一趟 3.5m 的桥架；红线本身不再被报成
    /// 「没有毫米标的线段」，也不进「找不到宿主」清单。
    /// 3.8.0.3：单行写法「梯形桥架200Wx100H-1300mm」同理（不再被报成「毫米标
    /// 没有归属线段」），宽度按「宽Wx高H」读。只读（只加临时实体）。
    /// </summary>
    public sealed class LegacyTraySelfTestCommand
    {
        [CommandMethod("UNCAD_LEGACY_TRAY_SELFTEST")]
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
            const double o = 600000000;
            ObjectId cable, redLine, trayText;
            string cableHandle, redHandle;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                Require(QuickLineMetadataStore.EnsureApplication(ctx.Db, tr), "U1LX metadata app");
                Require(ParallelAnnotationMetadata.EnsureApplication(ctx.Db, tr),
                    "parallel metadata app");
                // 电缆：一根带毫米标的横线（旧图框，没有桥架归属元数据）。
                cable = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(o, o, 0), new Point3d(o + 4000, o, 0)));
                ObjectId label = ctx.AddToCurrentSpace(tr, new DBText
                {
                    TextString = "1500mm",
                    Height = 180,
                    Position = new Point3d(o + 2000, o + 300, 0)
                });
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, cable, label),
                    "link the cable label");
                // 桥架红线：被缩短的一小段，离电缆 350；没有元数据。
                redLine = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(o + 1000, o + 350, 0),
                    new Point3d(o + 2500, o + 350, 0)));
                // 桥架标注：竖排（和红线不平行），第二行才是整趟长度。
                trayText = ctx.AddToCurrentSpace(tr, new MText
                {
                    Contents = "梯形桥架200Wx100H\\P3500mm",
                    TextHeight = 180,
                    Rotation = Math.PI / 2,
                    Location = new Point3d(o + 1000, o + 650, 0)
                });
                cableHandle = cable.Handle.ToString();
                redHandle = redLine.Handle.ToString();
                tr.Commit();
            }

            var boundary = new FrameRectangle("legacy-tray", o - 1000, o - 1000,
                o + 6000, o + 3000);
            QuickLineScanResult scan = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = new[] { boundary }
                });
            // 标注文字里的 3500mm 不是电缆毫米标，不能出现在「没有归属的毫米标」里。
            Require(!scan.UnboundLabels.Any(label =>
                    label.Text.IndexOf("梯形桥架", StringComparison.Ordinal) >= 0
                    || label.Text.IndexOf("3500mm", StringComparison.Ordinal) >= 0),
                "the tray annotation's length line is not a cable label");

            List<RouteAnnotationTextEntity> texts;
            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                texts = RouteAnnotationTextReader.Collect(ctx, tr);
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scan.Segments, boundary, null, texts,
                    scan.UnboundLabels, new FillSelection());
                tr.Commit();
            }
            Require(inspection.Segments.Count == 2,
                "one cable + one red line in scope, got " + inspection.Segments.Count);

            // 一趟桥架：长度取标注第二行（3500mm），宿主是红线旁边那根电缆。
            FillAnnotationRun run = inspection.AnnotationRuns.Single(item =>
                item.Kind == RouteAnnotationTextKind.Tray);
            Require(run.Label.IndexOf("梯形桥架200Wx100H", StringComparison.Ordinal) >= 0,
                "tray label text: " + run.Label);
            Require(run.LengthMillimetres == 3500.0,
                "whole-run length from the annotation: " + run.LengthMillimetres);
            Require(run.HostHandles.Count == 1 && run.HostHandles[0] == cableHandle,
                "red line resolves the cable host");
            Require(run.RecoveredBy == "红线就近", "recovered by the red line");
            // 3.8.0.3：宽度按「宽Wx高H」规格读（200），不再是缺省值。
            Require(inspection.Channels.ContainsKey(cableHandle)
                && inspection.Channels[cableHandle].TrayWidthMillimetres == 200.0
                && !inspection.Channels[cableHandle].TrayWidthAssumedDefault,
                "tray channel applied with the width read from the WxH spec");

            // 红线是标注线：不算漏标毫米标的电缆线，也不再报「找不到宿主」。
            Require(inspection.AnnotationLineHandles.Contains(redHandle),
                "the red line is classified as an annotation line");
            Require(!inspection.UnlabelledSegmentHandles.Contains(redHandle),
                "the red line is not reported as an unlabelled cable");
            Require(!inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("找不到宿主", StringComparison.Ordinal) >= 0),
                "no orphan-annotation issue");
            Require(!inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("没有毫米标", StringComparison.Ordinal) >= 0),
                "no unlabelled-cable issue");

            string summary = FillRouteRecognitionStage.BuildSummary(inspection, 1,
                "方向未判定", null);
            Require(summary.IndexOf("桥架 1 趟 共 3.5m", StringComparison.Ordinal) >= 0,
                "recognition summary: " + summary);

            // 3.8.0.3：单行写法「梯形桥架200Wx100H-1300mm」（现场 2026-11-25：
            // 规格和长度写在同一行、用连字符隔开，被报成「毫米标没有归属线段」）。
            const double p = 700000000;
            ObjectId red2;
            string cable2Handle, red2Handle;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                ObjectId cable2 = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(p, p, 0), new Point3d(p + 4000, p, 0)));
                ObjectId label2 = ctx.AddToCurrentSpace(tr, new DBText
                {
                    TextString = "2000mm",
                    Height = 180,
                    Position = new Point3d(p + 2000, p + 300, 0)
                });
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, cable2, label2),
                    "link the second cable label");
                red2 = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(p + 1000, p + 350, 0),
                    new Point3d(p + 2500, p + 350, 0)));
                ctx.AddToCurrentSpace(tr, new DBText
                {
                    TextString = "梯形桥架200Wx100H-1300mm",
                    Height = 180,
                    Position = new Point3d(p + 1000, p + 650, 0)
                });
                cable2Handle = cable2.Handle.ToString();
                red2Handle = red2.Handle.ToString();
                tr.Commit();
            }
            var boundary2 = new FrameRectangle("legacy-tray-single-line", p - 1000,
                p - 1000, p + 6000, p + 3000);
            QuickLineScanResult scan2 = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = new[] { boundary2 }
                });
            Require(!scan2.UnboundLabels.Any(label => label.Text.IndexOf("梯形桥架",
                    StringComparison.Ordinal) >= 0),
                "the single-line tray annotation is not a cable millimetre label");
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                texts = RouteAnnotationTextReader.Collect(ctx, tr);
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scan2.Segments, boundary2, null, texts,
                    scan2.UnboundLabels, new FillSelection());
                tr.Commit();
            }
            FillAnnotationRun single = inspection.AnnotationRuns.Single(item =>
                item.Kind == RouteAnnotationTextKind.Tray);
            Require(single.LengthMillimetres == 1300.0,
                "single-line annotation: whole-run length from the label, got "
                + single.LengthMillimetres);
            Require(single.TrayWidthMillimetres == 200.0,
                "single-line annotation: width read from the WxH spec, got "
                + single.TrayWidthMillimetres);
            Require(single.HostHandles.Count == 1 && single.HostHandles[0] == cable2Handle,
                "single-line annotation: red line resolves the cable host");
            Require(single.RecoveredBy == "红线就近",
                "single-line annotation recovered by the red line");
            Require(inspection.AnnotationLineHandles.Contains(red2Handle)
                && !inspection.UnlabelledSegmentHandles.Contains(red2Handle),
                "the second red line is an annotation line, not an unlabelled cable");
            Require(!inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("没有归属线段", StringComparison.Ordinal) >= 0),
                "no unbound-label issue for the single-line tray annotation");
            Require(!inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("找不到宿主", StringComparison.Ordinal) >= 0),
                "no orphan-annotation issue for the single-line tray annotation");

            _ = texts;
            _ = trayText;
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + what);
        }
    }
}

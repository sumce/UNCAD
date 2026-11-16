using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 3.3.0.1 现场回归工具（两个命令，都只读图纸语义——自检夹具在临时副本上）：
    ///
    /// UNCAD_ROUTE_DIAG —— 在真实图纸上走一遍 U1U 路由体检的生产链路
    /// （Collect → Scan → Inspect），逐环节报数。3.3.0 的现场缺陷（用户报告
    /// “执行 U1U 没看到 3D 图、桥架/线的图层也没变”）就是用它在天车项目
    /// MSRTL01.dwg 上复现的：扫描 24 段（17 段带毫米标），落入图框
    /// EntityIds 的 = 0——Collect 严格模式（TryAnchor）只把表格/文字/块归入
    /// EntityIds，Line 电缆线从来不在里面，3.3.0 体检阶段错误假设了它包含。
    ///
    /// UNCAD_ROUTE_INSPECT_SELFTEST —— 合成夹具 + 断言的回归锁：插入真实
    /// FRAME_20260812 定义、框内画 3 段正交链 + 3 个毫米标文本，断言按
    /// Boundary 几何归属后 inScope=3、预览非空、图层计划把线移到 U_CABLE。
    /// 必须在 XFrameTemplate.dwg 上跑（夹具要用其中的真实图框定义）。
    /// 报告写 %TEMP%\uncad-route-diag.txt 与 UNCAD_CAD_INTEGRATION_RESULT。
    /// </summary>
    public sealed class RouteDiagCommand
    {
        private const string DiagName = "UNCAD_ROUTE_DIAG";
        private const string SelfTestName = "UNCAD_ROUTE_INSPECT_SELFTEST";
        private const string ResultVariable = "UNCAD_CAD_INTEGRATION_RESULT";

        private static readonly List<string> Report = new List<string>();
        private static readonly double BaseX = 5000000;
        private static readonly double BaseY = 5000000;

        [CommandMethod(DiagName)]
        public void RunDiag()
        {
            string verdict;
            try { verdict = RunDiagCore(); }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                verdict = "FAIL " + ex.Message;
            }
            Finish(DiagName, verdict);
        }

        [CommandMethod(SelfTestName)]
        public void RunSelfTest()
        {
            string verdict;
            try { verdict = RunSelfTestCore(); }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                verdict = "FAIL " + ex.Message;
            }
            Finish(SelfTestName, verdict);
        }

        private static void Finish(string command, string verdict)
        {
            string text = verdict + Environment.NewLine
                + string.Join(Environment.NewLine, Report);
            Environment.SetEnvironmentVariable(ResultVariable, text);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "uncad-route-diag.txt"),
                    text, Encoding.UTF8);
            }
            catch { }
            Console.WriteLine("[" + command + "] " + verdict);
        }

        // ---------------- 诊断（真实图纸，只报数不断言） ----------------

        private static string RunDiagCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            Report.Clear();
            Info("=== " + DiagName + " 图纸: " + document.Name + " ===");

            IReadOnlyList<QuickLineCadSegment> scanned =
                FillRouteInspectionStage.Scan(ctx);
            Info("[1] Scan 线段总数: " + scanned.Count
                + "（带毫米标 " + scanned.Count(s => s.HasMillimetreLabel) + "）");

            var frameIds = new List<ObjectId>();
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(
                    ctx.Db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is BlockReference block
                        && !(block is Table))
                        frameIds.Add(id);
                }
                tr.Commit();
            }
            Info("[2] 候选图框块: " + frameIds.Count);

            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                List<ParallelAnnotationOwnershipEntry> allOutputs =
                    ParallelAnnotationOwnership.Collect(ctx, tr, null);
                Info("[3] 全图平行标注输出(带元数据): " + allOutputs.Count
                    + string.Join("", allOutputs.GroupBy(o => o.Kind)
                        .Select(g => "，KIND=" + g.Key + ":" + g.Count())));
                tr.Commit();
            }

            int inspected = 0;
            foreach (ObjectId frameId in frameIds.Take(3))
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(
                    ctx, new[] { frameId });
                if (regions.Errors.Count > 0)
                    Info("[4] 图框 " + frameId.Handle + " 分区错误: "
                        + string.Join("；", regions.Errors));
                if (regions.Groups.Count != 1) continue;
                FrameRegionGroup region = regions.Groups[0];
                inspected++;
                int entityIdHit = scanned.Count(
                    s => region.EntityIds.Contains(s.LineId));
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    FillRouteInspection inspection = FillRouteInspectionStage
                        .Inspect(ctx, tr, region.EntityIds, scanned,
                            "48/V", "37/V", true, region.Boundary);
                    tr.Commit();
                    Info("[4] 图框 " + frameId.Handle + ": EntityIds="
                        + region.EntityIds.Count + " 实体（扫描命中 " + entityIdHit
                        + "——严格模式不收 Line 属正常），Boundary 几何归属: 连通性 "
                        + inspection.ConnectivityText);
                    Info("    图层矫正: " + (inspection.LayerCorrectionText
                        ?? "(null)"));
                    Info("    预览: " + (inspection.Preview == null
                        ? "NULL"
                        : inspection.Preview.Segments.Count + " 段，柱位="
                          + inspection.Preview.AxisSummary));
                }
            }
            Info("[5] 实际体检图框数: " + inspected);
            return inspected > 0 ? "DONE" : "FAIL no frames";
        }

        // ---------------- 自检（合成夹具 + 断言） ----------------

        private static string RunSelfTestCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            Report.Clear();
            Info("=== " + SelfTestName + " 图纸: " + document.Name + " ===");

            // 夹具：插入真实图框定义 + 框内 3 段正交链 + 3 个毫米标文本。
            string frameName = FrameRegionCollector.CurrentFrameName;
            ObjectId frameId;
            var lineIds = new List<ObjectId>();
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(
                    ctx.Db.CurrentSpaceId, OpenMode.ForWrite);
                var blockTable = (BlockTable)tr.GetObject(
                    ctx.Db.BlockTableId, OpenMode.ForRead);
                if (!blockTable.Has(frameName))
                    throw new InvalidOperationException(
                        "模板缺少图框定义 " + frameName + "（必须在 XFrameTemplate.dwg 上跑）");
                var frame = new BlockReference(new Point3d(BaseX, BaseY, 0),
                    blockTable[frameName]);
                space.AppendEntity(frame);
                tr.AddNewlyCreatedDBObject(frame, true);
                frameId = frame.Id;
                tr.Commit();
            }
            FrameRegionCollection regions = FrameRegionCollector.Collect(
                ctx, new[] { frameId });
            if (regions.Groups.Count != 1)
                throw new InvalidOperationException("夹具图框未被识别: "
                    + string.Join("；", regions.Errors));
            var boundary = regions.Groups[0].Boundary;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(
                    ctx.Db.CurrentSpaceId, OpenMode.ForWrite);
                double cx = (boundary.MinX + boundary.MaxX) / 2.0;
                double cy = (boundary.MinY + boundary.MaxY) / 2.0;
                var points = new[]
                {
                    new Point3d(cx - 6000, cy, 0),
                    new Point3d(cx, cy, 0),
                    new Point3d(cx, cy + 4000, 0),
                    new Point3d(cx + 7000, cy + 4000, 0)
                };
                string[] labels = { "2000mm", "3500mm", "4800mm" };
                for (int index = 0; index < points.Length - 1; index++)
                {
                    // 线与毫米标连续创建（U1L 的历史创建顺序）——扫描器的
                    // 顺序启发（label.Order-1/-2 邻线）按这个顺序回配标签。
                    var line = new Line(points[index], points[index + 1]);
                    space.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                    lineIds.Add(line.Id);
                    var text = new DBText
                    {
                        Position = new Point3d(
                            (points[index].X + points[index + 1].X) / 2.0,
                            (points[index].Y + points[index + 1].Y) / 2.0 + 300,
                            0),
                        Height = 300,
                        TextString = labels[index]
                    };
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                }
                tr.Commit();
            }

            IReadOnlyList<QuickLineCadSegment> scanned =
                FillRouteInspectionStage.Scan(ctx);
            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    regions.Groups[0].EntityIds, scanned,
                    "48/V", "37/V", true, regions.Groups[0].Boundary);
                tr.Commit();
            }

            var failures = new List<string>();
            int entityIdHit = scanned.Count(
                s => regions.Groups[0].EntityIds.Contains(s.LineId));
            Info("EntityIds 严格模式扫描命中: " + entityIdHit
                + "（预期 0——Line 不进 EntityIds，3.3.0 缺陷根因）");
            if (inspection.Preview == null)
                failures.Add("预览为 NULL（3.3.0.1 修复无效？）");
            else
            {
                Info("预览: " + inspection.Preview.Segments.Count + " 段，柱位="
                    + inspection.Preview.AxisSummary);
                if (inspection.Preview.Segments.Count != 3)
                    failures.Add("预览段数 " + inspection.Preview.Segments.Count
                        + " ≠ 3");
            }
            Info("连通性: " + inspection.ConnectivityText);
            if (inspection.Connectivity == null
                || inspection.Connectivity.Components.Count != 1
                || inspection.Connectivity.IsolatedSegmentIds.Count != 0)
                failures.Add("连通性不是一条链: " + inspection.ConnectivityText);
            if (inspection.LayerPlan == null || inspection.LayerPlan.IsEmpty)
                failures.Add("图层矫正计划为空（Boundary 归属未生效？）");
            else
            {
                Info("矫正计划: " + inspection.LayerPlan.Counters.Describe()
                    + "，Moves=" + inspection.LayerPlan.Moves.Count);
                int lineMoves = inspection.LayerPlan.Moves.Count(
                    move => lineIds.Contains(move.Key));
                if (lineMoves != 3)
                    failures.Add("线段移层计划 " + lineMoves + " ≠ 3");
            }
            return failures.Count == 0
                ? "DONE"
                : "FAIL " + string.Join("；", failures);
        }

        private static void Info(string line) => Report.Add(line);
    }
}

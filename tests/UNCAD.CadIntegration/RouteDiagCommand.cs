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
            // run.ps1 门禁按自检约定从 %UNCAD_CAD_INTEGRATION_RESULT% 指向的
            // **文件**读 PASS（其它自检命令同款）；本命令判定词是 DONE，达标时
            // 补写 PASS、失败写 FAIL——先读路径再覆盖环境变量（原顺序会把
            // 路径先冲掉，run.ps1 因此永远读不到结果文件）。
            string resultPath = Environment.GetEnvironmentVariable(ResultVariable);
            Environment.SetEnvironmentVariable(ResultVariable, text);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "uncad-route-diag.txt"),
                    text, Encoding.UTF8);
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                try
                {
                    System.IO.File.WriteAllText(resultPath,
                        verdict.StartsWith("DONE", StringComparison.Ordinal)
                            ? "PASS" : "FAIL " + verdict);
                }
                catch { }
            }
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
                FillRouteInspectionStage.Scan(ctx).Segments;
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
                            region.Boundary);
                    tr.Commit();
                    Info("[4] 图框 " + frameId.Handle + ": EntityIds="
                        + region.EntityIds.Count + " 实体（扫描命中 " + entityIdHit
                        + "——严格模式不收 Line 属正常），Boundary 几何归属: 连通性 "
                        + inspection.ConnectivityText);
                    Info("    图层矫正: " + (inspection.LayerCorrectionText
                        ?? "(null)"));
                    Info("    已识别路由: " + inspection.Segments.Count + " 段");
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
            ObjectId hoseId = ObjectId.Null;
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
                // 3.3.4（图层收口）：框内补一个 U1B 软管替身——挂
                // UNCAD_PARALLEL KIND=U1B、SOURCE=自引用句柄（真实软管三件套
                // 的元数据形状）。带元数据实体被扫描器跳过（不进电缆链/连通
                // 性），宿主判框必然落空——必须经"边界几何直通"进矫正计划。
                if (!ParallelAnnotationMetadata.EnsureApplication(ctx.Db, tr))
                    throw new InvalidOperationException(
                        "夹具无法注册 UNCAD_PARALLEL RegApp");
                var hose = new Line(new Point3d(cx - 500, cy - 800, 0),
                    new Point3d(cx + 500, cy - 800, 0));
                space.AppendEntity(hose);
                tr.AddNewlyCreatedDBObject(hose, true);
                hoseId = hose.Id;
                ParallelAnnotationMetadata.Set(hose,
                    UNCAD.Core.Dwg.UncadLayerStandard.HoseAnnotationKind,
                    hose.Id.Handle.ToString());
                tr.Commit();
            }

            IReadOnlyList<QuickLineCadSegment> scanned =
                FillRouteInspectionStage.Scan(ctx).Segments;
            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    regions.Groups[0].EntityIds, scanned,
                    regions.Groups[0].Boundary);
                tr.Commit();
            }

            var failures = new List<string>();
            int entityIdHit = scanned.Count(
                s => regions.Groups[0].EntityIds.Contains(s.LineId));
            Info("EntityIds 严格模式扫描命中: " + entityIdHit
                + "（预期 0——Line 不进 EntityIds，3.3.0 缺陷根因）");
            if (inspection.Preview != null)
                failures.Add("填充路由检查仍构建了已停用的 3D 预览");
            if (inspection.Segments.Count != 3)
                failures.Add("识别路由段数 " + inspection.Segments.Count + " ≠ 3");
            int labelled = inspection.Segments.Count(
                segment => segment.HasMillimetreLabel);
            if (labelled != 3)
                failures.Add("识别到毫米标的路由段数 " + labelled + " ≠ 3");
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
                // 3.3.4（图层收口）：U1B 直通——框内软管替身（SOURCE=自引用，
                // 宿主判框必然落空）必须经边界几何归属进 U_HOSE 移层计划；
                // 旧接线 HoseEntities 恒 0（分支不可达）。
                if (inspection.LayerPlan.Counters.HoseEntities != 1)
                    failures.Add("软管直通移层计划 "
                        + inspection.LayerPlan.Counters.HoseEntities + " ≠ 1");
                if (!inspection.LayerPlan.Moves.Any(
                        move => move.Key == hoseId
                            && string.Equals(move.Value,
                                UNCAD.Core.Dwg.UncadLayerStandard.HoseLayerName,
                                StringComparison.OrdinalIgnoreCase)))
                    failures.Add("软管替身未进 U_HOSE 移层计划");
            }
            return failures.Count == 0
                ? "DONE"
                : "FAIL " + string.Join("；", failures);
        }

        [CommandMethod("UNCAD_MARKER_DIAG")]
        public void RunMarkerDiag()
        {
            string verdict;
            try { verdict = RunMarkerDiagCore(); }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                verdict = "FAIL " + ex.Message;
            }
            Finish("UNCAD_MARKER_DIAG", verdict);
        }

        /// <summary>
        /// 毫米标法医（用户报告"识别不到横着的毫米标"）：对每个图框范围内
        /// 含 mm 的文字逐个过 D-043 三道淘汰口（U1LX 关联活性 / 水平性 /
        /// 判据正则），报出每个疑似标记的死因。3.3.3.1 起关联口按"解析到
        /// 活线"判——"0"/悬空/复制残留的陈旧句柄不再一票否决。
        /// </summary>
        private static string RunMarkerDiagCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            Report.Clear();
            Info("=== UNCAD_MARKER_DIAG 图纸: " + document.Name + " ===");

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

            int frames = 0, candidates = 0;
            foreach (ObjectId frameId in frameIds)
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(
                    ctx, new[] { frameId });
                if (regions.Errors.Count > 0 || regions.Groups.Count != 1) continue;
                FrameRegionGroup region = regions.Groups[0];
                frames++;
                int frameCandidates = 0;
                using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in region.EntityIds)
                    {
                        string text;
                        double rotation;
                        DBObject source;
                        try { source = tr.GetObject(id, OpenMode.ForRead); }
                        catch { continue; }
                        if (source is DBText dbText)
                        {
                            text = dbText.TextString ?? "";
                            rotation = dbText.Rotation;
                        }
                        else if (source is MText mText)
                        {
                            text = UNCAD.Core.Text.TextParser.CleanMText(
                                mText.Contents ?? "");
                            rotation = mText.Rotation;
                        }
                        else continue;
                        if (!text.ToLowerInvariant().Contains("mm")) continue;
                        string association = QuickLineMetadataStore
                            .ReadAssociatedLineHandle(source);
                        string layer = "?";
                        try { layer = ((Entity)source).Layer ?? "?"; } catch { }
                        string rotationNote = (rotation * 180 / Math.PI)
                            .ToString("0.##") + "°";
                        string verdict = string.IsNullOrEmpty(association)
                            ? "无关联"
                            : "关联=" + association + "（3.3.3.1 按活性判）";
                        if (!UNCAD.Core.Fill.DeviceReserveMarker
                            .IsHorizontal(rotation))
                            verdict += " → ✗ 非水平 " + rotationNote;
                        else if (!UNCAD.Core.Fill.DeviceReserveMarker
                            .TryParseMillimeters(text.Trim(), out double mm))
                            verdict += " → ✗ 判据不过「" + text.Trim() + "」";
                        else
                        {
                            verdict += " → ✔ 候选 " + mm + "mm";
                            frameCandidates++;
                        }
                        Info("  框" + region.Handle + " 文字@" + id.Handle
                            + " 层=" + layer + " 旋转=" + rotationNote
                            + " " + verdict);
                    }
                    tr.Commit();
                }
                candidates += frameCandidates;
            }
            Info("体检图框 " + frames + " 个，候选 " + candidates + " 个。");
            return frames > 0 ? "DONE" : "FAIL no frames";
        }

        [CommandMethod("UNCAD_MARKER_SELFTEST")]
        public void RunMarkerSelfTest()
        {
            string verdict;
            try { verdict = RunMarkerSelfTestCore(); }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                verdict = "FAIL " + ex.Message;
            }
            Finish("UNCAD_MARKER_SELFTEST", verdict);
        }

        /// <summary>
        /// 3.3.3.1 回归锁（用户报告"识别不到横着的毫米标"，现场两日日志版本
        /// 指纹定位 3.2.0.6 排除口随升级生效）：夹具=真实图框内三根横排毫米标
        /// ——①垃圾关联 "0"（ObjectId.Null 存出的字面量）必须进吸收候选
        /// （3.2.0.6 旧判"非空即排除"的现场病灶）；②关联指向活 Line 的
        /// （真 U1LX 标注）必须被排除（3.2.0.6 的本意不回归）；③无关联的
        /// 照常进候选；再验证两个吸收标记提交后确实已删除、活关联毫米标仍保留。
        /// </summary>
        private static string RunMarkerSelfTestCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            Report.Clear();
            Info("=== UNCAD_MARKER_SELFTEST 图纸: " + document.Name + " ===");

            string frameName = FrameRegionCollector.CurrentFrameName;
            ObjectId garbageTextId, liveTextId, plainTextId;
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
                FrameRegionCollection regions = FrameRegionCollector.Collect(
                    ctx, new[] { frame.Id });
                if (regions.Groups.Count != 1)
                    throw new InvalidOperationException("夹具图框未被识别");
                var boundary = regions.Groups[0].Boundary;
                double cx = (boundary.MinX + boundary.MaxX) / 2.0;
                double cy = (boundary.MinY + boundary.MaxY) / 2.0;

                garbageTextId = AddHorizontalText(tr, space, cx - 6000, cy, "3000mm");
                liveTextId = AddHorizontalText(tr, space, cx, cy, "5000mm");
                plainTextId = AddHorizontalText(tr, space, cx + 6000, cy, "2000mm");

                // ①垃圾关联 "0"：模拟 ObjectId.Null 存出的历史脏数据。
                QuickLineMetadataStore.EnsureApplication(ctx.Db, tr);
                DBObject garbage = tr.GetObject(garbageTextId, OpenMode.ForWrite);
                garbage.XData = QuickLineMetadataStore.Build(garbage, "0", false);
                // ②活关联：真 Line + TryLinkLineAndLabel。
                var line = new Line(new Point3d(cx - 500, cy - 800, 0),
                    new Point3d(cx + 500, cy - 800, 0));
                space.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                if (!QuickLineCadService.TryLinkLineAndLabel(ctx, tr, line.Id,
                        liveTextId))
                    throw new InvalidOperationException("夹具活关联写入失败");
                tr.Commit();
            }

            var failures = new List<string>();
            DeviceReserveMarkerReader.Result result = DeviceReserveMarkerReader
                .CollectInSelection(ctx, new[] { garbageTextId, liveTextId, plainTextId });
            Info("候选数: " + result.Hits.Count + string.Join("", result.Hits
                .Select(hit => "，" + hit.Text + "=" + hit.Millimeters + "mm")));
            if (result.Hits.Count != 2)
                failures.Add("候选数 " + result.Hits.Count + " ≠ 2");
            if (!result.Hits.Any(hit => hit.Millimeters == 3000))
                failures.Add("垃圾关联 0 的横标未被收为候选（3.3.3.1 修复无效）");
            if (!result.Hits.Any(hit => hit.Millimeters == 2000))
                failures.Add("无关联横标未被收为候选");
            if (result.Hits.Any(hit => hit.Millimeters == 5000))
                failures.Add("活关联的真标注被误收（3.2.0.6 本意回归）");

            int erasedCount;
            using (Transaction transaction =
                ctx.Db.TransactionManager.StartTransaction())
            {
                erasedCount = DeviceReserveMarkerReader.EraseAbsorbed(transaction,
                    result.EntityIds.ToArray(), "UNCAD_MARKER_SELFTEST");
                transaction.Commit();
            }
            if (erasedCount != 2)
                failures.Add("吸收候选删除数 " + erasedCount + " ≠ 2");
            using (Transaction transaction =
                ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in result.EntityIds)
                {
                    DBObject marker = transaction.GetObject(id, OpenMode.ForRead, true);
                    if (marker == null || !marker.IsErased)
                        failures.Add("吸收的预留文字未从图纸删除，句柄 " + id.Handle);
                }
                DBObject linkedLabel = transaction.GetObject(liveTextId,
                    OpenMode.ForRead, true);
                if (linkedLabel == null || linkedLabel.IsErased)
                    failures.Add("U1LX 活关联毫米标被错误删除");
                transaction.Commit();
            }
            return failures.Count == 0 ? "DONE" : "FAIL " + string.Join("；", failures);
        }

        private static ObjectId AddHorizontalText(Transaction tr,
            BlockTableRecord space, double x, double y, string text)
        {
            var dbText = new DBText
            {
                Position = new Point3d(x, y, 0),
                Height = 300,
                Rotation = 0,
                TextString = text
            };
            space.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
            return dbText.Id;
        }

        private static void Info(string line) => Report.Add(line);
    }
}

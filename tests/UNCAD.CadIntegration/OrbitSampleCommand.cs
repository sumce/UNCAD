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
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_ORBIT_SAMPLE —— 3.3.7 U1U 3D 预览的**验收样图生成器**（用户口径：
    /// 「生成一个 3D 回路 上游在2楼 设备也在二楼的 并且有桥架 并且你需要
    /// 知道 线只有东南西北 上下的 没有斜着的线」）。在 XFrameTemplate.dwg
    /// 上跑：插入真实图框，框内画一条 2F 机台回路——
    ///
    ///   设备(2F)地沟 Z0 → 爬升 2200 到 I-L 盘顶 → 设备顶短走 → 爬升到
    ///   桥架标高 3900 → 桥架长跑（U1Q，200 宽，生产 ParallelCurveAnnotator
    ///   生成偏移线+标注+元数据）→ 爬到第三层母线 4900（上游在 2F 母线）。
    ///
    /// 全部端点落在 4800 柱网上（柱网规划器锚定上游端点、4800 柱距），段只有
    /// X/Y/Z 三向（U1L 语义：线只有东西南北上下，没有斜线）。生成后当场走
    /// 生产链路断言（Scan→Inspect→预览含桥架通道），再 SaveAs 到桌面
    /// 「UNCAD-3D验收-2F回路样图.dwg」——用户开样图跑 U1U 选图框即可验收
    /// 3.3.7 的建筑模型（本层=2F地面 重定位、母线玻璃层 3660/4220/4900、
    /// 华夫板、环氧地坪、柱网结构柱）。
    /// 结果按自检约定写 %UNCAD_CAD_INTEGRATION_RESULT% 指向的文件。
    /// </summary>
    public sealed class OrbitSampleCommand
    {
        private const string CommandName = "UNCAD_ORBIT_SAMPLE";
        private const string ResultVariable = "UNCAD_CAD_INTEGRATION_RESULT";

        private static readonly List<string> Report = new List<string>();

        [CommandMethod(CommandName)]
        public void Run()
        {
            string verdict;
            try { verdict = RunCore(); }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                verdict = "FAIL " + ex.Message;
            }
            Finish(verdict);
        }

        private static string RunCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            Report.Clear();
            Info("=== " + CommandName + " 图纸: " + document.Name + " ===");

            // ---- ① 图框（真实 FRAME 定义，RouteDiag 同款） ----
            string frameName = FrameRegionCollector.CurrentFrameName;
            ObjectId frameId;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(
                    ctx.Db.CurrentSpaceId, OpenMode.ForWrite);
                var blockTable = (BlockTable)tr.GetObject(
                    ctx.Db.BlockTableId, OpenMode.ForRead);
                if (!blockTable.Has(frameName))
                    throw new InvalidOperationException(
                        "模板缺少图框定义 " + frameName
                        + "（必须在 XFrameTemplate.dwg 上跑）");
                var frame = new BlockReference(new Point3d(0, 0, 0),
                    blockTable[frameName]);
                space.AppendEntity(frame);
                tr.AddNewlyCreatedDBObject(frame, true);
                frameId = frame.Id;
                tr.Commit();
            }
            FrameRegionCollection regions = FrameRegionCollector.Collect(
                ctx, new[] { frameId });
            if (regions.Groups.Count != 1)
                throw new InvalidOperationException("图框未被识别: "
                    + string.Join("；", regions.Errors));
            var boundary = regions.Groups[0].Boundary;
            Info("图框边界: " + boundary.MinX + "," + boundary.MinY
                + " ~ " + boundary.MaxX + "," + boundary.MaxY);

            // ---- ② 回路几何（4800 柱网格点、纯正交、Z 链 0→2200→3900→4900）----
            double availableW = boundary.MaxX - boundary.MinX;
            double availableH = boundary.MaxY - boundary.MinY;
            double bay = 4800;
            if (availableW < 4 * bay + 4800 || availableH < 3 * bay + 4800)
                bay = 2400; // 小图框退半跨，仍落在 4800 柱网上
            double originX = boundary.MinX + (availableW - 4 * bay) / 2.0;
            double originY = boundary.MinY + (availableH - 3 * bay) / 2.0;
            Point3d At(double latticeX, double latticeY, double z)
                => new Point3d(originX + latticeX * bay,
                    originY + latticeY * bay, z);
            // 上游端点=包络 X 最小端点（P0）；设备在 2F：地沟 Z0 爬到 I-L 盘
            // 顶 2200；桥架标高 3900；上游接第三层母线 4900（都在 2F 楼面上）。
            var points = new[]
            {
                At(0, 0, 0),        // P0 设备旁地沟（2F 地面）
                At(0, 0, 2200),     // P1 I-L 盘顶面
                At(1, 0, 2200),     // P2 设备顶短走
                At(1, 0, 3900),     // P3 桥架标高
                At(1, 3, 3900),     // P4 桥架长跑（Y+）
                At(4, 3, 3900),     // P5 桥架长跑（X+）
                At(4, 3, 4900)      // P6 第三层母线（上游，2F）
            };
            string[] labels =
                { "2200mm", "4800mm", "1700mm", "14400mm", "14400mm", "1000mm" };

            var lineIds = new List<ObjectId>();
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(
                    ctx.Db.CurrentSpaceId, OpenMode.ForWrite);
                // 挂 U1LX 元数据前先注册 RegApp（3.3.3.2 教训：未注册时写
                // XData 抛 eRegappIdNotFound）。
                if (!QuickLineMetadataStore.EnsureApplication(ctx.Db, tr))
                    throw new InvalidOperationException("无法注册 U1LX RegApp");
                for (int index = 0; index < points.Length - 1; index++)
                {
                    var line = new Line(points[index], points[index + 1]);
                    space.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                    lineIds.Add(line.Id);
                    // 毫米标（U1L 同款：文字挂 U1LX 线关联元数据，水平线放
                    // 上方、竖直线放侧面）。
                    bool horizontal = Math.Abs(
                        points[index].Z - points[index + 1].Z) < 1e-9;
                    Point3d mid = new Point3d(
                        (points[index].X + points[index + 1].X) / 2.0,
                        (points[index].Y + points[index + 1].Y) / 2.0,
                        (points[index].Z + points[index + 1].Z) / 2.0);
                    Point3d textPos = horizontal
                        ? new Point3d(mid.X, mid.Y + 400, mid.Z)
                        : new Point3d(mid.X + 400, mid.Y, mid.Z);
                    ObjectId textId = space.AppendEntity(new DBText
                    {
                        Position = textPos,
                        Height = 180,
                        TextString = labels[index]
                    });
                    tr.AddNewlyCreatedDBObject(
                        tr.GetObject(textId, OpenMode.ForWrite), true);
                    DBObject text = tr.GetObject(textId, OpenMode.ForWrite);
                    text.XData = QuickLineMetadataStore.Build(text,
                        line.Id.Handle.ToString(), true);
                }
                tr.Commit();
            }
            Info("回路线段: " + lineIds.Count + " 段（柱距 " + bay + "，全正交）");

            // ---- ③ 桥架（生产 U1Q：ParallelCurveAnnotator.AddRuns） ----
            int runs = ParallelCurveAnnotator.AddRuns(ctx,
                new[] { lineIds[3], lineIds[4] },
                new ParallelAnnotationOptions
                {
                    CurveOffset = Settings.GetDouble(
                        ConfigKeys.UnqLineOff, 15.0),
                    TextOffset = Settings.GetDouble(
                        ConfigKeys.UnqTextOff, 0.0),
                    TextHeight = Settings.GetDouble(
                        ConfigKeys.UnqHeight, 180.0),
                    Above = true,
                    ColorIndex = 1,
                    AnnotationKind = "U1Q",
                    LegacyLabelKey = UNCAD.Core.Fill.AnnotationLabelPair
                        .CollapseText
                },
                run => "梯形桥架200Wx100H\\P"
                    + (long)run.EstimatedLengthMm + "mm");
            Info("U1Q 桥架 run: " + runs + "（期望 1：P3→P4→P5 连续链合并）");
            if (runs != 1)
                throw new InvalidOperationException("桥架 run=" + runs + " 期望 1");

            // ---- ④ Scan → Inspect：验证路由与桥架归属；U1U 内嵌预览已停用 ----
            IReadOnlyList<QuickLineCadSegment> scanned =
                FillRouteInspectionStage.Scan(ctx).Segments;
            Info("Scan 线段: " + scanned.Count);
            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    regions.Groups[0].EntityIds, scanned, boundary, null);
                tr.Commit();
            }
            if (inspection.Preview != null)
                throw new InvalidOperationException(
                    "U1U 路由检查不应构建已停用的内嵌预览");
            Info("识别路由段数: " + inspection.Segments.Count);
            if (inspection.Segments.Count < 3)
                throw new InvalidOperationException("识别路由段数不足，期望 >=3");
            int trayOutputs = inspection.ParallelOutputs.Count(
                output => string.Equals(output.Kind, "U1Q",
                    StringComparison.OrdinalIgnoreCase));
            Info("当前图框桥架输出数: " + trayOutputs);
            if (trayOutputs < 1)
                throw new InvalidOperationException("桥架输出未归属到图框");

            // ---- ⑤ SaveAs 桌面样图 ----
            string target = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "UNCAD-3D验收-2F回路样图.dwg");
            ctx.Db.SaveAs(target, DwgVersion.Current);
            Info("样图已保存: " + target);
            Info("验收：开样图 → U13D → 选图框 → 应见 2F 建筑模型"
                + "（环氧地坪=场景地面、母线玻璃层 3660/4220/4900、桥架红色"
                + "双线沿 3900 标高、爬升 2200/4900 对齐 I-L 盘顶/第三层母线）");
            return "DONE sample=" + target;
        }

        private static void Info(string line)
        {
            Report.Add(line);
            Console.WriteLine("[ORBIT_SAMPLE] " + line);
        }

        private static void Finish(string verdict)
        {
            string text = verdict + Environment.NewLine
                + string.Join(Environment.NewLine, Report);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "uncad-orbit-sample.txt"),
                    text, Encoding.UTF8);
            }
            catch { }
            // 先读路径再覆盖环境变量（RouteDiag 注释在案的坑：原顺序会把
            // 路径先冲掉，run.ps1 永远读不到结果文件）。
            string resultPath = Environment.GetEnvironmentVariable(ResultVariable);
            Environment.SetEnvironmentVariable(ResultVariable, text);
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
            Console.WriteLine("[" + CommandName + "] " + verdict);
        }
    }
}

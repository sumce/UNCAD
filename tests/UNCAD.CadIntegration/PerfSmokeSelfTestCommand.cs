using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Geometry;
using UNCAD.Core.Stat;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 命令级性能冒烟基准（UNCAD_PERF_SMOKE），发布门槛用（release.ps1 默认执行，
    /// -SkipPerf 可跳过）。背景：2.9.0 的批量 U1U 存在 O(N²) 全图扫描，单帧 8.7 秒，
    /// 一天连发五个性能补丁；没有命令级基准时，这类回归只能到客户机器上才暴露。
    ///
    /// 在当前图纸（临时副本）里程序化生成"大图"：36 个图框（优先引用输入图纸中
    /// XFrameTemplate.dwg 的 FRAME_20261102 真实定义（FrameRegionCollector.
    /// CurrentFrameName，D-040）；没有则合成最小合法 xframe_v2 定义（上一代，
    /// XFrameV2Name））、3600 条随机 Line、每个图框 35 个统计文字 + 240 个框外
    /// 干扰文字。
    ///
    /// 计时两条历史热点读路径（与 U1U preflight 同一条链）：
    ///   1) Cad 层 FrameRegionCollector.Collect：整图图框分区收集；
    ///   2) Features→Cad→Core 层 FillSelectionCollector.Split +
    ///      FillStatisticsModule.Execute：按图框读统计文字并求和。
    ///
    /// 预算默认 30 秒（accoreconsole 无 GPU，宽松起步防误报），可用环境变量
    /// %UNCAD_PERF_SMOKE_BUDGET_MS% 覆盖（验证 FAIL 路径时设 1 即可）。
    /// 结果按自检约定写 %UNCAD_CAD_INTEGRATION_RESULT%（PASS / FAIL+明细），
    /// 耗时明细写控制台与 %UNCAD_PERF_SMOKE_REPORT%（缺省为临时目录）。
    /// </summary>
    public sealed class PerfSmokeSelfTestCommand
    {
        private const int FrameColumns = 6;
        private const int FrameRows = 6;                      // 36 个图框
        private const int ExpectedFrames = FrameColumns * FrameRows;
        private const int LineCount = 3600;                   // 随机 Line
        private const int TextsPerFrame = 35;                 // 33 DBText + 2 MText
        private const int MTextsPerFrame = 2;
        private const int OutsideTextCount = 240;             // 框外干扰文字
        private const double FrameGap = 12000;
        private const double GridBaseX = 5000000;
        private const double GridBaseY = 5000000;
        private const double SyntheticFrameWidth = 84000;
        private const double SyntheticFrameHeight = 59400;
        private const long DefaultBudgetMs = 30000;

        private static readonly List<string> Report = new List<string>();

        [CommandMethod("UNCAD_PERF_SMOKE")]
        public void Run()
        {
            try
            {
                WriteResult(RunCore());
            }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                WriteResult("FAIL\r\n" + ex);
            }
            finally
            {
                Dump();
            }
        }

        private static string RunCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            Info("=== UNCAD_PERF_SMOKE 性能冒烟基准 ===");
            Info("机器: " + Environment.MachineName + "；OS: " + Environment.OSVersion
                + "；处理器数: " + Environment.ProcessorCount);
            Info("图纸: " + document.Name);
            Info("覆盖层级: Cad(整图图框分区) + Features.Fill→Cad→Core(按图框统计文字求和，"
                + "与 U1U preflight 同一条链)；不覆盖表格写入与文件输出。");

            var fixtureWatch = Stopwatch.StartNew();
            PerfFixture fixture;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                fixture = BuildFixture(ctx, transaction);
                transaction.Commit();
            }
            fixtureWatch.Stop();
            Info("");
            Info("=== 夹具（生成耗时 " + fixtureWatch.ElapsedMilliseconds + " ms，不计入预算）===");
            Info("  图框: " + fixture.FrameIds.Count + " 个（" + fixture.FrameSource + "）");
            Info("  随机 Line: " + fixture.LineCount + " 条");
            Info("  框内统计文字: " + fixture.FrameTextCount + " 个；框外干扰文字: "
                + fixture.OutsideTextCount + " 个");

            // 阶段 1：整图 FrameRegionCollector 收集（批量 U1U 的热点）。
            // 取第一遍（冷启动）计时作为门槛值，第二遍仅作热态参考。
            FrameRegionCollection regions;
            var cold = Stopwatch.StartNew();
            regions = FrameRegionCollector.Collect(ctx, fixture.FrameIds.ToArray());
            cold.Stop();
            long collectCold = cold.ElapsedMilliseconds;
            ValidateCollection(regions);
            var warm = Stopwatch.StartNew();
            FrameRegionCollector.Collect(ctx, fixture.FrameIds.ToArray());
            warm.Stop();
            long collectWarm = warm.ElapsedMilliseconds;
            Info("");
            Info("=== 阶段 1) FrameRegionCollector.Collect 整图收集 ===");
            Info("  冷启动: " + collectCold + " ms；热态: " + collectWarm + " ms；图框分组: "
                + regions.Groups.Count + " 个");

            // 阶段 2：按图框 Split + 统计文字求和（U1U preflight 的同一条链）。
            StatisticsTotals coldTotals = RunPerFrameStatistics(ctx, regions);
            StatisticsTotals warmTotals = RunPerFrameStatistics(ctx, regions);
            Info("");
            Info("=== 阶段 2) 按图框 Split + FillStatisticsModule.Execute 统计求和 ===");
            Info("  冷启动: " + coldTotals.ElapsedMs + " ms；热态: " + warmTotals.ElapsedMs
                + " ms；图框: " + coldTotals.Frames + " 个；最慢单帧: "
                + coldTotals.WorstFrameMs + " ms");
            Info("  读取统计行: " + coldTotals.SourceLines + "；命中条目: "
                + coldTotals.Matches);

            long budget = ReadBudgetMs();
            long total = collectCold + coldTotals.ElapsedMs;
            Info("");
            Info("=== 预算判定 ===");
            Info("  整批收集 + 全图统计（冷启动合计）: " + total + " ms；预算: " + budget
                + " ms（accoreconsole 无 GPU 的保守值，可用 %UNCAD_PERF_SMOKE_BUDGET_MS% 覆盖）");
            string verdict;
            if (total > budget)
            {
                verdict = "FAIL\r\nUNCAD_PERF_SMOKE 性能冒烟超预算: 整图收集 " + collectCold
                    + " ms + 按图框统计求和 " + coldTotals.ElapsedMs + " ms = " + total
                    + " ms > 预算 " + budget + " ms。最慢单帧 " + coldTotals.WorstFrameMs
                    + " ms。各阶段耗时明细见控制台输出与性能报告文件；"
                    + "疑似 O(N²) 全图扫描回归（参照 2.9.0 批量 U1U 单帧 8.7 秒事故）。";
                Info("  结论: FAIL（超预算 " + (total - budget) + " ms）");
            }
            else
            {
                verdict = "PASS";
                Info("  结论: PASS（余量 " + (budget - total) + " ms）");
            }
            Info("VERDICT: " + verdict);
            return verdict;
        }

        private sealed class PerfFixture
        {
            public readonly List<ObjectId> FrameIds = new List<ObjectId>();
            public int LineCount;
            public int FrameTextCount;
            public int OutsideTextCount;
            public string FrameSource = "";
        }

        /// <summary>生成大图夹具。图框优先引用真实 FRAME_20261102 定义；否则合成
        /// 上一代 xframe_v2 定义（两者都仍被 FrameRegionCollector 识别）。</summary>
        private static PerfFixture BuildFixture(CadContext ctx, Transaction transaction)
        {
            Database db = ctx.Db;
            var blocks = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var space = (BlockTableRecord)transaction.GetObject(db.CurrentSpaceId,
                OpenMode.ForWrite);
            var fixture = new PerfFixture();

            ObjectId definitionId;
            if (blocks.Has(FrameRegionCollector.CurrentFrameName))
            {
                definitionId = blocks[FrameRegionCollector.CurrentFrameName];
                fixture.FrameSource = "输入图纸中的真实定义 "
                    + FrameRegionCollector.CurrentFrameName;
            }
            else
            {
                definitionId = CreateSyntheticFrameDefinition(transaction, blocks);
                fixture.FrameSource = "合成的最小合法定义 "
                    + FrameRegionCollector.XFrameV2Name + "（上一代；输入图纸中没有 "
                    + FrameRegionCollector.CurrentFrameName + "）";
            }

            // 探针图框量出真实边界，后续插入按同一偏移铺网格，边界即探针边界平移。
            var probe = new BlockReference(new Point3d(GridBaseX, GridBaseY, 0), definitionId);
            space.AppendEntity(probe);
            transaction.AddNewlyCreatedDBObject(probe, true);
            fixture.FrameIds.Add(probe.Id);
            FrameRectangle boundary = FrameRegionCollector.ReadBoundary(transaction, probe);
            double width = boundary.Width;
            double height = boundary.Height;
            double stepX = width + FrameGap;
            double stepY = height + FrameGap;
            for (int index = 1; index < ExpectedFrames; index++)
            {
                var offset = new Vector3d((index % FrameColumns) * stepX,
                    -(index / FrameColumns) * stepY, 0);
                var reference = new BlockReference(probe.Position.Add(offset), definitionId);
                space.AppendEntity(reference);
                transaction.AddNewlyCreatedDBObject(reference, true);
                fixture.FrameIds.Add(reference.Id);
            }

            var random = new Random(20261102);
            double marginX = width * 0.06;
            double marginY = height * 0.10;
            const int textColumns = 5;
            int textRows = TextsPerFrame / textColumns;
            double textStepX = (width - 2 * marginX) / (textColumns - 1);
            double textStepY = (height - 2 * marginY) / (textRows - 1);
            double textHeight = height * 0.012;
            for (int frame = 0; frame < ExpectedFrames; frame++)
            {
                double minX = boundary.MinX + (frame % FrameColumns) * stepX;
                double maxY = boundary.MaxY - (frame / FrameColumns) * stepY;
                for (int slot = 0; slot < TextsPerFrame; slot++)
                {
                    double x = minX + marginX + (slot % textColumns) * textStepX;
                    double y = maxY - marginY - (slot / textColumns) * textStepY;
                    string content = StatisticsContent(random, slot);
                    Entity entity = slot < TextsPerFrame - MTextsPerFrame
                        ? (Entity)new DBText
                        {
                            Position = new Point3d(x, y, 0),
                            Height = textHeight,
                            TextString = content
                        }
                        : new MText
                        {
                            Location = new Point3d(x, y, 0),
                            TextHeight = textHeight,
                            Contents = content
                        };
                    space.AppendEntity(entity);
                    transaction.AddNewlyCreatedDBObject(entity, true);
                    fixture.FrameTextCount++;
                }
            }

            // 随机 Line：85% 起点落在图框内（部分越界，验证跨界几何不被误归属），
            // 其余放在网格右侧的空旷带。
            double outerBaseX = boundary.MinX + FrameColumns * stepX + width * 0.6;
            double outerMinY = boundary.MinY - (FrameRows - 1) * stepY;
            double outerSpanY = height * (FrameRows + 2);
            for (int index = 0; index < LineCount; index++)
            {
                Point3d start;
                if (index % 100 < 85)
                {
                    int frame = random.Next(ExpectedFrames);
                    double minX = boundary.MinX + (frame % FrameColumns) * stepX;
                    double minY = boundary.MinY - (frame / FrameColumns) * stepY;
                    start = new Point3d(
                        minX + width * (0.03 + random.NextDouble() * 0.94),
                        minY + height * (0.03 + random.NextDouble() * 0.94), 0);
                }
                else
                {
                    start = new Point3d(outerBaseX + random.NextDouble() * width * 3,
                        outerMinY + random.NextDouble() * outerSpanY, 0);
                }
                double angle = random.NextDouble() * Math.PI * 2;
                double length = width * (0.05 + random.NextDouble() * 0.4);
                var line = new Line(start, new Point3d(
                    start.X + Math.Cos(angle) * length,
                    start.Y + Math.Sin(angle) * length, 0));
                space.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
                fixture.LineCount++;
            }

            for (int index = 0; index < OutsideTextCount; index++)
            {
                var text = new DBText
                {
                    Position = new Point3d(outerBaseX + random.NextDouble() * width * 3,
                        outerMinY + random.NextDouble() * outerSpanY, 0),
                    Height = textHeight,
                    TextString = StatisticsContent(random, index)
                };
                space.AppendEntity(text);
                transaction.AddNewlyCreatedDBObject(text, true);
                fixture.OutsideTextCount++;
            }
            return fixture;
        }

        /// <summary>
        /// 统计文字内容：裸长度（按电缆统计）、⌀20线管 标注（按线管统计）与
        /// 型号+长度（本引擎不命中，作为真实噪声）。统计引擎只认整十毫米数
        /// （TextParser 的 ^([0-9]+0)mm$ 约定），长度按 10 取整。
        /// </summary>
        private static string StatisticsContent(Random random, int slot)
        {
            int length = (60 + random.Next(420)) * 10;
            switch (slot % 10)
            {
                case 0:
                case 1:
                    return "YJV-4*2.5 " + length + "mm";
                case 2:
                    return "\u230020线管 " + length + "mm";
                default:
                    return length + "mm";
            }
        }

        private static ObjectId CreateSyntheticFrameDefinition(Transaction transaction,
            BlockTable blocks)
        {
            var definition = new BlockTableRecord
            {
                Name = FrameRegionCollector.XFrameV2Name
            };
            ObjectId id = blocks.Add(definition);
            transaction.AddNewlyCreatedDBObject(definition, true);
            var border = new Polyline();
            border.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            border.AddVertexAt(1, new Point2d(SyntheticFrameWidth, 0), 0, 0, 0);
            border.AddVertexAt(2, new Point2d(SyntheticFrameWidth, SyntheticFrameHeight), 0, 0, 0);
            border.AddVertexAt(3, new Point2d(0, SyntheticFrameHeight), 0, 0, 0);
            border.Closed = true;
            definition.AppendEntity(border);
            transaction.AddNewlyCreatedDBObject(border, true);
            return id;
        }

        private static void ValidateCollection(FrameRegionCollection regions)
        {
            if (regions.Errors.Count > 0)
                throw new InvalidOperationException("图框分区报错: "
                    + string.Join("; ", regions.Errors));
            if (regions.SelectedFrameCount != ExpectedFrames
                || regions.Groups.Count != ExpectedFrames)
                throw new InvalidOperationException("应收集到 " + ExpectedFrames
                    + " 个图框分组，实际 " + regions.Groups.Count + " 个（选中 "
                    + regions.SelectedFrameCount + "）。");
            int expectedPerFrame = 1 + TextsPerFrame;
            foreach (FrameRegionGroup group in regions.Groups)
                if (group.EntityIds.Count != expectedPerFrame)
                    throw new InvalidOperationException("图框 " + group.Handle + " 应归属 "
                        + expectedPerFrame + " 个对象（图框+统计文字），实际 "
                        + group.EntityIds.Count + " 个；归属或夹具回归。");
        }

        private sealed class StatisticsTotals
        {
            public int Frames;
            public long SourceLines;
            public long Matches;
            public long WorstFrameMs;
            public long ElapsedMs;
        }

        /// <summary>按图框执行 Split + 统计求和；与 BatchFillUpdateCoordinator.Preflight 同链。</summary>
        private static StatisticsTotals RunPerFrameStatistics(CadContext ctx,
            FrameRegionCollection regions)
        {
            // 显式快照而不是 StatisticsSettings.Current()：注册表里的 U1SET 开关
            // （本机 IncludeText=false）会让基准输入行数随机器配置漂移，断言和计时
            // 都不可复现。这里与出厂默认一致：三类来源全开，mm/格 250。
            StatisticsSettingsSnapshot settings = new StatisticsSettingsSnapshot
            {
                IncludeText = true,
                IncludeMText = true,
                IncludeDimension = true,
                Calculation = new StatCalculationOptions
                {
                    MmPerGrid = 250.0,
                    IncludeCable = true,
                    IncludeBridge = true,
                    IncludeConduit = true
                }
            };
            var totals = new StatisticsTotals();
            var watch = Stopwatch.StartNew();
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(transaction);
                foreach (FrameRegionGroup region in regions.Groups)
                {
                    var frameWatch = Stopwatch.StartNew();
                    FillSelection selection = FillSelectionCollector.Split(transaction,
                        region.EntityIds.ToArray(), true, definitions);
                    if (selection.TextIds.Length != TextsPerFrame)
                        throw new InvalidOperationException("图框 " + region.Handle
                            + " 应识别 " + TextsPerFrame + " 个统计文字，实际 "
                            + selection.TextIds.Length + " 个。");
                    SummationOutput output = FillStatisticsModule.Execute(ctx, transaction,
                        selection.TextIds, settings.Calculation.MmPerGrid,
                        selection.StatisticsScopeComplete, settings);
                    frameWatch.Stop();
                    if (output.SourceLineCount != TextsPerFrame)
                        throw new InvalidOperationException("图框 " + region.Handle
                            + " 应读取 " + TextsPerFrame + " 行统计输入，实际 "
                            + output.SourceLineCount + " 行。");
                    if (output.TotalMatchCount < 10)
                        throw new InvalidOperationException("图框 " + region.Handle
                            + " 统计命中异常偏少: " + output.TotalMatchCount
                            + " 条，统计链路疑似退化。");
                    totals.Frames++;
                    totals.SourceLines += output.SourceLineCount;
                    totals.Matches += output.TotalMatchCount;
                    if (frameWatch.ElapsedMilliseconds > totals.WorstFrameMs)
                        totals.WorstFrameMs = frameWatch.ElapsedMilliseconds;
                }
                transaction.Commit();
            }
            watch.Stop();
            totals.ElapsedMs = watch.ElapsedMilliseconds;
            return totals;
        }

        private static long ReadBudgetMs()
        {
            string raw = Environment.GetEnvironmentVariable("UNCAD_PERF_SMOKE_BUDGET_MS");
            if (!string.IsNullOrWhiteSpace(raw)
                && long.TryParse(raw.Trim(), out long budget) && budget > 0)
            {
                Info("预算覆盖: %UNCAD_PERF_SMOKE_BUDGET_MS%=" + budget + " ms");
                return budget;
            }
            return DefaultBudgetMs;
        }

        private static void Info(string line)
        {
            Report.Add(line);
            try
            {
                Application.DocumentManager.MdiActiveDocument.Editor
                    .WriteMessage("\n" + line + "\n");
            }
            catch
            {
                // 控制台不可用时报告文件仍然完整。
            }
        }

        private static void Dump()
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_PERF_SMOKE_REPORT");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "uncad-perf-smoke-report.txt");
            try
            {
                File.WriteAllText(path, string.Join("\r\n", Report.ToArray()),
                    new UTF8Encoding(true));
                Application.DocumentManager.MdiActiveDocument.Editor
                    .WriteMessage("\nPERF-SMOKE report: " + path + "\n");
            }
            catch
            {
                // 报告文件写不进去时，结果文件与控制台仍然可用。
            }
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (string.IsNullOrWhiteSpace(path)) return;
            // 带 BOM：run.ps1 用 Get-Content -Raw 读取，FAIL 明细里的中文不能变乱码。
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
    }
}

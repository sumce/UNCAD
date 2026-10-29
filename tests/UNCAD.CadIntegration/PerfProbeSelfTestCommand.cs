using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Excel;
using UNCAD.Features.Unl;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 只读性能探针（UNCAD_PERF_SELFTEST）：量出批量 U1F/U1U 的每帧固定开销来源。
    /// 不改图纸；报告写到 %UNCAD_PERF_REPORT%。
    /// </summary>
    public sealed class PerfProbeSelfTestCommand
    {
        private static readonly List<string> Lines = new List<string>();

        [CommandMethod("UNCAD_PERF_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                Report(document);
                WriteResult("PASS");
            }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                WriteResult("FAIL\r\n" + ex);
                throw;
            }
            finally
            {
                Dump();
            }
        }

        private static void Report(Document document)
        {
            Database db = document.Database;
            Info("图纸: " + document.Name);
            int entities = 0;
            int inserts = 0;
            var insertIds = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    entities++;
                    if (tr.GetObject(id, OpenMode.ForRead, true) is BlockReference)
                    {
                        inserts++;
                        insertIds.Add(id);
                    }
                }
                tr.Commit();
            }
            Info("模型空间实体: " + entities + "，块参照: " + inserts);

            // 1) QuickLineCadService.Scan：批量场景构建以前每个图框调一次（全空间扫描）。
            Info("");
            Info("=== 1) QuickLineCadService.Scan（整图扫描一次）===");
            var watch = Stopwatch.StartNew();
            IReadOnlyList<QuickLineCadSegment> scanned = QuickLineCadService.Scan(
                new CadContext(document), new QuickLineScanOptions { IncludeUnlabelled = true });
            watch.Stop();
            Info("  扫描一次耗时: " + watch.ElapsedMilliseconds + " ms，线段数: " + scanned.Count);

            // 2) 图框数量与 BuildRegionEnvelope 的每帧耗时（复用同一次扫描）。
            Info("");
            Info("=== 2) 逐帧场景构建（复用已扫描结果）===");
            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(
                new CadContext(document), insertIds.ToArray());
            Info("  图框数: " + regions.Groups.Count + "，错误: " + regions.Errors.Count);
            int limit = Math.Min(20, regions.Groups.Count);
            long total = 0;
            for (int index = 0; index < limit; index++)
            {
                watch.Restart();
                UNCAD.Features.Unl.QuickLine3dPreviewFeature.TryBuildSceneJson(
                    new CadContext(document),
                    regions.Groups[index], scanned, null,
                    out string json, out string issue);
                watch.Stop();
                total += watch.ElapsedMilliseconds;
                Info("  图框 " + (index + 1) + "：" + watch.ElapsedMilliseconds
                    + " ms，" + (json.Length > 0 ? json.Length + " 字符" : issue));
            }
            Info("  平均每帧: " + (limit == 0 ? 0 : total / limit) + " ms（复用扫描后）");

            // 3) SQLite 快照查询每次的耗时（场景构建旧实现每帧查一次）。
            Info("");
            Info("=== 3) SQLite 快照按机台查询（旧实现每帧一次）===");
            string source = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            if (source.Length > 0 && MachineWorkbookSource.TryGetSnapshot(source, out _))
            {
                List<string> machineIds = MachineWorkbookSnapshotStore.Default
                    .ReadMachineIds(source);
                string machineId = machineIds.Count > 0 ? machineIds[0] : "";
                if (machineId.Length > 0)
                {
                    for (int index = 0; index < 3; index++)
                    {
                        watch.Restart();
                        MachineWorkbookSnapshotStore.Default.ReadRowsForMachines(
                            source, new[] { machineId });
                        watch.Stop();
                        Info("  第 " + (index + 1) + " 次查询 " + machineId + ": "
                            + watch.ElapsedMilliseconds + " ms");
                    }
                }
            }
            else
            {
                Info("  （未配置快照，跳过）");
            }
        }

        private static void Info(string line) => Lines.Add(line);

        private static void Dump()
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_PERF_REPORT");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "uncad-perf-report.txt");
            try
            {
                File.WriteAllText(path, string.Join("\r\n", Lines.ToArray()),
                    new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, text);
        }
    }
}

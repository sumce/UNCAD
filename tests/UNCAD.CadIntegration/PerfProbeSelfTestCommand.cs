using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Excel;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 只读性能探针（UNCAD_PERF_SELFTEST）：量出 U1L3D/U13D 的整图扫描成本
    /// 与 SQLite 按机台查询成本。不改图纸；报告写到 %UNCAD_PERF_REPORT%。
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
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    entities++;
                    if (tr.GetObject(id, OpenMode.ForRead, true) is BlockReference) inserts++;
                }
                tr.Commit();
            }
            Info("模型空间实体: " + entities + "，块参照: " + inserts);

            // 1) QuickLineCadService.Scan：U1L3D/U13D 每条命令一次的全图扫描。
            Info("");
            Info("=== 1) QuickLineCadService.Scan（整图扫描一次）===");
            var watch = Stopwatch.StartNew();
            IReadOnlyList<QuickLineCadSegment> scanned = QuickLineCadService.Scan(
                new CadContext(document), new QuickLineScanOptions { IncludeUnlabelled = true });
            watch.Stop();
            Info("  扫描一次耗时: " + watch.ElapsedMilliseconds + " ms，线段数: " + scanned.Count);

            // 2) SQLite 快照按机台查询的耗时（逐帧读取机台的主要固定开销）。
            Info("");
            Info("=== 2) SQLite 快照按机台查询 ===");
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

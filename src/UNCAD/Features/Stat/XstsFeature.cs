using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Report;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Stat
{
    /// <summary>Analyzes selected frames and reports selected/missing circuits.</summary>
    [Feature("xsts", "回路统计", Commands = CommandIds.XstsFeatureCommands,
        Description = "框选多张图纸，统计机台回路数量并导出 Excel")]
    public sealed class XstsFeature : CommandBase
    {
        [CommandMethod(CommandIds.Statistics, CommandFlags.UsePickSet)]
        public void Statistics() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Statistics);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请选择要统计的 frame_20260812 图纸: ", new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[XSTS] 未选择图纸，未执行统计。");
                return;
            }
            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, selected);
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors) ctx.Write("\n[XSTS] " + error);
                return;
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[XSTS] 未找到有效图纸。");
                return;
            }

            var selectedRows = new List<XstsCircuitRecord>();
            foreach (FrameRegionGroup region in regions.Groups)
            {
                SubmissionRecord record = FrameIdentityReader.Read(ctx, region);
                selectedRows.Add(new XstsCircuitRecord(record.MachineId, record.DeviceName));
            }

            // When an Excel machine workbook is configured, use it as the expected set,
            // but scope it strictly to machine IDs present in the current selection.
            // Never compare one drawing against the whole project workbook: that makes
            // unrelated machines appear as thousands of missing circuits.
            var expectedRows = new List<XstsCircuitRecord>(selectedRows);
            string workbookPath = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            if (File.Exists(workbookPath))
            {
                try
                {
                    var selectedMachines = new HashSet<string>(selectedRows
                        .Select(item => item.MachineId.Trim())
                        .Where(value => value.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (MachineRow row in ExcelMachineReader.ReadRows(workbookPath))
                    {
                        if (!selectedMachines.Contains((row.MachineId ?? "").Trim())) continue;
                        expectedRows.Add(new XstsCircuitRecord(row.MachineId, row.CircuitName));
                    }
                }
                catch (System.Exception ex)
                {
                    ctx.Write("\n[XSTS] Excel 期望回路读取失败，已按当前图纸统计: " + ex.Message);
                }
            }
            XstsReport report = XstsReportBuilder.Build(selectedRows, expectedRows);
            string outputRoot = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            if (outputRoot.Length == 0) outputRoot = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            Directory.CreateDirectory(outputRoot);
            string path = Path.Combine(outputRoot, "UNCAD_XSTS_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
            XstsExcelReporter.WriteToFile(report, path);
            ctx.Write("\n[XSTS] 统计完成: " + report.Machines.Count + " 个机台、"
                + report.FrameCount + " 张图纸，"
                + report.SelectedCircuitCount + " 个已选回路，缺少 "
                + report.MissingCircuitCount + " 个回路。Excel: " + path);
            using (var form = new XstsForm(report, path))
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        }
    }
}

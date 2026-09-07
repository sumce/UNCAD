using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
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
                "\n请选择要统计的图框（" + FrameRegionCollector.SupportedFrameDescription + "）: ",
                new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[XSTS] 未选择图纸，未执行统计。");
                return;
            }
            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, selected);
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors) ctx.Write("\n[XSTS] " + error);
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[XSTS] 未找到有效图纸。");
                return;
            }

            var selectedRows = new List<XstsCircuitRecord>();
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(transaction);
                foreach (FrameRegionGroup region in regions.Groups)
                {
                    try
                    {
                        ExistingFillIdentity identity = FrameIdentityReader.ReadIdentity(
                            transaction, region, definitions);
                        selectedRows.Add(new XstsCircuitRecord(identity.MachineId,
                            identity.DeviceName));
                    }
                    catch (System.Exception ex)
                    {
                        // Preserve individual issue rows even though the read transaction is shared.
                        string detail = "图框 " + region.Handle + " 身份读取失败: " + ex.Message;
                        ctx.Write("\n[XSTS] " + detail);
                        selectedRows.Add(new XstsCircuitRecord("", "", detail));
                    }
                }
                transaction.Commit();
            }

            // When an Excel machine workbook is configured, use it as the expected set,
            // but scope it strictly to machine IDs present in the current selection.
            // Never compare one drawing against the whole project workbook: that makes
            // unrelated machines appear as thousands of missing circuits.
            var expectedRows = new List<XstsCircuitRecord>();
            XstsExpectedDataStatus expectedStatus;
            string expectedDetail;
            string configuredPath = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            if (configuredPath.Length == 0)
            {
                expectedStatus = XstsExpectedDataStatus.NotConfigured;
                expectedDetail = "未配置机台 Excel";
            }
            else
            {
                try
                {
                    if (!MachineWorkbookSource.TryGetSnapshot(configuredPath,
                        out MachineWorkbookSnapshotInfo snapshot))
                    {
                        expectedStatus = XstsExpectedDataStatus.FileNotFound;
                        expectedDetail = "机台数据尚未刷新到 SQLite，请在 U1SET 中点击“刷新”";
                    }
                    else
                    {
                        var selectedMachines = new HashSet<string>(selectedRows
                            .Select(item => item.MachineId.Trim())
                            .Where(value => value.Length > 0),
                            StringComparer.OrdinalIgnoreCase);
                        foreach (MachineRow row in MachineWorkbookSnapshotStore.Default
                            .ReadRowsForMachines(configuredPath, selectedMachines))
                        {
                            if (!selectedMachines.Contains((row.MachineId ?? "").Trim())) continue;
                            expectedRows.Add(new XstsCircuitRecord(row.MachineId,
                                row.CircuitName));
                        }
                        expectedStatus = XstsExpectedDataStatus.Available;
                        expectedDetail = "SQLite 快照（手动刷新时间 "
                            + (snapshot?.RefreshedUtc ?? "未知") + "）";
                    }
                }
                catch (System.Exception ex)
                {
                    expectedStatus = XstsExpectedDataStatus.ReadFailed;
                    expectedDetail = "SQLite 机台快照读取失败: " + ex.Message;
                }
            }
            XstsReport report = XstsReportBuilder.Build(selectedRows, expectedRows,
                expectedStatus, expectedDetail);
            // Include frames whose boundary could not be collected. They are still part of
            // the user's selection and are listed in the issue sheet; omitting them from the
            // headline would make the physical frame count disagree with the diagnostics.
            report.FrameCount = regions.SelectedFrameCount;
            foreach (string error in regions.Errors)
                report.Issues.Add(new XstsFrameIssue { Description = error });
            string outputRoot = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            if (outputRoot.Length == 0) outputRoot = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            Directory.CreateDirectory(outputRoot);
            string path = Path.Combine(outputRoot, "UNCAD_XSTS_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".xlsx");
            XstsExcelReporter.WriteToFile(report, path);
            string coverage = report.ExpectedDataAvailable
                && !report.HasUnknownMachineBaseline
                ? "缺少 " + report.MissingCircuitCount + " 个回路，多出 "
                    + report.UnexpectedCircuitCount + " 个回路"
                : report.ExpectedDataAvailable
                    ? "部分机台缺少/多出回路无法判断（" + report.ExpectedDataDetail + "）"
                    : "缺少回路无法判断（" + report.ExpectedDataDetail + "）";
            ctx.Write("\n[XSTS] 统计完成: " + report.Machines.Count + " 个机台、"
                + report.FrameCount + " 张图纸，"
                + report.SelectedCircuitCount + " 个有效已选回路，" + coverage
                + "，异常 " + report.Issues.Count + " 项。Excel: " + path);
            using (var form = new XstsForm(report, path))
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        }
    }
}

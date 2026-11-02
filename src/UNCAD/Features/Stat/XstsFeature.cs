using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using UNCAD.Core.Text;
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
        // 只在明显变慢时输出一行 [PERF]，避免正常命令刷屏日志。
        private const long PerfThresholdMilliseconds = 500;

        [CommandMethod(CommandIds.Statistics, CommandFlags.UsePickSet)]
        public void Statistics() => Run();

        /// <summary>
        /// XSTS 报告文件路径（纯路径计算，不创建目录）：直接放输出根目录
        /// <c>&lt;root&gt;\UNCAD_XSTS_时间戳.xlsx</c>（D-035 恢复 2.9.x 现场习惯，
        /// 不再按图纸/类型分层；根目录由 <see cref="OutputLayout.NormalizeRoot"/> 归一化）。
        /// </summary>
        internal static string ResolveOutputFile(string root, DateTime now)
        {
            return Path.Combine(root, "UNCAD_XSTS_"
                + now.ToString("yyyyMMdd_HHmmss_fff") + ".xlsx");
        }

        /// <summary>
        /// 准备 XSTS 输出目录并返回报告路径（创建目录 + 写占用预检 + 默认根写回配置），
        /// 失败抛出带中文原因的异常，由调用方统一提示且不写盘。
        /// </summary>
        private static string PrepareOutputFile(CadContext ctx)
        {
            string configuredRoot = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            string root = OutputLayout.NormalizeRoot(configuredRoot);
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (System.Exception ex)
            {
                throw new DirectoryNotFoundException(
                    "无法创建 XSTS 输出文件夹：" + root + "（" + ex.Message + "）", ex);
            }
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("XSTS 输出文件夹不存在：" + root);
            if (!string.Equals(configuredRoot, root, StringComparison.OrdinalIgnoreCase))
                Settings.Set(ConfigKeys.SubmitFolder, root);
            string path = ResolveOutputFile(root, DateTime.Now);
            // 报告文件若同名已存在且被占用，必须在写盘前中止并给出中文提示。
            OutputLayout.ValidateNotLocked(new[] { path });
            return path;
        }

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
            var perfWatch = Stopwatch.StartNew();
            var phaseWatch = Stopwatch.StartNew();
            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, selected);
            long collectMilliseconds = phaseWatch.ElapsedMilliseconds;
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors) ctx.Write("\n[XSTS] " + error);
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[XSTS] 未找到有效图纸。");
                return;
            }

            phaseWatch.Restart();
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
            long identityMilliseconds = phaseWatch.ElapsedMilliseconds;

            phaseWatch.Restart();
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
                        expectedDetail = "机台数据尚未刷新到 SQLite，请执行 U1DATA 或在 U1SET 中点击“刷新”";
                    }
                    else
                    {
                        var selectedMachines = new HashSet<string>(selectedRows
                            .Select(item => item.MachineId.Trim())
                            .Where(value => value.Length > 0),
                            IdentityTextNormalizer.Comparer);
                        foreach (MachineRow row in MachineWorkbookSnapshotStore.Default
                            .ReadRowsForMachines(configuredPath, selectedMachines))
                        {
                            if (!selectedMachines.Contains((row.MachineId ?? "").Trim())) continue;
                            expectedRows.Add(new XstsCircuitRecord(row.MachineId,
                                row.CircuitName, row.Batch, row.Seq));
                        }
                        expectedStatus = XstsExpectedDataStatus.Available;
                        expectedDetail = "SQLite 快照（最后成功刷新时间 "
                            + (snapshot?.RefreshedUtc ?? "未知") + "）";
                    }
                }
                catch (System.Exception ex)
                {
                    expectedStatus = XstsExpectedDataStatus.ReadFailed;
                    expectedDetail = "SQLite 机台快照读取失败: " + ex.Message;
                }
            }
            long expectedMilliseconds = phaseWatch.ElapsedMilliseconds;
            phaseWatch.Restart();
            XstsReport report = XstsReportBuilder.Build(selectedRows, expectedRows,
                expectedStatus, expectedDetail);
            long reportMilliseconds = phaseWatch.ElapsedMilliseconds;
            // Include frames whose boundary could not be collected. They are still part of
            // the user's selection and are listed in the issue sheet; omitting them from the
            // headline would make the physical frame count disagree with the diagnostics.
            report.FrameCount = regions.SelectedFrameCount;
            foreach (string error in regions.Errors)
                report.Issues.Add(new XstsFrameIssue { Description = error });
            // 输出直接放输出根目录（D-035，恢复 2.9.x 现场习惯）：<输出根>\UNCAD_XSTS_<时间>.xlsx。
            // 目录/占用预检在写盘之前完成；输出根为空时默认<我的文档>\UNCAD输出并写回 UNC_SUBMIT_FOLDER。
            string path;
            try
            {
                path = PrepareOutputFile(ctx);
            }
            catch (System.Exception ex)
            {
                Log.Error("XSTS 准备输出目录失败", ex);
                ctx.Write("\n[XSTS] 无法准备输出目录，未生成报告：\n[XSTS]   " + ex.Message);
                return;
            }
            phaseWatch.Restart();
            try
            {
                XstsExcelReporter.WriteToFile(report, path);
            }
            catch (System.Exception ex)
            {
                Log.Error("XSTS 报告写入失败", ex);
                ctx.Write("\n[XSTS] 报告写入失败，未生成 Excel：\n[XSTS]   " + ex.Message);
                return;
            }
            long excelMilliseconds = phaseWatch.ElapsedMilliseconds;
            perfWatch.Stop();
            if (perfWatch.ElapsedMilliseconds >= PerfThresholdMilliseconds)
            {
                Log.Info("[PERF] XSTS total=" + perfWatch.ElapsedMilliseconds + "ms"
                    + " collect=" + collectMilliseconds + "ms"
                    + " identity=" + identityMilliseconds + "ms"
                    + " expected=" + expectedMilliseconds + "ms"
                    + " report=" + reportMilliseconds + "ms"
                    + " xlsx=" + excelMilliseconds + "ms"
                    + " frames=" + regions.SelectedFrameCount
                    + " machines=" + report.Machines.Count
                    + " rows=" + (selectedRows.Count + expectedRows.Count));
            }
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

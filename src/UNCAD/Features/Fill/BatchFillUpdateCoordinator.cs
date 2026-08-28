using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// Preflights and writes several already-filled frame regions without reopening the machine
    /// picker or one review dialog per frame. New generation remains in FillFeature's single path.
    /// </summary>
    internal static class BatchFillUpdateCoordinator
    {
        private sealed class Plan
        {
            public FrameRegionGroup Region { get; set; }
            public FillSelection Selection { get; set; }
            public MachineRow Machine { get; set; }
            public SummationOutput Summation { get; set; }
            public CableStatResult Statistics { get; set; }
            public List<TableFillRow> Rows { get; set; }
        }

        public static void Execute(CadContext ctx, IReadOnlyList<FrameRegionGroup> regions)
        {
            if (regions == null || regions.Count < 2) return;
            FillRuntimeOptions options = FillSettings.Current();
            string path = FillFeature.ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null) return;

            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_UPDATE] 读取机台 Excel 失败: " + ex.Message);
                Log.Error("UNC_UPDATE batch read machine excel failed", ex);
                return;
            }
            if (workbook.MachineIds.Count == 0 || workbook.ListItems.Count == 0)
            {
                ctx.Write("\n[UNC_UPDATE] 机台 Excel 或内嵌固定清单没有可用数据。");
                return;
            }

            var plans = new List<Plan>();
            var errors = new List<string>();
            foreach (FrameRegionGroup region in regions)
                Preflight(ctx, region, options, workbook, plans, errors);

            if (errors.Count > 0)
            {
                string message = "批量更新预检失败，图纸未修改：\r\n\r\n"
                    + string.Join("\r\n", errors.Take(20));
                if (errors.Count > 20) message += "\r\n其余 " + (errors.Count - 20) + " 项请查看日志。";
                MessageBox.Show(Owner(), message, "UNC_UPDATE 批量预检",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                foreach (string error in errors) ctx.Write("\n[UNC_UPDATE] " + error);
                return;
            }

            string automaticExcelPath;
            try
            {
                AutomaticSubmissionService.ValidateIdentityKeys(plans.Select(plan =>
                    new KeyValuePair<string, string>(plan.Machine.MachineId,
                        plan.Machine.CircuitName)));
                automaticExcelPath = AutomaticSubmissionService.PrepareTargetPath();
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_UPDATE] Excel 自动记录路径不可用，图纸未修改: "
                    + ex.Message);
                return;
            }

            var summary = new StringBuilder();
            summary.AppendLine("将按图框边界批量更新 " + plans.Count + " 个已填图框：");
            summary.AppendLine();
            foreach (Plan plan in plans.Take(15))
            {
                summary.AppendLine("图框 " + plan.Region.Handle + "  |  "
                    + plan.Machine.MachineId + " / " + plan.Machine.CircuitName
                    + "  |  清单 " + plan.Rows.Count + " 项");
            }
            if (plans.Count > 15) summary.AppendLine("其余 " + (plans.Count - 15) + " 个图框...");
            summary.AppendLine();
            summary.Append("所有图框将在一个事务中写入，任一失败则整批回滚。");
            if (MessageBox.Show(Owner(), summary.ToString(), "UNC_UPDATE 批量确认",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1) != DialogResult.OK) return;

            int tableRows = 0;
            int frameBlocks = 0;
            int attributeValues = 0;
            try
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (Plan plan in plans)
                    {
                        int filled = FillTableModule.Write(ctx, transaction,
                            plan.Selection.TableIds, options.StartRow, options.ClearRows,
                            plan.Rows, options.TextHeight);
                        if (filled < 0)
                            throw new InvalidOperationException("图框 " + plan.Region.Handle
                                + " 的清单表写入失败。");
                        tableRows += filled;

                        FillWriteResult frame = CadBlockAttributeWriter.FillFrame(ctx, transaction,
                            plan.Selection.FrameBlockIds, plan.Machine,
                            options.BridgeInfo, plan.Statistics);
                        FillWriteResult device = CadBlockAttributeWriter.FillDeviceName(ctx,
                            transaction, plan.Selection.DeviceBlockIds,
                            DeviceBlockFiller.BuildValue(plan.Machine));
                        FillWriteResult upstreamInfo = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.UpstreamInfoBlockIds,
                            ConnectionBlockFiller.TagUpstreamInfo,
                            ConnectionBlockFiller.UpstreamInfo(plan.Machine), true);
                        FillWriteResult upstreamAxis = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.UpstreamAxisBlockIds,
                            ConnectionBlockFiller.TagUpstreamAxis,
                            ConnectionBlockFiller.UpstreamAxis(plan.Machine), false);
                        FillWriteResult downstreamAxis = CadBlockAttributeWriter.FillTagged(ctx,
                            transaction, plan.Selection.DownstreamAxisBlockIds,
                            ConnectionBlockFiller.TagDownstreamAxis,
                            ConnectionBlockFiller.DownstreamAxis(plan.Machine), false);
                        frameBlocks += frame.Blocks + device.Blocks + upstreamInfo.Blocks
                            + upstreamAxis.Blocks + downstreamAxis.Blocks;
                        attributeValues += frame.Values + device.Values + upstreamInfo.Values
                            + upstreamAxis.Values + downstreamAxis.Values;
                    }
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("UNC_UPDATE batch write failed", ex);
                ctx.Write("\n[UNC_UPDATE] 批量写入失败，整批已回滚: " + ex.Message);
                MessageBox.Show(Owner(), "批量写入失败，所有图框修改均已回滚。\r\n\r\n" + ex.Message,
                    "UNC_UPDATE", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AutomaticSubmissionWriteResult automaticExcel;
            try
            {
                automaticExcel = AutomaticSubmissionService.Write(ctx, automaticExcelPath,
                    regions.Select(region => region.EntityIds.ToArray()));
            }
            catch (System.Exception ex)
            {
                Log.Error("UNC_UPDATE automatic Excel update failed after CAD commit", ex);
                throw new InvalidOperationException("CAD 已批量更新，但 Excel 自动更新失败："
                    + ex.Message + "；目标文件 " + automaticExcelPath, ex);
            }

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[UNC_UPDATE] 批量完成：图框 " + plans.Count
                + " 个，表格写入 " + tableRows + " 行，块 " + frameBlocks
                + " 个共更新 " + attributeValues + " 项属性。");
            ctx.Write("\n[UNC_UPDATE] Excel 已自动更新：新增 "
                + automaticExcel.AddedCount + " 条，覆盖 " + automaticExcel.ReplacedCount
                + " 条，材料明细 " + automaticExcel.MaterialCount + " 项；文件 "
                + automaticExcel.FilePath);
        }

        private static void Preflight(CadContext ctx, FrameRegionGroup region,
            FillRuntimeOptions options, FillWorkbookSnapshot workbook,
            List<Plan> plans, List<string> errors)
        {
            string prefix = "图框 " + region.Handle + "：";
            FillSelection selection = FillSelectionCollector.Split(ctx, region.EntityIds.ToArray());
            if (selection.FrameBlockIds.Length != 1)
            {
                errors.Add(prefix + "没有读取到唯一图框块。");
                return;
            }
            if (selection.TableIds.Length != 1)
            {
                errors.Add(prefix + "需要且只能包含一个清单表，实际 "
                    + selection.TableIds.Length + " 个。");
                return;
            }

            SummationOutput summation = FillStatisticsModule.Execute(ctx,
                selection.TextIds, options.MmPerGrid);
            CableStatResult statistics = summation.Statistics;
            if (statistics.CableSum <= 0 && statistics.Bridges.Count == 0
                && statistics.Conduits.Count == 0)
            {
                errors.Add(prefix + "没有符合规则的电缆、桥架或线管统计文字。");
                return;
            }

            if (!FillSelectionCollector.TryReadExistingIdentity(ctx, selection,
                    out ExistingFillIdentity identity, out string identityError))
            {
                errors.Add(prefix + identityError);
                return;
            }
            MachineRow machine = ExistingFillIdentityResolver.MatchMachine(identity,
                workbook.FindRows(identity.MachineId), out string matchError);
            if (machine == null)
            {
                errors.Add(prefix + matchError);
                return;
            }

            TableGenerationOutput tablePlan = FillTableModule.Plan(machine,
                workbook.Catalog, statistics, options.Planning);
            FillReviewData review = tablePlan.CreateReview(machine, options.Planning);
            List<FillReviewItem> unresolved = review.Items.Where(item =>
                item.RequiresCatalogConfirmation).ToList();
            if (unresolved.Count > 0)
            {
                errors.Add(prefix + "固定清单未匹配："
                    + string.Join("、", unresolved.Select(item => item.Name + " " + item.Description)));
                return;
            }

            List<TableFillRow> rows = review.SelectedRows();
            int capacity = FillFeature.ResolveTableWriteCapacity(ctx, selection.TableIds,
                options.StartRow, options.ClearRows);
            if (rows.Count > capacity)
            {
                errors.Add(prefix + "清单 " + rows.Count + " 项超过表格容量 " + capacity + " 行。");
                return;
            }

            plans.Add(new Plan
            {
                Region = region,
                Selection = selection,
                Machine = machine,
                Summation = summation,
                Statistics = statistics,
                Rows = rows
            });
        }

        private static WindowWrapper Owner()
        {
            // AcCoreConsole has no main window; full AutoCAD still receives a modal owner.
            IntPtr handle = AcApplication.MainWindow == null
                ? IntPtr.Zero : AcApplication.MainWindow.Handle;
            return new WindowWrapper(handle);
        }
    }
}

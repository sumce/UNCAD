using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段4「规划」的结果：套用插座/下游策略后的默认清单、审阅数据、
    /// 以及后续阶段（评审后插座回放、容量核对）需要的读写状态。
    /// </summary>
    internal sealed class FillPlanResult
    {
        internal TableGenerationOutput TablePlan;
        internal string DefaultCableMeters;
        internal FillReviewData Review;
        internal List<TableFillRow> ExistingOutlets;
        internal bool DeviceHasOutlet;
        internal bool DeviceOutletStateKnown;
    }

    /// <summary>
    /// 阶段4「规划」：按机台、盘柜和实际求和结果生成有序默认清单——套用插座/
    /// 下游设备策略、恢复历史选项、补电缆异常替代，并产出审阅数据。表格可写
    /// 容量与 Downstream 附加说明也归这里。从 FillFeature 拆出：单框 U1F/U1U、
    /// 批量 U1U 与机台选择预览必须用同一套规划口径，防止"预览一套、写入一套"。
    /// </summary>
    internal static class FillPlanningStage
    {
        /// <summary>
        /// 生成默认清单与审阅数据。U1U 模式会把 CAD 现有出线行视为权威数量/材质，
        /// 并从 frameinfo_json 恢复母线插接箱选择；cableReserve 非 null 时把
        /// 预留明细预填进审阅数据。
        /// </summary>
        internal static FillPlanResult Build(CadContext ctx, FillSelection selection,
            MachineRow picked, BoqCatalogIndex catalog, CableStatResult statistics,
            FillRuntimeOptions options, bool updateMode,
            FrameInfoJsonRecord frameInfoPrevious, CableReserveInfo cableReserve,
            string originalCableModel)
        {
            FlexibleConduitCableMap.ApplyTo(picked);
            TableGenerationOutput tablePlan = FillTableModule.Plan(
                picked, catalog, statistics, options.Planning, options.AutoFill);
            WriteCablePlanNote(ctx, picked, tablePlan);
            List<TableFillRow> plannedRows = tablePlan.CopyDefaultRows();
            if (updateMode)
                plannedRows = FillUpdateRowMerger.Merge(ctx, selection, plannedRows,
                    statistics);
            // U1U treats existing CAD outlet rows as authoritative for quantity and
            // material.  U1F starts from a clean generated list instead.
            List<TableFillRow> existingOutlets = updateMode
                ? CadExistingOutletReader.Read(ctx, selection.TableIds,
                    options.StartRow, options.ClearRows)
                : new List<TableFillRow>();
            CadDynamicBlockStateService.TryReadDeviceHasOutlet(ctx,
                selection.DeviceBlockIds, out bool deviceHasOutlet,
                out bool deviceOutletStateKnown, out string deviceState);
            // Downstream 块（精确名 Downstream）与 upstream 用同一套三态；只有 socket box
            // 会额外加一套 4.x 插座箱/盘，且永不添加断路器（见 DownstreamStatePolicy）。
            CadDynamicBlockStateService.TryReadDownstreamState(ctx,
                selection.DeviceBlockIds, out DownstreamState downstreamEquipment,
                out bool downstreamStateKnown, out string downstreamState,
                out int downstreamBlocks);
            TableFillRow deviceOutlet = TableFillPlanner.BuildOutletRow(picked.Detail,
                catalog);
            List<TableFillRow> socketAdjustedRows;
            if (updateMode && !deviceOutletStateKnown)
            {
                // Missing/legacy Device blocks do not prove that an outlet was removed.
                // Preserve the current CAD outlet rows and let the user decide in review.
                socketAdjustedRows = UpdateOutletPolicy.PreserveExisting(
                    plannedRows, existingOutlets);
            }
            else
            {
                socketAdjustedRows = updateMode
                    ? DeviceOutletPolicy.ApplyForUpdate(plannedRows,
                        deviceHasOutlet, deviceOutlet, existingOutlets)
                    : DeviceOutletPolicy.Apply(plannedRows,
                        deviceHasOutlet, deviceOutlet);
            }
            socketAdjustedRows = DownstreamStatePolicy.Apply(socketAdjustedRows, catalog,
                options.DownstreamSocketEnabled, options.DownstreamPanelEnabled,
                downstreamEquipment, options.DownstreamSocketAmps,
                options.DownstreamPanelAmps);
            tablePlan = new TableGenerationOutput(socketAdjustedRows,
                tablePlan.DefaultCableMeters, tablePlan.AutoFill);
            ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + "] Device 状态: " + deviceState + "；插座清单: "
                + (deviceHasOutlet ? "输出 " + (deviceOutlet.Code.Length > 0
                    ? deviceOutlet.Code : "未匹配") : "不输出"));
            if (downstreamBlocks > 0)
                ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                    + "] Downstream 状态: " + downstreamState + "；附加清单: "
                    + DescribeDownstreamExtra(downstreamEquipment, options));
            string defaultCableMeters = tablePlan.DefaultCableMeters;
            FillReviewData review = tablePlan.CreateReview(picked, options.Planning,
                updateMode ? originalCableModel : null);
            if (updateMode)
                review.RestoreBusPlugBoxChoice(frameInfoPrevious
                    ?? FrameInfoJsonBlockWriter.Read(ctx,
                        selection.FrameInfoJsonBlockIds), catalog);
            if (cableReserve != null)
                CableReserveReview.InitializeReserveReview(review, cableReserve);
            ResolveMissingCableCatalog(review, catalog);
            return new FillPlanResult
            {
                TablePlan = tablePlan,
                DefaultCableMeters = defaultCableMeters,
                Review = review,
                ExistingOutlets = existingOutlets,
                DeviceHasOutlet = deviceHasOutlet,
                DeviceOutletStateKnown = deviceOutletStateKnown
            };
        }

        /// <summary>
        /// 阶段5 用户确认后重放插座策略：Device 可见性是最终插座权威，DETAIL 选不出
        /// 8.2/8.3 时退回用户在审阅窗勾选的插座行。返回 null 表示插座型号仍未
        /// 匹配，命令必须中止（与规划期相同的弹窗口径）。
        /// </summary>
        internal static List<TableFillRow> ApplyReviewedOutlets(CadContext ctx,
            List<TableFillRow> tableRows, MachineRow picked, FillPlanResult plan,
            BoqCatalogIndex catalog, bool updateMode)
        {
            TableFillRow deviceOutlet = TableFillPlanner.BuildOutletRow(picked.Detail,
                catalog);
            if (plan.DeviceHasOutlet && !deviceOutlet.CatalogMatched)
            {
                TableFillRow reviewedOutlet = tableRows.FirstOrDefault(row =>
                    UpdateOutletPolicy.IsOutlet(row) && row.CatalogMatched);
                if (reviewedOutlet == null)
                {
                    MessageBox.Show(new WindowWrapper(
                            Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                        "Device 当前为插座状态，但 DETAIL 电流无法匹配插座 8.2/8.3。"
                        + "请在清单确认中选择对应插座型号。",
                        "U1F 插座型号未匹配", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return null;
                }
                deviceOutlet = reviewedOutlet;
            }
            if (updateMode && !plan.DeviceOutletStateKnown)
                return UpdateOutletPolicy.PreserveExisting(tableRows, plan.ExistingOutlets);
            return updateMode
                ? DeviceOutletPolicy.ApplyForUpdate(tableRows, plan.DeviceHasOutlet,
                    deviceOutlet, plan.ExistingOutlets)
                : DeviceOutletPolicy.Apply(tableRows, plan.DeviceHasOutlet, deviceOutlet);
        }

        /// <summary>
        /// 把电缆型号的解析结果直接打到命令行:1*16 → 8.5 接地、匹配到
        /// 哪一项、或未匹配(未匹配行会被严格模式丢弃,只看表格发现的
        /// "只剩线管/软管"问题在这里一眼见底)。
        /// </summary>
        internal static void WriteCablePlanNote(CadContext ctx, MachineRow machine,
            TableGenerationOutput tablePlan)
        {
            if (tablePlan?.AutoFill.Cable == false)
            {
                ctx.Write("\n[U1F/U1U] 电缆自动填充已在 U1SET 中关闭。");
                return;
            }
            string model = (machine?.Cable ?? "").Trim();
            if (model.Length == 0)
            {
                ctx.Write("\n[U1F/U1U] 机台电缆型号为空(Excel U_电缆型号未读到值)。");
                Log.Info("U1F 电缆型号为空: 机台 " + (machine?.MachineId ?? "")
                    + ",Excel 未读到 U_电缆型号。");
                return;
            }
            if (TableFillPlanner.IsGroundingCableModel(model))
            {
                ctx.Write("\n[U1F/U1U] 电缆型号 \"" + model
                    + "\" → 8.5 设备接地独立连接。");
                Log.Info("U1F 电缆型号 \"" + model + "\" 映射到 8.5 设备接地独立连接。");
                return;
            }
            TableFillRow cableRow = tablePlan?.CopyDefaultRows().FirstOrDefault(row =>
                row.Category == TableFillCategory.Cable);
            string note = cableRow != null && cableRow.CatalogMatched
                ? "已匹配固定清单 " + cableRow.Code
                : "未匹配固定清单——该电缆行不会写入清单,请检查 U_电缆型号写法。";
            ctx.Write("\n[U1F/U1U] 电缆型号 \"" + model + "\" " + note);
            Log.Info("U1F 电缆型号 \"" + model + "\": " + note);
        }

        /// <summary>Downstream 状态对应的附加清单说明（命令行输出用）。</summary>
        internal static string DescribeDownstreamExtra(DownstreamState state,
            FillRuntimeOptions options)
        {
            switch (state)
            {
                case DownstreamState.SocketBox:
                    return "插座箱一套（" + options.DownstreamSocketAmps.ToString("0.##")
                        + "A，受“插座盘”自动填充开关控制）";
                case DownstreamState.ILinePanel:
                    return "电盘一套（" + options.DownstreamPanelAmps.ToString("0.##")
                        + "A，不加断路器，受“电盘”自动填充开关控制）";
                case DownstreamState.BusbarConnector:
                    return "无（busbar connector 只上色）";
                default:
                    return "无（状态未知）";
            }
        }

        internal static int ResolveWriteCapacity(CadContext ctx, ObjectId[] tableIds,
            int startRow, int configuredRows)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                int capacity = ResolveWriteCapacity(transaction, tableIds,
                    startRow, configuredRows);
                transaction.Commit();
                return capacity;
            }
        }

        internal static int ResolveWriteCapacity(Transaction transaction,
            ObjectId[] tableIds, int startRow, int configuredRows)
        {
            // Multiple selected tables are written together, so the smallest resolved
            // capacity is the only value that can guarantee the atomic write will fit.
            int capacity = int.MaxValue;
            foreach (ObjectId id in tableIds ?? Array.Empty<ObjectId>())
            {
                Table table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                if (table == null) continue;
                int zeroBasedStart = CadTableFillWriter.ResolveWriteStartRow(table, startRow);
                int available = zeroBasedStart < 0
                    ? 0 : table.Rows.Count - zeroBasedStart;
                capacity = Math.Min(capacity,
                    TableClearPolicy.ResolveRows(available, configuredRows));
            }
            return capacity == int.MaxValue ? 0 : capacity;
        }

        private static void ResolveMissingCableCatalog(FillReviewData review,
            BoqCatalogIndex catalog)
        {
            FillAnomaly anomaly = FillAnomalyDetector.MissingCable(review);
            if (anomaly == null) return;
            Log.Warn("U1F " + anomaly.Code + ": " + anomaly.Subject);
            var owner = new WindowWrapper(
                Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle);
            DialogResult replace = MessageBox.Show(owner,
                "固定清单找不到电缆型号：" + anomaly.Subject
                    + "\r\n\r\n是否从固定清单选择替代型号？"
                    + "\r\n替代型号只用于本次清单，图框和块属性中的设备原型号保持不变。",
                "U1F 电缆型号异常", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (replace != DialogResult.Yes) return;
            if (catalog.Cables.Count == 0)
            {
                MessageBox.Show(owner, "固定清单中没有可选择的电缆型号。",
                    "U1F 电缆型号异常", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达。
            var picker = new CableCatalogSelectionWindow(
                catalog.Cables, review.BoqCableModel);
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(picker);
            if (!picker.Confirmed || picker.SelectedItem == null) return;
            review.SetCableModel(picker.SelectedItem.Alias ?? picker.SelectedItem.Spec,
                catalog);
        }
    }
}

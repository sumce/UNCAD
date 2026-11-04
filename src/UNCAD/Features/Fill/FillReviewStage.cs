using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段5「评审」：清单确认窗的展示与确认后校验——U1U 身份锁定（不允许在
    /// 更新时改机台 ID/设备名）、空清单二次确认、表格可写容量核对，以及
    /// 更新对比条目的构造。从 FillFeature 拆出：单框与批量 U1U 的对比/校验
    /// 口径必须一致（BatchFillUpdateCoordinator 复用 BuildCompareItem）。
    /// </summary>
    internal static class FillReviewStage
    {
        /// <summary>弹清单确认窗；取消返回 false（confirmed 为 null）。</summary>
        internal static bool ShowReview(FillReviewData review, BoqCatalogIndex catalog,
            FillPlanningOptions planning, bool updateMode,
            out FillReviewData confirmed)
        {
            confirmed = null;
            // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达，Data 在确认后读取。
            var window = new FillReviewWindow(review, catalog, planning, updateMode);
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(window);
            if (!window.Confirmed) return false;
            confirmed = window.Data;
            return true;
        }

        /// <summary>
        /// U1U 身份锁定：更新模式下不允许在确认窗改机台 ID 或设备名称，
        /// 且确认后的身份必须仍能匹配快照行；任一失败返回 false 并中止。
        /// </summary>
        internal static bool ValidateIdentityAfterReview(CadContext ctx, bool updateMode,
            string updateMachineId, string updateDeviceName, FillReviewData review,
            FillWorkbookSnapshot workbook)
        {
            if (updateMode && (!IdentityTextNormalizer.Equals(updateMachineId,
                    review.Machine?.MachineId)
                || !IdentityTextNormalizer.Equals(updateDeviceName,
                    review.Machine?.CircuitName)))
            {
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "U1U 不允许在更新清单时修改机台 ID 或设备名称。请使用 U1F 为新身份建立清单。",
                    "U1U 身份锁定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (updateMode)
            {
                MachineRow validated = ExistingFillIdentityResolver.MatchMachine(
                    new ExistingFillIdentity
                    {
                        MachineId = review.Machine?.MachineId,
                        DeviceName = review.Machine?.CircuitName
                    }, workbook.FindRows(review.Machine?.MachineId), out string identityError);
                if (validated == null)
                {
                    ctx.Write("\n[U1U] " + identityError);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 写入前的最后两道确认：空清单必须明确同意“只清空模板数据区”，
        /// 清单行数不得超过所选表格实际可写容量。返回 false 表示用户中止
        /// 或容量不足（此时已弹窗说明）。
        /// </summary>
        internal static bool ConfirmRowsAndCapacity(CadContext ctx,
            List<TableFillRow> tableRows, FillSelection selection,
            FillRuntimeOptions options, out int tableCapacity)
        {
            if (tableRows.Count == 0
                && MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "当前没有要生成的清单项。继续将只清空模板数据区，不写入新清单。",
                    "U1F 空清单确认", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                != DialogResult.Yes)
            {
                tableCapacity = 0;
                return false;
            }
            tableCapacity = FillPlanningStage.ResolveWriteCapacity(ctx,
                selection.TableIds, options.StartRow, options.ClearRows);
            if (tableRows.Count > tableCapacity)
            {
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "清单共 " + tableRows.Count + " 项，所选表格实际可写范围只有 "
                    + tableCapacity + " 行。请删除部分清单项，或在 U1SET 中调整起始行和清除行数。",
                    "U1F 表格容量不足", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        internal static FillUpdateCompareItem BuildCompareItem(MachineRow machine,
            List<TableFillRow> plannedRows, List<TableFillRow> existingRows,
            FrameInfoJsonRecord previous, string selectedHandle,
            string legacyLastUpdated = "", CableReserveInfo reserve = null)
        {
            bool hasHistory = previous != null
                && (!string.IsNullOrWhiteSpace(previous.LastModifiedUtc)
                    || !string.IsNullOrWhiteSpace(previous.LastModifiedUser)
                    || !string.IsNullOrWhiteSpace(previous.MachineId));
            string lastUpdated = "";
            if (hasHistory
                && DateTime.TryParse(previous.LastModifiedUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
                lastUpdated = parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            if (lastUpdated.Length == 0) lastUpdated = legacyLastUpdated ?? "";
            var rowDiffs = FillRowDiffBuilder.Build(existingRows, plannedRows);
            Tuple<List<FillRowDiff>, List<FillRowDiff>> sides =
                FillRowDiffBuilder.BuildSides(existingRows, plannedRows);
            return new FillUpdateCompareItem
            {
                MachineId = machine.MachineId ?? "",
                DeviceName = machine.CircuitName ?? "",
                FrameHandle = selectedHandle ?? "",
                LastUpdatedText = lastUpdated,
                LastUpdatedUser = hasHistory ? previous.LastModifiedUser ?? "" : "",
                HasHistory = hasHistory || !string.IsNullOrWhiteSpace(lastUpdated),
                AddedCount = rowDiffs.Count(diff =>
                    diff.Status == FillRowDiff.StatusAdded),
                RemovedCount = rowDiffs.Count(diff =>
                    diff.Status == FillRowDiff.StatusRemoved),
                QuantityChangedCount = rowDiffs.Count(diff =>
                    diff.Status == FillRowDiff.StatusQuantity),
                PreviousRows = sides.Item1,
                PlannedRows = sides.Item2,
                CableCard = BuildCableCard(previous, reserve)
            };
        }

        /// <summary>
        /// 电缆长度卡片（2026-11-04 用户要求）：本次构成 = 实测 + 盘内（箱内）
        /// 预留 + 设备内预留 + 业主预留 = 总长，附上次总长与差值。reserve 为
        /// null（无实测长度等）时返回 null，卡片隐藏。上次总长来自
        /// frameinfo_json 的 TotalCableM；复制框记录已被句柄校验拒掉，不会
        /// 显示别的图框的旧总长。
        /// </summary>
        internal static CableLengthCard BuildCableCard(FrameInfoJsonRecord previous,
            CableReserveInfo reserve)
        {
            if (reserve == null) return null;
            var card = new CableLengthCard
            {
                NewTotal = TextFormatter.FormatNum(reserve.TotalMeters) + " m"
            };
            string previousTotal = ((previous?.TotalCableM) ?? "").Trim()
                .TrimEnd('m', 'M').Trim();
            if (previousTotal.Length > 0 && double.TryParse(previousTotal,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double previousMeters))
            {
                card.PreviousTotal = TextFormatter.FormatNum(previousMeters) + " m";
                double delta = reserve.TotalMeters - previousMeters;
                card.DeltaChanged = Math.Abs(delta) >= 0.05;
                card.Delta = card.DeltaChanged
                    ? (delta > 0 ? "+" : "−")
                        + TextFormatter.FormatNum(Math.Abs(delta)) + " m"
                    : "无变化";
            }
            string panelLabel = string.IsNullOrWhiteSpace(reserve.PanelReserveLabel)
                ? "盘内/箱内" : reserve.PanelReserveLabel;
            card.Components.Add(new CableComponentRow("实测",
                TextFormatter.FormatNum(reserve.MeasuredMeters) + " m", false));
            card.Components.Add(new CableComponentRow(panelLabel + "预留",
                "+" + TextFormatter.FormatNum(reserve.PanelReserveMillimeters / 1000d)
                    + " m", false));
            card.Components.Add(new CableComponentRow("设备内预留",
                "+" + TextFormatter.FormatNum(reserve.DeviceReserveMillimeters / 1000d)
                    + " m", false));
            card.Components.Add(new CableComponentRow("业主预留",
                "+" + TextFormatter.FormatNum(reserve.OwnerReserveMillimeters / 1000d)
                    + " m", false));
            card.Components.Add(new CableComponentRow("总长", card.NewTotal, true));
            return card;
        }
    }
}

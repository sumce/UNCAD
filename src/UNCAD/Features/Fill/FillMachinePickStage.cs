using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Features.Submit;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段3「选定机台/回路」：U1F 弹机台选择窗（带规划预览），U1U 按图框持久
    /// 身份匹配快照行；并按「现有清单 → frameinfo_json → 图框属性 → 项目特征 →
    /// 工作簿」的顺序解析 U1U 的替代电缆型号。从 FillFeature 拆出：
    /// 单框与批量 U1U 的身份/电缆口径必须一致（PdfExportFeature 也复用
    /// TryResolveFrameCircuit，两处口径漂移会让 PDF 挂到错误的回路上）。
    /// </summary>
    internal static class FillMachinePickStage
    {
        /// <summary>
        /// U1F 新建：弹机台选择窗；取消返回 null。预览与最终规划走同一条
        /// 电缆→软管直径映射路径，避免"预览一套、写入一套"。
        /// </summary>
        internal static MachineRow PickNewMachine(CadContext ctx,
            FillWorkbookSnapshot workbook, FillSelection selection,
            CableStatResult statistics, FillRuntimeOptions options)
        {
            BoqCatalogIndex catalog = workbook.Catalog;
            Func<MachineRow, string> preview = selected => BuildPreview(ctx, selected,
                catalog, statistics, selection, options);
            // 3.0 WPF 窗体（D-038）：结果用 Confirmed 表达。
            var window = new MachinePickerWindow(workbook.MachineIds,
                workbook.FindRows, preview);
            UNCAD.UI.ModalWindowHost.Show(window);
            if (!window.Confirmed) return null;
            return window.Selected;
        }

        /// <summary>
        /// （<see cref="FrameIdentityReader.ReadIdentity"/> 读图框持久身份 +
        /// <see cref="ExistingFillIdentityResolver.MatchMachine"/> 匹配 SQLite 快照行）。
        /// U1PDF 必须复用它：两处口径漂移会让 PDF 挂到错误的回路上。
        /// </summary>
        internal static bool TryResolveFrameCircuit(Transaction transaction,
            FrameRegionGroup region, FillWorkbookSnapshot workbook,
            CadBlockDefinitionReader definitions,
            out string machineId, out string circuitName, out string error)
        {
            machineId = "";
            circuitName = "";
            error = "";
            if (transaction == null || region == null || workbook == null)
            {
                error = "缺少图框或机台数据。";
                return false;
            }
            ExistingFillIdentity identity;
            try
            {
                identity = FrameIdentityReader.ReadIdentity(transaction, region, definitions);
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
            MachineRow machine = ExistingFillIdentityResolver.MatchMachine(identity,
                workbook.FindRows(identity.MachineId), out string matchError);
            if (machine == null)
            {
                error = matchError;
                return false;
            }
            machineId = (machine.MachineId ?? "").Trim();
            circuitName = (machine.CircuitName ?? "").Trim();
            if (machineId.Length == 0 || circuitName.Length == 0)
            {
                error = "图框缺少机台ID或回路名。";
                return false;
            }
            return true;
        }

        internal static void ResolveUpdateCableFromExistingTable(CadContext ctx,
            FillSelection selection, MachineRow picked, BoqCatalogIndex catalog,
            Transaction transaction = null, CadBlockDefinitionReader definitions = null)
        {
            if (picked == null) return;
            if (transaction == null)
            {
                using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    ResolveUpdateCableFromExistingTable(ctx, selection, picked, catalog,
                        readTransaction);
                    readTransaction.Commit();
                }
                return;
            }
            catalog = catalog ?? new BoqCatalogIndex(null);
            string originalModel = (picked.Cable ?? "").Trim();
            // 句柄校验（2026-11-04）：复制图框的源框 JSON 不得恢复替代型号。
            FrameInfoJsonRecord frameInfo = FrameInfoJsonBlockWriter.Read(transaction,
                FrameInfoJsonBlockWriter.ReadSources(selection),
                FrameInfoJsonBlockWriter.OwnFrameHandle(transaction,
                    selection?.FrameBlockIds));
            var sourceIds = new List<ObjectId>();
            sourceIds.AddRange(selection?.FrameBlockIds ?? Array.Empty<ObjectId>());
            sourceIds.AddRange(selection?.TableIds ?? Array.Empty<ObjectId>());
            SubmissionSourceData tableSource = CadSubmissionReader.Read(transaction,
                sourceIds.Distinct().ToArray(), definitions);

            // The current table is authoritative for procurement. If the row was merged or
            // deleted, frameinfo_json preserves the user's previously confirmed substitute.
            string tableModel = SubmissionRecordExtractor.ExtractTableCableModel(tableSource);
            ListItem tableModelMatch = catalog.FindCable(tableModel);
            if (tableModelMatch != null)
            {
                picked.Cable = tableModelMatch.Alias;
                ctx.Write("\n[U1U] 已按现有清单电缆型号匹配固定清单型号“"
                    + tableModelMatch.Alias + "”（编号 " + tableModelMatch.Code + "）。");
                return;
            }
            ListItem frameInfoMatch = catalog.FindCable(frameInfo?.BoqCableModel);
            if (frameInfoMatch != null)
            {
                picked.Cable = frameInfoMatch.Alias;
                ctx.Write("\n[U1U] 已按 frameinfo_json 保留的 BOQ 电缆替代型号匹配固定清单型号“"
                    + frameInfoMatch.Alias + "”（编号 " + frameInfoMatch.Code + "）。");
                return;
            }

            // The current frame attribute is the next source. It reflects a cable changed
            // in CAD after the workbook was created, while the workbook row can be stale.
            string frameModel = "";
            if (tableSource.Attributes.TryGetValue(FrameBlockFiller.TagCable,
                    out List<string> frameValues))
                frameModel = frameValues.Select(SubmissionRecordExtractor.ExtractCableModel)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
            ListItem frameMatch = catalog.FindCable(frameModel);
            if (frameMatch != null)
            {
                picked.Cable = frameMatch.Alias;
                return;
            }

            string tableFeature = SubmissionRecordExtractor.ExtractTableCableFeature(tableSource);
            ListItem featureMatch = catalog.FindCableByFeature(tableFeature);
            if (featureMatch != null)
            {
                // The table's project feature identifies the fixed catalog row; use its canonical alias
                // for planning instead of trying to compare the rendered model text again.
                picked.Cable = featureMatch.Alias;
                ctx.Write("\n[U1U] 原始电缆型号“" + originalModel
                    + "”无法匹配，已按现有清单项目特征使用固定清单型号“"
                    + featureMatch.Alias + "”（编号 " + featureMatch.Code + "）。");
                return;
            }

            // The workbook value is still useful when the drawing carries no cable
            // attribute at all. Leave an unmatched review row for the single-frame picker
            // or the batch picker instead of aborting before the user can choose.
            ListItem workbookMatch = catalog.FindCable(originalModel);
            if (workbookMatch != null)
            {
                picked.Cable = workbookMatch.Alias;
                return;
            }

            string tableValue = tableModel.Length > 0 ? tableModel
                : tableFeature.Length > 0 ? tableFeature : "未读取到";
            ctx.Write("\n[U1U] 电缆型号暂未匹配固定清单：原始型号“" + originalModel
                + "”；现有清单“" + tableValue + "”。将在确认窗口中选择固定清单型号。");
        }

        private static string BuildPreview(CadContext ctx, MachineRow row, BoqCatalogIndex catalog,
            CableStatResult statistics, FillSelection selection, FillRuntimeOptions options)
        {
            // Keep the picker preview on the same cable-derived diameter path as final planning.
            FlexibleConduitCableMap.ApplyTo(row);
            var preview = new StringBuilder();
            preview.AppendLine("▼ 回路详情");
            preview.AppendLine("  机台/设备：" + row.MachineId + " / " + row.CircuitName);
            preview.AppendLine("  盘柜类型："
                + (string.IsNullOrWhiteSpace(row.Next) ? "未指定" : row.Next));
            preview.AppendLine("  FR：" + row.Fr);
            preview.AppendLine("  配电详情：" + row.Detail);
            preview.AppendLine("  下游轴位：" + row.DownstreamAxis + " ｜ 上游轴位："
                + row.UpstreamAxis);

            List<TableFillRow> plannedRows = TableGenerationModule.Plan(
                new TableGenerationRequest(row, catalog, statistics, options.Planning,
                    options.AutoFill))
                .CopyDefaultRows();
            CadDynamicBlockStateService.TryReadDeviceHasOutlet(ctx,
                selection?.DeviceBlockIds, out bool previewHasOutlet,
                out _, out string previewDeviceState);
            plannedRows = DeviceOutletPolicy.Apply(plannedRows, previewHasOutlet,
                TableFillPlanner.BuildOutletRow(row.Detail, catalog));
            CadDynamicBlockStateService.TryReadDownstreamState(ctx,
                selection?.DeviceBlockIds, out DownstreamState previewDownstream,
                out _, out _, out _);
            plannedRows = DownstreamStatePolicy.Apply(plannedRows, catalog,
                options.DownstreamSocketEnabled, options.DownstreamPanelEnabled,
                previewDownstream, options.DownstreamSocketAmps,
                options.DownstreamPanelAmps);
            TableFillRow previewHose = plannedRows.FirstOrDefault(item =>
                item.Category == TableFillCategory.FlexibleConduit);
            if (previewHose != null)
            {
                string hoseMeters = FillSelectionCollector.ReadRuanguanLengthMeters(
                    ctx, selection?.RuanguanBlockIds);
                if (hoseMeters.Length == 0)
                    plannedRows.Remove(previewHose);
                else
                    previewHose.Quantity = hoseMeters;
            }
            preview.AppendLine("▼ 表格写入（覆盖，从 No." + options.StartRow + " 行开始，共 "
                + plannedRows.Count + " 项，文字高度 "
                + TextFormatter.FormatNum(options.TextHeight) + "）");
            for (int i = 0; i < plannedRows.Count; i++)
            {
                TableFillRow planned = plannedRows[i];
                preview.AppendLine("  No." + (options.StartRow + i) + " " + planned.Name
                    + " ｜ " + planned.Unit + " "
                    + (planned.Quantity.Length > 0 ? planned.Quantity : "数量待定")
                    + " ｜ 编号 " + (planned.Code.Length > 0 ? planned.Code : "未匹配"));
            }
            if (selection.FrameBlockIds.Length > 0)
            {
                preview.AppendLine("▼ 图框块属性（" + selection.FrameBlockIds.Length + " 个块）");
                foreach (var value in FrameBlockFiller.BuildValues(
                    row, statistics))
                    preview.AppendLine("  " + value.Key + " = " + value.Value);
            }
            if (selection.DeviceBlockIds.Length > 0)
                preview.AppendLine("▼ 设备动态块（" + selection.DeviceBlockIds.Length
                    + " 个）：DEVICENAME = " + DeviceBlockFiller.BuildValue(row)
                    + " ｜ 状态 " + previewDeviceState + " ｜ 插座 "
                    + (previewHasOutlet ? "输出" : "不输出"));
            if (selection.UpstreamStateBlockIds.Length > 0)
                preview.AppendLine("▼ upstream 状态："
                    + DynamicBlockStatePolicy.UpstreamVisibilityState(row.Next));
            if (selection.UpstreamInfoBlockIds.Length > 0)
                preview.AppendLine("▼ 上游信息块：" + row.Fr + " / " + row.Detail);
            if (selection.UpstreamAxisBlockIds.Length > 0)
                preview.AppendLine("▼ 上游轴位块：US = " + ConnectionBlockFiller.UpstreamAxis(row));
            if (selection.DownstreamAxisBlockIds.Length > 0)
                preview.AppendLine("▼ 下游轴位块：DS = " + ConnectionBlockFiller.DownstreamAxis(row));
            if (selection.TextIds.Length > 0)
                preview.AppendLine("▼ 统计源文字（" + selection.TextIds.Length + " 个，保持原内容）");
            return preview.ToString();
        }
    }
}

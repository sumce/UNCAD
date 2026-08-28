using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>Coordinates selection, Excel lookup, preview, and specialized CAD writers.</summary>
    [Feature("fill", "Excel 填充清单表",
        Commands = CommandIds.FillFeatureCommands,
        Description = "动态填充清单，并可读取已填充身份按当前统计重新生成")]
    public class FillFeature : CommandBase
    {
        [CommandMethod(CommandIds.Fill, CommandFlags.UsePickSet)]
        public void UncadFill() => Run();

        [CommandMethod(CommandIds.FillUpdate, CommandFlags.UsePickSet)]
        public void UncadFillUpdate() => Run(true);

        protected override void Execute(CadContext ctx) => ExecuteCore(ctx, false);

        protected override void Execute(CadContext ctx, object state)
            => ExecuteCore(ctx, state is bool updateMode && updateMode);

        private static void ExecuteCore(CadContext ctx, bool updateMode)
        {
            // 阶段1：检测写入目标和统计文字。此阶段只读图纸，不产生任何修改。
            FillSelection selection = FillSelectionCollector.Collect(ctx);
            if (selection.IsEmpty)
            {
                ctx.Write("\n[UNC_FILL] 未找到清单表/图框块/设备块/上下游信息块/统计文字。");
                return;
            }
            if (!selection.HasWriteTargets)
            {
                ctx.Write("\n[UNC_FILL] 已选到统计文字，但没有清单表或可写入块；本次未修改图纸。");
                return;
            }
            if (selection.TableIds.Length > 1 || selection.FrameBlockIds.Length > 1)
            {
                ctx.Write("\n[UNC_FILL] 一次只允许一个清单表和一个目标图框块，避免批量误写。");
                return;
            }

            // 阶段2：冻结配置并按实际标注求和，后续预览和写入复用同一结果。
            FillRuntimeOptions options = FillSettings.Current();
            SummationOutput summation = FillStatisticsModule.Execute(ctx,
                selection.TextIds, options.MmPerGrid);
            CableStatResult statistics = summation.Statistics;
            if (updateMode && statistics.CableSum <= 0 && statistics.Bridges.Count == 0
                && statistics.Conduits.Count == 0)
            {
                ctx.Write("\n[SUM-STAT/求和统计] 未框选到符合规则的电缆、桥架或线管长度文字，现有数据未修改。");
                return;
            }

            string path = ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null) return;

            // 阶段3：加载机台/盘柜数据（用户唯一的外部 Excel）和内嵌固定清单。
            // 清单随插件版本固化，不再检查外部清单文件；资源缺失属于程序集缺陷。
            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_FILL] 读取机台 Excel 失败: " + ex.Message);
                Log.Error("UNC_FILL read machine excel failed", ex);
                return;
            }
            ctx.Write("\n[UNC_FILL] 机台数据 "
                + (workbook.MachineCacheHit ? "已使用缓存" : "已重新加载")
                + "：" + workbook.MachineSourcePath
                + "；内嵌固定清单 " + workbook.CatalogItemCount + " 项。");

            List<string> machineIds = workbook.MachineIds;
            List<ListItem> listItems = workbook.ListItems;
            BoqCatalogIndex catalog = workbook.Catalog;
            if (machineIds.Count == 0)
            {
                ctx.Write("\n[UNC_FILL] Excel 中无机台ID数据。");
                return;
            }
            if (listItems.Count == 0)
            {
                // 固定清单为空代表插件资源或发布包损坏，不能让用户确认生成无编码项目。
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "插件内没有固定清单数据，已停止本次填充。请重新安装完整版本。",
                    "UNC_FILL 固定清单异常", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string bridgeInfo = options.BridgeInfo;
            int startRow = options.StartRow;
            int clearRowCount = options.ClearRows;
            double textHeight = options.TextHeight;

            MachineRow picked;
            if (updateMode)
            {
                if (!FillSelectionCollector.TryReadExistingIdentity(ctx, selection,
                    out ExistingFillIdentity identity, out string identityError))
                {
                    ctx.Write("\n[UNC_FILL_UPDATE] " + identityError);
                    return;
                }
                picked = ExistingFillIdentityResolver.MatchMachine(identity,
                    workbook.FindRows(identity.MachineId), out string matchError);
                if (picked == null)
                {
                    ctx.Write("\n[UNC_FILL_UPDATE] " + matchError);
                    return;
                }
                ctx.Write("\n[UNC_FILL_UPDATE] 已自动读取: "
                    + picked.MachineId + " " + picked.CircuitName);
            }
            else
            {
                Func<MachineRow, string> preview = selected => BuildPreview(selected, catalog,
                    statistics, selection, options);
                using (var form = new MachinePickerForm(machineIds, workbook.FindRows, preview))
                {
                    if (form.ShowDialog(new WindowWrapper(
                            Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle))
                        != DialogResult.OK) return;
                    picked = form.Selected;
                }
                ctx.Write("\n[UNC_FILL] 已选择: "
                    + picked.MachineId + " " + picked.CircuitName);
            }

            // 阶段4：根据机台、盘柜和实际求和结果生成有序默认清单。
            TableGenerationOutput tablePlan = FillTableModule.Plan(
                picked, catalog, statistics, options.Planning);
            string defaultCableMeters = tablePlan.DefaultCableMeters;
            FillReviewData review = tablePlan.CreateReview(picked, options.Planning);
            ResolveMissingCableCatalog(review, catalog);
            // 阶段5：用户修改、增加、删除或取消清单项；异常型号必须明确确认。
            using (var form = new FillReviewForm(review, catalog, options.Planning))
            {
                if (form.ShowDialog(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle))
                    != DialogResult.OK) return;
                review = form.Data;
            }
            // Machine.Cable remains the source-device value. A BOQ replacement updates only
            // the reviewed cable row, so frame/block attributes never receive a catalog substitute.
            picked = review.Machine;
            List<TableFillRow> tableRows = review.SelectedRows();
            // 记录用户确认后的真实输出，而不是默认规划行，便于直接核对取消勾选是否生效。
            Log.Info("UNC_FILL confirmed BOQ rows: " + string.Join(" | ",
                tableRows.ConvertAll(row => row.Code + ":" + row.Name)));
            if (tableRows.Count == 0
                && MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "当前没有要生成的清单项。继续将只清空模板数据区，不写入新清单。",
                    "UNC_FILL 空清单确认", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                    != DialogResult.Yes)
                return;
            int tableCapacity = ResolveTableWriteCapacity(ctx, selection.TableIds,
                startRow, clearRowCount);
            if (tableRows.Count > tableCapacity)
            {
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "清单共 " + tableRows.Count + " 项，所选表格实际可写范围只有 "
                        + tableCapacity + " 行。请删除部分清单项，或在UNC_SET中调整起始行和清除行数。",
                    "UNC_FILL 表格容量不足", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            string reviewedCableMeters = review.CableMeters ?? "";
            if (!string.Equals(defaultCableMeters, reviewedCableMeters,
                StringComparison.OrdinalIgnoreCase))
                ApplyCableLengthOverride(statistics, reviewedCableMeters);

            ConfigPrinter.Print(ctx, updateMode ? CommandIds.FillUpdate : CommandIds.Fill,
                ("清单行数", tableRows.Count.ToString()),
                ("表格容量", tableCapacity.ToString()),
                ("顺序", string.Join(" → ", tableRows.ConvertAll(row => row.Name))));

            int filled;
            FillWriteResult frameResult, deviceResult, upstreamInfoResult;
            FillWriteResult upstreamAxisResult, downstreamAxisResult;
            // 阶段6：先清除模板数据区，再按连续顺序写入清单和块属性；
            // 所有CAD修改共用一个事务，任一异常都会整体回滚。
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                filled = FillTableModule.Write(ctx, transaction, selection.TableIds,
                    startRow, clearRowCount, tableRows, textHeight);
                if (filled < 0) return;
                frameResult = CadBlockAttributeWriter.FillFrame(ctx, transaction,
                    selection.FrameBlockIds, picked, bridgeInfo, statistics);
                deviceResult = CadBlockAttributeWriter.FillDeviceName(ctx, transaction,
                    selection.DeviceBlockIds, DeviceBlockFiller.BuildValue(picked));
                upstreamInfoResult = CadBlockAttributeWriter.FillTagged(ctx, transaction,
                    selection.UpstreamInfoBlockIds, ConnectionBlockFiller.TagUpstreamInfo,
                    ConnectionBlockFiller.UpstreamInfo(picked), true);
                upstreamAxisResult = CadBlockAttributeWriter.FillTagged(ctx, transaction,
                    selection.UpstreamAxisBlockIds, ConnectionBlockFiller.TagUpstreamAxis,
                    ConnectionBlockFiller.UpstreamAxis(picked), false);
                downstreamAxisResult = CadBlockAttributeWriter.FillTagged(ctx, transaction,
                    selection.DownstreamAxisBlockIds, ConnectionBlockFiller.TagDownstreamAxis,
                    ConnectionBlockFiller.DownstreamAxis(picked), false);
                transaction.Commit();
            }

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + "] 完成：表格写入 " + filled + " 行；块属性更新 "
                + frameResult.Blocks + " 个块共 " + frameResult.Values + " 项；统计电缆 "
                + TextFormatter.FormatNum(statistics.CableSum) + "M，桥架规格 "
                + statistics.Bridges.Count + " 项，线管规格 " + statistics.Conduits.Count
                + " 项（SUM-STAT源 " + summation.SourceLineCount + " 行，命中 "
                + summation.TotalMatchCount + " 行）；设备动态块更新 "
                + deviceResult.Blocks + " 个；上游信息 "
                + upstreamInfoResult.Blocks + " 个，上游轴位 " + upstreamAxisResult.Blocks
                + " 个，下游轴位 " + downstreamAxisResult.Blocks + " 个。");
        }

        private static int ResolveTableWriteCapacity(CadContext ctx, ObjectId[] tableIds,
            int startRow, int configuredRows)
        {
            // Multiple selected tables are written together, so the smallest resolved
            // capacity is the only value that can guarantee the atomic write will fit.
            int capacity = int.MaxValue;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in tableIds ?? Array.Empty<ObjectId>())
                {
                    Table table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                    if (table == null) continue;
                    int firstDataRow = CadTableFillWriter.FirstDataRow(table);
                    int zeroBasedStart = firstDataRow + Math.Max(1, startRow) - 1;
                    int available = Math.Max(0, table.Rows.Count - zeroBasedStart);
                    capacity = Math.Min(capacity,
                        TableClearPolicy.ResolveRows(available, configuredRows));
                }
            }
            return capacity == int.MaxValue ? 0 : capacity;
        }

        private static void ResolveMissingCableCatalog(FillReviewData review,
            BoqCatalogIndex catalog)
        {
            FillAnomaly anomaly = FillAnomalyDetector.MissingCable(review);
            if (anomaly == null) return;
            Log.Warn("UNC_FILL " + anomaly.Code + ": " + anomaly.Subject);
            var owner = new WindowWrapper(
                Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle);
            DialogResult replace = MessageBox.Show(owner,
                "固定清单找不到电缆型号：" + anomaly.Subject
                    + "\r\n\r\n是否从固定清单选择替代型号？"
                    + "\r\n替代型号只用于本次清单，图框和块属性中的设备原型号保持不变。",
                "UNC_FILL 电缆型号异常", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (replace != DialogResult.Yes) return;
            if (catalog.Cables.Count == 0)
            {
                MessageBox.Show(owner, "固定清单中没有可选择的电缆型号。",
                    "UNC_FILL 电缆型号异常", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            using (var picker = new CableCatalogSelectionForm(
                catalog.Cables, review.BoqCableModel))
            {
                if (picker.ShowDialog(owner) != DialogResult.OK
                    || picker.SelectedItem == null) return;
                review.SetCableModel(picker.SelectedItem.Spec, catalog);
            }
        }

        private static void ApplyCableLengthOverride(CableStatResult statistics, string value)
        {
            string text = (value ?? "").Trim();
            double meters;
            bool parsed = double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out meters)
                || double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out meters);
            statistics.CableFormatted.Clear();
            statistics.CableSum = parsed && meters > 0 ? meters : 0;
            if (statistics.CableSum > 0)
                statistics.CableFormatted.Add(TextFormatter.FormatNum(statistics.CableSum));
        }

        private static string ResolveMachineWorkbookPath(
            CadContext ctx, string configuredPath)
        {
            string path = (configuredPath ?? "").Trim();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                ctx.Write("\n[UNC_FILL] 使用上次 Excel: " + path
                    + "（UNC_SET → Excel 填充 可修改）");
                return path;
            }

            using (var dialog = new OpenFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "选择机台数据 Excel"
            })
            {
                if (dialog.ShowDialog(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle))
                    != DialogResult.OK) return null;
                path = dialog.FileName;
            }
            Settings.Set(ConfigKeys.FillExcelPath, path);
            ctx.Write("\n[UNC_FILL] 已记住 Excel: " + path);
            return path;
        }

        private static string BuildPreview(MachineRow row, BoqCatalogIndex catalog,
            CableStatResult statistics, FillSelection selection, FillRuntimeOptions options)
        {
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
                new TableGenerationRequest(row, catalog, statistics, options.Planning))
                .CopyDefaultRows();
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
                    row, options.BridgeInfo, statistics))
                    preview.AppendLine("  " + value.Key + " = " + value.Value);
            }
            if (selection.DeviceBlockIds.Length > 0)
                preview.AppendLine("▼ 设备动态块（" + selection.DeviceBlockIds.Length
                    + " 个）：DEVICENAME = " + DeviceBlockFiller.BuildValue(row));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
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
            FillSelection selection = FillSelectionCollector.Collect(ctx);
            if (selection.IsEmpty)
            {
                ctx.Write("\n[UNC_FILL] 未找到清单表/图框块/设备块/上下游信息块/统计文字。");
                return;
            }
            if (selection.TableIds.Length > 1 || selection.FrameBlockIds.Length > 1)
            {
                ctx.Write("\n[UNC_FILL] 一次只允许一个清单表和一个目标图框块，避免批量误写。");
                return;
            }

            double mmPerGrid = Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0);
            CableStatResult statistics = FillSelectionCollector.CalculateStats(ctx,
                selection.TextIds, mmPerGrid);
            if (updateMode && statistics.CableSum <= 0 && statistics.Bridges.Count == 0
                && statistics.Conduits.Count == 0)
            {
                ctx.Write("\n[UNC_FILL_UPDATE] 未框选到符合规则的电缆、桥架或线管长度文字，现有数据未修改。");
                return;
            }

            string path = ResolveMachineWorkbookPath(ctx);
            if (path == null) return;

            string catalogPath = Settings.Get(ConfigKeys.FillCatalogPath, "").Trim();
            if (catalogPath.Length > 0 && !File.Exists(catalogPath))
            {
                ctx.Write("\n[UNC_FILL] 固定清单 Excel 不存在: " + catalogPath
                    + "（UNC_SET → Excel 填充 可修改）");
                return;
            }

            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path, catalogPath);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_FILL] 读取 Excel 失败: " + ex.Message);
                Log.Error("UNC_FILL read excel failed", ex);
                return;
            }
            ctx.Write("\n[UNC_FILL] 机台数据已刷新；固定清单 "
                + (workbook.CatalogCacheHit ? "已使用缓存" : "已重新加载")
                + ": " + workbook.CatalogSourcePath);

            List<string> machineIds = workbook.MachineIds;
            List<ListItem> listItems = workbook.ListItems;
            if (machineIds.Count == 0)
            {
                ctx.Write("\n[UNC_FILL] Excel 中无机台ID数据。");
                return;
            }
            if (listItems.Count == 0)
                ctx.Write("\n[UNC_FILL] 警告：未读取到清单项目，编号列将留空。");

            string bridgeInfo = Settings.Get(ConfigKeys.FillBridge, "");
            int startRow = (int)Settings.GetDouble(ConfigKeys.FillTableRow, 1.0);
            int clearRowCount = (int)Settings.GetDouble(ConfigKeys.FillClearRows,
                TableClearPolicy.DefaultRows);
            double textHeight = Settings.GetDouble(ConfigKeys.FillTextHeight,
                TableFillFormatter.DefaultTextHeight);
            if (textHeight <= 0) textHeight = TableFillFormatter.DefaultTextHeight;

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
                Func<MachineRow, string> preview = selected => BuildPreview(selected, listItems,
                    statistics, selection, startRow, textHeight, bridgeInfo);
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

            List<TableFillRow> defaultRows = TableFillPlanner.Build(picked, listItems, statistics);
            string defaultCableMeters = defaultRows.Find(row =>
                row.Category == TableFillCategory.Cable)?.Quantity ?? "";
            if (defaultCableMeters.Length == 0 && statistics.CableSum > 0)
                defaultCableMeters = TextFormatter.FormatNum(statistics.CableSum);
            FillReviewData review = FillReviewData.Create(picked, defaultRows);
            if (review.CableMeters.Length == 0) review.CableMeters = defaultCableMeters;
            using (var form = new FillReviewForm(review))
            {
                if (form.ShowDialog(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle))
                    != DialogResult.OK) return;
                review = form.Data;
            }
            picked = review.Machine;
            List<TableFillRow> tableRows = review.SelectedRows();
            string reviewedCableMeters = review.CableMeters ?? "";
            if (!string.Equals(defaultCableMeters, reviewedCableMeters,
                StringComparison.OrdinalIgnoreCase))
                ApplyCableLengthOverride(statistics, reviewedCableMeters);

            ConfigPrinter.Print(ctx, updateMode ? CommandIds.FillUpdate : CommandIds.Fill,
                ("清单行数", tableRows.Count.ToString()),
                ("顺序", string.Join(" → ", tableRows.ConvertAll(row => row.Name))));

            int filled = CadTableFillWriter.Fill(ctx, selection.TableIds, startRow,
                clearRowCount, tableRows, textHeight);
            if (filled < 0) return;
            FillWriteResult frameResult = CadBlockAttributeWriter.FillFrame(ctx,
                selection.FrameBlockIds, picked, bridgeInfo, statistics);
            FillWriteResult deviceResult = CadBlockAttributeWriter.FillDeviceName(ctx,
                selection.DeviceBlockIds, DeviceBlockFiller.BuildValue(picked));
            FillWriteResult upstreamInfoResult = CadBlockAttributeWriter.FillTagged(ctx,
                selection.UpstreamInfoBlockIds, ConnectionBlockFiller.TagUpstreamInfo,
                ConnectionBlockFiller.UpstreamInfo(picked), true);
            FillWriteResult upstreamAxisResult = CadBlockAttributeWriter.FillTagged(ctx,
                selection.UpstreamAxisBlockIds, ConnectionBlockFiller.TagUpstreamAxis,
                ConnectionBlockFiller.UpstreamAxis(picked), false);
            FillWriteResult downstreamAxisResult = CadBlockAttributeWriter.FillTagged(ctx,
                selection.DownstreamAxisBlockIds, ConnectionBlockFiller.TagDownstreamAxis,
                ConnectionBlockFiller.DownstreamAxis(picked), false);

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                + "] 完成：表格写入 " + filled + " 行；块属性更新 "
                + frameResult.Blocks + " 个块共 " + frameResult.Values + " 项；统计电缆 "
                + TextFormatter.FormatNum(statistics.CableSum) + "M，桥架规格 "
                + statistics.Bridges.Count + " 项，线管规格 " + statistics.Conduits.Count
                + " 项；设备动态块更新 " + deviceResult.Blocks + " 个；上游信息 "
                + upstreamInfoResult.Blocks + " 个，上游轴位 " + upstreamAxisResult.Blocks
                + " 个，下游轴位 " + downstreamAxisResult.Blocks + " 个。");
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

        private static string ResolveMachineWorkbookPath(CadContext ctx)
        {
            string path = Settings.Get(ConfigKeys.FillExcelPath, "");
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

        private static string BuildPreview(MachineRow row, List<ListItem> listItems,
            CableStatResult statistics, FillSelection selection, int startRow,
            double textHeight, string bridgeInfo)
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

            List<TableFillRow> plannedRows = TableFillPlanner.Build(row, listItems, statistics);
            preview.AppendLine("▼ 表格写入（覆盖，从 No." + startRow + " 行开始，共 "
                + plannedRows.Count + " 项，文字高度 "
                + TextFormatter.FormatNum(textHeight) + "）");
            for (int i = 0; i < plannedRows.Count; i++)
            {
                TableFillRow planned = plannedRows[i];
                preview.AppendLine("  No." + (startRow + i) + " " + planned.Name
                    + " ｜ " + planned.Unit + " "
                    + (planned.Quantity.Length > 0 ? planned.Quantity : "数量待定")
                    + " ｜ 编号 " + (planned.Code.Length > 0 ? planned.Code : "未匹配"));
            }
            if (selection.FrameBlockIds.Length > 0)
            {
                preview.AppendLine("▼ 图框块属性（" + selection.FrameBlockIds.Length + " 个块）");
                foreach (var value in FrameBlockFiller.BuildValues(row, bridgeInfo, statistics))
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Submission;
using UNCAD.Infra;
using UNCAD.UI;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace UNCAD.Features.Submit
{
    [Feature("submit", "提交设备信息",
        Commands = CommandIds.Submit,
        Description = "读取框选的 UNC_FILL 信息并更新提交记录 Excel")]
    public sealed class SubmitFeature : CommandBase
    {
        [CommandMethod(CommandIds.Submit, CommandFlags.UsePickSet)]
        public void UncadSubmit() => Run();

        protected override void Execute(CadContext ctx)
        {
            ObjectId[] ids = SelectionService.PickFirstOrPrompt(ctx,
                "请框选 UNC_FILL 已填充的图框、设备块、上下游块和清单表: ",
                new TypedValue(0, "ACAD_TABLE,INSERT,TEXT,MTEXT"));
            if (ids == null || ids.Length == 0) return;

            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, ids);
            if (regions.SelectedFrameCount > 1 && regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors)
                    ctx.Write("\n[UNC_SUBMIT] 图框分区失败: " + error);
                return;
            }

            var records = new List<SubmissionRecord>();
            try
            {
                if (regions.SelectedFrameCount > 1)
                {
                    // A wide selection can include unrelated blocks. Multiple selected frames
                    // are authoritative containers, so each record reads only its own region.
                    foreach (FrameRegionGroup group in regions.Groups)
                    {
                        try
                        {
                            records.Add(SubmissionRecordExtractor.Extract(
                                ReadSelection(ctx, group.EntityIds.ToArray())));
                        }
                        catch (System.Exception ex)
                        {
                            throw new InvalidDataException("图框 " + group.Handle + "：" + ex.Message, ex);
                        }
                    }
                }
                else
                {
                    // A single frame keeps the established explicit-selection contract. Spatial
                    // regrouping exists only to separate multiple frame_20260812 records.
                    records.Add(SubmissionRecordExtractor.Extract(ReadSelection(ctx, ids)));
                }
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_SUBMIT] 读取失败: " + ex.Message);
                Log.Warn("UNC_SUBMIT extraction failed: " + ex.Message);
                return;
            }

            string folder = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            if (!Directory.Exists(folder))
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = "选择 UNC_SUBMIT 提交表保存文件夹",
                    ShowNewFolderButton = true
                })
                {
                    if (dialog.ShowDialog(Owner()) != DialogResult.OK) return;
                    folder = dialog.SelectedPath;
                }
                Settings.Set(ConfigKeys.SubmitFolder, folder);
                ctx.Write("\n[UNC_SUBMIT] 已记住提交文件夹: " + folder);
            }

            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            var owner = Owner();
            List<SubmissionRecord> withoutMaterials = records.Where(record =>
                record.Materials.Count == 0).ToList();
            if (withoutMaterials.Count > 0)
            {
                string warning = records.Count == 1
                    ? BuildNoMaterialsMessage(records[0])
                    : "以下 " + withoutMaterials.Count + " 个图框没有读取到清单明细：\r\n"
                        + string.Join("\r\n", withoutMaterials.Take(15).Select(record =>
                            record.MachineId + " / " + record.DeviceName))
                        + "\r\n\r\n继续后这些图框只导出设备汇总。是否继续？";
                if (MessageBox.Show(owner, warning, "UNC_SUBMIT 清单明细异常",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }

            string summary;
            if (records.Count == 1)
            {
                SubmissionRecord record = records[0];
                string cableSummary = string.Equals(record.OriginalCable, record.Cable,
                    StringComparison.OrdinalIgnoreCase)
                    ? Display(record.Cable)
                    : Display(record.OriginalCable) + " → 清单替代 " + Display(record.Cable);
                summary = "机台ID: " + record.MachineId
                    + "\n设备名称: " + record.DeviceName
                    + "\n盘柜类型: " + record.PanelType
                    + "\n电缆: " + cableSummary + " / " + Meters(record.CableMeters)
                    + "\n软管: " + (record.Diameter.Length > 0 ? "Φ" + record.Diameter : "无")
                    + " / " + Meters(record.FlexibleConduitMeters)
                    + "\n桥架: " + Meters(record.BridgeMeters)
                    + "\n线管: " + Meters(record.ConduitMeters)
                    + "\n清单明细: " + record.Materials.Count + " 项"
                    + "\n\n提交到:\n" + path;
            }
            else
            {
                summary = "将按 frame_20260812 边界提交 " + records.Count + " 个图框。\n"
                    + "清单明细共 " + records.Sum(record => record.Materials.Count) + " 项。\n\n"
                    + string.Join("\n", records.Take(15).Select(record =>
                        record.MachineId + " / " + record.DeviceName))
                    + (records.Count > 15 ? "\n其余 " + (records.Count - 15) + " 个图框..." : "")
                    + "\n\n全部记录将一次写入:\n" + path;
            }
            if (MessageBox.Show(owner, summary, records.Count == 1 ? "UNC_SUBMIT 确认提交" : "UNC_SUBMIT 批量确认",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            try
            {
                if (records.Count == 1)
                {
                    SubmissionRecord record = records[0];
                    SubmissionWriteResult result = SubmissionWorkbookWriter.Upsert(
                        path, record, DateTimeOffset.Now);
                    ctx.Write("\n[UNC_SUBMIT] " + (result.ReplacedExisting
                        ? "已覆盖原记录（清理 " + result.RemovedDuplicates + " 条重复数据）"
                        : "已新增记录") + ": " + record.MachineId + " / " + record.DeviceName
                        + "；清单明细 " + record.Materials.Count + " 项已写入“"
                        + SubmissionWorkbookWriter.DetailSheetName + "”；更新时间 "
                        + result.UpdatedAt + "；文件 " + result.FilePath);
                }
                else
                {
                    SubmissionBatchWriteResult result = SubmissionWorkbookWriter.UpsertMany(
                        path, records, DateTimeOffset.Now);
                    ctx.Write("\n[UNC_SUBMIT] 批量完成：新增 " + result.AddedCount
                        + " 条，覆盖 " + result.ReplacedCount + " 条，清理重复 "
                        + result.RemovedDuplicates + " 条；清单明细 "
                        + records.Sum(record => record.Materials.Count) + " 项；文件 "
                        + result.FilePath);
                }
                SelectionService.ClearPickFirst(ctx);
            }
            catch (IOException ex)
            {
                ctx.Write("\n[UNC_SUBMIT] 写入失败，请确认 Excel 文件未被占用: " + ex.Message);
                Log.Error("UNC_SUBMIT write failed", ex);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_SUBMIT] 写入失败: " + ex.Message);
                Log.Error("UNC_SUBMIT write failed", ex);
            }
        }

        /// <summary>把“没有读到清单明细”变成可诊断的提示：区分没框到表、表是空的、数据列缺失。</summary>
        private static WindowWrapper Owner()
        {
            // Core Console has no main window; normal AutoCAD keeps the dialog parented.
            IntPtr handle = AcApplication.MainWindow == null
                ? IntPtr.Zero : AcApplication.MainWindow.Handle;
            return new WindowWrapper(handle);
        }

        private static string BuildNoMaterialsMessage(SubmissionRecord record)
        {
            string detail;
            if (record.TableRowsRead == 0 && record.TextEntityCount > 0)
            {
                detail = "框选到了 " + record.TextEntityCount
                    + " 个文字/多行文字，但没有 AutoCAD 表格实体。\r\n"
                    + "UNC_SUBMIT 读取的是真实清单表（ACAD_TABLE）。若你的清单是文字画的，\r\n"
                    + "请改用 UNC_FILL 的标准表格，或直接框选表格实体本身。";
            }
            else if (record.TableRowsRead == 0)
            {
                detail = "框选区域中没有读取到表格实体。请确认：\r\n"
                    + "1) 框选到了 UNC_FILL 使用的清单表（AutoCAD 表格实体）；\r\n"
                    + "2) 清单表所在图层没有被关闭或冻结；\r\n"
                    + "3) 若清单表在块/外部参照内部，请直接框选表格本身。";
            }
            else
            {
                detail = "已读取表格 " + record.TableRowsRead + " 行，但没有识别到材料明细行。请确认：\r\n"
                    + "1) 清单表已由 UNC_FILL 填充（数据行含 No.1 编号、名称、单位、数量和编码）；\r\n"
                    + "2) 表格不是只有表头或已被清空，请先运行 UNC_FILL；\r\n"
                    + "3) 表格数据列位置为标准列序（序号/名称/特征/单位/数量/编码）。";
            }
            return "框选内容中没有读取到清单明细。\r\n\r\n" + detail
                + "\r\n\r\n继续后只导出设备汇总，不会生成材料明细行。\r\n\r\n是否继续？";
        }

        private static string Display(string value)
            => string.IsNullOrWhiteSpace(value) ? "无" : value.Trim();

        private static string Meters(string value)
            => string.IsNullOrWhiteSpace(value) ? "无" : value.Trim() + " 米";

        private static SubmissionSourceData ReadSelection(CadContext ctx, ObjectId[] ids)
        {
            var source = new SubmissionSourceData();
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                    // AutoCAD Table inherits BlockReference, so the table branch must come first.
                    // Reversing this order silently drops all BOQ material rows from submissions.
                    if (entity is Table table) ReadTable(table, source);
                    else if (entity is BlockReference block) ReadBlock(transaction, block, source);
                    else if (entity is DBText || entity is MText) source.TextEntityCount++;
                }
            }
            return source;
        }

        private static void ReadBlock(Transaction transaction, BlockReference block,
            SubmissionSourceData source)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                    as AttributeReference;
                if (attribute == null) continue;
                string value = attribute.TextString ?? "";
                if (attribute.IsMTextAttribute)
                {
                    using (MText mtext = attribute.MTextAttribute)
                        if (mtext != null) value = mtext.Contents ?? value;
                }
                source.AddAttribute(attribute.Tag, value);
            }
            if (!block.IsDynamicBlock) return;
            foreach (DynamicBlockReferenceProperty property
                in block.DynamicBlockReferencePropertyCollection)
            {
                string value = Convert.ToString(property.Value) ?? "";
                if (value.Trim().Length == 0) continue;
                source.DynamicValues.Add(value);
                source.AddAttribute(property.PropertyName, value);
            }
        }

        private static void ReadTable(Table table, SubmissionSourceData source)
        {
            for (int row = 0; row < table.Rows.Count; row++)
            {
                var values = new string[table.Columns.Count];
                for (int column = 0; column < table.Columns.Count; column++)
                {
                    try { values[column] = table.Cells[row, column].TextString ?? ""; }
                    catch (System.Exception ex)
                    {
                        values[column] = "";
                        Log.Warn("UNC_SUBMIT read table cell failed: " + ex.Message);
                    }
                }
                source.AddTableRow(values);
            }
        }
    }
}

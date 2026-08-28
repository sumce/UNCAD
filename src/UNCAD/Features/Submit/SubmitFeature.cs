using System;
using System.IO;
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

            SubmissionRecord record;
            try { record = SubmissionRecordExtractor.Extract(ReadSelection(ctx, ids)); }
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
                    if (dialog.ShowDialog(new WindowWrapper(AcApplication.MainWindow.Handle))
                        != DialogResult.OK) return;
                    folder = dialog.SelectedPath;
                }
                Settings.Set(ConfigKeys.SubmitFolder, folder);
                ctx.Write("\n[UNC_SUBMIT] 已记住提交文件夹: " + folder);
            }

            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            var owner = new WindowWrapper(AcApplication.MainWindow.Handle);
            if (record.Materials.Count == 0
                && MessageBox.Show(owner,
                    "框选内容中没有读取到清单明细。继续后只导出设备汇总，不会生成材料明细行。\r\n\r\n是否继续？",
                    "UNC_SUBMIT 清单明细异常", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                    != DialogResult.Yes)
                return;
            string cableSummary = string.Equals(record.OriginalCable, record.Cable,
                StringComparison.OrdinalIgnoreCase)
                ? Display(record.Cable)
                : Display(record.OriginalCable) + " → 清单替代 " + Display(record.Cable);
            string summary = "机台ID: " + record.MachineId
                + "\n设备名称: " + record.DeviceName
                + "\n盘柜类型: " + record.PanelType
                + "\n电缆: " + cableSummary + " / " + Meters(record.CableMeters)
                + "\n软管: " + (record.Diameter.Length > 0 ? "Φ" + record.Diameter : "无")
                + " / " + Meters(record.FlexibleConduitMeters)
                + "\n桥架: " + Meters(record.BridgeMeters)
                + "\n线管: " + Meters(record.ConduitMeters)
                + "\n清单明细: " + record.Materials.Count + " 项"
                + "\n\n提交到:\n" + path;
            if (MessageBox.Show(owner, summary,
                "确认提交", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            try
            {
                SubmissionWriteResult result = SubmissionWorkbookWriter.Upsert(
                    path, record, DateTimeOffset.Now);
                SelectionService.ClearPickFirst(ctx);
                ctx.Write("\n[UNC_SUBMIT] " + (result.ReplacedExisting
                    ? "已覆盖原记录（清理 " + result.RemovedDuplicates + " 条重复数据）"
                    : "已新增记录") + ": " + record.MachineId + " / " + record.DeviceName
                    + "；清单明细 " + record.Materials.Count + " 项已写入“"
                    + SubmissionWorkbookWriter.DetailSheetName + "”；更新时间 "
                    + result.UpdatedAt + "；文件 " + result.FilePath);
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
                    if (entity is BlockReference block) ReadBlock(transaction, block, source);
                    else if (entity is Table table) ReadTable(table, source);
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Submission
{
    /// <summary>将已绘制清单写入固定 BOQ 模板，每个项目编码只更新工程量列。</summary>
    public static class BoqWorkbookWriter
    {
        public const string SheetName = "Sheet1";
        public const string OutputPrefix = "[BOQ]";
        public const string TemplateFileName = "BOQ模板.xlsx";
        private static readonly Regex ItemCodePattern = new Regex(
            @"^\d+\.\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string ResolveTemplatePath()
        {
            string[] candidates =
            {
                // 兼容用户提供的无分隔符路径，以及工作区内的实际文件路径。
                @"D:\Workspace\UNCADBOQ模板.xlsx",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BOQ_Template.xlsx"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TemplateFileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", TemplateFileName),
                @"D:\Workspace\UNCAD\BOQ模板.xlsx"
            };
            string path = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(path))
                throw new FileNotFoundException(
                    @"找不到固定 BOQ 模板，请确认 D:\Workspace\UNCADBOQ模板.xlsx 或插件目录内存在 BOQ模板.xlsx。",
                    TemplateFileName);
            return Path.GetFullPath(path);
        }

        public static string BuildFileName(string machineId)
        {
            string value = (machineId ?? "").Trim();
            if (value.Length == 0) throw new ArgumentException("机台ID不能为空。", nameof(machineId));
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value == "." || value == ".." || value.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidDataException("机台ID不能作为文件夹或文件名：" + value);
            return OutputPrefix + value + ".xlsx";
        }

        public static string BuildTargetPath(string outputRoot, string machineId)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new ArgumentException("BOQ 输出文件夹不能为空。", nameof(outputRoot));
            string fileName = BuildFileName(machineId);
            string machineFolder = Path.Combine(Path.GetFullPath(outputRoot), (machineId ?? "").Trim());
            Directory.CreateDirectory(machineFolder);
            return Path.Combine(machineFolder, fileName);
        }

        public static void ValidateTargetForUpdate(string targetPath, string templatePath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("BOQ 输出文件路径为空。", nameof(targetPath));
            if (!File.Exists(targetPath) && (string.IsNullOrWhiteSpace(templatePath)
                || !File.Exists(templatePath)))
                throw new FileNotFoundException("BOQ 模板不存在。", templatePath);
            string folder = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            if (string.IsNullOrWhiteSpace(folder))
                throw new DirectoryNotFoundException("BOQ 输出文件夹不存在。");
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".boq-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            }
            IWorkbook workbook = Load(targetPath, templatePath);
            workbook.Close();
        }

        public static void Write(string targetPath, string templatePath,
            IEnumerable<SubmissionRecord> records)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("BOQ 输出文件路径为空。", nameof(targetPath));
            List<SubmissionMaterial> materials = (records ?? Enumerable.Empty<SubmissionRecord>())
                .Where(record => record != null)
                .SelectMany(record => record.Materials ?? new List<SubmissionMaterial>())
                .ToList();
            if (materials.Count == 0)
                throw new InvalidDataException("没有可写入 BOQ 的清单材料。");

            string fullPath = Path.GetFullPath(targetPath);
            string folder = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(folder);
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            IWorkbook workbook = null;
            try
            {
                workbook = Load(fullPath, templatePath);
                ISheet sheet = workbook.GetSheet(SheetName) ?? workbook.GetSheetAt(0);
                Dictionary<string, int> rows = FindItemRows(sheet);
                Dictionary<string, decimal> quantities = new Dictionary<string, decimal>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (SubmissionMaterial material in materials)
                {
                    string code = (material.Code ?? material.Number ?? "").Trim();
                    if (code.Length == 0)
                        throw new InvalidDataException("BOQ 材料缺少项目编码，不能猜测固定清单项目。");
                    if (!rows.ContainsKey(code))
                        throw new InvalidDataException("固定 BOQ 模板不存在项目编码：" + code);
                    if (!TryParseQuantity(material.Quantity, out decimal quantity))
                        throw new InvalidDataException("BOQ 项目 " + code + " 的工程量不是有效数字："
                            + (material.Quantity ?? ""));
                    quantities[code] = quantities.TryGetValue(code, out decimal old)
                        ? old + quantity : quantity;
                }

                // 先清空所有明细数量，再写入本次机台最新清单，保留模板公式行。
                foreach (KeyValuePair<string, int> item in rows)
                    SetNumber(sheet.GetRow(item.Value).GetCell(4), 0m);
                foreach (KeyValuePair<string, decimal> item in quantities)
                    SetNumber(sheet.GetRow(rows[item.Key]).GetCell(4), item.Value);

                using (var stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                    workbook.Write(stream);
                workbook.Close();
                workbook = null;
                Replace(fullPath, temporary);
                temporary = null;
            }
            finally
            {
                workbook?.Close();
                try { if (!string.IsNullOrWhiteSpace(temporary) && File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static IWorkbook Load(string targetPath, string templatePath)
        {
            string source = File.Exists(targetPath) ? targetPath : templatePath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                throw new FileNotFoundException("BOQ 模板不存在。", source);
            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                return new XSSFWorkbook(stream);
        }

        private static Dictionary<string, int> FindItemRows(ISheet sheet)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                string code = CellText(row?.GetCell(0));
                if (!ItemCodePattern.IsMatch(code)) continue;
                if (result.ContainsKey(code))
                    throw new InvalidDataException("BOQ 模板存在重复项目编码：" + code);
                result[code] = rowIndex;
            }
            if (result.Count == 0)
                throw new InvalidDataException("BOQ 模板没有找到项目编码明细行。");
            return result;
        }

        private static bool TryParseQuantity(string value, out decimal quantity)
        {
            string text = (value ?? "").Trim();
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
                || decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out quantity);
        }

        private static void SetNumber(ICell cell, decimal value)
        {
            if (cell == null) return;
            cell.SetCellValue((double)value);
        }

        private static string CellText(ICell cell)
            => cell == null ? "" : (cell.ToString() ?? "").Trim();

        private static void Replace(string target, string temporary)
        {
            if (File.Exists(target))
            {
                string backup = target + ".bak";
                try
                {
                    File.Replace(temporary, target, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                catch
                {
                    File.Copy(temporary, target, true);
                    File.Delete(temporary);
                    if (File.Exists(backup)) File.Delete(backup);
                }
            }
            else File.Move(temporary, target);
        }
    }
}

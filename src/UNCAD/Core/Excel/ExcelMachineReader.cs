using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    public sealed class MachineRow
    {
        public string Region { get; set; }
        public string MachineId { get; set; }
        public string CircuitName { get; set; }
        public string Cable { get; set; }
        public string Fr { get; set; }
        public string Detail { get; set; }
        public string Seq { get; set; }
        public string Dia { get; set; }
        public string Next { get; set; }
        public string DownstreamAxis { get; set; }
        public string UpstreamAxis { get; set; }
        public string DeviceFloor { get; set; }
        public string PanelFloor { get; set; }
        public string FacilitySwitch { get; set; }

        public override string ToString()
        {
            return MachineId + " | " + CircuitName + " | 电缆:" + Cable
                + " | " + Detail + " | 序号:" + Seq + " | 软管" + Dia
                + " | NEXT:" + Next + " | 下游轴位:" + DownstreamAxis
                + " | 上游轴位:" + UpstreamAxis;
        }
    }

    /// <summary>
    /// Reads only the unified machine schema. No legacy aliases, inferred values,
    /// or fallback columns are accepted.
    /// </summary>
    public static class ExcelMachineReader
    {
        private sealed class MachineColumns
        {
            public int Region = -1;
            public int MachineId = -1;
            public int CircuitName = -1;
            public int Cable = -1;
            public int Fr = -1;
            public int Detail = -1;
            public int Seq = -1;
            public int Dia = -1;
            public int Next = -1;
            public int DownstreamAxis = -1;
            public int UpstreamAxis = -1;
            public int DeviceFloor = -1;
            public int PanelFloor = -1;
            public int FacilitySwitch = -1;
            public int HeaderRow;
        }

        public static List<MachineRow> FindRows(string filePath, string keyword)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                var workbook = new XSSFWorkbook(fs);
                try { return FindRows(ReadAll(workbook), keyword); }
                finally { workbook.Close(); }
            }
        }

        public static List<MachineRow> ReadRows(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("机台数据 Excel 路径不能为空。", nameof(filePath));
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                var workbook = new XSSFWorkbook(fs);
                try { return ReadAll(workbook); }
                finally { workbook.Close(); }
            }
        }

        public static List<string> DistinctMachineIds(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                var workbook = new XSSFWorkbook(fs);
                try { return DistinctMachineIds(ReadAll(workbook)); }
                finally { workbook.Close(); }
            }
        }

        internal static List<MachineRow> ReadAll(IWorkbook workbook)
        {
            if (workbook == null) throw new ArgumentNullException(nameof(workbook));

            MachineColumns columns;
            ISheet sheet = FindUnifiedSheet(workbook, out columns);
            if (sheet == null)
                throw new InvalidDataException(
                    "未找到唯一的统一机台数据表。仅支持 U_ 字段和普通回路名称，"
                    + "不支持旧字段、备用字段或自动猜测其它工作表。"
                    + "必需字段：U_区域、U_机台ID、U_设备楼层、U_设备轴位、"
                    + "U_上游编号、U_上游楼层、U_上游轴位、U_配电信息、U_电缆型号、"
                    + "U_上游类型、回路名称；可选字段：U_厂务开关、U_序号、U_软管直径。");

            return ReadBoundRows(workbook, sheet, columns);
        }

        private static List<MachineRow> ReadBoundRows(IWorkbook workbook, ISheet sheet,
            MachineColumns columns)
        {
            var result = new List<MachineRow>();
            var strikeoutByFontIndex = new Dictionary<short, bool>();
            for (int rowIndex = columns.HeaderRow + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;

                // The unified workbook contract is column-based: after the header is
                // bound, read only these exact columns from the same physical row.
                // Do not inspect or expand merged regions; a blank cell stays blank.
                ICell circuitCell = row.GetCell(columns.CircuitName);
                if (HasStrikeout(workbook, circuitCell, strikeoutByFontIndex)
                    || string.IsNullOrWhiteSpace(Text(circuitCell)))
                    continue;

                MachineRow machine = ToUnifiedRow(row, columns);
                if (string.IsNullOrWhiteSpace(machine.MachineId))
                    throw new InvalidDataException("机台数据表第 " + (rowIndex + 1)
                        + " 行存在回路名称，但 U_机台ID 为空或公式结果未保存。");
                result.Add(machine);
            }
            return result;
        }

        internal static List<MachineRow> FindRows(IEnumerable<MachineRow> rows, string keyword)
        {
            string key = (keyword ?? "").Trim();
            var all = rows ?? Enumerable.Empty<MachineRow>();
            var exact = all.Where(row => string.Equals((row.MachineId ?? "").Trim(), key,
                StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0 || key.Length == 0) return exact;
            return all.Where(row => (row.CircuitName ?? "").IndexOf(key,
                StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        internal static List<string> DistinctMachineIds(IEnumerable<MachineRow> rows)
        {
            return (rows ?? Enumerable.Empty<MachineRow>())
                .Select(row => (row.MachineId ?? "").Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ISheet FindUnifiedSheet(IWorkbook workbook,
            out MachineColumns columns)
        {
            ISheet matched = null;
            MachineColumns matchedColumns = null;
            for (int sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
            {
                ISheet sheet = workbook.GetSheetAt(sheetIndex);
                MachineColumns candidate;
                if (!TryBindUnified(sheet, out candidate)) continue;
                if (matched != null)
                    throw new InvalidDataException(
                        "工作簿包含多个 U_ 数据表，请只保留一个数据工作表后重新选择工作簿。");
                matched = sheet;
                matchedColumns = candidate;
            }
            columns = matchedColumns;
            return matched;
        }

        private static bool TryBindUnified(ISheet sheet, out MachineColumns columns)
        {
            int maxRow = Math.Min(50, sheet.LastRowNum);
            for (int rowIndex = 0; rowIndex <= maxRow; rowIndex++)
            {
                IRow header = sheet.GetRow(rowIndex);
                if (header == null) continue;

                // These are the only accepted names. Source workbooks must be
                // corrected instead of silently guessed.
                int region = FindUnique(header, "U_区域");
                int machineId = FindUnique(header, "U_机台ID");
                int deviceFloor = FindUnique(header, "U_设备楼层");
                int deviceAxis = FindUnique(header, "U_设备轴位");
                int upstreamId = FindUnique(header, "U_上游编号");
                int upstreamFloor = FindUnique(header, "U_上游楼层");
                int upstreamAxis = FindUnique(header, "U_上游轴位");
                int detail = FindUnique(header, "U_配电信息");
                int cable = FindUnique(header, "U_电缆型号");
                int next = FindUnique(header, "U_上游类型");
                int facilitySwitch = FindUnique(header, "U_厂务开关");
                // The ordinary circuit column may appear more than once in ledgers
                // that carry a copied/derived section.  Keep the leftmost column,
                // while U_ fields below remain strict and unique.
                int circuit = FindFirst(header, "回路名称");

                // Current ledgers derive these elsewhere. If present, read only
                // exact U_ names; no old “序号”/“DIA” aliases are accepted.
                int seq = FindUnique(header, "U_序号");
                int dia = FindUnique(header, "U_软管直径");

                if (region < 0 || machineId < 0 || deviceFloor < 0 || deviceAxis < 0
                    || upstreamId < 0 || upstreamFloor < 0 || upstreamAxis < 0
                    || detail < 0 || cable < 0 || next < 0 || circuit < 0)
                    continue;

                columns = new MachineColumns
                {
                    HeaderRow = rowIndex,
                    Region = region,
                    MachineId = machineId,
                    CircuitName = circuit,
                    Cable = cable,
                    Fr = upstreamId,
                    Detail = detail,
                    Seq = seq,
                    Dia = dia,
                    Next = next,
                    DownstreamAxis = deviceAxis,
                    UpstreamAxis = upstreamAxis,
                    DeviceFloor = deviceFloor,
                    PanelFloor = upstreamFloor,
                    FacilitySwitch = facilitySwitch
                };
                return true;
            }
            columns = null;
            return false;
        }

        private static int FindUnique(IRow header, string expected)
        {
            int found = -1;
            if (header == null) return found;
            for (int column = 0; column < header.LastCellNum; column++)
            {
                if (!ExcelHeaderBinder.Equals(header.GetCell(column), expected)) continue;
                if (found >= 0)
                    throw new InvalidDataException(
                        "工作表“" + header.Sheet.SheetName + "”存在重复字段“"
                        + expected + "”。");
                found = column;
            }
            return found;
        }

        private static int FindFirst(IRow header, string expected)
        {
            if (header == null) return -1;
            for (int column = 0; column < header.LastCellNum; column++)
                if (ExcelHeaderBinder.Equals(header.GetCell(column), expected)) return column;
            return -1;
        }

        private static MachineRow ToUnifiedRow(IRow row, MachineColumns columns)
        {
            return new MachineRow
            {
                Region = Text(GetCell(row, columns.Region)),
                MachineId = Text(GetCell(row, columns.MachineId)),
                CircuitName = Text(GetCell(row, columns.CircuitName)),
                Cable = Text(GetCell(row, columns.Cable)),
                Fr = Text(GetCell(row, columns.Fr)),
                Detail = Text(GetCell(row, columns.Detail)),
                Seq = Text(GetCell(row, columns.Seq)),
                Dia = Text(GetCell(row, columns.Dia)),
                Next = Text(GetCell(row, columns.Next)),
                DownstreamAxis = Text(GetCell(row, columns.DownstreamAxis)),
                UpstreamAxis = Text(GetCell(row, columns.UpstreamAxis)),
                DeviceFloor = Text(GetCell(row, columns.DeviceFloor)),
                PanelFloor = Text(GetCell(row, columns.PanelFloor)),
                FacilitySwitch = Text(GetCell(row, columns.FacilitySwitch))
            };
        }

        private static ICell GetCell(IRow row, int column)
            => row == null || column < 0 ? null : row.GetCell(column);

        private static string Text(ICell cell)
        {
            if (cell == null) return "";
            // A formula created/saved without a cached result is exposed by NPOI
            // as numeric zero. U_ columns are textual source data, so do not turn
            // that cache sentinel into a real machine ID or material value.
            if (cell.CellType == CellType.Formula
                && cell.CachedFormulaResultType == CellType.Numeric
                && Math.Abs(cell.NumericCellValue) < 1e-12)
                return "";

            // Machine rows only contain U_ fields and 回路名称.  Avoid the generic
            // reader's date/style inspection here; it is needlessly expensive for
            // thousands of text/formula cells and these columns are not dates.
            switch (cell.CellType)
            {
                case CellType.String:
                    return (cell.StringCellValue ?? "").Trim();
                case CellType.Numeric:
                    return cell.NumericCellValue.ToString("0.##", CultureInfo.InvariantCulture);
                case CellType.Boolean:
                    return cell.BooleanCellValue ? "TRUE" : "FALSE";
                case CellType.Formula:
                    switch (cell.CachedFormulaResultType)
                    {
                        case CellType.String:
                            return (cell.StringCellValue ?? "").Trim();
                        case CellType.Numeric:
                            return cell.NumericCellValue.ToString("0.##", CultureInfo.InvariantCulture);
                        case CellType.Boolean:
                            return cell.BooleanCellValue ? "TRUE" : "FALSE";
                        default:
                            return "";
                    }
                default:
                    return "";
            }
        }

        private static bool HasStrikeout(IWorkbook workbook, ICell cell,
            IDictionary<short, bool> strikeoutByFontIndex)
        {
            if (cell == null) return false;
            ICellStyle style = cell.CellStyle;
            if (style != null)
            {
                short fontIndex = style.FontIndex;
                if (!strikeoutByFontIndex.TryGetValue(fontIndex, out bool isStrikeout))
                {
                    IFont font = workbook.GetFontAt(fontIndex);
                    isStrikeout = font != null && font.IsStrikeout;
                    strikeoutByFontIndex[fontIndex] = isStrikeout;
                }
                if (isStrikeout) return true;
            }
            if (cell.CellType != CellType.String) return false;
            var xssf = cell.RichStringCellValue as XSSFRichTextString;
            if (xssf == null) return false;
            for (int run = 0; run < xssf.NumFormattingRuns; run++)
            {
                IFont font = xssf.GetFontOfFormattingRun(run);
                if (font != null && font.IsStrikeout) return true;
            }
            return false;
        }

    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Dwg;

namespace UNCAD.Core.Excel
{
    /// <summary>
    /// 读取 XLAYOUT 机台/回路筛选清单（D-055）：.xlsx（NPOI，第一个工作表）
    /// 或 .csv（UTF-8 BOM / UTF-8 / GBK 自动识别，逗号/分号/制表符自动嗅探）。
    /// 表头按别名自动认列，认不出机台ID列即报错并列出可接受列名（不猜位置）；
    /// 回路列可缺席（机台级清单）。行级规则：机台号为空的行跳过；同键条目去重。
    /// </summary>
    public static class XLayoutFilterListReader
    {
        /// <summary>机台ID 列可识别表头（去空白、忽略大小写比较）。</summary>
        public static readonly string[] MachineAliases =
            { "机台ID", "U_机台ID", "机台", "MachineID", "机台编号" };

        /// <summary>回路名称/设备名称 列可识别表头。</summary>
        public static readonly string[] CircuitAliases =
            { "回路名称", "U_回路名称", "设备名称", "回路", "设备" };

        public static bool TryRead(string path, out XLayoutFilterList list, out string error)
        {
            list = null;
            error = "";
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "清单文件路径为空。";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "清单文件不存在：" + path;
                return false;
            }
            string extension = Path.GetExtension(path) ?? "";
            bool isCsv = string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
            try
            {
                List<XLayoutFilterEntry> entries = isCsv
                    ? ReadCsv(path, out error)
                    : ReadXlsx(path, out error);
                if (entries == null) return false;
                list = new XLayoutFilterList(Deduplicate(entries), path);
                return true;
            }
            catch (System.Exception ex)
            {
                error = "读取清单文件失败：" + ex.Message;
                return false;
            }
        }

        private static List<XLayoutFilterEntry> ReadXlsx(string path, out string error)
        {
            error = "";
            // ExcelMachineReader 同款打开方式：共享读 + 允许删除，兼容 Excel 打开中的文件。
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                var workbook = new XSSFWorkbook(stream);
                try
                {
                    ISheet sheet = workbook.GetSheetAt(0);
                    if (sheet == null)
                    {
                        error = "清单文件没有工作表：" + path;
                        return null;
                    }
                    IRow header = FirstNonEmptyRow(sheet);
                    if (header == null)
                    {
                        error = MissingHeaderError(path);
                        return null;
                    }
                    int machineColumn;
                    try
                    {
                        machineColumn = ExcelHeaderBinder.Require(header,
                            sheet.SheetName, MachineAliases);
                    }
                    catch (InvalidDataException)
                    {
                        // ExcelHeaderBinder 只报第一个别名；清单场景要列出全部
                        // 可识别列名（用户拿到的清单表头五花八门）。
                        error = MissingHeaderError(path);
                        return null;
                    }
                    int circuitColumn = ExcelHeaderBinder.Optional(header,
                        sheet.SheetName, CircuitAliases);
                    var entries = new List<XLayoutFilterEntry>();
                    for (int r = header.RowNum + 1; r <= sheet.LastRowNum; r++)
                    {
                        IRow row = sheet.GetRow(r);
                        if (row == null) continue;
                        string machine = Cell(row, machineColumn);
                        if (machine.Length == 0) continue;
                        entries.Add(new XLayoutFilterEntry(machine,
                            circuitColumn >= 0 ? Cell(row, circuitColumn) : ""));
                    }
                    if (entries.Count == 0)
                        error = "清单没有数据行（机台ID 列全为空）：" + path;
                    return entries.Count > 0 ? entries : null;
                }
                finally { workbook.Close(); }
            }
        }

        private static List<XLayoutFilterEntry> ReadCsv(string path, out string error)
        {
            error = "";
            string[] lines = ReadTextLinesAutoEncoding(path);
            int headerIndex = lines.TakeWhile(line => Normalize(line).Length == 0).Count();
            if (headerIndex >= lines.Length)
            {
                error = MissingHeaderError(path);
                return null;
            }
            char delimiter = SniffDelimiter(lines[headerIndex]);
            string[] header = SplitCsvLine(lines[headerIndex], delimiter);
            int machineColumn = FindAliasColumn(header, MachineAliases);
            if (machineColumn < 0)
            {
                error = MissingHeaderError(path);
                return null;
            }
            int circuitColumn = FindAliasColumn(header, CircuitAliases);
            var entries = new List<XLayoutFilterEntry>();
            for (int r = headerIndex + 1; r < lines.Length; r++)
            {
                if (Normalize(lines[r]).Length == 0) continue;
                string[] cells = SplitCsvLine(lines[r], delimiter);
                string machine = Cell(cells, machineColumn);
                if (machine.Length == 0) continue;
                entries.Add(new XLayoutFilterEntry(machine,
                    circuitColumn >= 0 ? Cell(cells, circuitColumn) : ""));
            }
            if (entries.Count == 0)
                error = "清单没有数据行（机台ID 列全为空）：" + path;
            return entries.Count > 0 ? entries : null;
        }

        /// <summary>编码识别：UTF-8 BOM 直接用；无 BOM 先按严格 UTF-8 试解，
        /// 失败回退 GBK（现场导出的 CSV 常见 GBK）。</summary>
        private static string[] ReadTextLinesAutoEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                    .Split(NewlineChars, StringSplitOptions.None);
            try
            {
                // 严格 UTF-8：非法字节序列抛异常 → 走 GBK 回退。
                var strict = new UTF8Encoding(false, true);
                return strict.GetString(bytes)
                    .Split(NewlineChars, StringSplitOptions.None);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(936).GetString(bytes)
                    .Split(NewlineChars, StringSplitOptions.None);
            }
        }

        private static readonly string[] NewlineChars = { "\r\n", "\n", "\r" };
        private static readonly char[] Delimiters = { ',', ';', '\t' };

        private static char SniffDelimiter(string headerLine)
        {
            // 各分隔符计数，最多者胜（平手按 , ; \t 顺序取先）。
            char best = ',';
            int bestCount = -1;
            foreach (char delimiter in Delimiters)
            {
                int count = headerLine.Count(ch => ch == delimiter);
                if (count > bestCount)
                {
                    best = delimiter;
                    bestCount = count;
                }
            }
            return best;
        }

        private static string[] SplitCsvLine(string line, char delimiter)
            => (line ?? "").Split(delimiter);

        private static int FindAliasColumn(string[] header, string[] aliases)
        {
            foreach (string alias in aliases)
            {
                for (int c = 0; c < header.Length; c++)
                {
                    if (string.Equals(Normalize(header[c]), Normalize(alias),
                        StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            return -1;
        }

        private static string Cell(IRow row, int index)
        {
            ICell cell = index >= 0 && index < row.LastCellNum ? row.GetCell(index) : null;
            return ExcelColumnReader.CellToString(cell).Trim();
        }

        private static string Cell(string[] cells, int index)
            => index >= 0 && index < cells.Length ? cells[index].Trim() : "";

        private static IRow FirstNonEmptyRow(ISheet sheet)
        {
            for (int r = 0; r <= sheet.LastRowNum; r++)
            {
                IRow row = sheet.GetRow(r);
                if (row == null) continue;
                bool any = false;
                for (int c = 0; c < row.LastCellNum; c++)
                {
                    if (ExcelColumnReader.CellToString(row.GetCell(c))
                        .Trim().Length > 0) { any = true; break; }
                }
                if (any) return row;
            }
            return null;
        }

        private static string Normalize(string value)
            => new string((value ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).Trim();

        private static string MissingHeaderError(string path)
        {
            return "清单文件「" + Path.GetFileName(path) + "」认不出机台ID列。"
                + "可识别表头：" + string.Join(" / ", MachineAliases)
                + "（回路列可选：" + string.Join(" / ", CircuitAliases) + "）。";
        }

        /// <summary>同键条目去重（保持首次出现顺序）。</summary>
        private static List<XLayoutFilterEntry> Deduplicate(
            IEnumerable<XLayoutFilterEntry> entries)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<XLayoutFilterEntry>();
            foreach (XLayoutFilterEntry entry in entries)
            {
                if (!seen.Add(entry.MachineKey + "\u001f" + entry.CircuitKey)) continue;
                result.Add(entry);
            }
            return result;
        }
    }
}

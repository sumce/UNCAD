using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>固定清单条目：类/编码/名称/特征/单位/别名/全局迁移别名。</summary>
    public sealed class ListItem
    {
        public string Category { get; set; }
        public string Code { get; set; }     // 编号，如 1.25 / 3.8
        public string Name { get; set; }     // 项目名称，如 单芯电缆 XLPE / 包塑金属软管(波纹管)
        public string Feature { get; set; }  // 项目特征（完整描述模板）
        public string Unit { get; set; }     // 单位
        public string Alias { get; set; }    // 主别名，如 3*70+1*35 / 38mm
        public string Alias1 { get; set; }   // 迁移别名，如 32mm -> 本行38mm材料

        // 保留Spec兼容旧调用；固定清单的新正式字段名是“别名”。
        public string Spec
        {
            get => Alias ?? "";
            set => Alias = value;
        }
    }

    /// <summary>
    /// 读取工程量清单（在前 51 个物理行中自动定位含"项目特征"的表头）。
    /// 自动匹配严格使用“类”划定范围，再与“别名”精确比较；空类不参与判定。
    /// </summary>
    public static class ListItemReader
    {
        private const int HeaderScanRowLimit = 50;
        private const string EmbeddedCatalogResourceName =
            "UNCAD.Resources.embedded_catalog.tsv";
        private static readonly Lazy<List<ListItem>> EmbeddedCatalog =
            new Lazy<List<ListItem>>(LoadEmbeddedCatalog, true);
        private static readonly Lazy<BoqCatalogIndex> EmbeddedIndex =
            new Lazy<BoqCatalogIndex>(() => new BoqCatalogIndex(EmbeddedCatalog.Value), true);

        /// <summary>
        /// 内嵌固定清单：数据在编译期打包进插件，随版本发布，用户不再提供清单 Excel。
        /// 返回防御性副本，调用方修改不会污染共享内嵌数据。
        /// </summary>
        public static List<ListItem> ReadEmbedded()
            => CloneItems(EmbeddedCatalog.Value);

        /// <summary>
        /// 内嵌固定清单的只读索引，全进程只构建一次。命令运行时按规格查型号用它
        /// （U1Q/U1C 标注的第一行取清单「1.名称」）。
        /// </summary>
        public static BoqCatalogIndex EmbeddedCatalogIndex => EmbeddedIndex.Value;

        private static List<ListItem> LoadEmbeddedCatalog()
        {
            var assembly = typeof(ListItemReader).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                EmbeddedCatalogResourceName))
            {
                if (stream == null)
                    throw new InvalidDataException("插件内嵌固定清单资源缺失: "
                        + EmbeddedCatalogResourceName);
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return ParseEmbeddedTsv(reader.ReadToEnd());
            }
        }

        /// <summary>
        /// 解析内嵌 TSV：每行 编码/类/项目名称/项目特征/单位/别名/别名1。
        /// 转义规则与 scripts/GenerateEmbeddedCatalog.ps1 严格对称：\\ → \、\t → 制表符、
        /// \r → 回车、\n → 换行。
        /// </summary>
        public static List<ListItem> ParseEmbeddedTsv(string tsv)
        {
            var result = new List<ListItem>();
            string[] lines = (tsv ?? "").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string row = lines[index].TrimEnd('\r');
                if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] fields = row.Split('\t');
                if (fields.Length != 7)
                    throw new InvalidDataException("内嵌固定清单第 " + (index + 1)
                        + " 行必须包含 7 列，实际为 " + fields.Length + " 列。");

                ListItem item = new ListItem
                {
                    Code = Unescape(fields[0]).Trim(),
                    Category = Unescape(fields[1]).Trim(),
                    Name = Unescape(fields[2]).Trim(),
                    Feature = Unescape(fields[3]).Trim(),
                    Unit = Unescape(fields[4]).Trim(),
                    Alias = Unescape(fields[5]).Trim(),
                    Alias1 = Unescape(fields[6]).Trim()
                };
                if (item.Code.Length == 0 || item.Name.Length == 0)
                    throw new InvalidDataException("内嵌固定清单第 " + (index + 1)
                        + " 行缺少项目编码或项目名称。");
                result.Add(item);
            }
            return result;
        }

        private static string Unescape(string value)
            => (value ?? "").Replace("\\\\", "\\").Replace("\\t", "\t")
                .Replace("\\r", "\r").Replace("\\n", "\n");

        private static List<ListItem> CloneItems(IEnumerable<ListItem> source)
        {
            var result = new List<ListItem>();
            foreach (ListItem item in source ?? new List<ListItem>())
            {
                result.Add(new ListItem
                {
                    Category = item.Category,
                    Code = item.Code,
                    Name = item.Name,
                    Feature = item.Feature,
                    Unit = item.Unit,
                    Alias = item.Alias,
                    Alias1 = item.Alias1
                });
            }
            return result;
        }

        private sealed class ListColumns
        {
            public int Category;
            public int Code;
            public int Name;
            public int Feature;
            public int Unit;
            public int Alias;
            public int Alias1;
        }

        public static List<ListItem> ReadList(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                var wb = new XSSFWorkbook(fs);
                try { return ReadList(wb); }
                finally { wb.Close(); }
            }
        }

        internal static List<ListItem> ReadList(IWorkbook wb)
        {
            var result = new List<ListItem>();
            int headerRow;
            var sheet = FindListSheet(wb, out headerRow);
            ListColumns columns = BindColumns(sheet, headerRow);

            for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                string code = ExcelColumnReader.CellToString(row.GetCell(columns.Code)).Trim();
                string name = ExcelColumnReader.CellToString(row.GetCell(columns.Name)).Trim();
                if (code.Length == 0 || name.Length == 0) continue;
                if (name == "小计" || name == "合计"
                    || name.IndexOf("Note", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (code.IndexOf('.') < 0) continue;

                result.Add(new ListItem
                {
                    Category = Cell(row, columns.Category),
                    Code = code,
                    Name = name,
                    Feature = ExcelColumnReader.CellToString(row.GetCell(columns.Feature)).Trim(),
                    Unit = Cell(row, columns.Unit),
                    Alias = Cell(row, columns.Alias),
                    Alias1 = Cell(row, columns.Alias1)
                });
            }
            return result;
        }

        private static ISheet FindListSheet(IWorkbook wb, out int headerRow)
        {
            headerRow = -1;
            for (int i = 0; i < wb.NumberOfSheets; i++)
            {
                var sh = wb.GetSheetAt(i);
                int maxRow = Math.Min(HeaderScanRowLimit, sh.LastRowNum);
                for (int rowIndex = 0; rowIndex <= maxRow; rowIndex++)
                {
                    var hdr = sh.GetRow(rowIndex);
                    if (hdr == null) continue;
                    for (int c = 0; c < hdr.LastCellNum; c++)
                    {
                        if (!ExcelHeaderBinder.Equals(hdr.GetCell(c), "项目特征")) continue;
                        headerRow = rowIndex;
                        return sh;
                    }
                }
            }
            throw new InvalidDataException("未找到包含“项目特征”表头的固定清单工作表。");
        }

        private static ListColumns BindColumns(ISheet sheet, int headerRow)
        {
            IRow header = sheet.GetRow(headerRow);
            if (header == null)
                throw new InvalidDataException("固定清单工作表“" + sheet.SheetName + "”缺少表头行。");
            int alias = ExcelHeaderBinder.Optional(header, sheet.SheetName,
                "别名", "规格", "型号规格");
            // 兼容旧模板：规格/别名数据固定在第6列，但表头可能为空。
            if (alias < 0) alias = 5;
            return new ListColumns
            {
                Category = ExcelHeaderBinder.Optional(header, sheet.SheetName, "类", "类别"),
                Code = ExcelHeaderBinder.Require(header, sheet.SheetName, "编号", "项次编码"),
                Name = ExcelHeaderBinder.Require(header, sheet.SheetName, "项目名称"),
                Feature = ExcelHeaderBinder.Require(header, sheet.SheetName, "项目特征"),
                Unit = ExcelHeaderBinder.Require(header, sheet.SheetName, "单位"),
                Alias = alias,
                Alias1 = ExcelHeaderBinder.Optional(header, sheet.SheetName, "别名1", "迁移别名")
            };
        }

        private static string Cell(IRow row, int column)
            => column < 0 ? ""
                : ExcelColumnReader.CellToString(row.GetCell(column)).Trim();

        /// <summary>规范化电缆型号后，仅与“电缆”类的别名比较。</summary>
        public static ListItem FindCable(List<ListItem> items, string cableModel)
            => new BoqCatalogIndex(items).FindCable(cableModel);

        /// <summary>规范化直径后，仅与“软管”类的别名比较。</summary>
        public static ListItem FindConduit(List<ListItem> items, string dia)
            => new BoqCatalogIndex(items).FindFlexibleConduit(dia);
    }
}

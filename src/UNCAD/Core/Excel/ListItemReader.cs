using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>清单条目（Sheet2：编号/项目名称/项目特征/单位/规格）。</summary>
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
    /// 读取工程量清单（自动定位表头含"项目特征"的工作表，即 Sheet2）。
    /// 电缆/软管按规格匹配清单编号：电缆型号 ZB-YJV-3*70+1*35 ↔ 编号 1.25，软管直径 51 ↔ 编号 3.8。
    /// </summary>
    public static class ListItemReader
    {
        private const string EmbeddedCatalogResourceName =
            "UNCAD.Resources.embedded_catalog.tsv";
        private static readonly Lazy<List<ListItem>> EmbeddedCatalog =
            new Lazy<List<ListItem>>(LoadEmbeddedCatalog, true);

        /// <summary>
        /// 内嵌固定清单：数据在编译期打包进插件，随版本发布，用户不再提供清单 Excel。
        /// 返回防御性副本，调用方修改不会污染共享内嵌数据。
        /// </summary>
        public static List<ListItem> ReadEmbedded()
            => CloneItems(EmbeddedCatalog.Value);

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
            foreach (string line in (tsv ?? "").Split('\n'))
            {
                string row = line.TrimEnd('\r');
                if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] fields = row.Split('\t');
                if (fields.Length != 7) continue;
                result.Add(new ListItem
                {
                    Code = Unescape(fields[0]),
                    Category = Unescape(fields[1]),
                    Name = Unescape(fields[2]),
                    Feature = Unescape(fields[3]),
                    Unit = Unescape(fields[4]),
                    Alias = Unescape(fields[5]),
                    Alias1 = Unescape(fields[6])
                });
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
            var sheet = FindListSheet(wb);
            ListColumns columns = BindColumns(sheet);

            for (int r = 1; r <= sheet.LastRowNum; r++)
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

        private static ISheet FindListSheet(IWorkbook wb)
        {
            for (int i = 0; i < wb.NumberOfSheets; i++)
            {
                var sh = wb.GetSheetAt(i);
                var hdr = sh.GetRow(0);
                if (hdr == null) continue;
                for (int c = 0; c < hdr.LastCellNum; c++)
                {
                    if (ExcelHeaderBinder.Equals(hdr.GetCell(c), "项目特征")) return sh;
                }
            }
            throw new InvalidDataException("未找到包含“项目特征”表头的固定清单工作表。");
        }

        private static ListColumns BindColumns(ISheet sheet)
        {
            IRow header = sheet.GetRow(0);
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

        /// <summary>按电缆型号查清单条目：去掉 ZB-YJV[R]- 前缀后匹配规格列（电缆段编号 1.x）。</summary>
        public static ListItem FindCable(List<ListItem> items, string cableModel)
            => new BoqCatalogIndex(items).FindCable(cableModel);

        /// <summary>按软管直径查清单条目：规格 "51mm" 且名称含"软管"（配管段编号 3.x）。</summary>
        public static ListItem FindConduit(List<ListItem> items, string dia)
            => new BoqCatalogIndex(items).FindFlexibleConduit(dia);
    }
}

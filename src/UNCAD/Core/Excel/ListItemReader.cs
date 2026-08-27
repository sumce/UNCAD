using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>清单条目（Sheet2：编号/项目名称/项目特征/单位/规格）。</summary>
    public sealed class ListItem
    {
        public string Code { get; set; }     // 编号，如 1.25 / 3.8
        public string Name { get; set; }     // 项目名称，如 单芯电缆 XLPE / 包塑金属软管(波纹管)
        public string Feature { get; set; }  // 项目特征（完整描述模板）
        public string Unit { get; set; }     // 单位
        public string Spec { get; set; }     // 规格列，如 3*70+1*35 / 51mm / 400*100
    }

    /// <summary>
    /// 读取工程量清单（自动定位表头含"项目特征"的工作表，即 Sheet2）。
    /// 电缆/软管按规格匹配清单编号：电缆型号 ZB-YJV-3*70+1*35 ↔ 编号 1.25，软管直径 51 ↔ 编号 3.8。
    /// </summary>
    public static class ListItemReader
    {
        private sealed class ListColumns
        {
            public int Code;
            public int Name;
            public int Feature;
            public int Unit;
            public int Spec;
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
                    Code = code,
                    Name = name,
                    Feature = ExcelColumnReader.CellToString(row.GetCell(columns.Feature)).Trim(),
                    Unit = ExcelColumnReader.CellToString(row.GetCell(columns.Unit)).Trim(),
                    Spec = ExcelColumnReader.CellToString(row.GetCell(columns.Spec)).Trim()
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
            int spec = ExcelHeaderBinder.Optional(header, sheet.SheetName, "规格", "型号规格");
            // 兼容当前固定模板：规格数据位于第 6 列，但该列表头为空。
            if (spec < 0) spec = 5;
            return new ListColumns
            {
                Code = ExcelHeaderBinder.Require(header, sheet.SheetName, "编号", "项次编码"),
                Name = ExcelHeaderBinder.Require(header, sheet.SheetName, "项目名称"),
                Feature = ExcelHeaderBinder.Require(header, sheet.SheetName, "项目特征"),
                Unit = ExcelHeaderBinder.Require(header, sheet.SheetName, "单位"),
                Spec = spec
            };
        }

        /// <summary>按电缆型号查清单条目：去掉 ZB-YJV[R]- 前缀后匹配规格列（电缆段编号 1.x）。</summary>
        public static ListItem FindCable(List<ListItem> items, string cableModel)
        {
            if (string.IsNullOrEmpty(cableModel)) return null;
            string spec = NormalizeCable(cableModel);
            return items.FirstOrDefault(i => i.Code.StartsWith("1.", StringComparison.Ordinal) && i.Spec == spec);
        }

        /// <summary>按软管直径查清单条目：规格 "51mm" 且名称含"软管"（配管段编号 3.x）。</summary>
        public static ListItem FindConduit(List<ListItem> items, string dia)
        {
            if (string.IsNullOrEmpty(dia)) return null;
            string spec = dia.Trim() + "mm";
            return items.FirstOrDefault(i => i.Code.StartsWith("3.", StringComparison.Ordinal)
                && i.Spec == spec
                && i.Name.IndexOf("软管", StringComparison.Ordinal) >= 0);
        }

        /// <summary>"ZB-YJV-3*70+1*35" / "ZB-YJVR-3*2.5" → "3*70+1*35" / "3*2.5"。</summary>
        private static string NormalizeCable(string cable)
        {
            string s = cable.Trim();
            int idx = s.IndexOf('-');
            if (idx >= 0) s = s.Substring(idx + 1);
            idx = s.IndexOf('-');
            if (idx >= 0) s = s.Substring(idx + 1);
            return s;
        }
    }
}

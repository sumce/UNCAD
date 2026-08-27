using System;
using System.Collections.Generic;
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

        public override string ToString()
        {
            return MachineId + " | " + CircuitName + " | 电缆:" + Cable
                + " | " + Detail + " | 序号:" + Seq + " | Φ" + Dia
                + " | NEXT:" + Next + " | 下游轴位:" + DownstreamAxis
                + " | 上游轴位:" + UpstreamAxis;
        }
    }

    /// <summary>读取并查询 Excel 机台表。</summary>
    public static class ExcelMachineReader
    {
        private sealed class MachineColumns
        {
            public int Region;
            public int MachineId;
            public int CircuitName;
            public int Cable;
            public int Fr;
            public int Detail;
            public int Seq;
            public int Dia;
            public int Next;
            public int DownstreamAxis;
            public int UpstreamAxis;
        }

        public static List<MachineRow> FindRows(string filePath, string keyword)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var wb = new XSSFWorkbook(fs);
                try { return FindRows(ReadAll(wb), keyword); }
                finally { wb.Close(); }
            }
        }

        public static List<string> DistinctMachineIds(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var wb = new XSSFWorkbook(fs);
                try { return DistinctMachineIds(ReadAll(wb)); }
                finally { wb.Close(); }
            }
        }

        internal static List<MachineRow> ReadAll(IWorkbook workbook)
        {
            var result = new List<MachineRow>();
            var sheet = FindMachineSheet(workbook);
            MachineColumns columns = BindColumns(sheet);
            for (int r = 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var machine = ToRow(row, columns);
                if (!string.IsNullOrWhiteSpace(machine.MachineId)) result.Add(machine);
            }
            return result;
        }

        internal static List<MachineRow> FindRows(IEnumerable<MachineRow> rows, string keyword)
        {
            string kw = (keyword ?? "").Trim();
            var all = rows ?? Enumerable.Empty<MachineRow>();
            var exact = all.Where(r => string.Equals(r.MachineId, kw,
                StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;
            if (kw.Length == 0) return exact;
            return all.Where(r => (r.CircuitName ?? "").IndexOf(kw,
                StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        internal static List<string> DistinctMachineIds(IEnumerable<MachineRow> rows)
        {
            return rows.Select(r => r.MachineId?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ISheet FindMachineSheet(IWorkbook workbook)
        {
            for (int i = 0; i < workbook.NumberOfSheets; i++)
            {
                var sheet = workbook.GetSheetAt(i);
                var header = sheet.GetRow(0);
                if (header == null) continue;
                for (int c = 0; c < header.LastCellNum; c++)
                {
                    if (ExcelHeaderBinder.Equals(header.GetCell(c), "机台ID")) return sheet;
                }
            }
            throw new InvalidDataException("未找到包含“机台ID”表头的机台数据工作表。");
        }

        private static MachineColumns BindColumns(ISheet sheet)
        {
            IRow header = sheet.GetRow(0);
            if (header == null)
                throw new InvalidDataException("机台数据工作表“" + sheet.SheetName + "”缺少表头行。");
            return new MachineColumns
            {
                Region = ExcelHeaderBinder.Require(header, sheet.SheetName, "所属区域"),
                MachineId = ExcelHeaderBinder.Require(header, sheet.SheetName, "机台ID"),
                CircuitName = ExcelHeaderBinder.Require(header, sheet.SheetName, "回路名称", "设备名称"),
                Cable = ExcelHeaderBinder.Require(header, sheet.SheetName, "电缆型号"),
                Fr = ExcelHeaderBinder.Require(header, sheet.SheetName, "FR"),
                Detail = ExcelHeaderBinder.Require(header, sheet.SheetName, "详情", "配电详情"),
                Seq = ExcelHeaderBinder.Require(header, sheet.SheetName, "项目序号", "序号"),
                Dia = ExcelHeaderBinder.Require(header, sheet.SheetName, "软管直径", "DIA"),
                Next = ExcelHeaderBinder.Require(header, sheet.SheetName, "NEXT", "盘柜类型"),
                DownstreamAxis = ExcelHeaderBinder.Require(header, sheet.SheetName, "下游轴位"),
                UpstreamAxis = ExcelHeaderBinder.Require(header, sheet.SheetName, "上游轴位")
            };
        }

        private static MachineRow ToRow(IRow row, MachineColumns columns)
        {
            return new MachineRow
            {
                Region = ExcelColumnReader.CellToString(row.GetCell(columns.Region)),
                MachineId = ExcelColumnReader.CellToString(row.GetCell(columns.MachineId)),
                CircuitName = ExcelColumnReader.CellToString(row.GetCell(columns.CircuitName)),
                Cable = ExcelColumnReader.CellToString(row.GetCell(columns.Cable)),
                Fr = ExcelColumnReader.CellToString(row.GetCell(columns.Fr)),
                Detail = ExcelColumnReader.CellToString(row.GetCell(columns.Detail)),
                Seq = ExcelColumnReader.CellToString(row.GetCell(columns.Seq)),
                Dia = ExcelColumnReader.CellToString(row.GetCell(columns.Dia)),
                Next = ExcelColumnReader.CellToString(row.GetCell(columns.Next)),
                DownstreamAxis = ExcelColumnReader.CellToString(row.GetCell(columns.DownstreamAxis)),
                UpstreamAxis = ExcelColumnReader.CellToString(row.GetCell(columns.UpstreamAxis))
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NPOI.SS.UserModel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Submission
{
    /// <summary>
    /// BOQ 工作簿里的 DATA 工作表：一行一个回路（= 一个图框/设备），记录电缆、软管、
    /// 桥架、线管、上下游轴位与更新时间，便于按回路追溯数据来源。
    ///
    /// 更新语义与 BOQ 设备列保持一致：按「机台ID + 回路名」定位——有则更新、无则追加，
    /// 同机台其它回路的行保留不动。因此重复执行 U1F/U1U 只刷新那一行，不会累积重复行。
    /// </summary>
    internal static class BoqDataSheet
    {
        public const string SheetName = "DATA";

        private const int HeaderRowIndex = 0;
        private const int ColumnCount = 15;
        private const int UpdatedAtIndex = 0;
        private const int MachineIdIndex = 1;
        private const int DeviceNameIndex = 2;
        private const int PanelTypeIndex = 3;
        private const int CableIndex = 4;
        private const int CableMetersIndex = 5;
        private const int DiameterIndex = 6;
        private const int FlexibleMetersIndex = 7;
        private const int BridgeInfoIndex = 8;
        private const int BridgeMetersIndex = 9;
        private const int ConduitInfoIndex = 10;
        private const int ConduitMetersIndex = 11;
        private const int UpstreamAxisIndex = 12;
        private const int DownstreamAxisIndex = 13;
        private const int MaterialCountIndex = 14;

        internal static readonly string[] Headers =
        {
            "更新时间", "机台ID", "回路", "盘类型",
            "电缆型号", "电缆长度(m)",
            "软管管径", "软管长度(m)",
            "桥架信息", "桥架长度(m)",
            "线管信息", "线管长度(m)",
            "上游轴位", "下游轴位", "材料行数"
        };

        internal static void Write(IWorkbook workbook, IEnumerable<SubmissionRecord> records,
            DateTime updatedAt)
        {
            if (workbook == null) throw new ArgumentNullException(nameof(workbook));
            List<SubmissionRecord> source = (records ?? Enumerable.Empty<SubmissionRecord>())
                .Where(record => record != null).ToList();
            if (source.Count == 0) return;

            ISheet sheet = workbook.GetSheet(SheetName);
            bool created = sheet == null;
            if (created) sheet = workbook.CreateSheet(SheetName);
            IRow header = sheet.GetRow(HeaderRowIndex);
            bool headerMissing = header == null;
            WriteHeader(sheet);
            if (created || headerMissing) StyleNewSheet(workbook, sheet);

            Dictionary<string, int> rows = FindRows(sheet);
            string timestamp = updatedAt.ToString("yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
            foreach (SubmissionRecord record in source)
            {
                string machineId = (record.MachineId ?? "").Trim();
                string deviceName = (record.DeviceName ?? "").Trim();
                if (deviceName.Length == 0) continue;
                string key = RowKey(machineId, deviceName);
                if (!rows.TryGetValue(key, out int rowIndex))
                {
                    rowIndex = NextRowIndex(sheet);
                    rows[key] = rowIndex;
                }
                IRow row = sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);
                SetText(row, UpdatedAtIndex, timestamp);
                SetText(row, MachineIdIndex, machineId);
                SetText(row, DeviceNameIndex, deviceName);
                SetText(row, PanelTypeIndex, record.PanelType);
                SetText(row, CableIndex, record.Cable);
                SetNumber(row, CableMetersIndex, record.CableMeters);
                SetText(row, DiameterIndex, record.Diameter);
                SetNumber(row, FlexibleMetersIndex, record.FlexibleConduitMeters);
                SetText(row, BridgeInfoIndex, record.BridgeInfo);
                SetNumber(row, BridgeMetersIndex, record.BridgeMeters);
                SetText(row, ConduitInfoIndex, record.ConduitInfo);
                SetNumber(row, ConduitMetersIndex, record.ConduitMeters);
                SetText(row, UpstreamAxisIndex, record.UpstreamAxis);
                SetText(row, DownstreamAxisIndex, record.DownstreamAxis);
                SetNumber(row, MaterialCountIndex,
                    (record.Materials == null ? 0 : record.Materials.Count)
                        .ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void WriteHeader(ISheet sheet)
        {
            IRow row = sheet.GetRow(HeaderRowIndex) ?? sheet.CreateRow(HeaderRowIndex);
            for (int column = 0; column < ColumnCount; column++)
            {
                if (row.GetCell(column) == null) row.CreateCell(column);
            }
            for (int column = 0; column < Headers.Length; column++)
                row.GetCell(column).SetCellValue(Headers[column]);
        }

        private static void StyleNewSheet(IWorkbook workbook, ISheet sheet)
        {
            try
            {
                IFont font = workbook.CreateFont();
                font.IsBold = true;
                ICellStyle style = workbook.CreateCellStyle();
                style.SetFont(font);
                IRow header = sheet.GetRow(HeaderRowIndex);
                for (int column = 0; column < ColumnCount; column++)
                {
                    ICell cell = header?.GetCell(column);
                    if (cell != null) cell.CellStyle = style;
                }
                sheet.CreateFreezePane(0, 1);
                int[] widths = { 19, 14, 18, 12, 26, 14, 10, 13, 22, 13, 22, 13, 12, 12, 10 };
                for (int column = 0; column < widths.Length; column++)
                    sheet.SetColumnWidth(column, widths[column] * 256);
            }
            catch
            {
                // 样式只是锦上添花：失败不影响数据写入。
            }
        }

        private static Dictionary<string, int> FindRows(ISheet sheet)
        {
            var rows = new Dictionary<string, int>(IdentityTextNormalizer.Comparer);
            for (int rowIndex = HeaderRowIndex + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;
                string machineId = CellText(row.GetCell(MachineIdIndex));
                string deviceName = CellText(row.GetCell(DeviceNameIndex));
                if (deviceName.Length == 0) continue;
                rows[RowKey(machineId, deviceName)] = rowIndex;
            }
            return rows;
        }

        private static int NextRowIndex(ISheet sheet)
        {
            int rowIndex = HeaderRowIndex + 1;
            while (sheet.GetRow(rowIndex) != null) rowIndex++;
            return rowIndex;
        }

        private static string RowKey(string machineId, string deviceName)
            => (machineId ?? "").Trim() + "\n" + (deviceName ?? "").Trim();

        private static string CellText(ICell cell)
            => cell == null ? "" : (cell.ToString() ?? "").Trim();

        private static void SetText(IRow row, int column, string value)
        {
            ICell cell = row.GetCell(column) ?? row.CreateCell(column);
            cell.SetCellType(CellType.String);
            cell.SetCellValue((value ?? "").Trim());
        }

        private static void SetNumber(IRow row, int column, string value)
        {
            ICell cell = row.GetCell(column) ?? row.CreateCell(column);
            if (TryParseNumber(value, out decimal number))
            {
                cell.SetCellValue((double)number);
                return;
            }
            SetText(row, column, value);
        }

        private static bool TryParseNumber(string value, out decimal number)
        {
            string text = (value ?? "").Trim();
            number = 0m;
            if (text.Length == 0) return false;
            return decimal.TryParse(text, NumberStyles.Float,
                       CultureInfo.InvariantCulture, out number)
                   || decimal.TryParse(text, NumberStyles.Float,
                       CultureInfo.CurrentCulture, out number);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NPOI.SS.UserModel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Core.Submission
{
    /// <summary>
    /// BOQ 工作簿里的 DATA 工作表：一行一个回路（= 一个图框/设备），记录电缆、软管、
    /// 桥架、线管、上下游轴位与更新时间，便于按回路追溯数据来源。
    ///
    /// 更新语义与 BOQ 设备列保持一致：按「机台ID + 回路名 + 上游编号」定位——
    /// 有则更新、无则追加，同机台其它回路的行保留不动。因此重复执行 U1F/U1U
    /// 只刷新那一行，不会累积重复行。
    ///
    /// D-057：行键加上游编号。旧版只按「机台ID + 回路名」，同机台同名回路
    /// （上游编号不同）互相覆盖、只留最后一行。旧工作簿里没有上游编号的行
    /// （该回路名在表里只有一行）首次遇到带上游的记录时沿用并补上上游编号。
    /// </summary>
    internal static class BoqDataSheet
    {
        public const string SheetName = "DATA";

        private const int HeaderRowIndex = 0;
        private const int ColumnCount = 16;
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
        /// <summary>D-057：上游编号（追加在末尾，老工作簿的列位不变）。</summary>
        internal const int UpstreamIdIndex = 15;

        internal static readonly string[] Headers =
        {
            "更新时间", "机台ID", "回路", "盘类型",
            "电缆型号", "电缆长度(m)",
            "软管管径", "软管长度(m)",
            "桥架信息", "桥架长度(m)",
            "线管信息", "线管长度(m)",
            "上游轴位", "下游轴位", "材料行数",
            "上游编号"
        };

        /// <summary>表里现有行的索引：完整行键 → 行号；机台+回路名 → 行数。</summary>
        private sealed class SheetIndex
        {
            public Dictionary<string, int> Rows =
                new Dictionary<string, int>(IdentityTextNormalizer.Comparer);

            public Dictionary<string, int> NameCounts =
                new Dictionary<string, int>(IdentityTextNormalizer.Comparer);
        }

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

            SheetIndex index = FindRows(sheet);
            string timestamp = updatedAt.ToString("yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
            foreach (SubmissionRecord record in source)
            {
                string machineId = (record.MachineId ?? "").Trim();
                string deviceName = (record.DeviceName ?? "").Trim();
                if (deviceName.Length == 0) continue;
                string upstream = (record.UpstreamId ?? "").Trim();
                string upstreamKey = CircuitIdentityKey.UpstreamKey(upstream);
                string key = RowKey(machineId, deviceName, upstreamKey);
                if (!index.Rows.TryGetValue(key, out int rowIndex))
                {
                    string nameKey = NameKey(machineId, deviceName);
                    index.NameCounts.TryGetValue(nameKey, out int named);
                    if (upstreamKey.Length > 0 && named == 1
                        && index.Rows.TryGetValue(RowKey(machineId, deviceName, ""),
                            out int legacyRow))
                    {
                        // 旧版留下的“无上游”行，且这个回路名在表里只有这一行：
                        // 沿用它并补上上游编号（不留两行）。
                        index.Rows.Remove(RowKey(machineId, deviceName, ""));
                        rowIndex = legacyRow;
                    }
                    else
                    {
                        rowIndex = NextRowIndex(sheet);
                        index.NameCounts[nameKey] = named + 1;
                    }
                    index.Rows[key] = rowIndex;
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
                SetText(row, UpstreamIdIndex, upstream);
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
                int[] widths = { 19, 14, 18, 12, 26, 14, 10, 13, 22, 13, 22, 13, 12, 12, 10, 18 };
                for (int column = 0; column < widths.Length; column++)
                    sheet.SetColumnWidth(column, widths[column] * 256);
            }
            catch
            {
                // 样式只是锦上添花：失败不影响数据写入。
            }
        }

        private static SheetIndex FindRows(ISheet sheet)
        {
            var index = new SheetIndex();
            for (int rowIndex = HeaderRowIndex + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null) continue;
                string machineId = CellText(row.GetCell(MachineIdIndex));
                string deviceName = CellText(row.GetCell(DeviceNameIndex));
                if (deviceName.Length == 0) continue;
                string upstreamKey = CircuitIdentityKey.UpstreamKey(
                    CellText(row.GetCell(UpstreamIdIndex)));
                index.Rows[RowKey(machineId, deviceName, upstreamKey)] = rowIndex;
                string nameKey = NameKey(machineId, deviceName);
                index.NameCounts[nameKey] = index.NameCounts.TryGetValue(nameKey,
                    out int named) ? named + 1 : 1;
            }
            return index;
        }

        private static int NextRowIndex(ISheet sheet)
        {
            int rowIndex = HeaderRowIndex + 1;
            while (sheet.GetRow(rowIndex) != null) rowIndex++;
            return rowIndex;
        }

        // '\u001f' 不是空白：IdentityTextNormalizer 的比较器忽略空白，用它做
        // 分隔符才不会把「机台/回路名/上游」的边界抹掉。
        private static string RowKey(string machineId, string deviceName, string upstreamKey)
            => (machineId ?? "").Trim() + "\u001f" + (deviceName ?? "").Trim()
                + "\u001f" + (upstreamKey ?? "");

        private static string NameKey(string machineId, string deviceName)
            => (machineId ?? "").Trim() + "\u001f" + (deviceName ?? "").Trim();

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

using System;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Features.Fill
{
    internal static class CadDrawingInfoTableWriter
    {
        internal static bool IsDrawingInfoTable(Table table)
            => CadTableLayoutClassifier.IsDrawingInfoTable(table);

        internal static bool IsCurrentDrawingInfoTable(Table table)
            => CadTableLayoutClassifier.IsCurrentDrawingInfoTable(table);

        internal static bool RepairSplitFloorLayout(Transaction transaction, ObjectId id)
        {
            Table table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
            if (table == null || table.Columns.Count != 7
                || !CadTableLayoutClassifier.TryFindDrawingInfoHeader(table,
                    out int headerRow)
                || CadTableLayoutClassifier.IsCurrentDrawingInfoTable(table)) return false;

            int valueRow = headerRow + 1;
            string deviceFloor = CellText(table, valueRow, 1);
            string upstreamFloor = CellText(table, valueRow, 2);
            string floor = JoinFloors(deviceFloor, upstreamFloor);
            string drafter = CellText(table, valueRow, 3);
            string reviewer = CellText(table, valueRow, 4);
            string date = CellText(table, valueRow, 5);
            string version = CellText(table, valueRow, 6);

            table.UpgradeOpen();
            table.Cells[headerRow, 1].TextString = "楼层";
            table.Cells[headerRow, 2].TextString = "制图";
            table.Cells[headerRow, 3].TextString = "审核";
            table.Cells[headerRow, 4].TextString = "日期";
            table.Cells[headerRow, 5].TextString = "版本";
            table.Cells[headerRow, 6].TextString = "";
            if (valueRow < table.Rows.Count)
            {
                table.Cells[valueRow, 1].TextString = floor;
                table.Cells[valueRow, 2].TextString = drafter;
                table.Cells[valueRow, 3].TextString = reviewer;
                table.Cells[valueRow, 4].TextString = date;
                table.Cells[valueRow, 5].TextString = version;
                table.Cells[valueRow, 6].TextString = "";
            }
            table.GenerateLayout();
            table.RecordGraphicsModified(true);
            return true;
        }

        internal static void Write(Transaction transaction, ObjectId[] tableIds,
            MachineRow machine, DateTime now)
        {
            string floor = JoinFloors(machine?.DeviceFloor, machine?.PanelFloor);
            string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            foreach (ObjectId id in tableIds ?? Array.Empty<ObjectId>())
            {
                Table table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                if (!CadTableLayoutClassifier.TryFindCurrentDrawingInfoHeader(table,
                    out int headerRow)
                    || headerRow + 1 >= table.Rows.Count) continue;
                table.UpgradeOpen();
                table.Cells[headerRow + 1, 1].TextString = floor;
                table.Cells[headerRow + 1, 4].TextString = date;
                table.RecordGraphicsModified(true);
            }
        }

        private static string JoinFloors(string deviceFloor, string upstreamFloor)
            => string.Join("/", new[] { deviceFloor, upstreamFloor }
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0));

        private static string CellText(Table table, int row, int column)
            => row >= 0 && row < table.Rows.Count && column >= 0
                && column < table.Columns.Count
                ? TextParser.CleanMText(table.Cells[row, column].TextString ?? "").Trim()
                : "";
    }
}

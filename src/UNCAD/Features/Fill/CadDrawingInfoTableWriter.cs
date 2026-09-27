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
#pragma warning disable 618 // AutoCAD 2022 direct Table setters avoid stale Cell wrappers.
    internal static class CadDrawingInfoTableWriter
    {
        internal static bool IsDrawingInfoTable(Table table)
            => CadTableLayoutClassifier.IsDrawingInfoTable(table);

        internal static bool IsCurrentDrawingInfoTable(Table table)
            => CadTableLayoutClassifier.IsCurrentDrawingInfoTable(table);

        internal static bool RepairSplitFloorLayout(Transaction transaction, ObjectId id)
        {
            Table table = transaction.GetObject(id, OpenMode.ForWrite, true) as Table;
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

            table.SetTextString(headerRow, 1, "楼层");
            table.SetTextString(headerRow, 2, "制图");
            table.SetTextString(headerRow, 3, "审核");
            table.SetTextString(headerRow, 4, "日期");
            table.SetTextString(headerRow, 5, "版本");
            table.SetTextString(headerRow, 6, "");
            if (valueRow < table.Rows.Count)
            {
                table.SetTextString(valueRow, 1, floor);
                table.SetTextString(valueRow, 2, drafter);
                table.SetTextString(valueRow, 3, reviewer);
                table.SetTextString(valueRow, 4, date);
                table.SetTextString(valueRow, 5, version);
                table.SetTextString(valueRow, 6, "");
            }
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
                Table table = transaction.GetObject(id, OpenMode.ForWrite, true) as Table;
                if (!CadTableLayoutClassifier.TryFindCurrentDrawingInfoHeader(table,
                    out int headerRow)
                    || headerRow + 1 >= table.Rows.Count) continue;
                table.SetTextString(headerRow + 1, 1, floor);
                table.SetTextString(headerRow + 1, 4, date);
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
                ? TextParser.CleanMText(table.TextString(row, column) ?? "").Trim()
                : "";
    }
#pragma warning restore 618
}

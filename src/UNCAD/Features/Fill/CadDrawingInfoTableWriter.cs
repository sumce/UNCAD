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
            string deviceFloor = TextParser.CleanMText(valueRow < table.Rows.Count
                ? table.Cells[valueRow, 1].TextString ?? "" : "").Trim();
            string upstreamFloor = TextParser.CleanMText(valueRow < table.Rows.Count
                ? table.Cells[valueRow, 2].TextString ?? "" : "").Trim();
            string floor = JoinFloors(deviceFloor, upstreamFloor);
            double floorWidth = table.Columns[1].Width + table.Columns[2].Width;

            table.UpgradeOpen();
            table.DeleteColumns(2, 1);
            table.Columns[1].Width = floorWidth;
            table.Cells[headerRow, 1].TextString = "楼层";
            if (valueRow < table.Rows.Count) table.Cells[valueRow, 1].TextString = floor;
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
    }
}

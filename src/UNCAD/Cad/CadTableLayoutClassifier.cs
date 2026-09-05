using System;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Fill;

namespace UNCAD.Cad
{
    internal static class CadTableLayoutClassifier
    {
        internal static bool IsDrawingInfoTable(Table table)
            => TryFindDrawingInfoHeader(table, out _);

        internal static bool IsCurrentDrawingInfoTable(Table table)
            => TryFindCurrentDrawingInfoHeader(table, out _);

        internal static bool TryFindDrawingInfoHeader(Table table, out int headerRow)
            => TryFindDrawingInfoHeader(table, false, out headerRow);

        internal static bool TryFindCurrentDrawingInfoHeader(Table table,
            out int headerRow)
            => TryFindDrawingInfoHeader(table, true, out headerRow);

        private static bool TryFindDrawingInfoHeader(Table table, bool currentOnly,
            out int headerRow)
        {
            headerRow = -1;
            if (table == null || table.Columns.Count < 6) return false;
            int rowCount = table.Rows.Count;
            int columnCount = Math.Min(table.Columns.Count, 7);
            for (int row = 0; row < rowCount; row++)
            {
                string first = table.Cells[row, 0].TextString ?? "";
                if (!TableLayoutClassifier.IsDrawingInfoHeaderStart(first)) continue;
                var cells = new string[columnCount];
                cells[0] = first;
                for (int column = 1; column < cells.Length; column++)
                    cells[column] = table.Cells[row, column].TextString ?? "";
                bool matches = currentOnly
                    ? TableLayoutClassifier.IsCurrentDrawingInfoHeader(cells)
                    : TableLayoutClassifier.IsDrawingInfoHeader(cells);
                if (!matches) continue;
                headerRow = row;
                return true;
            }
            return false;
        }
    }
}

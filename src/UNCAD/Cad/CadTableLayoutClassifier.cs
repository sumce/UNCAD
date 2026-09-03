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
            for (int row = 0; row < table.Rows.Count; row++)
            {
                var cells = new string[Math.Min(table.Columns.Count, 7)];
                for (int column = 0; column < cells.Length; column++)
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

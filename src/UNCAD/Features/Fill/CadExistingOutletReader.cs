using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>Reads the outlet rows that U1U must preserve verbatim from the current CAD BOQ.</summary>
    internal static class CadExistingOutletReader
    {
        public static List<TableFillRow> Read(CadContext ctx, ObjectId[] tableIds,
            int configuredStartRow, int configuredClearRows)
        {
            var outlets = new List<TableFillRow>();
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in tableIds ?? new ObjectId[0])
                {
                    var table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                    if (table == null || table.Columns.Count < 6) continue;
                    int startRow = CadTableFillWriter.ResolveWriteStartRow(
                        table, configuredStartRow);
                    if (startRow < 0) continue;
                    int rowsToRead = TableClearPolicy.ResolveRows(
                        table.Rows.Count - startRow, configuredClearRows);
                    int endRow = Math.Min(table.Rows.Count, startRow + rowsToRead);
                    // Only the exact range U1U will clear and rewrite is authoritative.
                    for (int rowIndex = startRow; rowIndex < endRow; rowIndex++)
                    {
                        var row = new TableFillRow
                        {
                            Category = TableFillCategory.Manual,
                            SortOrder = rowIndex,
                            Name = CellText(table, rowIndex, 1),
                            Description = CellText(table, rowIndex, 2),
                            Unit = CellText(table, rowIndex, 3),
                            Quantity = CellText(table, rowIndex, 4),
                            Code = CellText(table, rowIndex, 5),
                            CatalogMatched = true
                        };
                        if (UpdateOutletPolicy.IsOutlet(row)) outlets.Add(row);
                    }
                }
                transaction.Commit();
            }
            return outlets;
        }

        private static string CellText(Table table, int row, int column)
        {
            try { return table.Cells[row, column].TextString ?? ""; }
            catch (System.Exception ex)
            {
                Log.Warn("U1U read existing outlet cell failed: " + ex.Message);
                return "";
            }
        }
    }
}

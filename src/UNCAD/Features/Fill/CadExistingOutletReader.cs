using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
#pragma warning disable 618 // AutoCAD 2022 direct table reads avoid stale Cell wrappers.
    /// <summary>Reads the outlet rows that U1U must preserve verbatim from the current CAD BOQ.</summary>
    internal static class CadExistingOutletReader
    {
        public static List<TableFillRow> Read(CadContext ctx, ObjectId[] tableIds,
            int configuredStartRow, int configuredClearRows)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                List<TableFillRow> result = Read(transaction, tableIds,
                    configuredStartRow, configuredClearRows);
                transaction.Commit();
                return result;
            }
        }

        internal static List<TableFillRow> Read(Transaction transaction,
            ObjectId[] tableIds, int configuredStartRow, int configuredClearRows)
        {
            var outlets = new List<TableFillRow>();
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
                    // 老图纸常把固定编码放在 NO. 列而留空"项次编码"。与
                    // FillUpdateRowMerger/SubmissionRecordExtractor 一样回退取一次，
                    // 否则这条行会被当成"无编码"，U1F/U1U 写到 BOQ 阶段才整批中止。
                    string number = CellText(table, rowIndex, 0);
                    string code = CellText(table, rowIndex, 5).Trim();
                    string catalogCode = SubmissionRecordExtractor.IsCatalogCode(code) ? code
                        : SubmissionRecordExtractor.IsCatalogCode(number) ? number.Trim() : "";
                    var row = new TableFillRow
                    {
                        Category = TableFillCategory.Manual,
                        SortOrder = rowIndex,
                        Name = CellText(table, rowIndex, 1),
                        Description = CellText(table, rowIndex, 2),
                        Unit = CellText(table, rowIndex, 3),
                        Quantity = CellText(table, rowIndex, 4),
                        Code = catalogCode,
                        // 没有固定编码的既有行保持 unmatched，交给 D-016 在事务前拦住。
                        CatalogMatched = catalogCode.Length > 0
                    };
                    if (UpdateOutletPolicy.IsOutlet(row)) outlets.Add(row);
                }
            }
            return outlets;
        }

        private static string CellText(Table table, int row, int column)
        {
            try { return table.TextString(row, column) ?? ""; }
            catch (System.Exception ex)
            {
                Log.Warn("U1U read existing outlet cell failed: " + ex.Message);
                return "";
            }
        }
    }
#pragma warning restore 618
}

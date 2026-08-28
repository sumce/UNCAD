using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    internal static class CadTableFillWriter
    {
        public static int Fill(CadContext ctx, ObjectId[] tableIds, int startRow,
            int clearRowCount, List<TableFillRow> plannedRows, double textHeight)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                int filled = Fill(ctx, transaction, tableIds, startRow,
                    clearRowCount, plannedRows, textHeight);
                if (filled >= 0) transaction.Commit();
                return filled;
            }
        }

        internal static int Fill(CadContext ctx, Transaction transaction,
            ObjectId[] tableIds, int startRow, int clearRowCount,
            List<TableFillRow> plannedRows, double textHeight)
        {
            plannedRows = plannedRows ?? new List<TableFillRow>();
            tableIds = tableIds ?? new ObjectId[0];
            int filled = 0;
            Transaction tr = transaction;
            foreach (ObjectId id in tableIds)
            {
                var table = tr.GetObject(id, OpenMode.ForRead) as Table;
                if (table == null) continue;
                if (table.Rows.Count < 2 || table.Columns.Count < 6)
                {
                    ctx.Write("\n[BOQ-TABLE/清单表格] 表格格式不兼容：至少需要表头和 1 个数据行、共 6 列，本次未写入。");
                    return -1;
                }
                table.UpgradeOpen();
                double[] rowHeights = CaptureRowHeights(table);
                double[] columnWidths = CaptureColumnWidths(table);

                int row = ResolveWriteStartRow(table, startRow);
                if (row < 0)
                {
                    ctx.Write("\n[BOQ-TABLE/清单表格] 起始位置之后没有可写入的数据行，本次未写入。");
                    return -1;
                }
                int available = table.Rows.Count - row;
                int rowsToClear = TableClearPolicy.ResolveRows(available, clearRowCount);
                if (!TableClearPolicy.CanFit(plannedRows.Count, rowsToClear))
                {
                    ctx.Write("\n[BOQ-TABLE/清单表格] 勾选清单超过模板清除范围：需要 " + plannedRows.Count
                        + " 行，当前配置清除 " + rowsToClear
                        + " 行。请减少勾选或在配置中心增大清除行数。");
                    return -1;
                }

                table.SuppressRegenerateTable(true);
                try
                {
                    for (int clearRow = row; clearRow < row + rowsToClear; clearRow++)
                        for (int column = 1; column <= 5; column++)
                            SetCellTextPreservingFormat(table, clearRow, column, "");

                    for (int i = 0; i < plannedRows.Count; i++)
                    {
                        TableFillRow planned = plannedRows[i];
                        int targetRow = row + i;
                        SetCellTextPreservingFormat(table, targetRow, 1, planned.Name,
                            textHeight, true);
                        SetCellTextPreservingFormat(table, targetRow, 2, planned.Description,
                            textHeight, true);
                        SetCellTextPreservingFormat(table, targetRow, 3, planned.Unit,
                            textHeight, true);
                        SetCellTextPreservingFormat(table, targetRow, 4, planned.Quantity,
                            textHeight, true);
                        SetCellTextPreservingFormat(table, targetRow, 5, planned.Code,
                            textHeight, true);
                    }
                    RestoreTableDimensions(table, rowHeights, columnWidths);
                    LockGeneratedRowHeights(table, row, plannedRows.Count);
                }
                finally
                {
                    table.SuppressRegenerateTable(false);
                    RestoreTableDimensions(table, rowHeights, columnWidths);
                    LockGeneratedRowHeights(table, row, plannedRows.Count);
                }
                filled += plannedRows.Count;
            }
            return filled;
        }

        private static void SetCellTextPreservingFormat(Table table, int row, int column,
            string text, double? targetTextHeight = null, bool fitToCell = false)
        {
            Cell cell = table.Cells[row, column];
            double? originalTextHeight = cell.TextHeight;
            ObjectId? textStyleId = cell.TextStyleId;

            cell.TextString = text ?? "";
            cell.TextHeight = targetTextHeight ?? originalTextHeight;
            cell.TextStyleId = textStyleId;
            if (!fitToCell) return;
            foreach (CellContent content in cell.Contents)
            {
                if (targetTextHeight.HasValue) content.TextHeight = targetTextHeight.Value;
                content.IsAutoScale = true;
            }
        }

        private static double[] CaptureRowHeights(Table table)
        {
            var heights = new double[table.Rows.Count];
            for (int row = 0; row < heights.Length; row++) heights[row] = table.Rows[row].Height;
            return heights;
        }

        private static double[] CaptureColumnWidths(Table table)
        {
            var widths = new double[table.Columns.Count];
            for (int column = 0; column < widths.Length; column++)
                widths[column] = table.Columns[column].Width;
            return widths;
        }

        private static void RestoreTableDimensions(Table table, double[] rowHeights,
            double[] columnWidths)
        {
            for (int row = 0; row < rowHeights.Length && row < table.Rows.Count; row++)
            {
                try { table.Rows[row].Height = rowHeights[row]; }
                catch (System.Exception ex)
                {
                    Log.Warn("UNC_F restore row height " + row + " failed: " + ex.Message);
                }
            }
            for (int column = 0; column < columnWidths.Length
                && column < table.Columns.Count; column++)
            {
                try { table.Columns[column].Width = columnWidths[column]; }
                catch (System.Exception ex)
                {
                    Log.Warn("UNC_F restore column width " + column + " failed: " + ex.Message);
                }
            }
        }

        private static void LockGeneratedRowHeights(Table table, int startRow, int count)
        {
            int endRow = Math.Min(table.Rows.Count, startRow + count);
            for (int row = Math.Max(0, startRow); row < endRow; row++)
            {
                try { table.Rows[row].Height = TableFillFormatter.GeneratedRowHeight; }
                catch (System.Exception ex)
                {
                    Log.Warn("UNC_F lock generated row height " + row + " failed: "
                        + ex.Message);
                }
            }
        }

        // Capacity preflight and the actual transaction must resolve the same final data row.
        internal static int ResolveWriteStartRow(Table table, int startRow)
        {
            int row = FirstDataRow(table) + Math.Max(1, startRow) - 1;
            int guard = 0;
            while (row >= 0 && row < table.Rows.Count
                && IsHeaderLike(table, row) && guard++ < 8) row++;
            return row >= 0 && row < table.Rows.Count ? row : -1;
        }

        // FillFeature 的容量预检必须使用同一套表头定位规则，避免预检与写入落在不同数据行。
        internal static int FirstDataRow(Table table)
        {
            int rows = table.Rows.Count;
            int limit = Math.Min(rows, 10);
            int header = -1;
            for (int row = 0; row < limit; row++)
                if (IsHeaderLike(table, row)) header = row;

            if (header >= 0)
            {
                int row = header + 1;
                while (row < rows && IsHeaderLike(table, row)) row++;
                return Math.Min(row, rows - 1);
            }

            for (int row = 0; row < limit; row++)
            {
                string number = table.Cells[row, 0].TextString.Trim();
                if (TableLayoutClassifier.IsNumberedDataRow(number)) return row;
            }
            return rows > 1 ? 1 : 0;
        }

        private static bool IsHeaderLike(Table table, int row)
        {
            string number = table.Cells[row, 0].TextString.Trim();
            string name = table.Cells[row, 1].TextString.Trim();
            string code = table.Columns.Count > 5 ? table.Cells[row, 5].TextString.Trim() : "";
            return TableLayoutClassifier.IsHeaderLike(number, name, code);
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
#pragma warning disable 618 // AutoCAD 2022 native Table APIs avoid wrapper invalidation here.
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
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                var table = tr.GetObject(id, OpenMode.ForWrite, true) as Table;
                if (table == null) continue;
                if (table.Rows.Count < 2 || table.Columns.Count < 6)
                {
                    ctx.Write("\n[BOQ-TABLE/清单表格] 表格格式不兼容：至少需要表头和 1 个数据行、共 6 列，本次未写入。");
                    return -1;
                }
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

                // Do not toggle the table regeneration switch here.  AutoCAD 2022 can
                // dereference the native table while regeneration is restored from
                // a finally block, which is an uncatchable access violation.  The
                // BOQ table is small; direct Table.Set* calls are slower by a few
                // milliseconds but keep the transaction in a valid native state.
                // The shipped template may lock cells to protect hand edits; the
                // plugin still owns the generated range, so unlock it first.
                UnlockWriteRange(table, row, rowsToClear, plannedRows.Count);

                for (int clearRow = row; clearRow < row + rowsToClear; clearRow++)
                    for (int column = 0; column <= 5; column++)
                        SetCellTextPreservingFormat(table, clearRow, column, "",
                            fitToCell: true);

                for (int i = 0; i < plannedRows.Count; i++)
                {
                    TableFillRow planned = plannedRows[i];
                    int targetRow = row + i;
                    SetCellTextPreservingFormat(table, targetRow, 0, (i + 1).ToString(),
                        textHeight, true);
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

                // Apply the BOQ data-row height after content and per-content
                // auto-scale overrides have been cleared.  AutoCAD may require a
                // few extra drawing units for one wrapped row; use the largest
                // native minimum and apply it to the whole generated range so
                // rows stay uniform instead of differing by content.
                LockGeneratedRowHeights(table, row, rowsToClear);
                table.RecordGraphicsModified(true);
                filled += plannedRows.Count;
            }
            return filled;
        }

        /// <summary>
        /// Reapplies generated-row geometry after another CAD reader may have
        /// caused AutoCAD to regenerate the table.  Call this immediately before
        /// transaction commit because native table layout is lazy.
        /// </summary>
        internal static void FixGeneratedRowHeights(Transaction transaction,
            ObjectId[] tableIds, int startRow, int clearRowCount)
        {
            foreach (ObjectId id in tableIds ?? Array.Empty<ObjectId>())
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                Table table = transaction.GetObject(id, OpenMode.ForWrite, true) as Table;
                if (table == null) continue;
                int row = ResolveWriteStartRow(table, startRow);
                if (row < 0) continue;
                int count = TableClearPolicy.ResolveRows(table.Rows.Count - row,
                    clearRowCount);
                LockGeneratedRowHeights(table, row, count);
                table.RecordGraphicsModified(true);
            }
        }

        /// <summary>
        /// Clears cell lock flags over the clear range plus every planned data row
        /// so a template locked against hand edits stays writable by the plugin.
        /// Header rows above the write start are left as authored.
        /// </summary>
        private static void UnlockWriteRange(Table table, int startRow, int rowsToClear,
            int plannedRowCount)
        {
            int lastRow = Math.Min(startRow + Math.Max(rowsToClear, plannedRowCount) - 1,
                table.Rows.Count - 1);
            for (int row = startRow; row <= lastRow; row++)
            {
                for (int column = 0; column < Math.Min(6, table.Columns.Count); column++)
                {
                    try
                    {
                        CellStates state = table.GetCellState(row, column);
                        CellStates unlocked = state & ~(CellStates.ContentLocked
                            | CellStates.FormatLocked | CellStates.ContentReadOnly
                            | CellStates.FormatReadOnly);
                        if (unlocked != state) table.SetCellState(row, column, unlocked);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warn("U1F 清单表单元格解锁失败: 行 " + row + " 列 " + column
                            + "，" + ex.Message);
                    }
                }
            }
        }

        private static void SetCellTextPreservingFormat(Table table, int row, int column,
            string text, double? targetTextHeight = null, bool fitToCell = false)
        {
            // Use Table's native cell methods instead of holding Cell/CellContent
            // wrappers across a text mutation.  This keeps every native call tied
            // to the live table object while AutoCAD regenerates the cell.
            table.SetTextString(row, column, text ?? "");
            if (targetTextHeight.HasValue)
            {
                table.SetTextHeight(row, column, targetTextHeight.Value);
                table.SetTextHeight(row, column, 0, targetTextHeight.Value);
            }
            if (fitToCell)
            {
                // Existing BOQ rows may carry a per-content override left by an
                // older build.  SetAutoScale(false) changes the cell default but
                // does not clear that override, so force content slot 0 as well.
                table.SetAutoScale(row, column, false);
                table.SetIsAutoScale(row, column, 0, false);
            }
        }

        private static void LockGeneratedRowHeights(Table table, int startRow, int count)
        {
            int endRow = Math.Min(table.Rows.Count, startRow + Math.Max(0, count));
            double fixedHeight = TableFillFormatter.GeneratedRowHeight;
            for (int row = Math.Max(0, startRow); row < endRow; row++)
            {
                try { fixedHeight = Math.Max(fixedHeight, table.MinimumRowHeight(row)); }
                catch (System.Exception ex)
                {
                    Log.Warn("U1F read generated row minimum height " + row + " failed: "
                        + ex.Message);
                }
            }
            for (int row = Math.Max(0, startRow); row < endRow; row++)
            {
                try
                {
                    table.SetRowHeight(row, fixedHeight);
                }
                catch (System.Exception ex)
                {
                    Log.Warn("U1F lock generated row height " + row + " failed: "
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
                string number = ReadCellText(table, row, 0);
                if (TableLayoutClassifier.IsNumberedDataRow(number)) return row;
            }
            return rows > 1 ? 1 : 0;
        }

        private static bool IsHeaderLike(Table table, int row)
        {
            string number = ReadCellText(table, row, 0);
            string name = ReadCellText(table, row, 1);
            string code = table.Columns.Count > 5 ? ReadCellText(table, row, 5) : "";
            return TableLayoutClassifier.IsHeaderLike(number, name, code);
        }

        private static string ReadCellText(Table table, int row, int column)
        {
            try
            {
                return table.TextString(row, column)?.Trim() ?? "";
            }
            catch (System.Exception ex)
            {
                Log.Warn("U1F 读取清单单元格失败: 行 " + row + " 列 " + column
                    + "，" + ex.Message);
                return "";
            }
        }
    }
#pragma warning restore 618
}

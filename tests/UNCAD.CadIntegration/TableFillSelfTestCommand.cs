using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
#pragma warning disable 618 // AutoCAD 2022 Table APIs used by the production writer.
    /// <summary>Exercises the BOQ table writer inside an AutoCAD 2022 transaction.</summary>
    public sealed class TableFillSelfTestCommand
    {
        [CommandMethod("UNCAD_TABLE_FILL_SELFTEST")]
        public void Run()
        {
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            string stage = "initial fill";
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                var ctx = new CadContext(document);
                ObjectId tableId;
                var widths = new[] { 2800d, 8000d, 20000d, 2800d, 3200d, 5000d };
                double headerHeight;
                var rows = new List<TableFillRow>
                {
                    new TableFillRow
                    {
                        Name = "电缆",
                        Description = string.Join(@"\P", Enumerable.Repeat(
                            "这是用于验证长项目特征不得撑大清单行的完整描述", 30)),
                        Unit = "M", Quantity = "12.5", Code = "1.1"
                    },
                    new TableFillRow
                    {
                        Name = "桥架", Description = "梯形桥架200Wx100H",
                        Unit = "M", Quantity = "5", Code = "2.1"
                    }
                };
                stage = "shipped template";
                VerifyTemplate(ctx, rows);
                stage = "initial fill";
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = new Table();
                    table.SetDatabaseDefaults(ctx.Db);
                    table.SetSize(13, 6);
                    for (int column = 0; column < widths.Length; column++)
                        table.SetColumnWidth(column, widths[column]);
                    table.SetRowHeight(2200d);
                    table.Position = Point3d.Origin;
                    table.Cells[1, 0].TextString = "NO.";
                    table.Cells[1, 1].TextString = "项目名称";
                    table.Cells[1, 2].TextString = "项目特征描述";
                    table.Cells[1, 3].TextString = "单位";
                    table.Cells[1, 4].TextString = "数量";
                    table.Cells[1, 5].TextString = "项次编码";
                    table.GenerateLayout();
                    tableId = ctx.AddToCurrentSpace(transaction, table);
                    transaction.Commit();
                }

                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var original = (Table)transaction.GetObject(tableId, OpenMode.ForRead);
                    headerHeight = original.Rows[1].Height;
                    int filled = CadTableFillWriter.Fill(ctx, transaction,
                        new[] { tableId }, 1, 11, rows, 500d);
                    if (filled != 2) throw new InvalidOperationException(
                        "Expected two filled BOQ rows, got " + filled + ".");
                    CadTableFillWriter.FixGeneratedRowHeights(transaction,
                        new[] { tableId }, 1, 11);
                    transaction.Commit();
                }

                // Re-open after commit. AutoCAD may regenerate a Table while the
                // transaction is closing; checking only the live writer wrapper
                // would miss that regression.
                double fittedTextHeight;
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForRead);
                    AssertFixedGeometry(table, widths, headerHeight);
                    if (!table.TextString(2, 2).Contains(@"\pxsm0.85;")
                        || !table.TextString(2, 2).Contains(rows[0].Description))
                        throw new InvalidOperationException("Long description was lost.");
                    fittedTextHeight = table.GetTextHeight(2, 2, 0);
                    if (fittedTextHeight >= 500 || table.GetTextHeight(3, 2, 0) != 500)
                        throw new InvalidOperationException("Only overflowing rows should shrink text.");
                    transaction.Commit();
                }

                // U1U must be stable across repeated writes, including native regeneration.
                stage = "repeated fill";
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    CadTableFillWriter.Fill(ctx, transaction, new[] { tableId }, 1, 11, rows, 500d);
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForWrite);
                    table.RecomputeTableBlock(true);
                    CadTableFillWriter.FixGeneratedRowHeights(transaction, new[] { tableId }, 1, 11);
                    transaction.Commit();
                }
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForRead);
                    AssertFixedGeometry(table, widths, headerHeight);
                    if (Math.Abs(table.GetTextHeight(2, 2, 0) - fittedTextHeight) > 0.01)
                        throw new InvalidOperationException("Repeated fill compounded text scaling.");
                    transaction.Commit();
                }

                string drawingPath = Path.Combine(Path.GetTempPath(), "uncad-table-"
                    + Guid.NewGuid().ToString("N") + ".dwg");
                stage = "DWG reopen";
                try
                {
                    using (Database snapshot = ctx.Db.Wblock(new ObjectIdCollection(
                        new[] { tableId }), Point3d.Origin))
                        snapshot.SaveAs(drawingPath, DwgVersion.Current);
                    using (var reopened = new Database(false, true))
                    {
                        reopened.ReadDwgFile(drawingPath, FileOpenMode.OpenForReadAndAllShare, false, null);
                        reopened.CloseInput(true);
                        using (Transaction transaction = reopened.TransactionManager.StartTransaction())
                        {
                            var blocks = (BlockTable)transaction.GetObject(reopened.BlockTableId, OpenMode.ForRead);
                            var space = (BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                            Table table = space.Cast<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead))
                                .OfType<Table>().Single();
                            AssertFixedGeometry(table, widths, headerHeight);
                            transaction.Commit();
                        }
                    }
                }
                finally { if (File.Exists(drawingPath)) File.Delete(drawingPath); }

                // A later short description restores the configured text size, not the fitted size.
                stage = "short description";
                rows[0].Description = "短描述";
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    CadTableFillWriter.Fill(ctx, transaction, new[] { tableId }, 1, 11, rows, 500d);
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForRead);
                    AssertFixedGeometry(table, widths, headerHeight);
                    if (table.GetTextHeight(2, 2, 0) != 500)
                        throw new InvalidOperationException("Short description kept its shrunken text size.");
                    transaction.Commit();
                }

                // Impossible margins must abort instead of silently enlarging every row.
                stage = "impossible margins";
                ObjectId invalidId;
                double invalidHeight;
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)((Table)transaction.GetObject(tableId, OpenMode.ForRead)).Clone();
                    table.SetMargin(2, 2, CellMargins.Top, TableFillFormatter.GeneratedRowHeight);
                    invalidId = ctx.AddToCurrentSpace(transaction, table);
                    transaction.Commit();
                }
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)transaction.GetObject(invalidId, OpenMode.ForRead);
                    invalidHeight = table.Rows[2].Height;
                    bool rejected = false;
                    rows[0].Description = "must not be written";
                    try { CadTableFillWriter.Fill(ctx, transaction, new[] { invalidId }, 1, 11, rows, 500d); }
                    catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new InvalidOperationException("Impossible cell margins were silently accepted.");
                    transaction.Abort();
                }
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForWrite);
                    stage = "rollback verification";
                    AssertFixedGeometry(table, widths, headerHeight);
                    var invalid = (Table)transaction.GetObject(invalidId, OpenMode.ForWrite);
                    if (invalid.TextString(2, 2) != "短描述"
                        || Math.Abs(invalid.Rows[2].Height - invalidHeight) > 0.01)
                        throw new InvalidOperationException("Rejected fill modified the table.");
                    invalid.Erase();
                    table.Erase();
                    transaction.Commit();
                }

                document.Editor.WriteMessage("\nUNCAD_TABLE_FILL_SELFTEST:PASS\n");
                if (!string.IsNullOrWhiteSpace(resultPath))
                    File.WriteAllText(resultPath, "PASS");
            }
            catch (System.Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(resultPath))
                    File.WriteAllText(resultPath, stage + ": " + ex);
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                    "\nUNCAD_TABLE_FILL_SELFTEST:FAIL\n" + ex + "\n");
            }
        }

        private static void AssertFixedGeometry(Table table, double[] widths, double headerHeight)
        {
            for (int column = 0; column < widths.Length; column++)
                if (Math.Abs(table.Columns[column].Width - widths[column]) > 0.01)
                    throw new InvalidOperationException("Column width changed.");
            if (Math.Abs(table.Rows[1].Height - headerHeight) > 0.01)
                throw new InvalidOperationException("Header height changed.");
            for (int row = 2; row < table.Rows.Count; row++)
            {
                double height = table.Rows[row].Height;
                if (Math.Abs(height - TableFillFormatter.GeneratedRowHeight) > 0.01)
                    throw new InvalidOperationException("Generated BOQ row " + row + " height was "
                        + height + ", expected fixed height " + TableFillFormatter.GeneratedRowHeight + ".");
                for (int column = 0; column < 6; column++)
                    if (table.IsAutoScale(row, column) || table.GetIsAutoScale(row, column, 0))
                        throw new InvalidOperationException("Generated BOQ cell still has AutoScale enabled.");
            }
        }

        private static void VerifyTemplate(CadContext ctx, List<TableFillRow> rows)
        {
            ObjectId id;
            double[] widths;
            double headerHeight;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)transaction.GetObject(ctx.CurrentSpaceId, OpenMode.ForRead);
                Table template = space.Cast<ObjectId>().Select(item => transaction.GetObject(item, OpenMode.ForRead))
                    .OfType<Table>().FirstOrDefault(item => item.Rows.Count == 13 && item.Columns.Count == 6);
                if (template == null) throw new InvalidOperationException("Run this test with XFrameTemplate.dwg.");
                widths = Enumerable.Range(0, 6).Select(column => template.Columns[column].Width).ToArray();
                headerHeight = template.Rows[1].Height;
                id = ctx.AddToCurrentSpace(transaction, (Table)template.Clone());
                transaction.Commit();
            }
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                CadTableFillWriter.Fill(ctx, transaction, new[] { id }, 1, 11, rows, 500d);
                CadTableFillWriter.FixGeneratedRowHeights(transaction, new[] { id }, 1, 11);
                transaction.Commit();
            }
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var table = (Table)transaction.GetObject(id, OpenMode.ForWrite);
                table.RecomputeTableBlock(true);
                AssertFixedGeometry(table, widths, headerHeight);
                if (!table.TextString(2, 2).Contains(@"\pxsm0.85;")
                    || !table.TextString(2, 2).Contains(rows[0].Description))
                    throw new InvalidOperationException("Template description changed.");
                table.Erase();
                transaction.Commit();
            }
        }
    }
#pragma warning restore 618
}

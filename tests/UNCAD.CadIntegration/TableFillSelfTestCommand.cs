using System;
using System.Collections.Generic;
using System.IO;
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
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                var ctx = new CadContext(document);
                ObjectId tableId;
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = new Table();
                    table.SetDatabaseDefaults(ctx.Db);
                    table.SetSize(13, 6);
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
                    int filled = CadTableFillWriter.Fill(ctx, transaction,
                        new[] { tableId }, 1, 11,
                        new List<TableFillRow>
                        {
                            new TableFillRow
                            {
                                Name = "电缆",
                                Description = @"1.名称:一段较长的清单特征描述\P2.说明:固定行高",
                                Unit = "M",
                                Quantity = "12.5",
                                Code = "1.1"
                            },
                            new TableFillRow
                            {
                                Name = "桥架",
                                Description = "梯形桥架200Wx100H",
                                Unit = "M",
                                Quantity = "5",
                                Code = "2.1"
                            }
                        }, 500d);
                    if (filled != 2) throw new InvalidOperationException(
                        "Expected two filled BOQ rows, got " + filled + ".");

                    transaction.Commit();
                }

                // Re-open after commit. AutoCAD may regenerate a Table while the
                // transaction is closing; checking only the live writer wrapper
                // would miss that regression.
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForRead);
                    double? uniformHeight = null;
                    for (int row = 2; row < table.Rows.Count; row++)
                    {
                        double height = table.Rows[row].Height;
                        if (height + 0.01 < TableFillFormatter.GeneratedRowHeight)
                            throw new InvalidOperationException(
                                "Generated BOQ row " + row + " height after commit was " + height
                                + ", below minimum " + TableFillFormatter.GeneratedRowHeight + ".");
                        if (uniformHeight.HasValue && Math.Abs(height - uniformHeight.Value) > 0.01)
                            throw new InvalidOperationException(
                                "Generated BOQ row " + row + " height after commit was " + height
                                + ", expected uniform height " + uniformHeight.Value + ".");
                        uniformHeight = height;
                        for (int column = 0; column < 6; column++)
                            if (table.IsAutoScale(row, column))
                                throw new InvalidOperationException(
                                    "Generated BOQ cell " + row + "," + column
                                    + " still has AutoScale enabled after commit.");
                    }
                    transaction.Commit();
                }

                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var table = (Table)transaction.GetObject(tableId, OpenMode.ForWrite);
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
                    File.WriteAllText(resultPath, ex.ToString());
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                    "\nUNCAD_TABLE_FILL_SELFTEST:FAIL\n" + ex + "\n");
            }
        }
    }
#pragma warning restore 618
}

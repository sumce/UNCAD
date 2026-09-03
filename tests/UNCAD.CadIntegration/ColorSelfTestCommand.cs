using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
    public sealed class ColorSelfTestCommand
    {
        [CommandMethod("UNCAD_COLOR_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            Editor editor = document.Editor;
            ObjectId lineId;
            ObjectId nestedLineId;
            ObjectId nestedTextId;
            ObjectId nestedReferenceId;
            ObjectId referenceId;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(database.BlockTableId,
                    OpenMode.ForWrite);
                var nestedDefinition = new BlockTableRecord
                {
                    Name = "UNCAD_COLOR_SELFTEST_NESTED_" + Guid.NewGuid().ToString("N")
                };
                ObjectId nestedDefinitionId = table.Add(nestedDefinition);
                transaction.AddNewlyCreatedDBObject(nestedDefinition, true);
                var nestedLine = new Line(new Point3d(0, 0, 0), new Point3d(5, 0, 0))
                {
                    ColorIndex = 1
                };
                nestedLineId = nestedDefinition.AppendEntity(nestedLine);
                transaction.AddNewlyCreatedDBObject(nestedLine, true);
                var nestedText = new DBText
                {
                    TextString = "upstream number",
                    Position = new Point3d(0, 2, 0),
                    Height = 1,
                    ColorIndex = 1
                };
                nestedTextId = nestedDefinition.AppendEntity(nestedText);
                transaction.AddNewlyCreatedDBObject(nestedText, true);

                var definition = new BlockTableRecord
                {
                    Name = "UNCAD_COLOR_SELFTEST_" + Guid.NewGuid().ToString("N")
                };
                ObjectId definitionId = table.Add(definition);
                transaction.AddNewlyCreatedDBObject(definition, true);
                var line = new Line(Point3d.Origin, new Point3d(10, 0, 0))
                {
                    ColorIndex = 1
                };
                lineId = definition.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
                var nestedReference = new BlockReference(Point3d.Origin, nestedDefinitionId)
                {
                    ColorIndex = 1
                };
                nestedReferenceId = definition.AppendEntity(nestedReference);
                transaction.AddNewlyCreatedDBObject(nestedReference, true);

                var model = (BlockTableRecord)transaction.GetObject(
                    table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var reference = new BlockReference(Point3d.Origin, definitionId)
                {
                    ColorIndex = 2
                };
                referenceId = model.AppendEntity(reference);
                transaction.AddNewlyCreatedDBObject(reference, true);
                transaction.Commit();
            }

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                int changed = CadBlockColorWriter.Apply(transaction,
                    new[] { referenceId }, 3);
                transaction.Commit();
                if (changed != 1) throw new InvalidOperationException(
                    "Expected one changed block, got " + changed + ".");
            }

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var reference = (BlockReference)transaction.GetObject(referenceId,
                    OpenMode.ForRead);
                var line = (Line)transaction.GetObject(lineId, OpenMode.ForRead);
                var nestedReference = (BlockReference)transaction.GetObject(nestedReferenceId,
                    OpenMode.ForRead);
                var nestedLine = (Line)transaction.GetObject(nestedLineId, OpenMode.ForRead);
                var nestedText = (DBText)transaction.GetObject(nestedTextId, OpenMode.ForRead);
                if (reference.ColorIndex != 3 || line.ColorIndex != 0
                    || nestedReference.ColorIndex != 0 || nestedLine.ColorIndex != 0
                    || nestedText.ColorIndex != 0)
                    throw new InvalidOperationException("Color inheritance verification failed.");
            }
            editor.WriteMessage("\nUNCAD_COLOR_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                File.WriteAllText(resultPath, "PASS");
        }
    }
}

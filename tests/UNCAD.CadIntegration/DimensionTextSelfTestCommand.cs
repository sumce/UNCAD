using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Text;

namespace UNCAD.CadIntegration
{
    public sealed class DimensionTextSelfTestCommand
    {
        [CommandMethod("UNCAD_U1D_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            var context = new CadContext(document);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var space = transaction.GetObject(database.CurrentSpaceId,
                    OpenMode.ForWrite) as BlockTableRecord;
                var dimension = new AlignedDimension(
                    new Point3d(0, 0, 0), new Point3d(3000, 0, 0),
                    new Point3d(0, 500, 0), @"{\fSimSun;3000mm}",
                    database.Dimstyle)
                {
                    TextPosition = new Point3d(1500, 500, 0),
                    TextRotation = Math.PI / 6.0
                };
                ObjectId dimensionId = space.AppendEntity(dimension);
                transaction.AddNewlyCreatedDBObject(dimension, true);

                DimensionTextConversionResult result =
                    AlignedDimensionTextConverter.Convert(context, transaction,
                        new[] { dimensionId }, 222.0);
                if (result.ConvertedCount != 1 || dimension.IsErased
                    || dimension.DimensionText != DimensionTextFormatter.SuppressedDimensionText)
                    throw new InvalidOperationException("U1D did not preserve and suppress the source dimension.");

                var text = transaction.GetObject(result.TextIds[0], OpenMode.ForRead) as DBText;
                if (text == null || text.TextString != "3000mm"
                    || Math.Abs(text.Height - 222.0) > 0.0001
                    || Math.Abs(text.Rotation - Math.PI / 6.0) > 0.0001
                    || text.AlignmentPoint.DistanceTo(new Point3d(1500, 500, 0)) > 0.0001)
                    throw new InvalidOperationException("U1D text value, height, angle or position is incorrect.");

                DimensionTextConversionResult repeated =
                    AlignedDimensionTextConverter.Convert(context, transaction,
                        new[] { dimensionId }, 222.0);
                if (repeated.ConvertedCount != 0 || repeated.AlreadyConvertedCount != 1)
                    throw new InvalidOperationException("Repeated U1D created duplicate text.");
            }

            document.Editor.WriteMessage("\nUNCAD_U1D_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                File.WriteAllText(resultPath, "PASS");
        }
    }
}

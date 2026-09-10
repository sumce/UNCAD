using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// Verifies the U1U/U1F statistics pipeline reads aligned/rotated dimension text
    /// overrides (for example 2000mm) and skips auto-measured dimensions.
    /// </summary>
    public sealed class StatisticsDimensionSelfTestCommand
    {
        [CommandMethod("UNCAD_DIMSTAT_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            var context = new CadContext(document);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var space = transaction.GetObject(database.CurrentSpaceId,
                    OpenMode.ForWrite) as BlockTableRecord;
                var aligned = new AlignedDimension(
                    new Point3d(0, 0, 0), new Point3d(2000, 0, 0),
                    new Point3d(0, 500, 0), "2000mm", database.Dimstyle);
                ObjectId alignedId = space.AppendEntity(aligned);
                transaction.AddNewlyCreatedDBObject(aligned, true);

                var rotated = new RotatedDimension(
                    0.0, new Point3d(0, 2000, 0), new Point3d(3000, 2000, 0),
                    new Point3d(0, 2500, 0), "3000mm", database.Dimstyle);
                ObjectId rotatedId = space.AppendEntity(rotated);
                transaction.AddNewlyCreatedDBObject(rotated, true);

                // Auto-measured: no text override. Must never enter statistics input.
                var measured = new AlignedDimension(
                    new Point3d(10000, 0, 0), new Point3d(12000, 0, 0),
                    new Point3d(10000, 500, 0), "", database.Dimstyle);
                ObjectId measuredId = space.AppendEntity(measured);
                transaction.AddNewlyCreatedDBObject(measured, true);

                transaction.Commit();

                var lines = new List<string>();
                using (Transaction read = database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in new[] { alignedId, rotatedId, measuredId })
                    {
                        var entity = (Entity)read.GetObject(id, OpenMode.ForRead);
                        if (!StatisticsTextReader.IsSupported(entity))
                            throw new InvalidOperationException(
                                "StatisticsTextReader rejected dimension " + entity.GetType().Name);
                        StatisticsTextReader.AppendLines(entity, true, false, true, lines);
                    }
                    read.Commit();
                }

                if (!lines.SequenceEqual(new[] { "2000mm", "3000mm" }))
                    throw new InvalidOperationException(
                        "Dimension overrides produced unexpected statistics lines: "
                        + string.Join(" | ", lines));

                // The auto-measured dimension must stay silent for statistics.
                lines.Clear();
                using (Transaction read = database.TransactionManager.StartTransaction())
                {
                    StatisticsTextReader.AppendLines(
                        (Entity)read.GetObject(measuredId, OpenMode.ForRead),
                        true, false, true, lines);
                    read.Commit();
                }
                if (lines.Count != 0)
                    throw new InvalidOperationException(
                        "Auto-measured dimension entered statistics input.");

                // Run the same cleaning/summing stage U1U/U1F use.
                SummationOutput output = SummationModule.Execute(new SummationRequest(
                    lines.ConvertAll(TextParser.CleanMText), new StatCalculationOptions()));
                if (output.CableMatchCount != 0 || output.Statistics.CableSum != 0d)
                    throw new InvalidOperationException("Auto-measured dimension was summed.");

                SummationOutput expected = SummationModule.Execute(new SummationRequest(
                    new[] { "2000mm", "3000mm" }, new StatCalculationOptions()));
                if (expected.CableMatchCount != 2
                    || Math.Abs(expected.Statistics.CableSum - 5.0) > 0.0001)
                    throw new InvalidOperationException(
                        "Dimension overrides were not summed as cable lengths.");
            }

            document.Editor.WriteMessage("\nUNCAD_DIMSTAT_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                File.WriteAllText(resultPath, "PASS");
        }
    }
}

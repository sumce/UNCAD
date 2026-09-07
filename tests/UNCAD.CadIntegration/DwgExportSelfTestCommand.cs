using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Features.DwgExport;

namespace UNCAD.CadIntegration
{
    public sealed class DwgExportSelfTestCommand
    {
        [CommandMethod("UNCAD_U1DWG_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            string tempRoot = Path.Combine(Path.GetTempPath(),
                "uncad-u1dwg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                FrameRegionGroup first;
                FrameRegionGroup second;
                CreateFixture(document.Database, out first, out second);
                var frames = new[]
                {
                    new DwgExportFrame(second, "M-TEST", "D2"),
                    new DwgExportFrame(first, "M-TEST", "D1")
                };
                IReadOnlyList<DwgFramePlacement> layout = DwgExportLayout.Arrange(frames);
                string output = Path.Combine(tempRoot, "M-TEST.dwg");

                DwgFrameExportService.WriteMachine(document.Database, output, layout,
                    "M-TEST");
                VerifyOutput(output);
                Pass(document);
            }
            catch (System.Exception ex)
            {
                string resultPath = Environment.GetEnvironmentVariable(
                    "UNCAD_CAD_INTEGRATION_RESULT");
                if (!string.IsNullOrWhiteSpace(resultPath))
                    File.WriteAllText(resultPath, "FAIL\r\n" + ex);
                throw;
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static void CreateFixture(Database database, out FrameRegionGroup first,
            out FrameRegionGroup second)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForWrite);
                TextStyleTable styles = (TextStyleTable)transaction.GetObject(
                    database.TextStyleTableId, OpenMode.ForRead);
                TextStyleTableRecord standard = (TextStyleTableRecord)transaction.GetObject(
                    styles["Standard"], OpenMode.ForWrite);
                standard.FileName = "arial.ttf";
                standard.BigFontFileName = "";
                standard.XScale = 0.85d;
                standard.TextSize = 3.5d;
                standard.ObliquingAngle = 0.1d;
                foreach (ObjectId id in space.Cast<ObjectId>().ToArray())
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
                    if (entity != null && !entity.IsErased) entity.Erase();
                }

                // Raise the source handle seed so a Wblock-based implementation cannot
                // accidentally pass by receiving the same small handles in its new database.
                for (int index = 0; index < 64; index++)
                {
                    var discarded = new Line(Point3d.Origin, new Point3d(1, 0, 0));
                    space.AppendEntity(discarded);
                    transaction.AddNewlyCreatedDBObject(discarded, true);
                    discarded.Erase();
                }

                first = AddFrameContent(transaction, space, "F1", 1000d, 1000d);
                second = AddFrameContent(transaction, space, "F2", 5000d, 2000d);
                transaction.Commit();
            }
        }

        private static FrameRegionGroup AddFrameContent(Transaction transaction,
            BlockTableRecord space, string label, double x, double y)
        {
            var line = new Line(new Point3d(x + 10d, y + 10d, 0d),
                new Point3d(x + 90d, y + 10d, 0d));
            space.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);

            var text = new DBText
            {
                TextString = label,
                Position = new Point3d(x + 20d, y + 20d, 0d),
                Height = 2.5d
            };
            space.AppendEntity(text);
            transaction.AddNewlyCreatedDBObject(text, true);

            var group = new FrameRegionGroup
            {
                Handle = label,
                Boundary = new FrameRectangle(label, x, y, x + 100d, y + 50d)
            };
            group.EntityIds.Add(line.ObjectId);
            group.EntityIds.Add(text.ObjectId);
            return group;
        }

        private static void VerifyOutput(string path)
        {
            using (var database = new Database(false, true))
            {
                database.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, "");
                database.CloseInput(true);
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                        database.CurrentSpaceId, OpenMode.ForRead);
                    TextStyleTable styles = (TextStyleTable)transaction.GetObject(
                        database.TextStyleTableId, OpenMode.ForRead);
                    TextStyleTableRecord standard = (TextStyleTableRecord)transaction.GetObject(
                        styles["Standard"], OpenMode.ForRead);
                    Require(string.Equals(standard.FileName, "arial.ttf",
                        StringComparison.OrdinalIgnoreCase),
                        "source Standard font was not preserved");
                    Require(Math.Abs(standard.XScale - 0.85d) <= 0.000001d,
                        "source Standard width factor was not preserved");
                    Require(Math.Abs(standard.TextSize - 3.5d) <= 0.000001d,
                        "source Standard text size was not preserved");
                    Require(Math.Abs(standard.ObliquingAngle - 0.1d) <= 0.000001d,
                        "source Standard oblique angle was not preserved");
                    var positions = new Dictionary<string, Point3d>(StringComparer.Ordinal);
                    foreach (ObjectId id in space)
                    {
                        DBText text = transaction.GetObject(id, OpenMode.ForRead, true) as DBText;
                        if (text != null && (text.TextString == "F1" || text.TextString == "F2"))
                            positions[text.TextString] = text.Position;
                    }
                    Require(positions.Count == 2, "fixture texts were not exported");
                    Require(Near(positions["F1"], 20d, 20d), "first frame was misplaced");
                    Require(Near(positions["F2"], 10120d, 20d), "second frame was misplaced");
                }
            }
        }

        private static bool Near(Point3d point, double x, double y)
            => Math.Abs(point.X - x) <= 0.000001d
                && Math.Abs(point.Y - y) <= 0.000001d;

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(
                "U1DWG self-test: " + message);
        }

        private static void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_U1DWG_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                File.WriteAllText(resultPath, "PASS");
        }
    }
}

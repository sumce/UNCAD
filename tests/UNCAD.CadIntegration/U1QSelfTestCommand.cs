using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
    /// <summary>Exercises U1Q pick-first filtering and generated-line re-selection.</summary>
    public sealed class U1QSelfTestCommand
    {
        [CommandMethod("UNCAD_U1Q_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            CadContext context = new CadContext(document);
            ObjectId sourceId = ObjectId.Null;
            var generatedIds = new List<ObjectId>();
            ObjectId textId = ObjectId.Null;
            try
            {
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    sourceId = context.AddToCurrentSpace(transaction,
                        new Line(new Point3d(90000000, 90000000, 0),
                            new Point3d(90001000, 90001000, 0)));
                    textId = context.AddToCurrentSpace(transaction, new DBText
                    {
                        Position = new Point3d(90000000, 90000020, 0),
                        Height = 5,
                        TextString = "not a curve"
                    });
                    transaction.Commit();
                }

                // A mixed pick-first set must retain the line and discard the text.
                document.Editor.SetImpliedSelection(new[] { textId, sourceId });
                double offset = 15;
                ObjectId[] filtered = SelectionService.PickCurvesWithOffset(context,
                    "", "", ref offset, null);
                Require(filtered != null && filtered.Length == 1
                    && filtered[0] == sourceId, "pick-first curve filtering");

                int first = ParallelCurveAnnotator.Add(context, filtered,
                    Options("first"));
                Require(first == 1, "initial U1Q generation");
                generatedIds.AddRange(ReadGenerated(document.Database, sourceId));
                Require(generatedIds.Count >= 2, "initial generated entities");
                Require(Math.Abs(ReadMTextRotation(document.Database, generatedIds)
                    - Math.PI / 4.0) <= 1e-6, "two-line MText rotation");

                // Selecting the visible generated red line must resolve to the source
                // line and replace the old output rather than silently doing nothing.
                ObjectId generatedCurve = generatedIds.First(id => IsCurve(document.Database, id));
                document.Editor.SetImpliedSelection(new[] { generatedCurve });
                offset = 15;
                ObjectId[] selectedGenerated = SelectionService.PickCurvesWithOffset(context,
                    "", "", ref offset, null);
                Require(selectedGenerated != null && selectedGenerated.Length == 1,
                    "generated-line pick");
                int second = ParallelCurveAnnotator.Add(context, selectedGenerated,
                    Options("second"));
                Require(second == 1, "generated-line source resolution");
                Require(ReadGenerated(document.Database, sourceId).Count >= 2,
                    "replacement output");

                VerifyLegacyBridgeRotation(context);

                Pass(document);
            }
            finally
            {
                document.Editor.SetImpliedSelection(new ObjectId[0]);
                Cleanup(document.Database, sourceId, textId, generatedIds);
            }
        }

        private static ParallelAnnotationOptions Options(string suffix)
            => new ParallelAnnotationOptions
            {
                CurveOffset = 15,
                TextOffset = 0,
                TextHeight = 5,
                Above = true,
                ColorIndex = 1,
                AnnotationKind = "U1Q",
                LabelFactory = _ => "梯形桥架200Wx100H\\P"
                    + (suffix == "first" ? "2500mm" : "3000mm")
            };

        private static void VerifyLegacyBridgeRotation(CadContext context)
        {
            ObjectId legacyId = ObjectId.Null;
            ObjectId migratedId = ObjectId.Null;
            double expected = Math.PI / 6.0;
            Point3d anchor = new Point3d(90002000, 90002000, 0);
            try
            {
                using (Transaction transaction = context.Db.TransactionManager
                    .StartTransaction())
                {
                    legacyId = context.AddToCurrentSpace(transaction,
                        EntityFactory.DBText(context, "桥架200*100 2500mm", anchor,
                            5, expected, AttachmentPoint.BottomCenter, 1));
                    transaction.Commit();
                }
                using (Transaction transaction = context.Db.TransactionManager
                    .StartTransaction())
                {
                    Require(BridgeLabelMigrationWriter.Migrate(transaction,
                        new[] { legacyId }, 250.0) == 1, "legacy bridge migration");
                    transaction.Commit();
                }
                using (Transaction transaction = context.Db.TransactionManager
                    .StartTransaction())
                {
                    BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                        context.Db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        MText text = transaction.GetObject(id, OpenMode.ForRead, true)
                            as MText;
                        if (text == null || text.Location.DistanceTo(anchor) > 1e-6
                            || text.Contents != "梯形桥架200Wx100H\\P2500mm") continue;
                        migratedId = id;
                        Require(Math.Abs(text.Rotation - expected) <= 1e-6,
                            "migrated MText rotation");
                        break;
                    }
                    Require(!migratedId.IsNull, "migrated MText exists");
                }
            }
            finally
            {
                Cleanup(context.Db, legacyId, ObjectId.Null,
                    migratedId.IsNull ? new ObjectId[0] : new[] { migratedId });
            }
        }

        private static double ReadMTextRotation(Database database,
            IEnumerable<ObjectId> ids)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is MText text)
                        return text.Rotation;
                }
                return double.NaN;
            }
        }

        private static List<ObjectId> ReadGenerated(Database database, ObjectId sourceId)
        {
            var result = new List<ObjectId>();
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForRead);
                string handle = sourceId.Handle.ToString();
                foreach (ObjectId id in space)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                    if (entity != null && ParallelAnnotationMetadata.TryRead(entity,
                        out string kind, out string source)
                        && kind == "U1Q" && source == handle)
                        result.Add(id);
                }
                transaction.Commit();
            }
            return result;
        }

        private static bool IsCurve(Database database, ObjectId id)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                bool result = transaction.GetObject(id, OpenMode.ForRead, true) is Curve;
                transaction.Commit();
                return result;
            }
        }

        private static void Cleanup(Database database, ObjectId sourceId, ObjectId textId,
            IEnumerable<ObjectId> generatedIds)
        {
            var ids = new HashSet<ObjectId>(generatedIds ?? Enumerable.Empty<ObjectId>());
            if (!sourceId.IsNull) ids.Add(sourceId);
            if (!textId.IsNull) ids.Add(textId);
            if (sourceId.IsNull) return;
            foreach (ObjectId id in ReadGenerated(database, sourceId)) ids.Add(id);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    try
                    {
                        Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true)
                            as Entity;
                        if (entity != null && !entity.IsErased) entity.Erase();
                    }
                    catch { }
                }
                transaction.Commit();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (condition) return;
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                System.IO.File.WriteAllText(resultPath, "FAIL: " + message);
            throw new InvalidOperationException("U1Q self-test: " + message);
        }

        private static void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_U1Q_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                System.IO.File.WriteAllText(resultPath, "PASS");
        }
    }
}

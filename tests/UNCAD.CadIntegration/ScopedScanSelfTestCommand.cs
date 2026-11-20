using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Geometry;

namespace UNCAD.CadIntegration
{
    public sealed class ScopedScanSelfTestCommand
    {
        [CommandMethod("UNCAD_SCOPED_SCAN_SELFTEST")]
        public void Run()
        {
            string result = "PASS";
            try { RunCore(); }
            catch (System.Exception ex) { result = ex.ToString(); }
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, result);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + result + "\n");
        }

        private static void RunCore()
        {
            var ctx = new CadContext(Application.DocumentManager.MdiActiveDocument);
            const double origin = 500000000;
            const int count = 3000;
            var lineIds = new List<ObjectId>();
            ObjectId joinedId, movedLabelId, staleLabelId, contestedId, outsideId, sharedLabelId;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < count; i++)
                    lineIds.Add(ctx.AddToCurrentSpace(tr, new Line(
                        new Point3d(origin + i * 5000, origin, 0),
                        new Point3d(origin + i * 5000 + 1000, origin, 0))));

                movedLabelId = ObjectId.Null;
                // Separate creation order forces geometric matching for the unlinked labels.
                for (int i = 0; i < count; i++)
                {
                    ObjectId label = ctx.AddToCurrentSpace(tr, new DBText
                    {
                        TextString = (3001 + i) + "mm", Height = 50,
                        Position = new Point3d(origin + i * 5000 + 500,
                            origin + (i == 0 ? -50000 : 100), 0)
                    });
                    if (i == 0)
                    {
                        movedLabelId = label;
                        Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, lineIds[0], label),
                            "link moved label");
                    }
                }
                ObjectId retired = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(origin, origin + 2000, 0),
                    new Point3d(origin + 2000, origin + 2000, 0)));
                staleLabelId = ctx.AddToCurrentSpace(tr, new DBText
                {
                    TextString = "700mm", Height = 50,
                    Position = new Point3d(origin + 500, origin + 2050, 0)
                });
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, retired, staleLabelId),
                    "link stale label");
                tr.GetObject(retired, OpenMode.ForWrite).Erase();
                var joined = new Polyline();
                joined.AddVertexAt(0, new Point2d(origin, origin + 2000), 0, 0, 0);
                joined.AddVertexAt(1, new Point2d(origin + 1000, origin + 2000), 0, 0, 0);
                joined.AddVertexAt(2, new Point2d(origin + 2000, origin + 2000), 0, 0, 0);
                joinedId = ctx.AddToCurrentSpace(tr, joined);

                ObjectId foreign = ctx.AddToCurrentSpace(tr, new DBText
                {
                    TextString = "999mm", Height = 50,
                    Position = new Point3d(origin + 510, origin + 2050, 0)
                });
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, lineIds[100], foreign),
                    "link live foreign source");
                var hose = new Line(new Point3d(origin, origin + 2500, 0),
                    new Point3d(origin + 2000, origin + 2500, 0));
                ctx.AddToCurrentSpace(tr, hose);
                Require(ParallelAnnotationMetadata.EnsureApplication(ctx.Db, tr), "metadata application");
                ParallelAnnotationMetadata.Set(hose, "U1B", hose.Handle.ToString());
                outsideId = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(origin + 1000, origin + 3100, 0),
                    new Point3d(origin + 2000, origin + 3100, 0)));
                contestedId = ctx.AddToCurrentSpace(tr, new Line(
                    new Point3d(origin + 1000, origin + 2900, 0),
                    new Point3d(origin + 2000, origin + 2900, 0)));
                ctx.AddToCurrentSpace(tr, new DBPoint(new Point3d(origin, origin, 0)));
                ctx.AddToCurrentSpace(tr, new DBPoint(new Point3d(origin, origin, 0)));
                sharedLabelId = ctx.AddToCurrentSpace(tr, new DBText
                {
                    TextString = "1101mm", Height = 50,
                    Position = new Point3d(origin + 1500, origin + 3050, 0)
                });
                tr.Commit();
            }

            var boundary = new FrameRectangle("selected",
                origin - 10, origin - 10, origin + 3000, origin + 3000);
            var timer = Stopwatch.StartNew();
            var full = QuickLineCadService.Scan(ctx, new QuickLineScanOptions
            {
                StaleAssociationMaximumDistance = 200
            });
            long fullMs = timer.ElapsedMilliseconds;
            timer.Restart();
            var scoped = QuickLineCadService.Scan(ctx, new QuickLineScanOptions
            {
                StaleAssociationMaximumDistance = 200,
                FrameBoundaries = new[] { boundary }
            });
            long scopedMs = timer.ElapsedMilliseconds;
            var expected = full.Where(segment =>
                boundary.Contains(segment.MidPoint.X, segment.MidPoint.Y)).ToArray();
            Require(scoped.Count == 3 && expected.Length == 3, "scope and hose exclusion");
            Require(scoped.Select(item => item.LineId).SequenceEqual(expected.Select(item => item.LineId)),
                "scope preserves order");
            for (int i = 0; i < expected.Length; i++)
            {
                Require(scoped[i].LabelId == expected[i].LabelId
                    && scoped[i].LabelMillimetres == expected[i].LabelMillimetres
                    && scoped[i].RestoredLabelIds.SequenceEqual(expected[i].RestoredLabelIds),
                    "scope preserves label ownership and values");
            }
            Require(scoped.Single(item => item.LineId == lineIds[0]).LabelId == movedLabelId,
                "explicit label outside frame");
            Require(scoped.Single(item => item.LineId == joinedId).LabelId == staleLabelId,
                "joined path recovery rejects live foreign association");
            Require(full.Single(item => item.LineId == outsideId).LabelId == sharedLabelId
                && scoped.Single(item => item.LineId == contestedId).LabelId.IsNull,
                "scope preserves unlinked label competition across frame boundary");
            CheckSpatialCandidates(lineIds);
            string report = "3000 outside routes: full=" + fullMs + "ms; scoped="
                + scopedMs + "ms; selected=" + scoped.Count + "; ownership identical.";
            string reportPath = Environment.GetEnvironmentVariable("UNCAD_SCOPED_SCAN_REPORT");
            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, report);
            ctx.Write("\n" + report + "\n");
        }

        private static void CheckSpatialCandidates(IReadOnlyList<ObjectId> ids)
        {
            Type service = typeof(QuickLineCadService);
            Type lineType = service.GetNestedType("LineSnapshot", BindingFlags.NonPublic);
            Type labelType = service.GetNestedType("LabelSnapshot", BindingFlags.NonPublic);
            Type indexType = service.GetNestedType("LabelSpatialIndex", BindingFlags.NonPublic);
            MethodInfo score = service.GetMethod("TryScore", BindingFlags.Static | BindingFlags.NonPublic);
            var labels = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(labelType));
            var lines = new List<object>();
            var random = new Random(42);
            for (int i = 0; i < 120; i++)
            {
                var start = new Point3d(random.Next(-5000, 5000), random.Next(-5000, 5000), i % 3 * 50);
                var end = start + new Vector3d(random.Next(1, 5000), random.Next(-5000, 5000), i % 2 * 800);
                lines.Add(Activator.CreateInstance(lineType, ids[i], start, end,
                    i, "", false, false, new[] { start, end }));
                Point3d middle = start + (end - start) * 0.5;
                double height = i % 9 == 0 ? 800 : 20;
                bool dimension = i % 3 == 0;
                Vector3d offset = new Vector3d(i % 5 * 100, i % 7 * 100, 0);
                labels.Add(Activator.CreateInstance(labelType, ids[i + 200], "1234mm", 1234d,
                    middle, new[] { middle + offset, middle + new Vector3d(2000, 0, 0) },
                    height, i + 200, "", "", false, 0d, dimension,
                    dimension ? (object)(start + offset) : null,
                    dimension ? (object)(end + offset) : null, dimension ? "Dimension" : "DBText"));
            }
            object index = Activator.CreateInstance(indexType, labels);
            int matches = 0;
            foreach (double distance in new[] { 0d, 10d, 500d })
            foreach (object line in lines)
            {
                var near = ((System.Collections.IEnumerable)indexType.GetMethod("Near")
                    .Invoke(index, new[] { line, distance })).Cast<object>().ToHashSet();
                foreach (object label in labels)
                {
                    object[] args = { line, label, distance, 0d };
                    if (!(bool)score.Invoke(null, args)) continue;
                    matches++;
                    Require(near.Contains(label), "spatial index must retain every valid TryScore pair");
                }
            }
            Require(matches > 100, "spatial index oracle exercises valid text and dimension pairs");
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}

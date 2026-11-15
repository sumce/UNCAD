using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
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
                ObjectId generatedText = FindMText(document.Database, generatedIds);
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    MText broken = (MText)transaction.GetObject(generatedText,
                        OpenMode.ForWrite);
                    broken.Rotation = 0;
                    transaction.Commit();
                }
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    Require(BridgeLabelMigrationWriter.Migrate(transaction,
                        new[] { generatedText }, 250.0) == 1,
                        "already-migrated rotation repair");
                    transaction.Commit();
                }
                Require(Math.Abs(ReadMTextRotation(document.Database, generatedIds)
                    - Math.PI / 4.0) <= 1e-6, "repaired MText rotation");

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

                VerifyLongAnnotationAnchor();
                VerifyLegacyBridgeRotation(context);
                VerifyBatchDiagonalLines(context);
                VerifyRunMerging(context);

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

        /// <summary>
        /// D-050（2026-11-09）连续段合并为一趟 + 偏移不交叉的真机断言：
        /// L 形三段连通（两段带 U1L 毫米标、一段无标签走几何兜底）→ 1 趟、
        /// 一条连续偏移折线 + 一个标注（长度=U1L 标签合计 6000mm）；偏移折线与
        /// 基线**无任何交点**且全程同一侧（用户报告"红线与基线交叉"的直接
        /// 回归证明）；两个不连通组→2 趟；点中趟内任一成员重跑→整趟替换。
        /// </summary>
        private static void VerifyRunMerging(CadContext context)
        {
            double bx = 90005000, by = 90000000;
            var lines = new List<ObjectId>();
            var labels = new List<ObjectId>();
            try
            {
                using (Transaction transaction = context.Db.TransactionManager
                    .StartTransaction())
                {
                    lines.Add(context.AddToCurrentSpace(transaction, new Line(
                        new Point3d(bx, by, 0), new Point3d(bx + 1000, by, 0))));
                    lines.Add(context.AddToCurrentSpace(transaction, new Line(
                        new Point3d(bx + 1000, by, 0),
                        new Point3d(bx + 1000, by + 1000, 0))));
                    lines.Add(context.AddToCurrentSpace(transaction, new Line(
                        new Point3d(bx + 1000, by + 1000, 0),
                        new Point3d(bx + 2000, by + 1000, 0))));
                    // U1L 毫米标：L1=2000、L2=3000；L3 无标签（几何兜底 1000）。
                    QuickLineMetadataStore.EnsureApplication(context.Db, transaction);
                    labels.Add(AddU1LLabel(context, transaction,
                        new Point3d(bx + 500, by + 20, 0), "2000mm", lines[0]));
                    labels.Add(AddU1LLabel(context, transaction,
                        new Point3d(bx + 1020, by + 500, 0), "3000mm", lines[1]));
                    transaction.Commit();
                }

                // ① 连续三段 → 1 趟 + 一个标注；长度 = 2000+3000+1000 = 6000mm。
                int runs = ParallelCurveAnnotator.AddRuns(context, lines.ToArray(),
                    RunOptions("U1Q", 1, true, 15), run =>
                        "梯形桥架200Wx100H\\P" + (long)run.EstimatedLengthMm + "mm");
                Require(runs == 1, "continuous chain should merge into one run");
                List<ObjectId> generated = ReadRunGenerated(context.Db, lines);
                Require(generated.Count == 2,
                    "one run = one offset polyline + one label (got "
                    + generated.Count + ")");
                Polyline offsetPolyline = FindPolyline(context.Db, generated);
                MText runLabel = FindMTextEntity(context.Db, generated);
                Require(offsetPolyline != null && runLabel != null,
                    "run output must contain a polyline and an MText");
                Require(offsetPolyline.NumberOfVertices == 4,
                    "continuous offset should keep all corner vertices (got "
                    + offsetPolyline.NumberOfVertices + ")");
                Require(runLabel.Contents.Contains("6000mm"),
                    "run label length must sum U1L labels + geometry (got "
                    + runLabel.Contents + ")");

                // ② 不交叉（用户报告的缺陷的直接回归证明）+ 全程同一侧。
                Require(!IntersectsSource(context.Db, offsetPolyline, lines),
                    "offset polyline must never cross the baseline chain");
                Require(StaysOnOneSide(context.Db, offsetPolyline, lines, 15,
                    out bool aboveSide) && aboveSide,
                    "offset polyline must stay on one world side (above)");

                // ③ 不连通两组 → 2 趟。
                var splitLines = new List<ObjectId>();
                try
                {
                    using (Transaction transaction = context.Db.TransactionManager
                        .StartTransaction())
                    {
                        splitLines.Add(context.AddToCurrentSpace(transaction, new Line(
                            new Point3d(bx + 5000, by, 0),
                            new Point3d(bx + 5800, by, 0))));
                        splitLines.Add(context.AddToCurrentSpace(transaction, new Line(
                            new Point3d(bx + 7000, by, 0),
                            new Point3d(bx + 7800, by, 0))));
                        transaction.Commit();
                    }
                    int splitRuns = ParallelCurveAnnotator.AddRuns(context,
                        splitLines.ToArray(), RunOptions("U1Q", 1, true, 15),
                        run => "梯形桥架200Wx100H\\P" + (long)run.EstimatedLengthMm
                            + "mm");
                    Require(splitRuns == 2, "two disconnected groups = two runs");
                    Require(ReadRunGenerated(context.Db, splitLines).Count == 4,
                        "two runs = 2 polylines + 2 labels");
                }
                finally { CleanupIds(context.Db, splitLines.Concat(
                    ReadRunGenerated(context.Db, splitLines))); }

                // ④ 点中趟内任一成员（L2）重跑 → 整趟替换，无旧输出残留。
                int rerun = ParallelCurveAnnotator.AddRuns(context,
                    new[] { lines[1] }, RunOptions("U1Q", 1, true, 15),
                    run => "梯形桥架200Wx100H\\P" + (long)run.EstimatedLengthMm + "mm");
                Require(rerun == 1, "re-run from one member rebuilds the whole run");
                Require(ReadRunGenerated(context.Db, lines).Count == 2,
                    "whole-run replacement must not leave stale outputs");

                // ⑤ U1C 显式偏移几何（D-050 Amendment 后生产默认已是桥架车道
                // 上侧 15；此处仍以显式 下侧/210 证明偏移与侧向完全可配置）。
                int conduitRuns = ParallelCurveAnnotator.AddRuns(context,
                    lines.ToArray(), RunOptions("U1C", 6, false, 210),
                    run => "镀锌穿线管EMT PIPE 20mm(3/4\")\\P2000mm");
                Require(conduitRuns == 1, "U1C run generation");
                List<ObjectId> conduitGenerated = ReadRunGenerated(context.Db,
                    lines, "U1C");
                MText conduitLabel = FindMTextEntity(context.Db, conduitGenerated);
                Require(conduitLabel != null, "U1C run must produce a label");
                double laneDistance = MinDistanceToLines(context.Db,
                    conduitLabel.Location, lines);
                Require(laneDistance >= 150,
                    "U1C explicit lane must be honored (>=150 from baseline, got "
                    + laneDistance.ToString("0.#") + ")");
                double toU1L1 = conduitLabel.Location.DistanceTo(
                    new Point3d(bx + 500, by + 20, 0));
                double toU1L2 = conduitLabel.Location.DistanceTo(
                    new Point3d(bx + 1020, by + 500, 0));
                Require(toU1L1 >= 100 && toU1L2 >= 100,
                    "U1C explicit lane must clear the U1L millimetre band (distances "
                    + toU1L1.ToString("0.#") + "/" + toU1L2.ToString("0.#") + ")");
            }
            finally
            {
                var all = new List<ObjectId>(lines);
                all.AddRange(labels);
                all.AddRange(ReadRunGenerated(context.Db, lines));
                all.AddRange(ReadRunGenerated(context.Db, lines, "U1C"));
                CleanupIds(context.Db, all);
            }
        }

        private static ParallelAnnotationOptions RunOptions(string kind, short color,
            bool above, double curveOffset)
            => new ParallelAnnotationOptions
            {
                CurveOffset = curveOffset,
                TextOffset = 0,
                TextHeight = 5,
                Above = above,
                ColorIndex = color,
                AnnotationKind = kind,
                LegacyLabelKey = UNCAD.Core.Fill.AnnotationLabelPair.CollapseText
            };

        private static ObjectId AddU1LLabel(CadContext context, Transaction transaction,
            Point3d position, string text, ObjectId line)
        {
            ObjectId id = context.AddToCurrentSpace(transaction, new DBText
            {
                Position = position,
                Height = 5,
                TextString = text,
                Rotation = 0
            });
            DBObject label = transaction.GetObject(id, OpenMode.ForWrite);
            label.XData = QuickLineMetadataStore.Build(label,
                line.Handle.ToString(), true);
            return id;
        }

        private static List<ObjectId> ReadRunGenerated(Database database,
            List<ObjectId> sources, string kind = "U1Q")
        {
            var handles = new HashSet<string>(
                sources.Select(id => id.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
            var result = new List<ObjectId>();
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    if (!(transaction.GetObject(id, OpenMode.ForRead, true) is Entity
                        entity)) continue;
                    if (!ParallelAnnotationMetadata.TryRead(entity,
                        out string entityKind, out string source)) continue;
                    if (!string.Equals(entityKind, kind,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    if (!handles.Contains(source)
                        && !ParallelAnnotationMetadata.TryReadRunHandles(entity)
                            .Any(handles.Contains)) continue;
                    result.Add(id);
                }
                transaction.Commit();
            }
            return result;
        }

        private static Polyline FindPolyline(Database database, List<ObjectId> ids)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                foreach (ObjectId id in ids)
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is Polyline p)
                        return p;
                return null;
            }
        }

        /// <summary>已有 FindMText 返回 ObjectId；这里要拿实体本体断言内容/位置。</summary>
        private static MText FindMTextEntity(Database database, List<ObjectId> ids)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                foreach (ObjectId id in ids)
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is MText text)
                        return text;
                return null;
            }
        }

        /// <summary>偏移折线的任意段与基线链任意段的 2D 相交检测（含端点接触）。</summary>
        private static bool IntersectsSource(Database database, Polyline offset,
            List<ObjectId> sources)
        {
            var offsetSegments = ReadPolylineSegments(offset);
            var sourceSegments = new List<Tuple<Point2d, Point2d>>();
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                foreach (ObjectId id in sources)
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is Line line)
                        sourceSegments.Add(Tuple.Create(
                            new Point2d(line.StartPoint.X, line.StartPoint.Y),
                            new Point2d(line.EndPoint.X, line.EndPoint.Y)));
                transaction.Commit();
            }
            foreach (Tuple<Point2d, Point2d> a in offsetSegments)
                foreach (Tuple<Point2d, Point2d> b in sourceSegments)
                    if (SegmentsIntersect(a.Item1, a.Item2, b.Item1, b.Item2))
                        return true;
            return false;
        }

        private static List<Tuple<Point2d, Point2d>> ReadPolylineSegments(Polyline p)
        {
            var result = new List<Tuple<Point2d, Point2d>>();
            for (int i = 0; i < p.NumberOfVertices - 1; i++)
                result.Add(Tuple.Create(p.GetPoint2dAt(i), p.GetPoint2dAt(i + 1)));
            return result;
        }

        /// <summary>偏移折线全程同一世界侧：每段中点到最近基线段的带符号距离。</summary>
        private static bool StaysOnOneSide(Database database, Polyline offset,
            List<ObjectId> sources, double offsetDistance, out bool aboveSide)
        {
            aboveSide = false;
            int? side = null;
            var sourceSegments = new List<Tuple<Point2d, Point2d>>();
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                foreach (ObjectId id in sources)
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is Line line)
                        sourceSegments.Add(Tuple.Create(
                            new Point2d(line.StartPoint.X, line.StartPoint.Y),
                            new Point2d(line.EndPoint.X, line.EndPoint.Y)));
                transaction.Commit();
            }
            foreach (Tuple<Point2d, Point2d> segment in ReadPolylineSegments(offset))
            {
                var middle = new Point2d((segment.Item1.X + segment.Item2.X) / 2,
                    (segment.Item2.Y + segment.Item1.Y) / 2);
                double best = double.MaxValue;
                int bestIndex = -1;
                for (int i = 0; i < sourceSegments.Count; i++)
                {
                    double d = DistanceToSegment(middle, sourceSegments[i]);
                    if (d < best)
                    {
                        best = d;
                        bestIndex = i;
                    }
                }
                if (bestIndex < 0 || best < offsetDistance * 0.8) return false;
                Tuple<Point2d, Point2d> source = sourceSegments[bestIndex];
                double cross = (source.Item2.X - source.Item1.X)
                    * (middle.Y - source.Item1.Y)
                    - (source.Item2.Y - source.Item1.Y)
                    * (middle.X - source.Item1.X);
                int current = cross >= 0 ? 1 : -1;
                if (side == null) side = current;
                else if (side != current) return false;
            }
            aboveSide = side == 1;
            return side != null;
        }

        private static double MinDistanceToLines(Database database, Point3d point,
            List<ObjectId> sources)
        {
            double best = double.MaxValue;
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                foreach (ObjectId id in sources)
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is Line line)
                        best = Math.Min(best, DistanceToSegment(
                            new Point2d(point.X, point.Y),
                            Tuple.Create(
                                new Point2d(line.StartPoint.X, line.StartPoint.Y),
                                new Point2d(line.EndPoint.X, line.EndPoint.Y))));
                transaction.Commit();
            }
            return best;
        }

        private static double DistanceToSegment(Point2d point,
            Tuple<Point2d, Point2d> segment)
        {
            double dx = segment.Item2.X - segment.Item1.X;
            double dy = segment.Item2.Y - segment.Item1.Y;
            double t = ((point.X - segment.Item1.X) * dx
                        + (point.Y - segment.Item1.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            double px = segment.Item1.X + dx * t, py = segment.Item1.Y + dy * t;
            return Math.Sqrt((point.X - px) * (point.X - px)
                + (point.Y - py) * (point.Y - py));
        }

        private static bool SegmentsIntersect(Point2d a1, Point2d a2, Point2d b1,
            Point2d b2)
        {
            double d1 = Cross(b1, b2, a1), d2 = Cross(b1, b2, a2);
            double d3 = Cross(a1, a2, b1), d4 = Cross(a1, a2, b2);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            const double eps = 1e-6;
            return Math.Abs(d1) < eps || Math.Abs(d2) < eps
                || Math.Abs(d3) < eps || Math.Abs(d4) < eps;
        }

        private static double Cross(Point2d o, Point2d a, Point2d b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

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

        private static void VerifyLongAnnotationAnchor()
        {
            using (var text = new MText
            {
                Location = new Point3d(90003000, 90003000, 0),
                Contents = "镀锌穿线管EMT PIPE 20mm(3/4\")\\P2000mm",
                TextHeight = 5
            })
            {
                Require(FrameRegionCollector.TryAnnotationAnchor(text,
                    out Point3d anchor) && anchor == text.Location,
                    "long annotation uses authored frame anchor");
            }
        }

        private static void VerifyBatchDiagonalLines(CadContext context)
        {
            var sourceIds = new List<ObjectId>();
            var cleanupIds = new List<ObjectId>();
            try
            {
                using (Transaction transaction = context.Db.TransactionManager
                    .StartTransaction())
                {
                    for (int i = 0; i < 72; i++)
                    {
                        double angle = (i + 1) * Math.PI / 74.0;
                        Point3d start = new Point3d(90100000 + i * 2000,
                            90100000, 0);
                        Point3d end = start + new Vector3d(Math.Cos(angle) * 1000,
                            Math.Sin(angle) * 1000, 0);
                        ObjectId id = context.AddToCurrentSpace(transaction,
                            new Line(i % 2 == 0 ? start : end,
                                i % 2 == 0 ? end : start));
                        sourceIds.Add(id);
                        cleanupIds.Add(id);
                    }
                    transaction.Commit();
                }

                int count = ParallelCurveAnnotator.Add(context, sourceIds.ToArray(),
                    Options("batch"));
                var missing = new List<int>();
                for (int i = 0; i < sourceIds.Count; i++)
                {
                    List<ObjectId> generated = ReadGenerated(context.Db, sourceIds[i]);
                    cleanupIds.AddRange(generated);
                    if (generated.Count == 0) missing.Add(i + 1);
                }
                Require(count == sourceIds.Count, "batch diagonal U1Q generation: "
                    + count + "/" + sourceIds.Count + "; missing="
                    + string.Join(",", missing));
            }
            finally
            {
                CleanupIds(context.Db, cleanupIds);
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

        private static ObjectId FindMText(Database database, IEnumerable<ObjectId> ids)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                    if (transaction.GetObject(id, OpenMode.ForRead, true) is MText)
                        return id;
                return ObjectId.Null;
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

        private static void CleanupIds(Database database, IEnumerable<ObjectId> ids)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids ?? Enumerable.Empty<ObjectId>())
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

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace UNCAD.Cad
{
    public sealed class ParallelAnnotationOptions
    {
        public double CurveOffset { get; set; }
        public double TextOffset { get; set; }
        public double TextHeight { get; set; }
        public bool Above { get; set; }
        public short ColorIndex { get; set; }
        public Func<double, string> LabelFactory { get; set; }
    }

    /// <summary>用 AutoCAD 原生偏移曲线生成桥架/线管平行标注。</summary>
    public static class ParallelCurveAnnotator
    {
        public static int Add(CadContext ctx, ObjectId[] ids, ParallelAnnotationOptions options)
        {
            if (ids == null || options == null || options.LabelFactory == null) return 0;
            int count = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, tr);
                foreach (ObjectId id in ids)
                {
                    var source = tr.GetObject(id, OpenMode.ForRead, true) as Curve;
                    if (!IsSupported(source)) continue;

                    double length = GetLength(source);
                    if (length <= 0) continue;
                    Point3d sourceMid = source.GetPointAtDist(length / 2.0);
                    Vector3d tangent = source.GetFirstDerivative(sourceMid);
                    if (tangent.Length < 1e-9) continue;
                    double sourceAngle = GeoMath.ReadableAngle(sourceMid, sourceMid + tangent);
                    double sideAngle = GeoMath.SideDirection(sourceAngle, options.Above);
                    var desired = new Vector3d(Math.Cos(sideAngle), Math.Sin(sideAngle), 0);

                    OffsetCandidate positive = CreateCandidate(source, options.CurveOffset, sourceMid, desired);
                    OffsetCandidate negative = CreateCandidate(source, -options.CurveOffset, sourceMid, desired);
                    OffsetCandidate selected = SelectCandidate(positive, negative);
                    OffsetCandidate rejected = ReferenceEquals(selected, positive) ? negative : positive;
                    rejected?.Dispose();
                    if (selected == null) continue;

                    try
                    {
                        foreach (Entity entity in selected.Entities)
                        {
                            entity.LayerId = ctx.CurrentLayerId;
                            if (options.ColorIndex != 0) entity.ColorIndex = options.ColorIndex;
                            ctx.AddToCurrentSpace(tr, entity);
                        }

                        Curve labelCurve = selected.LabelCurve;
                        Point3d curvePoint = labelCurve.GetClosestPointTo(sourceMid, false);
                        Vector3d labelTangent = labelCurve.GetFirstDerivative(curvePoint);
                        double textAngle = labelTangent.Length < 1e-9
                            ? sourceAngle
                            : GeoMath.ReadableAngle(curvePoint, curvePoint + labelTangent);
                        Point3d textPoint = GeoMath.Polar(curvePoint,
                            GeoMath.SideDirection(textAngle, options.Above), options.TextOffset);
                        var text = EntityFactory.DBText(ctx, options.LabelFactory(length), textPoint,
                            options.TextHeight, textAngle,
                            options.Above ? AttachmentPoint.BottomCenter : AttachmentPoint.TopCenter,
                            options.ColorIndex, styleId);
                        ctx.AddToCurrentSpace(tr, text);
                        selected.Detach();
                        count++;
                    }
                    finally
                    {
                        selected.Dispose();
                    }
                }
                tr.Commit();
            }
            return count;
        }

        private static bool IsSupported(Curve curve)
        {
            return curve is Line || curve is Polyline || curve is Polyline2d;
        }

        private static double GetLength(Curve curve)
        {
            try
            {
                return Math.Abs(curve.GetDistanceAtParameter(curve.EndParam)
                    - curve.GetDistanceAtParameter(curve.StartParam));
            }
            catch
            {
                return 0;
            }
        }

        private static OffsetCandidate CreateCandidate(
            Curve source, double distance, Point3d sourceMid, Vector3d desired)
        {
            DBObjectCollection objects = null;
            try
            {
                objects = source.GetOffsetCurves(distance);
                var entities = new List<Entity>();
                Curve labelCurve = null;
                double score = double.NegativeInfinity;
                foreach (DBObject obj in objects)
                {
                    if (!(obj is Entity entity) || !(obj is Curve curve))
                    {
                        obj.Dispose();
                        continue;
                    }
                    entities.Add(entity);
                    Point3d nearest = curve.GetClosestPointTo(sourceMid, false);
                    double candidateScore = sourceMid.GetVectorTo(nearest).DotProduct(desired);
                    if (candidateScore > score)
                    {
                        score = candidateScore;
                        labelCurve = curve;
                    }
                }
                return entities.Count == 0 || labelCurve == null
                    ? null
                    : new OffsetCandidate(entities, labelCurve, score);
            }
            catch
            {
                if (objects != null)
                    foreach (DBObject obj in objects) obj.Dispose();
                return null;
            }
        }

        private static OffsetCandidate SelectCandidate(OffsetCandidate first, OffsetCandidate second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return first.Score >= second.Score ? first : second;
        }

        private sealed class OffsetCandidate : IDisposable
        {
            private bool _attached;

            public OffsetCandidate(List<Entity> entities, Curve labelCurve, double score)
            {
                Entities = entities;
                LabelCurve = labelCurve;
                Score = score;
            }

            public List<Entity> Entities { get; }
            public Curve LabelCurve { get; }
            public double Score { get; }

            public void Detach() => _attached = true;

            public void Dispose()
            {
                if (_attached) return;
                foreach (Entity entity in Entities) entity.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Geometry;

namespace UNCAD.Cad.QuickLine
{
    /// <summary>One planned semicircle opening on a straight run.</summary>
    public sealed class ArchBridgeOpening
    {
        internal ArchBridgeOpening(Point3d first, Point3d second, double bulge,
            double firstFraction, double secondFraction)
        {
            First = first;
            Second = second;
            Bulge = bulge;
            FirstFraction = firstFraction;
            SecondFraction = secondFraction;
        }

        public Point3d First { get; }
        public Point3d Second { get; }
        public double Bulge { get; }
        public double FirstFraction { get; }
        public double SecondFraction { get; }
    }

    /// <summary>
    /// Shared U1R/Xauto semicircle-hop geometry.  Plans hop openings on a
    /// straight XY run and builds the merged open Polyline that replaces the
    /// source Line; the bulge always faces "up" exactly like the manual U1R
    /// clicks (<see cref="SemicircleGeometry.Bulge"/>).
    /// </summary>
    public static class ArchBridgePolyline
    {
        private const double Tolerance = 1e-7;

        /// <summary>
        /// Plans one opening at <paramref name="centerDistance"/> from the
        /// start point.  This is the single-hop core of U1R's planning step.
        /// </summary>
        public static bool TryPlanOpening(Point3d start, Point3d end,
            double centerDistance, double diameter, out ArchBridgeOpening opening,
            out string warning)
        {
            opening = null;
            warning = string.Empty;
            if (diameter <= Tolerance || double.IsNaN(diameter)
                || double.IsInfinity(diameter))
            {
                warning = "拱桥直径无效。";
                return false;
            }
            if (Math.Abs(start.Z - end.Z) > Tolerance)
            {
                warning = "仅支持位于同一 XY 平面的二维直线。";
                return false;
            }
            Vector3d path = end - start;
            double totalLength = path.Length;
            if (totalLength <= diameter + Tolerance)
            {
                warning = "直线段长度必须大于洞口直径，已跳过。";
                return false;
            }
            if (!SemicircleGeometry.CanSplit(totalLength, diameter,
                    centerDistance, Tolerance))
            {
                warning = "位置太靠近直线段端点，已跳过。";
                return false;
            }

            double radius = diameter / 2.0;
            double fraction = centerDistance / totalLength;
            Point3d center = start + path * fraction;
            double firstFraction = (centerDistance - radius) / totalLength;
            double secondFraction = (centerDistance + radius) / totalLength;
            Point3d first = start + path * firstFraction;
            Point3d second = start + path * secondFraction;
            double bulge = SemicircleGeometry.Bulge(center.X, center.Y,
                first.X, first.Y, second.X, second.Y, radius);
            opening = new ArchBridgeOpening(first, second, bulge,
                firstFraction, secondFraction);
            return true;
        }

        /// <summary>
        /// Plans several openings on one straight run.  Centers are travelled
        /// from the start; each must clear the endpoints by a radius and the
        /// previous hop by a full diameter, so the merged result matches what
        /// sequential manual U1R clicks would produce.
        /// </summary>
        public static bool TryPlanVertices(Point3d start, Point3d end,
            IReadOnlyList<double> centerDistances, double diameter,
            out List<KeyValuePair<Point2d, double>> vertices, out string warning)
        {
            vertices = null;
            warning = string.Empty;
            if (centerDistances == null || centerDistances.Count == 0)
            {
                warning = "没有可开洞的位置。";
                return false;
            }

            var ordered = new List<double>(centerDistances);
            ordered.Sort();
            vertices = new List<KeyValuePair<Point2d, double>>
            {
                new KeyValuePair<Point2d, double>(
                    new Point2d(start.X, start.Y), 0.0)
            };
            double radius = diameter / 2.0;
            double lastCenter = double.NaN;
            foreach (double centerDistance in ordered)
            {
                if (!TryPlanOpening(start, end, centerDistance, diameter,
                        out ArchBridgeOpening opening, out warning)) return false;
                if (!double.IsNaN(lastCenter)
                    && centerDistance - lastCenter < diameter - Tolerance)
                {
                    warning = "相邻洞位距离小于洞口直径，已跳过。";
                    return false;
                }
                vertices.Add(new KeyValuePair<Point2d, double>(
                    new Point2d(opening.First.X, opening.First.Y),
                    opening.Bulge));
                vertices.Add(new KeyValuePair<Point2d, double>(
                    new Point2d(opening.Second.X, opening.Second.Y), 0.0));
                lastCenter = centerDistance;
            }
            vertices.Add(new KeyValuePair<Point2d, double>(
                new Point2d(end.X, end.Y), 0.0));
            return true;
        }

        /// <summary>
        /// Builds the open XY Polyline that replaces <paramref name="line"/>,
        /// inheriting its layer/colour/linetype, elevation and thickness.
        /// The caller appends it to the owner space and erases the source
        /// line inside the same transaction.
        /// </summary>
        public static Polyline BuildMerged(Database database, Line line,
            IReadOnlyList<KeyValuePair<Point2d, double>> vertices)
        {
            if (database == null) throw new ArgumentNullException("database");
            if (line == null) throw new ArgumentNullException("line");
            if (vertices == null || vertices.Count < 2)
                throw new ArgumentException("At least two vertices are required.",
                    "vertices");

            var merged = new Polyline(vertices.Count);
            merged.SetDatabaseDefaults(database);
            merged.SetPropertiesFrom(line);
            merged.Normal = Vector3d.ZAxis;
            merged.Elevation = line.StartPoint.Z;
            merged.Thickness = line.Thickness;
            for (int index = 0; index < vertices.Count; index++)
            {
                KeyValuePair<Point2d, double> vertex = vertices[index];
                merged.AddVertexAt(index, vertex.Key, vertex.Value, 0.0, 0.0);
            }
            return merged;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Cad.QuickLine;

namespace UNCAD.Cad
{
    /// <summary>
    /// D-050（2026-11-09）连续趟几何装配（Cad 侧）：
    /// ① 源曲线 → 有序顶点链（弧段按距离自适应采样后共线简化，直线只剩拐点）；
    /// ② U1L 毫米标 → 成员线句柄映射（UNCAD_U1LX 元数据关联）；
    /// ③ 趟长度推算——成员线有 U1L 标签用标签值，没有用几何长度兜底，
    ///    合计四舍五入到 10mm（统计契约 TextParser 要求桥架长度结尾为 0），
    ///    用户生成后仍可手改（桥架通常比电缆略长，推算只是初值）。
    /// </summary>
    internal static class ParallelRunGeometry
    {
        /// <summary>长度推算的取整步长（统计解析要求 `数字0mm`）。</summary>
        internal const double LengthRoundingMm = 10.0;

        /// <summary>
        /// 提取源曲线的顶点链与几何长度。Line 只取两端点；多段线/二维多段线
        /// 按距离采样（步长 25~400 自适应）再按转角 0.2° 做共线简化——直线段
        /// 只剩拐点，弧段保留细分点。失败或退化返回 false。
        /// </summary>
        internal static bool TryExtractChain(Transaction transaction, ObjectId curveId,
            out List<QuickLinePoint> vertices, out double lengthMm)
        {
            vertices = null;
            lengthMm = 0;
            Curve curve;
            try
            {
                curve = transaction.GetObject(curveId, OpenMode.ForRead, true) as Curve;
            }
            catch { return false; }
            if (curve == null) return false;
            try
            {
                if (curve is Line line)
                {
                    lengthMm = line.Length;
                    if (lengthMm <= 0) return false;
                    vertices = new List<QuickLinePoint>
                    {
                        ToPlanar(line.StartPoint), ToPlanar(line.EndPoint)
                    };
                    return true;
                }
                lengthMm = Math.Abs(curve.GetDistanceAtParameter(curve.EndParam)
                    - curve.GetDistanceAtParameter(curve.StartParam));
                if (lengthMm <= 0) return false;
                double step = Math.Max(25.0, Math.Min(400.0, lengthMm / 64.0));
                var sampled = new List<QuickLinePoint>();
                for (double d = 0; d < lengthMm; d += step)
                    sampled.Add(ToPlanar(curve.GetPointAtDist(d)));
                sampled.Add(ToPlanar(curve.GetPointAtDist(lengthMm)));
                vertices = SimplifyCollinear(sampled, 0.2 * Math.PI / 180.0);
                return vertices.Count >= 2;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>当前空间里 U1L 毫米标 → 成员线句柄 的映射（一趟只扫一次）。</summary>
        internal static Dictionary<string, double> CollectU1LMillimetresByLineHandle(
            CadContext ctx, Transaction transaction)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            BlockTableRecord space;
            try
            {
                space = transaction.GetObject(ctx.Db.CurrentSpaceId, OpenMode.ForRead,
                    false) as BlockTableRecord;
            }
            catch { return result; }
            if (space == null) return result;
            foreach (ObjectId id in space)
            {
                // 3.2.0.6（审计修复）：口径对齐扫描链路（DBText+MText）——此前只认
                // 单行文字，MText 形态的 U1L 毫米标即使带句柄关联也不参与趟长度
                // 推算，静默退回几何长度。
                Entity entity;
                try
                {
                    entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                }
                catch { continue; }
                if (!(entity is DBText || entity is MText)) continue;
                string contents;
                try
                {
                    contents = entity is MText mText
                        ? UNCAD.Core.Text.TextParser.CleanMText(mText.Contents ?? "")
                        : ((DBText)entity).TextString ?? "";
                }
                catch { continue; }
                string lineHandle;
                try
                {
                    lineHandle = QuickLineMetadataStore.ReadAssociatedLineHandle(entity);
                }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(lineHandle)
                    || result.ContainsKey(lineHandle))
                    continue;
                if (Core.QuickLine.QuickLineMillimeterText.TryParseCad(contents,
                        out double millimetres) && millimetres > 0)
                    result[lineHandle] = millimetres;
            }
            return result;
        }

        /// <summary>趟长度推算：U1L 标签优先、成员几何长度兜底，合计取整到 10。</summary>
        internal static double EstimateRunLengthMm(ParallelRun run,
            Dictionary<string, double> u1LMillimetres)
        {
            if (run == null) return LengthRoundingMm;
            double total = 0;
            foreach (ParallelRunMember member in run.Members)
            {
                if (u1LMillimetres != null
                    && u1LMillimetres.TryGetValue(member.Handle, out double labelled)
                    && labelled > 0)
                    total += labelled;
                else
                    total += member.LengthMm;
            }
            double rounded = Math.Round(total / LengthRoundingMm) * LengthRoundingMm;
            return Math.Max(LengthRoundingMm, rounded);
        }

        /// <summary>沿顶点链走指定距离，返回该点与所在段的方向（单位向量）。</summary>
        internal static void PointAndDirectionAtDistance(
            IReadOnlyList<QuickLinePoint> vertices, double distance,
            out QuickLinePoint point, out QuickLinePoint direction)
        {
            if (vertices == null || vertices.Count < 2)
            {
                point = new QuickLinePoint(0, 0);
                direction = new QuickLinePoint(1, 0);
                return;
            }
            double remaining = Math.Max(0, distance);
            for (int i = 0; i < vertices.Count - 1; i++)
            {
                double segmentLength = vertices[i].DistanceTo(vertices[i + 1]);
                if (segmentLength < 1e-12) continue;
                if (i == vertices.Count - 2 || remaining <= segmentLength)
                {
                    double t = Math.Max(0, Math.Min(1, remaining / segmentLength));
                    point = new QuickLinePoint(
                        vertices[i].X + (vertices[i + 1].X - vertices[i].X) * t,
                        vertices[i].Y + (vertices[i + 1].Y - vertices[i].Y) * t);
                    direction = new QuickLinePoint(
                        (vertices[i + 1].X - vertices[i].X) / segmentLength,
                        (vertices[i + 1].Y - vertices[i].Y) / segmentLength);
                    return;
                }
                remaining -= segmentLength;
            }
            point = vertices[vertices.Count - 1];
            QuickLinePoint last = vertices[vertices.Count - 2];
            QuickLinePoint end = vertices[vertices.Count - 1];
            double length = last.DistanceTo(end);
            direction = length < 1e-12
                ? new QuickLinePoint(1, 0)
                : new QuickLinePoint((end.X - last.X) / length, (end.Y - last.Y) / length);
        }

        /// <summary>顶点链总长（按段求和）。</summary>
        internal static double ChainLengthMm(IReadOnlyList<QuickLinePoint> vertices)
        {
            double total = 0;
            for (int i = 0; vertices != null && i < vertices.Count - 1; i++)
                total += vertices[i].DistanceTo(vertices[i + 1]);
            return total;
        }

        private static List<QuickLinePoint> SimplifyCollinear(
            List<QuickLinePoint> points, double turnToleranceRadians)
        {
            var result = new List<QuickLinePoint> { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                QuickLinePoint lastKept = result[result.Count - 1];
                QuickLinePoint next = points[i + 1];
                double incomingX = points[i].X - lastKept.X;
                double incomingY = points[i].Y - lastKept.Y;
                double outgoingX = next.X - points[i].X;
                double outgoingY = next.Y - points[i].Y;
                double cross = incomingX * outgoingY - incomingY * outgoingX;
                double incoming = Math.Sqrt(incomingX * incomingX + incomingY * incomingY);
                double outgoing = Math.Sqrt(outgoingX * outgoingX + outgoingY * outgoingY);
                if (incoming < 1e-12 || outgoing < 1e-12) continue;
                double turn = Math.Abs(Math.Asin(Math.Max(-1,
                    Math.Min(1, cross / (incoming * outgoing)))));
                if (turn >= turnToleranceRadians) result.Add(points[i]);
            }
            result.Add(points[points.Count - 1]);
            return result;
        }

        private static QuickLinePoint ToPlanar(Point3d point)
            => new QuickLinePoint(point.X, point.Y);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UNCAD.Core.QuickLine
{
    /// <summary>A drawn route segment unfolded onto the plan (XY) plane.</summary>
    public sealed class QuickLinePlanSegment
    {
        public QuickLinePlanSegment(string id, double startX, double startY,
            double endX, double endY, double distanceMillimetres)
        {
            Id = id;
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            DistanceMillimetres = distanceMillimetres;
        }

        public string Id { get; }
        public double StartX { get; }
        public double StartY { get; }
        public double EndX { get; }
        public double EndY { get; }
        public double DistanceMillimetres { get; }
    }

    /// <summary>
    /// 把 3D 东南等轴测绘制结果展开为平面图:等轴测 X 轴 → 平面 +X,
    /// Y 轴 → 平面 +Y,Z 轴(垂直段)不产生平面线。长度 1:1 保留键入毫米值。
    /// </summary>
    public static class QuickLinePlanUnfold
    {
        /// <summary>环路回到已访问节点时允许的坐标偏差(绘图单位)。</summary>
        public const double LoopTolerance = 0.5;

        public static IReadOnlyList<QuickLinePlanSegment> Build(
            IReadOnlyList<QuickLineCreatedSegment> createdSegments)
        {
            var result = new List<QuickLinePlanSegment>();
            if (createdSegments == null) return result;

            // 从根节点 N1 开始沿段推导各端点平面坐标;协议保证全连通。
            var positions = new Dictionary<string, double[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["N1"] = new[] { 0.0, 0.0 }
            };

            foreach (QuickLineCreatedSegment segment in createdSegments)
            {
                if (segment == null
                    || !positions.TryGetValue(segment.StartNodeId ?? "",
                        out double[] start))
                {
                    continue;
                }
                double distance = segment.DistanceMillimetres;
                if (!IsFinite(distance) || distance <= 0.0) continue;

                double[] delta = PlanDelta(segment.Axis, segment.DirectionSign, distance);
                double[] end =
                {
                    start[0] + delta[0],
                    start[1] + delta[1]
                };

                // 环路闭合:端点已有坐标且与推导值明显不符时,保留已访问坐标、跳过该段,
                // 不让整条路线报废。
                if (positions.TryGetValue(segment.EndNodeId ?? "", out double[] existing))
                {
                    if (Math.Abs(existing[0] - end[0]) > LoopTolerance
                        || Math.Abs(existing[1] - end[1]) > LoopTolerance)
                        continue;
                }
                else
                {
                    positions[segment.EndNodeId] = end;
                }

                if (Math.Abs(delta[0]) <= 0.0 && Math.Abs(delta[1]) <= 0.0)
                    continue; // 纯垂直段在平面中退化为点,不生成线。
                result.Add(new QuickLinePlanSegment(segment.Id,
                    start[0], start[1], end[0], end[1], distance));
            }
            return result;
        }

        private static double[] PlanDelta(QuickLineSpatialAxis axis, int directionSign,
            double distance)
        {
            int sign = directionSign < 0 ? -1 : 1;
            switch (axis)
            {
                case QuickLineSpatialAxis.X:
                    return new[] { sign * distance, 0.0 };
                case QuickLineSpatialAxis.Y:
                    return new[] { 0.0, sign * distance };
                default:
                    return new[] { 0.0, 0.0 };
            }
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

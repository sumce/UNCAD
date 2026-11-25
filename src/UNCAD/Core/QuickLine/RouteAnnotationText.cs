using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Text;

namespace UNCAD.Core.QuickLine
{
    /// <summary>D-058：一段文字属于哪一种平行标注。</summary>
    public enum RouteAnnotationTextKind
    {
        None = 0,
        Tray,
        Conduit
    }

    /// <summary>
    /// D-058：图纸里的一个平行标注文字（桥架/线管）。带归属元数据的由
    /// ParallelAnnotationOwnership 精确归属；没有元数据的（元数据时代之前的
    /// 旧输出、手工改写过的文字）才走「旁边就近」绑定。
    /// </summary>
    public sealed class RouteAnnotationTextStamp
    {
        public RouteAnnotationTextStamp(string entityId, string text,
            QuickLinePoint anchor, double? rotationRadians,
            bool hasOwnershipMetadata, RouteAnnotationTextKind kind,
            double height = 0.0, QuickLinePoint? center = null,
            double extentRadiusMillimetres = 0.0)
        {
            EntityId = entityId ?? string.Empty;
            Text = text ?? string.Empty;
            Anchor = anchor;
            Center = center ?? anchor;
            ExtentRadiusMillimetres = double.IsNaN(extentRadiusMillimetres)
                || double.IsInfinity(extentRadiusMillimetres)
                || extentRadiusMillimetres < 0.0 ? 0.0 : extentRadiusMillimetres;
            RotationRadians = rotationRadians;
            HasOwnershipMetadata = hasOwnershipMetadata;
            Kind = kind;
            Height = height;
        }

        public string EntityId { get; }
        public string Text { get; }
        public QuickLinePoint Anchor { get; }
        /// <summary>文字外框中心；读不到外框时等于 Anchor。</summary>
        public QuickLinePoint Center { get; }
        /// <summary>文字外框半对角线（mm）；0 = 没读到外框。</summary>
        public double ExtentRadiusMillimetres { get; }
        /// <summary>文字旋转角（弧度）；读不到时为空（绑定时不校验平行）。</summary>
        public double? RotationRadians { get; }
        /// <summary>文字高度（图形单位）；读不到时为 0。</summary>
        public double Height { get; }
        public bool HasOwnershipMetadata { get; }
        public RouteAnnotationTextKind Kind { get; }
    }

    /// <summary>一个没有元数据的标注文字就近绑定到的带毫米标线段。</summary>
    public sealed class RouteAnnotationTextBinding
    {
        internal RouteAnnotationTextBinding(string segmentId,
            RouteAnnotationTextKind kind, string text,
            double perpendicularDistance, double? trayWidthMillimetres,
            bool trayWidthAssumedDefault)
        {
            SegmentId = segmentId ?? string.Empty;
            Kind = kind;
            Text = text ?? string.Empty;
            PerpendicularDistance = perpendicularDistance;
            TrayWidthMillimetres = trayWidthMillimetres;
            TrayWidthAssumedDefault = trayWidthAssumedDefault;
        }

        public string SegmentId { get; }
        public RouteAnnotationTextKind Kind { get; }
        public string Text { get; }
        /// <summary>文字定位点到线段的垂距（图形单位）。</summary>
        public double PerpendicularDistance { get; }
        /// <summary>桥架宽度（mm）；文字里解析不出规格时为空。</summary>
        public double? TrayWidthMillimetres { get; }
        public bool TrayWidthAssumedDefault { get; }
    }

    /// <summary>
    /// D-058：平行标注文字的识别与就近绑定（U1U 3D 预览用，只读不写图）。
    ///
    /// 识别口径：桥架/线管肯定是「旁边跟着一段桥架/线管文字」——文字按统计
    /// 引擎同一套语法认（单行「桥架200*100 2500mm」「⌀20线管 2000mm」，两行
    /// 「型号 + 长度」）。带归属元数据的文字不走这里（元数据归属是精确的）；
    /// 没有元数据的文字绑定到旁边最近、与其平行、带毫米标的电缆线段。
    /// </summary>
    public static class RouteAnnotationTextPlanner
    {
        /// <summary>文字到线段的最大垂距（图形单位）：U1Q/U1C 标注紧贴线旁。</summary>
        public const double DefaultMaximumPerpendicularDistance = 600.0;
        /// <summary>文字与线段的最大夹角（弧度，按无方向线段计）。</summary>
        public const double DefaultAngleToleranceRadians = Math.PI / 9.0;

        private static readonly Regex BareBridgeSpec = new Regex(
            @"^桥架\s*[0-9]+(?:\.[0-9]+)?\s*[\*xX×]\s*[0-9]+(?:\.[0-9]+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex BareConduitSpec = new Regex(
            @"^(?:[⌀ØΦ]\s*[0-9]+(?:\.[0-9]+)?\s*线管|线管\s*[0-9]+(?:\.[0-9]+)?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        /// <summary>
        /// 3.8.0.3：单行、长度写在行尾的桥架/线管标注（旧图框、手写）——关键字
        /// 后面跟规格，行尾是这一趟的总长：「梯形桥架200Wx100H-1300mm」。规格
        /// 必须以数字或字母结尾；以数字结尾时和长度之间必须有分隔符
        /// （「200*100 3000mm」），以字母结尾时可以紧挨着（「100H1300mm」）——
        /// 否则长度会被拆成「250 + 0mm」；长度是 0 的也不算。mm 之后不能再有
        /// 文字（「… 2500mm 预留」不算）。带「共用」字样的共用桥架明确不进来
        /// （用户口径 2026-11-25「共用桥架不用管」）：它的长度照旧不读。
        /// </summary>
        private static readonly Regex SingleLineTrailingLength = new Regex(
            @"^(?![^\r\n]*共用)(?:(?<head>[^\r\n]*?(?:桥架|线管)[^\r\n]*?[A-Za-z])[\s\-—_:：]*|(?<head>[^\r\n]*?(?:桥架|线管)[^\r\n]*?[0-9])[\s\-—_:：]+)(?!(?:0+(?:\.0+)?|\.0+)\s*mm$)(?<length>[0-9]+(?:\.[0-9]+)?)\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        /// <summary>宽高规格的「200Wx100H」写法（用户 U1Q 的出标格式）。</summary>
        private static readonly Regex WidthHeightSpec = new Regex(
            @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*[Ww]\s*[*xX×]\s*[0-9]+(?:\.[0-9]+)?\s*[Hh]",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>判断一段文字是不是桥架/线管标注文字。</summary>
        public static RouteAnnotationTextKind Classify(string text)
        {
            List<string> lines = TextParser.SplitMTextLines(
                TextParser.CleanMText(text ?? string.Empty));
            if (lines.Count == 0) return RouteAnnotationTextKind.None;
            if (lines.Count == 1) return ClassifySingleLine(lines[0]);

            // 多行：与统计引擎同一口径——有一行桥架/线管标题，另有一行裸长度。
            bool tray = false;
            bool conduit = false;
            bool length = false;
            foreach (string line in lines)
            {
                if (line.IndexOf("线管", StringComparison.Ordinal) >= 0
                    || BareConduitSpec.IsMatch(line))
                    conduit = true;
                else if (line.IndexOf("桥架", StringComparison.Ordinal) >= 0
                    || BareBridgeSpec.IsMatch(line))
                    tray = true;
                else if (TextParser.IsBareLengthToken(line))
                    length = true;
            }
            if (!length) return RouteAnnotationTextKind.None;
            if (tray && !conduit) return RouteAnnotationTextKind.Tray;
            if (conduit && !tray) return RouteAnnotationTextKind.Conduit;
            return RouteAnnotationTextKind.None;
        }

        /// <summary>
        /// 把没有元数据的桥架/线管文字就近绑定到带毫米标的线段。已经带通道
        /// 归属的线段（ownedSegmentIds）不再接收——元数据归属优先，绝不覆盖。
        /// 文字旁边没有平行线段、或两条线段离得几乎一样近时该文字不绑定。
        /// </summary>
        public static IReadOnlyList<RouteAnnotationTextBinding> Bind(
            IEnumerable<RouteAnnotationTextStamp> texts,
            IEnumerable<QuickLineSegment> labelled,
            ISet<string> ownedSegmentIds = null,
            double maximumPerpendicularDistance = DefaultMaximumPerpendicularDistance,
            double angleToleranceRadians = DefaultAngleToleranceRadians)
        {
            if (double.IsNaN(maximumPerpendicularDistance)
                || double.IsInfinity(maximumPerpendicularDistance)
                || maximumPerpendicularDistance <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(maximumPerpendicularDistance));
            if (double.IsNaN(angleToleranceRadians)
                || double.IsInfinity(angleToleranceRadians)
                || angleToleranceRadians < 0.0
                || angleToleranceRadians > Math.PI / 2.0)
                throw new ArgumentOutOfRangeException(nameof(angleToleranceRadians));
            List<QuickLineSegment> segments = (labelled
                ?? Enumerable.Empty<QuickLineSegment>())
                .Where(item => item != null).ToList();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ownedSegmentIds != null)
                foreach (string id in ownedSegmentIds) taken.Add(id);
            var result = new List<RouteAnnotationTextBinding>();

            foreach (RouteAnnotationTextStamp text in (texts
                ?? Enumerable.Empty<RouteAnnotationTextStamp>())
                .Where(item => item != null && !item.HasOwnershipMetadata
                    && item.Kind != RouteAnnotationTextKind.None
                    && item.Anchor != null)
                .OrderBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase))
            {
                QuickLineSegment best = null;
                double bestScore = double.PositiveInfinity;
                double secondScore = double.PositiveInfinity;
                foreach (QuickLineSegment segment in segments)
                {
                    if (!IsCandidate(segment, text, maximumPerpendicularDistance,
                        angleToleranceRadians, out double score)) continue;
                    if (score < bestScore)
                    {
                        secondScore = bestScore;
                        bestScore = score;
                        best = segment;
                    }
                    else if (score < secondScore)
                        secondScore = score;
                }
                if (best == null || taken.Contains(best.Id)) continue;
                // 两条线段离这段文字几乎一样近：归属说不准，不猜。
                if (secondScore - bestScore <= Math.Max(5.0, bestScore * 0.1))
                    continue;
                taken.Add(best.Id);
                double? width = null;
                bool assumed = false;
                if (text.Kind == RouteAnnotationTextKind.Tray)
                {
                    if (TryParseTrayWidthMillimetres(text.Text, out double parsed))
                        width = parsed;
                    else assumed = true;
                }
                result.Add(new RouteAnnotationTextBinding(best.Id, text.Kind,
                    FirstLine(text.Text), bestScore, width, assumed));
            }
            return result;
        }

        /// <summary>
        /// D-059：标注文字里的整趟长度（mm）。两行标注（型号 + 长度）取裸长度行；
        /// 单行按桥架/线管的统计语法读。读不出返回 null，不猜。
        /// </summary>
        public static double? ParseLengthMillimetres(string text)
        {
            List<string> lines = TextParser.SplitMTextLines(
                TextParser.CleanMText(text ?? string.Empty));
            if (lines.Count == 0) return null;
            if (lines.Count > 1)
            {
                foreach (string line in lines)
                    if (TextParser.ExtractCableLength(line).HasValue)
                        return TextParser.ExtractCableLength(line);
                return null;
            }
            string single = lines[0];
            if (BridgeLabelFormatter.TryParseMillimetreLabel(single, out _,
                out double millimetres)) return millimetres;
            double? conduit = TextParser.ExtractConduitLength(single);
            if (conduit.HasValue) return conduit;
            // 3.8.0.3：单行、长度在行尾的旧写法（「梯形桥架200Wx100H-1300mm」）。
            Match trailing = SingleLineTrailingLength.Match(single);
            if (trailing.Success
                && double.TryParse(trailing.Groups["length"].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double total)
                && total > 0.0) return total;
            return null;
        }

        /// <summary>
        /// 3.8.0.3：桥架标注里的宽度（mm）。先按统计语法「桥架200*100」读，再按
        /// U1Q 出标的「200Wx100H」读；都读不出返回 false（调用方用缺省并注明）。
        /// </summary>
        public static bool TryParseTrayWidthMillimetres(string text,
            out double widthMillimetres)
        {
            if (BridgeLabelFormatter.TryParseTrayWidthMillimetres(text,
                out widthMillimetres)) return true;
            Match match = WidthHeightSpec.Match(TextParser.CleanMText(
                text ?? string.Empty));
            if (!match.Success
                || !double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out widthMillimetres)
                || widthMillimetres <= 0.0)
            {
                widthMillimetres = 0.0;
                return false;
            }
            return true;
        }

        private static RouteAnnotationTextKind ClassifySingleLine(string line)
        {
            if (TextParser.ExtractConduitLength(line).HasValue)
                return RouteAnnotationTextKind.Conduit;
            if (TextParser.TryExtractBridgeLabel(line, out _, out _))
                return RouteAnnotationTextKind.Tray;
            if (BridgeLabelFormatter.TryParseMillimetreLabel(line,
                out _, out _))
                return RouteAnnotationTextKind.Tray;
            if (BareConduitSpec.IsMatch(line))
                return RouteAnnotationTextKind.Conduit;
            if (BareBridgeSpec.IsMatch(line))
                return RouteAnnotationTextKind.Tray;
            // 3.8.0.3：单行、长度在行尾的旧写法。既有规格语法都认不出时才走这里
            // （「… 2500mm 预留」这类 mm 后面还有文字的仍然不算）。
            Match trailing = SingleLineTrailingLength.Match(line);
            if (trailing.Success)
            {
                string head = trailing.Groups["head"].Value;
                bool tray = head.IndexOf("桥架", StringComparison.Ordinal) >= 0;
                bool conduit = head.IndexOf("线管", StringComparison.Ordinal) >= 0;
                if (tray && !conduit) return RouteAnnotationTextKind.Tray;
                if (conduit && !tray) return RouteAnnotationTextKind.Conduit;
            }
            return RouteAnnotationTextKind.None;
        }

        private static bool IsCandidate(QuickLineSegment segment,
            RouteAnnotationTextStamp text, double maximumPerpendicularDistance,
            double angleToleranceRadians, out double score)
        {
            score = double.PositiveInfinity;
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18) return false;
            if (text.RotationRadians.HasValue
                && !double.IsNaN(text.RotationRadians.Value)
                && !double.IsInfinity(text.RotationRadians.Value))
            {
                double lineAngle = Math.Atan2(dy, dx);
                double difference = Math.Abs(NormalizeHalfTurn(
                    text.RotationRadians.Value - lineAngle));
                if (difference > angleToleranceRadians) return false;
            }
            double offsetX = text.Anchor.X - segment.Start.X;
            double offsetY = text.Anchor.Y - segment.Start.Y;
            double parameter = (offsetX * dx + offsetY * dy) / lengthSquared;
            if (parameter < -0.1 || parameter > 1.1) return false;
            double perpendicular = Math.Abs(offsetX * dy - offsetY * dx)
                / Math.Sqrt(lengthSquared);
            if (perpendicular > maximumPerpendicularDistance) return false;
            // 垂距为主；文字落到线段两端之外的超出量记一点惩罚。
            double overshoot = Math.Max(0.0, -parameter) + Math.Max(0.0, parameter - 1.0);
            score = perpendicular + overshoot * Math.Sqrt(lengthSquared);
            return true;
        }

        private static double NormalizeHalfTurn(double angle)
        {
            double normalized = angle % Math.PI;
            if (normalized < 0.0) normalized += Math.PI;
            return normalized <= Math.PI / 2.0
                ? normalized : Math.PI - normalized;
        }

        private static string FirstLine(string text)
        {
            List<string> lines = TextParser.SplitMTextLines(
                TextParser.CleanMText(text ?? string.Empty));
            return lines.Count > 0 ? lines[0] : string.Empty;
        }
    }

    /// <summary>
    /// D-058：认出「桥架/线管自己的那根平行线」。元数据时代之前的 U1Q/U1C
    /// 输出没有元数据，会被扫描器当成没有毫米标的普通线段；它紧贴着一根
    /// 带桥架/线管通道的电缆线段、与之平行、长度不超出——那就是标注线，
    /// 不是漏标毫米标的电缆线。
    /// </summary>
    public static class RouteAnnotationLineClassifier
    {
        /// <summary>平行线到电缆线的最大垂距（图形单位）。</summary>
        public const double DefaultMaximumOffset = 100.0;
        /// <summary>与电缆线的最大夹角（弧度，按无方向线段计）。</summary>
        public const double DefaultAngleToleranceRadians = Math.PI / 60.0;

        /// <summary>
        /// 返回 unlabelled 里属于「带通道电缆线段的平行标注线」的线段 id。
        /// channelHosts 是带桥架/线管通道的电缆线段。
        /// </summary>
        public static HashSet<string> FindOffsetLines(
            IEnumerable<QuickLineSegment> unlabelled,
            IEnumerable<QuickLineSegment> channelHosts,
            double maximumOffset = DefaultMaximumOffset,
            double angleToleranceRadians = DefaultAngleToleranceRadians)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (double.IsNaN(maximumOffset) || double.IsInfinity(maximumOffset)
                || maximumOffset < 0.0)
                throw new ArgumentOutOfRangeException(nameof(maximumOffset));
            if (double.IsNaN(angleToleranceRadians)
                || double.IsInfinity(angleToleranceRadians)
                || angleToleranceRadians < 0.0
                || angleToleranceRadians > Math.PI / 2.0)
                throw new ArgumentOutOfRangeException(nameof(angleToleranceRadians));
            List<QuickLineSegment> hosts = (channelHosts
                ?? Enumerable.Empty<QuickLineSegment>())
                .Where(item => item != null).ToList();
            foreach (QuickLineSegment line in (unlabelled
                ?? Enumerable.Empty<QuickLineSegment>())
                .Where(item => item != null))
                if (hosts.Any(host => IsOffsetLineOf(line, host, maximumOffset,
                    angleToleranceRadians)))
                    result.Add(line.Id);
            return result;
        }

        private static bool IsOffsetLineOf(QuickLineSegment line,
            QuickLineSegment host, double maximumOffset, double angleTolerance)
        {
            double hx = host.End.X - host.Start.X;
            double hy = host.End.Y - host.Start.Y;
            double hostLengthSquared = hx * hx + hy * hy;
            if (hostLengthSquared <= 1e-18) return false;
            double lx = line.End.X - line.Start.X;
            double ly = line.End.Y - line.Start.Y;
            if (lx * lx + ly * ly <= 1e-18) return false;
            double cross = hx * ly - hy * lx;
            double dot = hx * lx + hy * ly;
            double sine = Math.Abs(cross) / Math.Sqrt(hostLengthSquared
                * (lx * lx + ly * ly));
            // 无方向线段的夹角：平行或反向平行都算平行。
            double angle = Math.Atan2(sine, Math.Abs(dot) / Math.Sqrt(
                hostLengthSquared * (lx * lx + ly * ly)));
            if (angle > angleTolerance) return false;

            double hostLength = Math.Sqrt(hostLengthSquared);
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            foreach (QuickLinePoint point in new[] { line.Start, line.End })
            {
                double offsetX = point.X - host.Start.X;
                double offsetY = point.Y - host.Start.Y;
                double perpendicular = Math.Abs(offsetX * hy - offsetY * hx)
                    / hostLength;
                if (perpendicular > maximumOffset) return false;
                double parameter = (offsetX * hx + offsetY * hy) / hostLengthSquared;
                low = Math.Min(low, parameter);
                high = Math.Max(high, parameter);
            }
            // 必须与电缆线段有重叠，且不大幅超出电缆线段范围。
            return high >= 0.0 && low <= 1.0
                && high - low <= 1.0 + maximumOffset / hostLength;
        }
    }
}

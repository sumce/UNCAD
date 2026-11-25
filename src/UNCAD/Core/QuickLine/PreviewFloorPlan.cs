using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Text;

namespace UNCAD.Core.QuickLine
{
    /// <summary>3D 预览里的一块楼层平面。</summary>
    public sealed class PreviewFloorSlab
    {
        internal PreviewFloorSlab(int number, double elevationMillimetres,
            double sceneZ, bool elevationKnown, string sourceLabel)
        {
            Number = number;
            ElevationMillimetres = elevationMillimetres;
            SceneZ = sceneZ;
            ElevationKnown = elevationKnown;
            SourceLabel = sourceLabel ?? string.Empty;
        }

        /// <summary>楼层号（1F=1；地下层为负）。</summary>
        public int Number { get; }
        /// <summary>「2F」「3F」「B1」。</summary>
        public string Label => (Number < 0 ? "B" + (-Number) : Number.ToString(
            CultureInfo.InvariantCulture)) + "F";
        /// <summary>该层地面的绝对标高（mm，1F 地面 = 0）。</summary>
        public double ElevationMillimetres { get; }
        /// <summary>相对场景零点的高度（mm）：最低的那块平面在 0。</summary>
        public double SceneZ { get; }
        /// <summary>标高来自 U1SET 的标高清单；false = 按缺省层高推的。</summary>
        public bool ElevationKnown { get; }
        /// <summary>
        /// 3.8.0.3：标高取的是 U1SET 标高清单里的哪一条（「2F地面」「3F高架地板面」）；
        /// 清单里没有这一层、按缺省层高推出来时为空。
        /// </summary>
        public string SourceLabel { get; }
    }

    /// <summary>
    /// D-059：U1U 3D 预览的楼层平面——只画台账上下游楼层之间的那几层
    /// （「根据上下游信息表的楼层来决定渲染」）。标高取 U1SET 标高清单里的
    /// 「NF地面 / NF高架地板面」（设备站在高架地板上，有高架地板面就用它），
    /// 清单里没有的楼层按缺省层高推。
    ///
    /// 跨层校验（用户口径，3.8.0.3 更正为「上下游端点距离改成 2F 地面到设备的
    /// 距离，2F→3F 在 7.6~7.8」）：上下游不在同一层时，从上游楼层地面量到设备端
    /// 的垂直距离应等于设备站立平面与上游地面的标高差，允许低 0.2 m/层。
    /// 以前直接拿线路两端的高差去比——图框里画不出地面，插座盘离地那 1 m 不在
    /// 线路的毫米标里，量法和比的不是一个起算点（现场：「实际 6.2 m」）。
    /// </summary>
    public sealed class PreviewFloorPlan
    {
        /// <summary>
        /// 缺省层高（mm）：标高清单里没有的楼层按它往上/下推。本项目 2F 地面到
        /// 3F 高架地板面（设备站立面）是 7.8 m；两层地面之间是 7.2 m。
        /// </summary>
        public const double DefaultFloorHeightMillimetres = 7800.0;
        /// <summary>跨层高度允许的偏差（mm / 层）：7.8 − 0.2 = 7.6 m 下限的来源。</summary>
        public const double RiseTolerancePerFloorMillimetres = 200.0;

        private static readonly Regex FloorEntry = new Regex(
            @"(\d+)\s*F\s*(高架地板面|地面)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex FloorToken = new Regex(
            @"^\s*([Bb]|[Ff])?\s*(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private PreviewFloorPlan(IReadOnlyList<PreviewFloorSlab> floors,
            int? upstreamFloor, int? deviceFloor, string source)
        {
            Floors = floors;
            UpstreamFloor = upstreamFloor;
            DeviceFloor = deviceFloor;
            Source = source ?? string.Empty;
            UpstreamSlab = floors.FirstOrDefault(slab => slab.Number == upstreamFloor);
            DeviceSlab = floors.FirstOrDefault(slab => slab.Number == deviceFloor);
        }

        /// <summary>要画的楼层平面，按楼层从低到高。</summary>
        public IReadOnlyList<PreviewFloorSlab> Floors { get; }
        public int? UpstreamFloor { get; }
        public int? DeviceFloor { get; }
        /// <summary>上游所在的楼层平面（台账认不出上游楼层时为 null）。</summary>
        public PreviewFloorSlab UpstreamSlab { get; }
        /// <summary>设备所在的楼层平面（台账认不出设备楼层时为 null）。</summary>
        public PreviewFloorSlab DeviceSlab { get; }
        /// <summary>楼层来源：台账 / 台账（另一端认不出）/ U1SET 本层 / 无。</summary>
        public string Source { get; }
        /// <summary>上下游各在一层且不是同一层——才做跨层高度校验。</summary>
        public bool CrossesFloors => UpstreamSlab != null && DeviceSlab != null
            && UpstreamSlab.Number != DeviceSlab.Number;

        /// <summary>
        /// 跨层高度校验：量的是「上游楼层地面 → 设备端」的垂直距离（mm）。
        /// basePointZ / deviceZ 是线路基础点和设备端在场景里的高度；基础点是
        /// 上游端点（端点段是向上的立管时是立管底，
        /// <see cref="PreviewCabinet.BasePoint"/>）。baseHeightMillimetres 是
        /// 基础点离上游楼层地面的高度（插座盘 1 m，其它盘 0）——图框里画不出
        /// 地面，这一截不在线路的毫米标里，要从楼层地面量就加回来。
        /// 返回警告文案；不需要校验（同层 / 楼层认不出）或高度符合时返回 null。
        /// </summary>
        public string EvaluateRise(double basePointZ, double deviceZ,
            double baseHeightMillimetres = 0.0)
        {
            if (!CrossesFloors) return null;
            if (double.IsNaN(basePointZ) || double.IsInfinity(basePointZ)
                || double.IsNaN(deviceZ) || double.IsInfinity(deviceZ)) return null;
            if (double.IsNaN(baseHeightMillimetres)
                || double.IsInfinity(baseHeightMillimetres)) return null;
            double expected = DeviceSlab.ElevationMillimetres
                - UpstreamSlab.ElevationMillimetres;
            int storeys = Math.Max(1, Math.Abs(DeviceSlab.Number - UpstreamSlab.Number));
            double tolerance = RiseTolerancePerFloorMillimetres * storeys;
            double routeRise = deviceZ - basePointZ;
            double actual = baseHeightMillimetres + routeRise;
            if (Math.Abs(actual - expected) <= tolerance) return null;
            double sign = expected >= 0 ? 1.0 : -1.0;
            double high = expected;
            double low = expected - sign * tolerance;
            string first = Meters(sign > 0 ? low : high);
            string second = Meters(sign > 0 ? high : low);
            string measured = "线路竖向 " + Meters(routeRise) + " m";
            if (baseHeightMillimetres > 0.0)
                measured = "上游端离地 " + Meters(baseHeightMillimetres) + " m + "
                    + measured;
            return "跨层高度不符：台账 " + UpstreamSlab.Label + "→" + DeviceSlab.Label
                + "，" + SlabName(UpstreamSlab) + "到设备端应在 " + first + "~"
                + second + " m（按 U1SET 标高清单 " + SlabName(UpstreamSlab) + " → "
                + SlabName(DeviceSlab) + " 差 " + Meters(expected) + " m），实际 "
                + Meters(actual) + " m（" + measured
                + "）——请核对竖向段的毫米标";
        }

        /// <summary>清单里有这一层用条目名；没有（按层高推的）用楼层名并注明。</summary>
        private static string SlabName(PreviewFloorSlab slab)
            => slab.ElevationKnown
                ? (string.IsNullOrEmpty(slab.SourceLabel) ? slab.Label : slab.SourceLabel)
                : slab.Label + "（标高清单里没有，按层高推算）";

        /// <summary>
        /// 由台账的上游楼层、设备楼层和 U1SET 标高清单规划楼层平面。
        /// baseFloorDirective 是清单里的「本层=2F地面」指令（台账楼层都认不出时
        /// 退回它）。
        /// </summary>
        public static PreviewFloorPlan Plan(string upstreamFloorText,
            string deviceFloorText, string baseFloorDirective,
            IEnumerable<PreviewReferenceLevel> levels)
        {
            var grounds = new Dictionary<int, double>();
            var raised = new Dictionary<int, double>();
            // 3.8.0.3：记下每个标高是清单里的哪一条（「2F地面」「3F高架地板面」），
            // 跨层校验的文案里点名——7.6~7.8 从哪来的，一眼能对上 U1SET。
            var groundLabels = new Dictionary<int, string>();
            var raisedLabels = new Dictionary<int, string>();
            foreach (PreviewReferenceLevel level in levels ?? Array.Empty<PreviewReferenceLevel>())
            {
                if (level == null) continue;
                Match match = FloorEntry.Match(level.Label ?? string.Empty);
                if (!match.Success
                    || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int number)
                    || number <= 0) continue;
                if (match.Groups[2].Value == "高架地板面")
                {
                    raised[number] = level.ZMillimetres;
                    raisedLabels[number] = (level.Label ?? string.Empty).Trim();
                }
                else
                {
                    grounds[number] = level.ZMillimetres;
                    groundLabels[number] = (level.Label ?? string.Empty).Trim();
                }
            }

            int? upstream = ParseFloorNumber(upstreamFloorText);
            int? device = ParseFloorNumber(deviceFloorText);
            List<int> shown = new List<int>();
            string source;
            if (upstream.HasValue && device.HasValue)
            {
                shown.AddRange(Enumerable.Range(Math.Min(upstream.Value, device.Value),
                    Math.Abs(upstream.Value - device.Value) + 1));
                source = "台账";
            }
            else if (upstream.HasValue || device.HasValue)
            {
                shown.Add(upstream ?? device.Value);
                source = "台账（另一端的楼层认不出）";
            }
            else
            {
                int? directive = ParseFloorNumber(StripDirective(baseFloorDirective));
                if (directive.HasValue)
                {
                    shown.Add(directive.Value);
                    source = "U1SET 本层";
                }
                else source = "无";
            }

            // 清单里没有的楼层：从最近一个有标高的地面按层高往上/下推。
            double height = DefaultFloorHeightMillimetres;
            int anchorFloor = 1;
            double anchorElevation = 0.0;
            if (grounds.TryGetValue(1, out double first))
            {
                anchorElevation = first;
            }
            else if (grounds.TryGetValue(2, out double second))
            {
                height = second;
                anchorFloor = 2;
                anchorElevation = second;
            }
            else if (grounds.Count > 0)
            {
                anchorFloor = grounds.Keys.Min();
                anchorElevation = grounds[anchorFloor];
            }
            var slabs = new List<PreviewFloorSlab>();
            if (shown.Count > 0)
            {
                double lowest = double.PositiveInfinity;
                var elevations = new Dictionary<int, SlabElevation>();
                foreach (int number in shown)
                {
                    SlabElevation elevation = Elevations(number, grounds, groundLabels,
                        raised, raisedLabels, height, anchorFloor, anchorElevation);
                    elevations[number] = elevation;
                    lowest = Math.Min(lowest, elevation.Millimetres);
                }
                foreach (int number in shown.OrderBy(item => item))
                    slabs.Add(new PreviewFloorSlab(number,
                        elevations[number].Millimetres,
                        elevations[number].Millimetres - lowest,
                        elevations[number].Known, elevations[number].SourceLabel));
            }
            return new PreviewFloorPlan(slabs, upstream, device, source);
        }

        /// <summary>
        /// 台账楼层文字 → 楼层号：「3F」「3」「f3」「3层」「B1」「B1F」；认不出返回 null。
        /// </summary>
        public static int? ParseFloorNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string normalized = text.Trim().Normalize(System.Text.NormalizationForm.FormKC);
            Match match = FloorToken.Match(normalized);
            if (!match.Success) return null;
            if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int number) || number <= 0) return null;
            string prefix = match.Groups[1].Value;
            return string.Equals(prefix, "B", StringComparison.OrdinalIgnoreCase)
                ? -number : number;
        }

        private static SlabElevation Elevations(int number,
            Dictionary<int, double> grounds, Dictionary<int, string> groundLabels,
            Dictionary<int, double> raised, Dictionary<int, string> raisedLabels,
            double height, int anchorFloor, double anchorElevation)
        {
            if (number <= 0) return new SlabElevation(0.0, true, string.Empty);
            if (raised.TryGetValue(number, out double raisedElevation))
                return new SlabElevation(raisedElevation, true,
                    LabelOf(raisedLabels, number));
            if (grounds.TryGetValue(number, out double ground))
                return new SlabElevation(ground, true, LabelOf(groundLabels, number));
            // 清单里没有这一层：从锚定楼层按层高推。
            return new SlabElevation(
                anchorElevation + height * (number - anchorFloor), false, string.Empty);
        }

        private static string LabelOf(Dictionary<int, string> labels, int number)
        {
            labels.TryGetValue(number, out string label);
            return (label ?? string.Empty).Trim();
        }

        private sealed class SlabElevation
        {
            internal SlabElevation(double millimetres, bool known, string sourceLabel)
            {
                Millimetres = millimetres;
                Known = known;
                SourceLabel = sourceLabel ?? string.Empty;
            }

            internal double Millimetres { get; }
            internal bool Known { get; }
            internal string SourceLabel { get; }
        }

        private static string StripDirective(string directive)
        {
            string text = (directive ?? string.Empty).Trim();
            int equals = text.IndexOf('=');
            return equals >= 0 && equals < text.Length - 1
                ? text.Substring(equals + 1) : text;
        }

        internal static string Meters(double millimetres)
            => (millimetres / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
    }
}

using System.Globalization;
using UNCAD.Core.QuickLine;

namespace UNCAD.Infra
{
    /// <summary>
    /// D-058：线路识别设置（U1SET「图框与排版」页）。U1U 的 3D 预览、连通性
    /// 报告和 U13D 共用同一份断口容差，命令开始时读一次。
    /// </summary>
    public static class QuickLineRouteSettings
    {
        /// <summary>
        /// 断口容差（图形单位）：0 表示只用严格容差；没设、乱填或负数回落缺省 50，
        /// 超过上限按上限。
        /// </summary>
        public static double GapTolerance()
            => ParseGapTolerance(Settings.Get(ConfigKeys.RouteGapTolerance, null));

        /// <summary>GapTolerance 的纯函数部分（可单测，不碰注册表）。</summary>
        internal static double ParseGapTolerance(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return QuickLineRouteConnector.DefaultGapTolerance;
            if (!double.TryParse(raw.Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                return QuickLineRouteConnector.DefaultGapTolerance;
            return QuickLineRouteConnector.ClampGapTolerance(value);
        }
    }
}

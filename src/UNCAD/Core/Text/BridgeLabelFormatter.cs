using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Text
{
    /// <summary>Normalizes U1Q labels to the bridge-specification plus millimetre format.</summary>
    public static class BridgeLabelFormatter
    {
        private static readonly Regex GridLabel = new Regex(
            @"^桥架\s*([0-9]+(?:\.[0-9]+)?)\s*[\*xX×]\s*([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*格$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex MillimetreLabel = new Regex(
            @"^桥架\s*([0-9]+(?:\.[0-9]+)?)\s*[\*xX×]\s*([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*mm$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string NormalizeOrDefault(string value, double mmPerGrid,
            string fallback = "桥架200*100 2500mm")
        {
            if (TryNormalize(value, mmPerGrid, out string normalized)) return normalized;
            if (TryNormalize(fallback, mmPerGrid, out normalized)) return normalized;
            return "桥架200*100 2500mm";
        }

        public static bool TryNormalize(string value, double mmPerGrid,
            out string normalized)
        {
            normalized = "";
            if (TryMigrateLegacyGrid(value, mmPerGrid, out normalized)) return true;

            string text = (value ?? "").Trim();
            Match millimetre = MillimetreLabel.Match(text);
            if (!millimetre.Success || !TryNumber(millimetre, 1, out double width)
                || !TryNumber(millimetre, 2, out double height)
                || !TryNumber(millimetre, 3, out double totalMillimetres)
                || width <= 0 || height <= 0 || totalMillimetres <= 0) return false;
            normalized = "桥架" + Format(width) + "*" + Format(height)
                + " " + Format(totalMillimetres) + "mm";
            return true;
        }

        /// <summary>
        /// 拆出现行毫米写法：规格（桥架宽*高）与毫米长度。U1Q 生成两行标注时用它
        /// 取出规格去查固定清单型号；长度写法已在 <see cref="TryNormalize"/> 里校验。
        /// </summary>
        public static bool TryParseMillimetreLabel(string value, out string spec,
            out double millimetres)
        {
            spec = "";
            millimetres = 0;
            Match millimetre = MillimetreLabel.Match((value ?? "").Trim());
            if (!millimetre.Success || !TryNumber(millimetre, 1, out double width)
                || !TryNumber(millimetre, 2, out double height)
                || !TryNumber(millimetre, 3, out double total)
                || width <= 0 || height <= 0 || total <= 0) return false;
            spec = "桥架" + Format(width) + "*" + Format(height);
            millimetres = total;
            return true;
        }

        /// <summary>把毫米数写成标注用的长度文字（2500 → “2500mm”）。</summary>
        public static string FormatLengthText(double millimetres)
            => Format(millimetres) + "mm";

        /// <summary>
        /// Converts only the legacy grid-count form. Current millimetre labels
        /// deliberately return false so U1F/U1U can migrate old CAD text without
        /// rewriting already-current annotations.
        /// </summary>
        public static bool TryMigrateLegacyGrid(string value, double mmPerGrid,
            out string normalized)
        {
            normalized = "";
            Match grid = GridLabel.Match((value ?? "").Trim());
            if (!grid.Success || !TryNumber(grid, 1, out double width)
                || !TryNumber(grid, 2, out double height)
                || !TryNumber(grid, 3, out double grids)
                || width <= 0 || height <= 0 || grids <= 0
                || !IsValidScale(mmPerGrid)) return false;
            normalized = "桥架" + Format(width) + "*" + Format(height)
                + " " + Format(grids * mmPerGrid) + "mm";
            return true;
        }

        /// <summary>
        /// 旧格数 → 毫米写法，且结果必须仍能被统计读出来，否则不迁移。
        ///
        /// 格数允许小数（12.5格），每格毫米也可调，两者相乘很容易得到 3125 这种
        /// 不以 0 结尾的数；而统计只认“结尾 0”的长度。若照常迁移，这条标注会被
        /// 改成统计读不出的样子，下一次 U1F/U1U 就静默丢了这个规格的桥架。
        /// 保留旧的格数写法反而是安全的——旧写法统计照样认。
        ///
        /// 注意不要把这个判据并进 <see cref="TryMigrateLegacyGrid"/>：U1Q 的标识
        /// 规范化也用那个方法，在那里返回 false 会让配置悄悄回退成默认规格。
        /// </summary>
        public static bool TryMigrateLegacyGridReadable(string value, double mmPerGrid,
            out string normalized)
        {
            normalized = "";
            if (!TryMigrateLegacyGrid(value, mmPerGrid, out string migrated)) return false;
            if (!TextParser.TryExtractBridgeLabel(migrated, mmPerGrid, out _, out _))
                return false;
            normalized = migrated;
            return true;
        }

        private static bool IsValidScale(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

        private static bool TryNumber(Match match, int group, out double value)
            => double.TryParse(match.Groups[group].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static string Format(double value)
            => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

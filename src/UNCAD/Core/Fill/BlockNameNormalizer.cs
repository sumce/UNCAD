using System.Text.RegularExpressions;

namespace UNCAD.Core.Fill
{
    internal static class BlockNameNormalizer
    {
        private static readonly Regex MangledSuffix = new Regex(@"(\$\d+\$)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string RemoveMangledSuffix(string name)
            => string.IsNullOrEmpty(name) ? "" : MangledSuffix.Replace(name, "");

        /// <summary>
        /// 软管块族判定：<c>Ruanguan</c> 以及它的派生块名（<c>Ruanguan_GW</c> 等）。
        ///
        /// 为什么按"首段"而不是全等：同一台设备的软管块在不同项目里有
        /// <c>Ruanguan_GW</c> 这类后缀变体，它们与 <c>Ruanguan</c> 是同一个业务对象，
        /// 必须同等识别（读长度、写型号/长度、参与 U1L/U1F/U1U 与 U1C 的软管逻辑）。
        /// 只有分隔符之后的差异才被接受，<c>RuanguanXYZ</c> 这种黏连名不算同族。
        /// </summary>
        public static bool IsFlexibleConduitBlock(string name)
        {
            string value = RemoveMangledSuffix((name ?? "").Trim());
            if (value.Length == 0) return false;
            int separator = value.IndexOfAny(new[] { '_', '-', ' ' });
            string head = separator < 0 ? value : value.Substring(0, separator);
            return string.Equals(head, "Ruanguan", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

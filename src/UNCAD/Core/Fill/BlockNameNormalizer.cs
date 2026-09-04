using System.Text.RegularExpressions;

namespace UNCAD.Core.Fill
{
    internal static class BlockNameNormalizer
    {
        private static readonly Regex MangledSuffix = new Regex(@"(\$\d+\$)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string RemoveMangledSuffix(string name)
            => string.IsNullOrEmpty(name) ? "" : MangledSuffix.Replace(name, "");
    }
}

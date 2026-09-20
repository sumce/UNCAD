using System;
using System.Collections.Generic;
using System.Text;

namespace UNCAD.Core.Text
{
    /// <summary>
    /// Provides the comparison key used for machine IDs and device names.
    /// Display and persisted values remain unchanged; only comparisons ignore
    /// Unicode whitespace and letter casing.
    /// </summary>
    public static class IdentityTextNormalizer
    {
        public static IEqualityComparer<string> Comparer { get; } =
            new IdentityComparer();

        public static string Key(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var result = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (!char.IsWhiteSpace(character)) result.Append(character);
            }
            return result.ToString();
        }

        public static bool Equals(string left, string right)
            => string.Equals(Key(left), Key(right), StringComparison.OrdinalIgnoreCase);

        public static bool Contains(string value, string fragment)
        {
            string candidate = Key(value);
            string needle = Key(fragment);
            return needle.Length > 0
                && candidate.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool EndsWith(string value, string suffix)
            => Key(value).EndsWith(Key(suffix), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Removes a known device suffix while retaining the source's original
        /// spacing in the returned machine ID.
        /// </summary>
        public static bool TryGetPrefixBeforeSuffix(string value, string suffix,
            out string prefix)
        {
            prefix = "";
            string source = (value ?? "").Trim();
            string compact = Key(source);
            string marker = "-" + Key(suffix);
            if (Key(suffix).Length == 0
                || !compact.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
                return false;

            prefix = TakeOriginalPrefix(source, compact.Length - marker.Length)
                .TrimEnd('-').Trim();
            return prefix.Length > 0;
        }

        /// <summary>
        /// Removes a known machine prefix while retaining the source's original
        /// spacing in the returned device name.
        /// </summary>
        public static bool TryGetSuffixAfterPrefix(string value, string prefix,
            out string suffix)
        {
            suffix = "";
            string source = (value ?? "").Trim();
            string compact = Key(source);
            string marker = Key(prefix) + "-";
            if (Key(prefix).Length == 0
                || !compact.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return false;

            suffix = TakeOriginalSuffix(source, marker.Length).Trim();
            return suffix.Length > 0;
        }

        private static string TakeOriginalPrefix(string source, int nonWhitespaceCount)
        {
            if (nonWhitespaceCount <= 0) return "";
            int seen = 0;
            for (int index = 0; index < source.Length; index++)
            {
                if (char.IsWhiteSpace(source[index])) continue;
                seen++;
                if (seen == nonWhitespaceCount) return source.Substring(0, index + 1);
            }
            return source;
        }

        private static string TakeOriginalSuffix(string source, int nonWhitespaceOffset)
        {
            if (nonWhitespaceOffset <= 0) return source;
            int seen = 0;
            for (int index = 0; index < source.Length; index++)
            {
                if (char.IsWhiteSpace(source[index])) continue;
                seen++;
                if (seen == nonWhitespaceOffset) return source.Substring(index + 1);
            }
            return "";
        }

        private sealed class IdentityComparer : IEqualityComparer<string>
        {
            public bool Equals(string left, string right)
                => IdentityTextNormalizer.Equals(left, right);

            public int GetHashCode(string value)
                => StringComparer.OrdinalIgnoreCase.GetHashCode(Key(value));
        }
    }
}

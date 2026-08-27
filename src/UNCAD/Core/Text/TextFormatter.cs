using System.Collections.Generic;
using System.Globalization;

namespace UNCAD.Core.Text
{
    /// <summary>输出格式化（纯 C#）。</summary>
    public static class TextFormatter
    {
        /// <summary>对应 LISP (rtos v 2 2) 再去尾零："1.00"-&gt;"1"、"1.50"-&gt;"1.5"。</summary>
        public static string FormatNum(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Join(IEnumerable<string> items, string delim)
        {
            return string.Join(delim, items);
        }
    }
}

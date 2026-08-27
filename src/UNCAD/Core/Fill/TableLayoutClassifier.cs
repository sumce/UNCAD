using System;
using System.Text.RegularExpressions;

namespace UNCAD.Core.Fill
{
    /// <summary>清单表标题、表头和编号数据行的纯文本识别规则。</summary>
    public static class TableLayoutClassifier
    {
        private static readonly Regex NumberedRowRegex = new Regex(
            @"^(?:NO\.?\s*)?\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsHeaderLike(string column0, string column1, string column5)
        {
            string c0 = Normalize(column0);
            string c1 = Normalize(column1);
            string c5 = Normalize(column5);

            return EqualsAny(c0, "NO", "NO.", "序号", "编号", "项次")
                || EqualsAny(c1, "项目", "项目名称", "名称", "设备", "设备名称", "回路", "回路名称")
                || EqualsAny(c5, "编码", "项目编码", "编号", "项次", "项次编码");
        }

        public static bool IsNumberedDataRow(string value)
        {
            return NumberedRowRegex.IsMatch((value ?? "").Trim());
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim();
        }

        private static bool EqualsAny(string value, params string[] candidates)
        {
            foreach (string candidate in candidates)
                if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}

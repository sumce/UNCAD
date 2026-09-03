using System;
using System.Text.RegularExpressions;
using UNCAD.Core.Text;

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

        public static bool IsDrawingInfoHeader(params string[] cells)
        {
            return IsCurrentDrawingInfoHeader(cells)
                || MatchesDrawingInfoHeader(cells,
                    "专业", "设备楼层", "上游楼层", "制图", "审核", "日期", "版本");
        }

        public static bool IsCurrentDrawingInfoHeader(params string[] cells)
        {
            return MatchesDrawingInfoHeader(cells,
                "专业", "楼层", "制图", "审核", "日期", "版本");
        }

        private static bool MatchesDrawingInfoHeader(string[] cells,
            params string[] expected)
        {
            if (cells == null || cells.Length < expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
                if (!Normalize(cells[i]).Equals(expected[i],
                    StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string Normalize(string value)
        {
            return TextParser.CleanMText(value).Trim();
        }

        private static bool EqualsAny(string value, params string[] candidates)
        {
            foreach (string candidate in candidates)
                if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;

namespace UNCAD.Core.Excel
{
    internal static class ExcelHeaderBinder
    {
        public static bool Equals(ICell cell, string expected)
        {
            return string.Equals(Normalize(ExcelColumnReader.CellToString(cell)),
                Normalize(expected), StringComparison.OrdinalIgnoreCase);
        }

        public static int Require(IRow header, string sheetName, params string[] aliases)
        {
            int found = Find(header, sheetName, aliases);
            if (found < 0)
                throw new InvalidDataException("工作表“" + sheetName + "”缺少必需字段“"
                    + aliases[0] + "”。");
            return found;
        }

        public static int Optional(IRow header, string sheetName, params string[] aliases)
            => Find(header, sheetName, aliases);

        private static int Find(IRow header, string sheetName, string[] aliases)
        {
            int found = -1;
            for (int c = 0; c < header.LastCellNum; c++)
            {
                if (!aliases.Any(alias => Equals(header.GetCell(c), alias))) continue;
                if (found >= 0)
                    throw new InvalidDataException("工作表“" + sheetName + "”存在重复字段“"
                        + aliases[0] + "”（第 " + (found + 1) + "、" + (c + 1) + " 列）。");
                found = c;
            }
            return found;
        }

        private static string Normalize(string value)
            => new string((value ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).Trim();
    }
}

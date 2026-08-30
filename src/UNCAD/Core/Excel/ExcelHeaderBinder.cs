using System;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>Resolves required and optional worksheet headers with alias priority.</summary>
    public static class ExcelHeaderBinder
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
            if (header == null) return -1;
            // Aliases are alternatives, not duplicate columns. Prefer the first alias
            // in the contract and reject only repeated instances of that same alias.
            foreach (string alias in aliases ?? new string[0])
            {
                int found = -1;
                for (int c = 0; c < header.LastCellNum; c++)
                {
                    if (!Equals(header.GetCell(c), alias)) continue;
                    if (found >= 0)
                        throw new InvalidDataException("工作表“" + sheetName + "”存在重复字段“"
                            + alias + "”（第 " + (found + 1) + "、" + (c + 1) + " 列）。");
                    found = c;
                }
                if (found >= 0) return found;
            }
            return -1;
        }

        private static string Normalize(string value)
            => new string((value ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).Trim();
    }
}

using System;

namespace UNCAD.Core.Fill
{
    public static class TableClearPolicy
    {
        public const int DefaultRows = 11;

        public static int ResolveRows(int availableRows, int configuredRows)
        {
            if (availableRows <= 0) return 0;
            return Math.Min(availableRows, Math.Max(1, configuredRows));
        }

        public static bool CanFit(int selectedRows, int rowsToClear)
            => selectedRows >= 0 && selectedRows <= rowsToClear;
    }
}

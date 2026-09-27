using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class CadTableFillWriterContractTests
    {
        [Fact]
        public void GeneratedRowsUseDirectNativeApiAndFixedClearRange()
        {
            string source = File.ReadAllText(PathOf("src", "UNCAD", "Features",
                "Fill", "CadTableFillWriter.cs"));
            Assert.DoesNotContain("SuppressRegenerateTable", source);
            Assert.DoesNotContain("RestoreTableDimensions", source);
            Assert.Contains("OpenMode.ForWrite, true", source);
            Assert.Contains("TableFillFormatter.GeneratedRowHeight", source);
            Assert.Contains("MinimumRowHeight", source);
            Assert.Contains("table.SetRowHeight(row,", source);
            Assert.Contains("table.SetAutoScale(row, column, false)", source);
            Assert.Contains("table.SetIsAutoScale(row, column, 0, false)", source);
            Assert.Contains("FixGeneratedRowHeights", source);
            Assert.Contains("table.RecordGraphicsModified(true)", source);
        }

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

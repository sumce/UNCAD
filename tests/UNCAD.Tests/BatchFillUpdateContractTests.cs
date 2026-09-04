using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class BatchFillUpdateContractTests
    {
        [Fact]
        public void BatchUpdate_ShowsAtMostOneCompareDialog()
        {
            string source = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            Assert.Equal(1, Count(source, "new FillUpdateCompareForm("));
        }

        private static string Read(params string[] parts)
            => File.ReadAllText(PathOf(parts));

        private static int Count(string value, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(token, offset,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }
            return count;
        }

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

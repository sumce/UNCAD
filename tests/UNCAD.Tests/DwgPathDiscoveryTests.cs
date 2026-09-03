using System;
using System.IO;
using System.Linq;
using UNCAD.Core.Dwg;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class DwgPathDiscoveryTests
    {
        [Fact]
        public void Find_RecursesDeduplicatesAndIgnoresOtherExtensions()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "uncad_dwg_discovery_" + Guid.NewGuid().ToString("N"));
            string child = Path.Combine(root, "child");
            Directory.CreateDirectory(child);
            string first = Path.Combine(root, "A.dwg");
            string second = Path.Combine(child, "B.DWG");
            File.WriteAllText(first, "a");
            File.WriteAllText(second, "b");
            File.WriteAllText(Path.Combine(child, "ignore.txt"), "x");
            try
            {
                DwgPathDiscoveryResult result = DwgPathDiscovery.Find(
                    new[] { root, first });

                Assert.Empty(result.Errors);
                Assert.Equal(2, result.Files.Count);
                Assert.Contains(result.Files, item => string.Equals(item, first,
                    StringComparison.OrdinalIgnoreCase));
                Assert.Contains(result.Files, item => string.Equals(item, second,
                    StringComparison.OrdinalIgnoreCase));
                Assert.Equal(result.Files.OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase), result.Files);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}

using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class SubmitFeatureContractTests
    {
        [Fact]
        public void ReadSelection_ClassifiesTableBeforeBlockReference()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "SubmitFeature.cs"));
            int table = source.IndexOf("if (entity is Table table)", StringComparison.Ordinal);
            int block = source.IndexOf("else if (entity is BlockReference block)",
                StringComparison.Ordinal);

            // Autodesk Table derives from BlockReference; order is a behavioral contract.
            Assert.True(table >= 0);
            Assert.True(block > table);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

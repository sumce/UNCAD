using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class AutomaticSubmissionServiceContractTests
    {
        [Fact]
        public void ReadSelection_ClassifiesTableBeforeBlockReference()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "AutomaticSubmissionService.cs"));
            int table = source.IndexOf("if (entity is Table table)", StringComparison.Ordinal);
            int block = source.IndexOf("else if (entity is BlockReference block)",
                StringComparison.Ordinal);

            // Autodesk Table derives from BlockReference; order is a behavioral contract.
            Assert.True(table >= 0);
            Assert.True(block > table);
        }

        [Fact]
        public void U1S_SubmitsCurrentFramesWithoutMachineWorkbookOrCadWrites()
        {
            string service = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "AutomaticSubmissionService.cs"));
            string fill = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));
            string batch = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdateCoordinator.cs"));
            string submit = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "SubmitFeature.cs"));

            Assert.DoesNotContain("CommandMethod", service);
            Assert.Contains("CommandMethod(CommandIds.Submit", submit);
            Assert.Contains("FrameRegionCollector.Collect", submit);
            Assert.Contains("CadSubmissionReader.Read", submit);
            Assert.Contains("AutomaticSubmissionService.Write", submit);
            Assert.Contains("record.Materials.Count", submit);
            Assert.DoesNotContain("FillWorkbookSnapshot", submit);
            Assert.DoesNotContain("StartTransaction", submit);
            Assert.Contains("AutomaticSubmissionService.Write", fill);
            Assert.Contains("AutomaticSubmissionService.Write", batch);
            Assert.Contains("throw new InvalidOperationException(\"CAD 已更新，但 BOQ", fill);
            Assert.Contains("throw new InvalidOperationException(\"CAD 已批量更新，但 BOQ", batch);
            Assert.Contains("string outputRoot = Path.GetFullPath(filePath);", service);
            Assert.DoesNotContain("Path.GetDirectoryName(Path.GetFullPath(filePath))", service);
            Assert.DoesNotContain("批量图框包含重复的机台/设备", service);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

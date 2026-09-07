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
            Assert.Contains("FrameIdentityReader.Read", submit);
            Assert.Contains("FrameIdentityReader.Read(ctx, current, ids,", service);
            Assert.Contains("AutomaticSubmissionService.Write", submit);
            Assert.Contains("record.Materials.Count", submit);
            Assert.DoesNotContain("FillWorkbookSnapshot", submit);
            Assert.DoesNotContain("StartTransaction", submit);
            Assert.Contains("Write(ctx, transaction", fill);
            Assert.Contains("Write(ctx, transaction", batch);
            Assert.Contains("BatchFillConfirmationForm", batch);
            Assert.DoesNotContain("MessageBox.Show(Owner(), summary", batch);
            Assert.DoesNotContain("after CAD commit", fill);
            Assert.DoesNotContain("after CAD commit", batch);
            Assert.Contains("string outputRoot = Path.GetFullPath(filePath);", service);
            Assert.DoesNotContain("Path.GetDirectoryName(Path.GetFullPath(filePath))", service);
            Assert.DoesNotContain("批量图框包含重复的机台/设备", service);

            int identity = batch.IndexOf("TryReadExistingIdentity", StringComparison.Ordinal);
            int tableCount = batch.IndexOf("if (!validTableShape)",
                StringComparison.Ordinal);
            Assert.True(identity >= 0 && tableCount > identity,
                "Batch U1U must identify the machine before reporting table-shape errors.");
        }

        [Fact]
        public void BatchU1U_RejectsUnmatchedRowsToKeepCadAndBoqAligned()
        {
            string service = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "AutomaticSubmissionService.cs"));
            string batch = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdateCoordinator.cs"));

            Assert.DoesNotContain("WriteBatchWithDefaults", service);
            Assert.DoesNotContain("AllowUnmatchedDefaults", batch);
            Assert.Contains("批量更新已取消", batch);
            Assert.Contains("AutomaticSubmissionService.Write(ctx, transaction", batch);
        }

        [Fact]
        public void FillWriters_AllowEmptyMaterialsToClearBoqColumns()
        {
            string service = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "AutomaticSubmissionService.cs"));

            // U1F/U1U must pass true for allowEmptyMaterials so a deliberately
            // empty CAD table clears the corresponding BOQ device column.
            Assert.Contains("sourceGroups, null, false, true);", service);
            Assert.Contains("sourceGroups, batch, false, true);", service);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class XLayoutFeatureContractTests
    {
        [Fact]
        public void Feature_UsesSharedFrameCollectionAndRunsBeforeSummary()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "XLayout", "XLayoutFeature.cs"));
            Assert.Contains("CommandMethod(CommandIds.XLayout", source);
            Assert.Contains("CollectForLayout", source);
            Assert.Contains("FrameIdentityReader.Read", source);
            Assert.Contains("XLayoutLayout.Arrange", source);
            Assert.Contains("transaction.Commit();", source);
            Assert.Contains("XLayoutSummaryForm", source);
            Assert.Contains("PromptPointOptions", source);
            Assert.Contains("AllowNone = true", source);
            Assert.Contains("layoutOrigin.X", source);
            Assert.Contains("layoutOrigin.Y", source);
            Assert.Contains("MachineLabelTextHeight = 25000d", source);
            Assert.Contains("MachineLabelLeftDistance = 300000d", source);
            Assert.Contains("placement.TranslationX + placement.Item.Boundary.MinX", source);
            Assert.Contains("AttachmentPoint.BaseLeft", source);
            Assert.Contains("EntityFactory.DBText", source);
            Assert.DoesNotContain("label.AdjustAlignment(ctx.Db)", source);
        }

        [Fact]
        public void ExportAndLayoutUseAnchorOwnershipForAllEntities()
        {
            string collector = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "FrameRegionCollector.cs"));
            Assert.Contains("CollectCore(ctx, selectedIds, false, true)", collector);
            Assert.Contains("CollectCore(ctx, selectedIds, true, true)", collector);
            Assert.Contains("if (useAnchorOwnership)", collector);
            Assert.Contains("TryAnchor(entity, out Point3d anchor, includeAllEntities)", collector);
            Assert.Contains("SelectAnchorOwner(owners, anchor)", collector);
            Assert.Contains("group.Boundary.Intersects(minX, minY, maxX, maxY)", collector);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

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
            // 3.0 WPF 窗体（D-038）：统计窗必须走新窗体，WinForms 版已删除。
            Assert.Contains("XLayoutSummaryWindow", source);
            Assert.DoesNotContain("XLayoutSummaryForm", source);
            // 3.0 大批量响应性（2026-11-02）：写入必须边写边刷 + 跳过已在位图框。
            Assert.Contains("QueueForGraphicsFlush", source);
            Assert.Contains("UpdateScreen", source);
            Assert.Contains("displacement.Length", source);
            Assert.Contains("PromptPointOptions", source);
            Assert.Contains("AllowNone = true", source);
            Assert.Contains("layoutOrigin.X", source);
            Assert.Contains("layoutOrigin.Y", source);
            // D-057：标签字高/离框距离与图框间距改为 U1SET「图框与排版」可配
            //（XLayoutRuntimeOptions 一次快照），默认值即旧常量 25000 / 300000。
            Assert.Contains("XLayoutSettings.Current()", source);
            Assert.Contains("layout.LabelTextHeight", source);
            Assert.Contains("layout.LabelLeftDistance", source);
            Assert.Contains("layout.Spacing", source);
            string options = File.ReadAllText(RepoFile("src", "UNCAD", "Core", "Dwg",
                "XLayoutRuntimeOptions.cs"));
            Assert.Contains("DefaultLabelTextHeight = 25000d", options);
            Assert.Contains("DefaultLabelLeftDistance = 300000d", options);
            Assert.Contains("placement.TranslationX + placement.Item.Boundary.MinX", source);
            Assert.Contains("AttachmentPoint.BaseLeft", source);
            Assert.Contains("EntityFactory.DBText", source);
            Assert.DoesNotContain("label.AdjustAlignment(ctx.Db)", source);
        }

        [Fact]
        public void Feature_ReportsFailuresInUiAndKeepsCommandLineForCadPromptsOnly()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "XLayout", "XLayoutFeature.cs"));

            Assert.Contains("ReportFailure(ctx", source);
            Assert.Contains("ctx.Operation?.Fail", source);
            Assert.Contains("ctx.Operation?.Cancel", source);
            Assert.Contains("XLayoutFailureWindow", source);
            Assert.Contains("Log.Error(\"[XLAYOUT]", source);
            Assert.DoesNotContain("ctx.Write(", source);
        }

        [Fact]
        public void IdentityConflictDiagnosticsIncludeDeviceBlockProvenance()
        {
            string collector = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillSelectionCollector.cs"));

            Assert.Contains("DEVICENAME 候选块诊断", collector);
            Assert.Contains("block.Handle", collector);
            Assert.Contains("block.Position", collector);
            Assert.Contains("AttributeReference", collector);
            Assert.Contains("AttributeDefinition 默认值", collector);
            Assert.Contains("attributePosition", collector);
        }

        [Fact]
        public void LayoutIdentityComesFromFrameAttributesOnly()
        {
            // D-045 修订（2026-11-22，XLAYOUT 现场报告「全图报 DEVICENAME 冲突、
            // 单机正常」）：布局身份只读图框自身属性，设备块 DEVICENAME 不参与，
            // 「图框属性与设备块不一致」在布局路径结构性不可能发生。
            string feature = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "XLayout", "XLayoutFeature.cs"));
            string reader = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Submit", "FrameIdentityReader.cs"));
            string collector = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillSelectionCollector.cs"));

            Assert.Contains("FrameIdentityReader.ReadLayoutIdentity(readTransaction", feature);
            Assert.DoesNotContain("FrameIdentityReader.ReadIdentity(", feature);
            Assert.Contains("internal static ExistingFillIdentity ReadLayoutIdentity", reader);
            Assert.Contains("out _, false)", reader);
            Assert.Contains("bool includeDeviceBlocks", collector);
            Assert.Contains("if (includeDeviceBlocks)", collector);
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

        [Fact]
        public void LayoutFallsBackToInsertPointForDisplacedBlockGeometry()
        {
            string collector = File.ReadAllText(RepoFile("src", "UNCAD", "Cad",
                "FrameRegionCollector.cs"));
            Assert.Contains("entity is BlockReference block && !(entity is Table)", collector);
            Assert.Contains("TryAssignAtAnchor(boundaryIndex, block.Position, id)", collector);
            Assert.Contains("anchor = block.Position", collector);
            Assert.Contains("FindAnchorOwner(boundaryIndex, anchor", collector);
            Assert.Contains("TryAssignByBounds(boundaryIndex, entity, id, includeAllEntities)",
                collector);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

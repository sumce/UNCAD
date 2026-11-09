using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-050（2026-11-09）连续趟合并的接线契约：U1Q/U1C 选集先经
    /// ParallelRunPlanner 分趟、偏移走 OffsetPolylineBuilder（解析式 miter，
    /// 根治"红线与基线交叉"）、U1Q 长度用 U1L 标签推算、U1C 拆出独立配置键、
    /// 迁移角度修复感知 RUN 成员。行为细节由 OffsetPolylineBuilderTests /
    /// ParallelRunPlannerTests / UNCAD_U1Q_SELFTEST 锁定。
    /// </summary>
    public sealed class ParallelRunContractTests
    {
        [Fact]
        public void UnqFeature_GeneratesRuns_WithU1LDerivedLength()
        {
            string source = Read("src", "UNCAD", "Features", "Unq", "UnqFeature.cs");
            Assert.Contains("ParallelCurveAnnotator.AddRuns(", source);
            // 长度初值来自推算（AddRuns 已填 EstimatedLengthMm），固定 2500 只是兜底。
            Assert.Contains("run.EstimatedLengthMm", source);
            Assert.Contains("趟。", source);
        }

        [Fact]
        public void ConduitFeature_GeneratesRuns_KeepsPlaceholderLength()
        {
            string source = Read("src", "UNCAD", "Features", "Conduit",
                "ConduitFeature.cs");
            Assert.Contains("ParallelCurveAnnotator.AddRuns(", source);
            // D-050 只给 U1Q 接 U1L 推算；U1C 长度仍是 2000mm 人工占位（业务约定）。
            Assert.Contains("run => label", source);
            Assert.Contains("趟 ⌀", source);
        }

        [Fact]
        public void Annotator_AddRuns_WiresPlannerAndAnalyticOffset()
        {
            string source = Read("src", "UNCAD", "Cad",
                "ParallelCurveAnnotator.cs");
            int plan = source.IndexOf("ParallelRunPlanner.Plan(", StringComparison.Ordinal);
            int offset = source.IndexOf("OffsetPolylineBuilder.TryBuild(",
                StringComparison.Ordinal);
            int label = source.IndexOf("runLabelFactory(run)", StringComparison.Ordinal);
            Assert.True(plan >= 0 && offset > plan && label > offset,
                "AddRuns 必须：分趟 → 解析式偏移 → 生成标注（顺序接线）");
            // 整趟替换失败保护：一趟都没生成不得删旧输出。
            Assert.Contains("if (created > 0) ErasePreviousEntities", source);
            // RUN 元数据写入（SOURCE=首成员 + RUN=全部成员）。
            Assert.Contains("ParallelAnnotationMetadata.Set(curve, options.AnnotationKind,",
                source);
            Assert.Contains("firstHandle, runHandles", source);
        }

        [Fact]
        public void RunMetadata_AndRotationHelpers_Exist()
        {
            string metadata = Read("src", "UNCAD", "Cad",
                "ParallelAnnotationMetadata.cs");
            Assert.Contains("private const string RunKey = \"RUN\"", metadata);
            Assert.Contains("public static List<string> TryReadRunHandles(", metadata);
            Assert.Contains("public static bool TryReadAnnotationRotation(", metadata);
            Assert.Contains("public static bool MatchesAnyRunSegmentTangent(", metadata);

            string migration = Read("src", "UNCAD", "Features", "Fill",
                "BridgeLabelMigrationWriter.cs");
            Assert.Contains("TryReadAnnotationRotation(", migration);
            Assert.Contains("MatchesAnyRunSegmentTangent(", migration);
        }

        [Fact]
        public void LengthEstimator_PrefersU1LLabelAndRoundsTo10()
        {
            string source = Read("src", "UNCAD", "Cad", "ParallelRunGeometry.cs");
            Assert.Contains("u1LMillimetres.TryGetValue(member.Handle", source);
            Assert.Contains("member.LengthMm", source); // 无标签段的几何兜底
            Assert.Contains("LengthRoundingMm", source);
            Assert.Contains("QuickLineMetadataStore.ReadAssociatedLineHandle", source);
        }

        private static string Read(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
        }
    }
}

using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 防上帝类回潮（D-032/D-037 逻辑清晰线）：U1F/U1U 的阶段逻辑必须住在
    /// FillStatisticsStage / FillWorkbookStage / FillMachinePickStage / FillCoverageGate /
    /// FillPlanningStage / FillReviewStage / FillRuanguanStage / CableReserveReview
    /// 等服务里，FillFeature 只做编排和事务边界。一旦行数回升，说明阶段实现
    /// 又被塞回了编排类。
    /// </summary>
    public sealed class FillFeatureSizeGuardTests
    {
        private const int MaxFillFeatureLines = 800;

        [Fact]
        public void FillFeature_StaysASlimOrchestrator()
        {
            string[] lines = File.ReadAllLines(RepoFile("src", "UNCAD", "Features",
                "Fill", "FillFeature.cs"));
            string source = string.Join("\n", lines);

            Assert.True(lines.Length <= MaxFillFeatureLines,
                "FillFeature.cs 当前 " + lines.Length + " 行，超过编排类上限 "
                + MaxFillFeatureLines + " 行；新的阶段逻辑应拆到 FillStatisticsStage/"
                + "FillWorkbookStage/FillMachinePickStage/FillCoverageGate/"
                + "FillPlanningStage/FillReviewStage/FillRuanguanStage 或 Core 的服务里。");
            // 编排类必须保留官方阶段注释（docs/DEVELOPMENT.md 的七阶段划分）。
            for (int stage = 1; stage <= 7; stage++)
                Assert.Contains("阶段" + stage + "：", source);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

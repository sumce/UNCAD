using System;
using System.IO;
using System.Linq;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 2026-11-05 用户报告"自动转换设备预留和米数统计冲突"+"U1F 改变桥架标注
    /// 多行文本角度"。两类修复的回归契约：
    ///
    /// ① D-043 预留标记与电缆长度判据天然重叠（横排 "3000mm" 既满足
    ///    TryParseMillimeters 又满足 ExtractCableLength）——吸收的标记文本必须
    ///    从米数统计排除，否则同一笔毫米数双算（实测一次、设备内预留又一次）。
    ///    行为接线：json/标记决策先于统计；FillStatisticsModule 支持排除集；
    ///    单框与批量都传吸收文本。
    /// ② 桥架标注角度修复（RepairMigratedRotation）必须先确认标注角度与绑定
    ///    曲线任何一段都不一致才动 Rotation——多段桥架拐角附近最近点会落到
    ///    另一段上，把写对了的标注掰歪。
    /// 行为细节由 DeviceReserveMarkerTests / StatCalculatorTests 覆盖；
    /// 这里锁定碰撞类别的存在与接线顺序。
    /// </summary>
    public sealed class ReserveMarkerStatisticsContractTests
    {
        [Fact]
        public void MarkerTextPattern_ReallyCollides_WithCableLengthPattern()
        {
            // 碰撞类别存在性证明："数字+mm（无空格）"两边都命中——这就是必须
            // 排除的原因（电缆判据 ^数字0mm$ 不容空白；带空格的 "1200 mm" 只
            // 是标记候选，不进电缆统计，无冲突）。
            foreach (string marker in new[] { "3000mm", "500mm" })
            {
                Assert.True(DeviceReserveMarker.TryParseMillimeters(marker, out _),
                    marker + " 应是 D-043 预留标记候选");
                Assert.True(TextParser.ExtractCableLength(marker).HasValue,
                    marker + " 同时会被米数统计当电缆长度——不排除就双算");
            }
            // 纯数字（无 mm）是标记候选但不进电缆统计——无冲突。
            Assert.True(DeviceReserveMarker.TryParseMillimeters("3000", out _));
            Assert.False(TextParser.ExtractCableLength("3000").HasValue);
            Assert.True(DeviceReserveMarker.TryParseMillimeters("1200 mm", out _));
            Assert.False(TextParser.ExtractCableLength("1200 mm").HasValue);
        }

        [Fact]
        public void StatisticsPipeline_SupportsTextExclusion()
        {
            // 统计在文本集层面排除：被吸收的标记不再进入 CableSum。
            var result = StatCalculator.Calculate(new[] { "2000mm" }, 250.0);
            Assert.Equal(2.0, result.CableSum, 6);
            // 排除由调用方在文本集完成（FillStatisticsModule 过滤 textIds）；
            // 这里锁定排除后的等价结果为空。
            var excluded = StatCalculator.Calculate(Array.Empty<string>(), 250.0);
            Assert.Equal(0.0, excluded.CableSum, 6);
        }

        [Fact]
        public void MarkerDecision_RunsBeforeStatistics_InBothFlows()
        {
            // 单框：决策在 FillReserveStage（json/标记恢复阶段），编排类先跑
            // 预留阶段再跑统计阶段，并把排除集传给统计。
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            int reserve = fill.IndexOf("FillReserveStage.Resolve(ctx",
                StringComparison.Ordinal);
            int statistics = fill.IndexOf("FillStatisticsStage.Execute(",
                StringComparison.Ordinal);
            Assert.True(reserve >= 0 && statistics > reserve,
                "单框：预留阶段（含标记决策）必须先于统计（吸收文本要从统计排除）");
            Assert.Contains("markerExcludedIds", fill);

            string stage = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            Assert.Contains("DeviceReserveMarker.Decide(", stage);
            Assert.Contains("markerExcludedIds.AddRange(reserveMarkers.EntityIds)", stage);
            Assert.Contains("不再计入米数统计", stage);

            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            int batchDecide = batch.IndexOf("DeviceReserveMarker.Decide(",
                StringComparison.Ordinal);
            int batchStatistics = batch.IndexOf("FillStatisticsModule.Execute(ctx, readTransaction",
                StringComparison.Ordinal);
            Assert.True(batchDecide >= 0 && batchStatistics > batchDecide,
                "批量：预留标记决策必须先于统计");
            Assert.Contains("markerExcludedIds", batch);
        }

        [Fact]
        public void StatisticsModule_HasExclusionParameter()
        {
            string modules = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillModules.cs"));
            Assert.Contains("IReadOnlyCollection<ObjectId> excludeTextIds = null", modules);
            Assert.Contains("textIds.Where(id => !excluded.Contains(id)).ToArray()", modules);
            Assert.Contains("D-043", modules);
        }

        [Fact]
        public void BridgeRotationRepair_RespectsExistingSegmentAlignment()
        {
            string writer = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BridgeLabelMigrationWriter.cs"));
            int guard = writer.IndexOf(
                "ParallelAnnotationMetadata.MatchesAnySegmentTangent(",
                StringComparison.Ordinal);
            int rotate = writer.IndexOf("text.Rotation = rotation;",
                StringComparison.Ordinal);
            Assert.True(guard >= 0, "角度修复必须先做任意段一致性检查");
            Assert.True(rotate > guard, "一致性检查必须在写 Rotation 之前");

            string metadata = File.ReadAllText(Repo("src", "UNCAD", "Cad",
                "ParallelAnnotationMetadata.cs"));
            Assert.Contains("public static bool MatchesAnySegmentTangent(", metadata);
            // 多段曲线必须逐段比对（拐角处最近点落错段是本 bug 的根因）。
            Assert.Contains("GetLineSegmentAt(index)", metadata);
            Assert.Contains("ReadableAngle(segment.StartPoint,", metadata);
        }

        [Fact]
        public void CopiedFrame_CableInfoRecovery_IsGuardedByForeignJsonCheck()
        {
            // 2026-11-06 用户报告：复制图框 U1F 自动带出 3000 设备预留。JSON 被
            // 句柄校验拒绝后，CABLE_INFO 恢复兜底又从源框残留文本里把「数值
            // （设备内）」救活——换条路继承。两个恢复点都必须以外来 JSON 检测
            // 为守卫：本框元数据是复制来的 → CABLE_INFO 同源不可信，一并跳过。
            string stage = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            int guard = stage.IndexOf("foreignJsonNote.Length == 0", StringComparison.Ordinal);
            int recovery = stage.IndexOf("TryParseDeviceReserveMillimeters(", StringComparison.Ordinal);
            Assert.True(guard >= 0 && recovery > guard,
                "单框恢复必须在句柄守卫之内（外来 JSON 拒绝时不得恢复 CABLE_INFO 预留）");
            Assert.Contains("为避免继承源机台的设备内预留", stage);

            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            int batchGuard = batch.IndexOf("foreignJsonNote.Length == 0", StringComparison.Ordinal);
            int batchRecovery = batch.IndexOf("TryParseDeviceReserveMillimeters(", StringComparison.Ordinal);
            Assert.True(batchGuard >= 0 && batchRecovery > batchGuard,
                "批量恢复同样必须带句柄守卫");
            Assert.Contains("CABLE_INFO 同源同样不可信", batch);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}

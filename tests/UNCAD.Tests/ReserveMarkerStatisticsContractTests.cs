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

            // 2026-11-09：批量预检（含标记决策与统计调用）拆到
            // BatchFillUpdatePreflight.Build；写事务侧仍在协调器。
            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs"));
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
            // 2026-11-09（D-050）：护栏升级为整趟版——与任意成员的任意一段一致
            // 即不动；单段标注行为等价（无 RUN 时退回单源检查）。
            int guard = writer.IndexOf(
                "ParallelAnnotationMetadata.MatchesAnyRunSegmentTangent(",
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

            // 2026-11-09：批量恢复守卫随 Preflight 拆到 BatchFillUpdatePreflight。
            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs"));
            int batchGuard = batch.IndexOf("foreignJsonNote.Length == 0", StringComparison.Ordinal);
            int batchRecovery = batch.IndexOf("TryParseDeviceReserveMillimeters(", StringComparison.Ordinal);
            Assert.True(batchGuard >= 0 && batchRecovery > batchGuard,
                "批量恢复同样必须带句柄守卫");
            Assert.Contains("CABLE_INFO 同源同样不可信", batch);
        }

        [Fact]
        public void CopiedFrames_AreClearedBeforeRedefinitionWrite()
        {
            // 2026-11-06 用户要求：U1F/U1U 定义复制来的图框时要把源框信息
            // 清理干净——不只是"不继承"，写事务里要显式清空 CABLE_INFO/
            // CONDUIT_INFO/FRAMEINFO 旧属性再写新机台数据（同一事务，
            // 整体提交或回滚，不存在半清空状态）。
            string writer = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "CadBlockAttributeWriter.cs"));
            Assert.Contains("internal static int ClearStaleInfo(", writer);
            Assert.Contains("FrameBlockFiller.TagCable", writer);
            Assert.Contains("FrameBlockFiller.TagConduit", writer);
            Assert.Contains("FrameInfoJsonBlockWriter.IsJsonTagName(tag)", writer);

            string stage = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            Assert.Contains("copiedFrameDetected", stage);

            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            int clear = fill.IndexOf("CadBlockAttributeWriter.ClearStaleInfo(",
                StringComparison.Ordinal);
            int migrate = fill.IndexOf("FrameInfoJsonBlockWriter.FillOrMigrate(",
                StringComparison.Ordinal);
            Assert.True(clear >= 0 && migrate > clear,
                "单框写事务必须先清空旧信息再写新 JSON");
            // 3.0.0.14：清空还必须先于 FillFrame——旧顺序先写后清，复制框刚写入的
            // CABLE_INFO/CONDUIT_INFO 会被清理步骤抹空（用户报告：左下角电缆信息
            // 直接不出现）。
            int frameWrite = fill.IndexOf("CadBlockAttributeWriter.FillFrame(",
                StringComparison.Ordinal);
            Assert.True(clear >= 0 && frameWrite > clear,
                "单框写事务必须先清空旧信息再写 CABLE_INFO/CONDUIT_INFO");

            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            int batchClear = batch.IndexOf("CadBlockAttributeWriter.ClearStaleInfo(",
                StringComparison.Ordinal);
            int batchMigrate = batch.IndexOf("FrameInfoJsonBlockWriter.FillOrMigrate(",
                StringComparison.Ordinal);
            Assert.True(batchClear >= 0 && batchMigrate > batchClear,
                "批量写事务必须先清空旧信息再写新 JSON");
            int batchFrameWrite = batch.IndexOf("CadBlockAttributeWriter.FillFrame(",
                StringComparison.Ordinal);
            Assert.True(batchClear >= 0 && batchFrameWrite > batchClear,
                "批量写事务必须先清空旧信息再写 CABLE_INFO/CONDUIT_INFO");
            // 复制框检测在预检阶段（BatchFillUpdatePreflight）置位并随 Plan 传递。
            string batchPreflight = File.ReadAllText(Repo("src", "UNCAD", "Features",
                "Fill", "BatchFillUpdatePreflight.cs"));
            Assert.Contains("plan_CopiedFrame = true", batchPreflight);
            Assert.Contains("CopiedFrame = plan_CopiedFrame", batchPreflight);
        }

        [Fact]
        public void CopiedFrame_MarkerAbsorption_IsGuardedByForeignJsonCheck()
        {
            // 3.0.0.16（U1F/U1U 审计）：复制框携带源框的横排毫米标（"3000"）时
            // D-043 自动吸收没有外来守卫——源机台的设备内预留从毫米标这条暗道
            // 静默继承回来（批量路径连确认窗都不弹），还把图上文字删了。
            // 修复：外来 JSON 时只从统计排除（"3000mm"不排除会当电缆长度双算），
            // 不吸收、不删除、点名提示。
            string stage = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            int guard = stage.IndexOf(
                "reserveMarkers.Hits.Count == 1 && foreignJsonNote.Length == 0",
                StringComparison.Ordinal);
            int decide = stage.IndexOf("DeviceReserveMarker.Decide(",
                StringComparison.Ordinal);
            Assert.True(guard >= 0 && decide > guard,
                "单图：唯一毫米标的自动吸收必须在「非外来 JSON」守卫之内");
            Assert.Contains("未自动吸收为设备内预留", stage);
            Assert.Contains("不计入米数统计", stage);

            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs"));
            int batchGuard = batch.IndexOf(
                "reserveMarkers.Hits.Count == 1 && foreignJsonNote.Length == 0",
                StringComparison.Ordinal);
            int batchDecide = batch.IndexOf("DeviceReserveMarker.Decide(",
                StringComparison.Ordinal);
            Assert.True(batchGuard >= 0 && batchDecide > batchGuard,
                "批量：唯一毫米标的自动吸收同样必须在「非外来 JSON」守卫之内");
            Assert.Contains("未自动吸收（源框残留", batch);
        }

        [Fact]
        public void CopiedFrame_FillFrameNeverPreservesJustClearedValues()
        {
            // 3.0.0.16：复制框 ClearStaleInfo 之后 FillFrame 不得再走「保留旧值」
            // ——旧值刚被清成空串，“保留”保住的是空（CABLE_INFO 停留空，与 U1F
            // 写型号行的结果不一致）。复制框一律按 U1F 口径现算现写。
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            int clear = fill.IndexOf("CadBlockAttributeWriter.ClearStaleInfo(",
                StringComparison.Ordinal);
            int frameWrite = fill.IndexOf("CadBlockAttributeWriter.FillFrame(",
                StringComparison.Ordinal);
            int preserveGuard = fill.IndexOf("!copiedFrameDetected",
                StringComparison.Ordinal);
            Assert.True(clear >= 0 && frameWrite > clear && preserveGuard > clear,
                "单图：清空之后的 FillFrame 传参必须带 !copiedFrameDetected 守卫");

            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            int batchClear = batch.IndexOf("CadBlockAttributeWriter.ClearStaleInfo(",
                StringComparison.Ordinal);
            int batchFrameWrite = batch.IndexOf("CadBlockAttributeWriter.FillFrame(",
                StringComparison.Ordinal);
            int batchGuard = batch.IndexOf("!plan.CopiedFrame",
                StringComparison.Ordinal);
            Assert.True(batchClear >= 0 && batchFrameWrite > batchClear
                && batchGuard > batchClear,
                "批量：清空之后的 FillFrame 传参必须带 !plan.CopiedFrame 守卫");
        }

        [Fact]
        public void BatchU1u_EmptyBoqPlans_SkipsBoqStageEntirely()
        {
            // 3.0.0.16：全部机台缺回路且无一强制生成时 boqPlans 为空——D-044 的
            // 正常结果（图纸更新、BOQ 跳过）。旧代码照样调 Write，空记录抛
            // “没有可写入 Excel 的图框记录”，弹“BOQ 输出失败请重跑 U1S”假错误，
            // 而 U1S 的回路硬门禁又把用户拦住（死胡同）。必须整段跳过。
            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            int skip = batch.IndexOf("if (boqPlans.Count == 0)",
                StringComparison.Ordinal);
            int boqWrite = batch.IndexOf("AutomaticSubmissionService.Write(",
                StringComparison.Ordinal);
            Assert.True(skip >= 0 && boqWrite > skip,
                "批量：boqPlans 为空必须在 BOQ 写入之前整段跳过");
        }

        [Fact]
        public void BatchU1u_PreflightErrorGate_PrecedesReserveWindow()
        {
            // 3.0.0.16：预检错误门先于一切询问窗——旧顺序让用户先白填设备内
            // 预留窗、再被告知“预检失败整批取消”。
            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            int errorGate = batch.IndexOf("批量更新预检失败，图纸未修改",
                StringComparison.Ordinal);
            int reserveWindow = batch.IndexOf("new DeviceReserveWindow(",
                StringComparison.Ordinal);
            Assert.True(errorGate >= 0 && reserveWindow > errorGate,
                "批量：预检错误弹窗必须排在设备内预留填写窗之前");
        }

        [Fact]
        public void MachineRedefineGuard_RunsAfterPick_BeforeReserveResolution()
        {
            // 3.0.0.17（现场报告“U1F 会计算复制过来之前的预留米数”）：机台身份
            // 守卫必须在机台选定之后（U1F 才知道选的是不是原机台）、电缆预留
            // 解析之前（旧记录/建议值不得流进清单与公式）。
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            int pick = fill.IndexOf("FillMachinePickStage.PickNewMachine(",
                StringComparison.Ordinal);
            int guard = fill.IndexOf("FrameRedefineGuard.Apply(",
                StringComparison.Ordinal);
            int reserve = fill.IndexOf("ResolveCableReserve(",
                StringComparison.Ordinal);
            Assert.True(pick >= 0, "U1F 选机台入口必须存在");
            Assert.True(guard > pick, "身份守卫必须在选机台之后（否则不知道选了谁）");
            Assert.True(reserve > guard, "身份守卫必须在电缆预留解析之前");
            // 物理身份由预留阶段带出（复制框上=源机台身份），守卫拿它判原属。
            string stage = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            Assert.Contains("out ExistingFillIdentity frameIdentity", stage);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}

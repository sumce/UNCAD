using System;
using System.IO;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.0（D-054 特性①②④）：法定图层规范与接线契约——三图层名/色号冻结、
    /// KIND 区分桥架/线管（未知不猜）、计数文案口径、U1L 新线落 U_CABLE、
    /// 矫正进既有写事务、开关键名、扫描器接受开放 Polyline。
    /// </summary>
    public sealed class LayerGovernorContractTests
    {
        [Fact]
        public void GovernedLayerNamesAndColors_AreFrozenVerbatim()
        {
            // 用户拍板，一字不差全大写、无空格。3.3.3（D-054 Amendment）：
            // U_HOSE=软管（U1B 输出，色号 3 绿）入法定家族。
            Assert.Equal("U_CABLE", UncadLayerStandard.CableLayerName);
            Assert.Equal("U_TRAY", UncadLayerStandard.TrayLayerName);
            Assert.Equal("U_CONDUIT", UncadLayerStandard.ConduitLayerName);
            Assert.Equal("U_HOSE", UncadLayerStandard.HoseLayerName);
            Assert.Equal((short)7, UncadLayerStandard.CableColorIndex);
            Assert.Equal((short)1, UncadLayerStandard.TrayColorIndex);
            Assert.Equal((short)4, UncadLayerStandard.ConduitColorIndex);
            Assert.Equal((short)3, UncadLayerStandard.HoseColorIndex);
            Assert.Equal(new[] { "U_CABLE", "U_TRAY", "U_CONDUIT", "U_HOSE" },
                UncadLayerStandard.GovernedLayerNames.ToArray());
        }

        [Fact]
        public void AnnotationKind_ResolvesLayerAndNeverGuesses()
        {
            // KIND 自 D-018/D-050 元数据引入起恒写入：U1Q→桥架层、U1C→线管层、
            // U1B→软管层（3.3.3）。
            Assert.Equal(UncadLayerStandard.TrayLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("U1Q"));
            Assert.Equal(UncadLayerStandard.TrayLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("u1q"));
            Assert.Equal(UncadLayerStandard.ConduitLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("U1C"));
            Assert.Equal(UncadLayerStandard.HoseLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("U1B"));
            Assert.Equal(UncadLayerStandard.HoseLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("u1b"));
            // 其它种类（未来/未知）不猜：null = 不移动、点名「无法判定」。
            Assert.Null(UncadLayerStandard.TryResolveAnnotationLayer("U1X"));
            Assert.Null(UncadLayerStandard.TryResolveAnnotationLayer(""));
            Assert.Null(UncadLayerStandard.TryResolveAnnotationLayer(null));

            Assert.Equal((short)1,
                UncadLayerStandard.ColorIndexOf(UncadLayerStandard.TrayLayerName));
            Assert.Equal((short)3,
                UncadLayerStandard.ColorIndexOf(UncadLayerStandard.HoseLayerName));
            Assert.Throws<ArgumentException>(() =>
                UncadLayerStandard.ColorIndexOf("0"));
        }

        [Fact]
        public void CorrectionCounters_DescribeFollowsTheAgreedFormat()
        {
            var counters = new UncadLayerCorrectionCounters
            {
                CableEntities = 3,
                TrayEntities = 2,
                ConduitEntities = 1,
                HoseEntities = 4
            };
            Assert.Equal(10, counters.TotalMoves);
            Assert.Equal("图层矫正：10 个实体移正（电缆 3/桥架 2/线管 1/软管 4/无法判定 0）",
                counters.Describe());
            Assert.True(counters.IsEmpty == false);

            var undeterminable = new UncadLayerCorrectionCounters
            {
                UndeterminableEntities = 1
            };
            undeterminable.UndeterminableHandles.Add("2A5F");
            Assert.Equal("图层矫正：0 个实体移正（电缆 0/桥架 0/线管 0/软管 0/无法判定 1）；"
                + "无法判定：2A5F", undeterminable.Describe());
            Assert.False(undeterminable.IsEmpty);
            Assert.True(new UncadLayerCorrectionCounters().IsEmpty);
        }

        [Fact]
        public void ConfigKey_FollowsTheFillFamilyNaming()
        {
            Assert.Equal("UNC_FILL_LAYER_GOVERNOR", ConfigKeys.FillLayerGovernor);
        }

        [Fact]
        public void U1L_NewLinesAndLabels_LandOnCableLayer()
        {
            string unl = Source("src", "UNCAD", "Features", "Unl", "UnlFeature.cs");
            Assert.Contains("LayerGovernorService.EnsureLayer", unl);
            Assert.Contains("UncadLayerStandard.CableLayerName", unl);
            Assert.Contains("line.LayerId = cableLayerId", unl);
            Assert.Contains("t.LayerId = cableLayerId", unl);

            // U1LX 新生成的毫米标同层（TryWriteSegmentMillimetre 新建路径）。
            string service = Source("src", "UNCAD", "Cad", "QuickLine",
                "QuickLineCadService.cs");
            Assert.Contains("label.LayerId = cableLayerId", service);
        }

        [Fact]
        public void FillCommands_ApplyCorrectionInsideTheExistingWriteTransaction()
        {
            string fill = Source("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            int transactionStart = fill.IndexOf(
                "ctx.Db.TransactionManager.StartTransaction()", StringComparison.Ordinal);
            int apply = fill.IndexOf("LayerGovernorService.Apply", StringComparison.Ordinal);
            int commit = fill.IndexOf("transaction.Commit()", StringComparison.Ordinal);
            Assert.True(transactionStart >= 0 && apply > transactionStart
                && commit > apply,
                "图层矫正必须落在既有写事务内（一次 U 连同填充一起回退）。");
            // 编排只调阶段类；确认窗接收体检报告。D-058：U1U 的 3D 预览在阶段类
            // 里构建，编排器不碰场景（U1F 不建）。
            Assert.Contains("FillRouteInspectionStage.InspectSelectionAndReport", fill);
            Assert.Contains("out FillReviewData review, routeInspection", fill);
            Assert.Contains("U1F 不建", fill);

            // 阶段类承载报告口径：连通性报告级（不拦截）+ 图层矫正文案。
            string stage = Source("src", "UNCAD", "Features", "Fill",
                "FillRouteInspectionStage.cs");
            Assert.Contains("连通性：", stage);
            Assert.Contains("报告级检查，不拦截写入", stage);
            Assert.Contains("LayerCorrectionText", stage);
            Assert.Contains("ConfigKeys.FillLayerGovernor", stage);
            Assert.Contains("LayerGovernorService.Plan", stage);
            Assert.Contains("ParallelAnnotationOwnership.Collect", stage);
            // D-058：只有 U1U（updateMode）挂 3D 预览；挂预览包在 try/catch 里，
            // 场景构建失败只降级成诊断，绝不拦截写入。
            Assert.Contains("if (updateMode)", stage);
            Assert.Contains("AttachPreview(ctx, readTransaction, inspection, selection, picked)",
                stage);
            Assert.Contains("inspection.Preview = BuildPreview(", stage);
            Assert.Contains("3D 预览场景构建失败", stage);
            int attach = stage.IndexOf("internal static void AttachPreview", StringComparison.Ordinal);
            int guard = stage.IndexOf("catch (System.Exception ex)", attach, StringComparison.Ordinal);
            Assert.True(attach >= 0 && guard > attach,
                "AttachPreview 必须 try/catch 降级（预览是展示件，不能炸 U1U 主流程）");

            string batch = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            Assert.Contains("LayerGovernorService.Apply", batch);
            Assert.Contains("FillRouteInspectionStage.Scan(ctx, drawingStyle,", batch);
            Assert.Contains("FillRouteInspectionStage.CorrectDrawingSettings",
                fill);
            Assert.Contains("FillRouteInspectionStage.CorrectDrawingSettings",
                batch);
            Assert.Contains("BatchFillConfirmationWindow(confirmationRows",
                batch);
            string preflight = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs");
            Assert.Contains("FillRouteInspectionStage.Inspect", preflight);
            Assert.Contains("ConnectivityText", preflight);
        }

        [Fact]
        public void Scanner_AcceptsOpenPolylinesAsFlaggedSegments()
        {
            string service = Source("src", "UNCAD", "Cad", "QuickLine",
                "QuickLineCadService.cs");
            // 3.3.0（D-054）：开放 LWPOLYLINE 作为一个段返回；闭合不是电缆路由。
            Assert.Contains("PolylineClass", service);
            Assert.Contains("if (!polyline.Closed", service);
            Assert.Contains("GetBulgeAt", service);
            Assert.Contains("IsBridgePolyline", service);
            // Xauto 把标记带给 Planner（含开洞 Polyline 的分量整分量跳过）。
            string xauto = Source("src", "UNCAD", "Features", "XAuto",
                "XAutoFeature.cs");
            Assert.Contains("item.IsPolyline, item.IsBridgePolyline", xauto);
        }

        [Fact]
        public void JoinedU1lLabels_RequireStaleUniqueAssociationAndReachCorrections()
        {
            string service = Source("src", "UNCAD", "Cad", "QuickLine",
                "QuickLineCadService.cs");
            Assert.Contains("AssociateStaleLabelsWithJoinedPolylines", service);
            Assert.Contains("item.IsPolyline && !item.IsBridgePolyline", service);
            Assert.Contains("linesByHandle.ContainsKey(label.AssociatedLineHandle)",
                service);
            Assert.Contains("FindDeletedAssociationLabels", service);
            Assert.Contains("options.StaleAssociationMaximumDistance.HasValue",
                service);
            Assert.Contains("database.GetObjectId(false, new Handle(raw), 0)",
                service);
            Assert.Contains("QuickLineStaleLabelMatcher.FindUniqueNearestPath",
                service);
            string stage = Source("src", "UNCAD", "Features", "Fill",
                "FillRouteInspectionStage.cs");
            Assert.Contains("StaleAssociationMaximumDistance", stage);
            Assert.Contains("styleSettings.StaleLabelAssociationMaximumDistance",
                stage);

            string layerGovernor = Source("src", "UNCAD", "Cad",
                "LayerGovernorService.cs");
            string styleCorrector = Source("src", "UNCAD", "Cad",
                "FillDrawingSettingsCorrector.cs");
            Assert.Contains("segment.RestoredLabelIds", layerGovernor);
            Assert.Contains("segment.RestoredLabelIds", styleCorrector);
            Assert.Contains("BuildStyleStamp", styleCorrector);
            Assert.Contains("QuickLineStaleLabelMatcher", service);
        }

        private static string Source(params string[] parts)
        {
            string root = RepoRoot();
            return File.ReadAllText(Path.Combine(root,
                Path.Combine(parts)));
        }

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
    }
}

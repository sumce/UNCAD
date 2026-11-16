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
            // 用户拍板，一字不差全大写、无空格。
            Assert.Equal("U_CABLE", UncadLayerStandard.CableLayerName);
            Assert.Equal("U_TRAY", UncadLayerStandard.TrayLayerName);
            Assert.Equal("U_CONDUIT", UncadLayerStandard.ConduitLayerName);
            Assert.Equal((short)7, UncadLayerStandard.CableColorIndex);
            Assert.Equal((short)1, UncadLayerStandard.TrayColorIndex);
            Assert.Equal((short)4, UncadLayerStandard.ConduitColorIndex);
            Assert.Equal(new[] { "U_CABLE", "U_TRAY", "U_CONDUIT" },
                UncadLayerStandard.GovernedLayerNames.ToArray());
        }

        [Fact]
        public void AnnotationKind_ResolvesLayerAndNeverGuesses()
        {
            // KIND 自 D-018/D-050 元数据引入起恒写入：U1Q→桥架层、U1C→线管层。
            Assert.Equal(UncadLayerStandard.TrayLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("U1Q"));
            Assert.Equal(UncadLayerStandard.TrayLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("u1q"));
            Assert.Equal(UncadLayerStandard.ConduitLayerName,
                UncadLayerStandard.TryResolveAnnotationLayer("U1C"));
            // 其它种类（未来/未知）不猜：null = 不移动、点名「无法判定」。
            Assert.Null(UncadLayerStandard.TryResolveAnnotationLayer("U1X"));
            Assert.Null(UncadLayerStandard.TryResolveAnnotationLayer(""));
            Assert.Null(UncadLayerStandard.TryResolveAnnotationLayer(null));

            Assert.Equal((short)1,
                UncadLayerStandard.ColorIndexOf(UncadLayerStandard.TrayLayerName));
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
                ConduitEntities = 1
            };
            Assert.Equal(6, counters.TotalMoves);
            Assert.Equal("图层矫正：6 个实体移正（电缆 3/桥架 2/线管 1/无法判定 0）",
                counters.Describe());
            Assert.True(counters.IsEmpty == false);

            var undeterminable = new UncadLayerCorrectionCounters
            {
                UndeterminableEntities = 1
            };
            undeterminable.UndeterminableHandles.Add("2A5F");
            Assert.Equal("图层矫正：0 个实体移正（电缆 0/桥架 0/线管 0/无法判定 1）；"
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
            // 编排只调阶段类；确认窗把体检结果传给 ShowReview（预览+矫正报告）。
            Assert.Contains("FillRouteInspectionStage.InspectSelectionAndReport", fill);
            Assert.Contains("out FillReviewData review, routeInspection", fill);

            // 阶段类承载报告口径：连通性报告级（不拦截）+ 图层矫正文案。
            string stage = Source("src", "UNCAD", "Features", "Fill",
                "FillRouteInspectionStage.cs");
            Assert.Contains("连通性：", stage);
            Assert.Contains("报告级检查，不拦截写入", stage);
            Assert.Contains("LayerCorrectionText", stage);
            Assert.Contains("ConfigKeys.FillLayerGovernor", stage);
            Assert.Contains("LayerGovernorService.Plan", stage);
            Assert.Contains("ParallelAnnotationOwnership.Collect", stage);
            Assert.Contains("QuickLineAxisSpan.Describe", stage);

            string batch = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            Assert.Contains("LayerGovernorService.Apply", batch);
            Assert.Contains("FillRouteInspectionStage.Scan(ctx)", batch);
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

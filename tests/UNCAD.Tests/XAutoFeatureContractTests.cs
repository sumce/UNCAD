using System;
using System.IO;
using System.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// Xauto 契约：命令注册、复用 U1R 直径设置、单写事务失败回滚、
    /// 洞后重挂 U1LX 关联、绝不改写标注文字或图框属性、不联网。
    /// </summary>
    public class XAutoFeatureContractTests
    {
        [Fact]
        public void XAuto_IsARegisteredCanonicalCommand()
        {
            Assert.Contains(CommandIds.XAuto, CommandIds.Registered);
            Assert.Contains(CommandIds.XAuto, CommandIds.Canonical);
            Assert.Equal("Xauto", CommandIds.XAuto);
        }

        [Fact]
        public void XAuto_OrchestratesTheSharedPlannersAndBridgeGeometry()
        {
            string feature = Source("src", "UNCAD", "Features", "XAuto",
                "XAutoFeature.cs");

            Assert.Contains("ProductMetadata.EnsureCommandAllowed(CommandIds.XAuto)",
                feature);
            Assert.Contains("SketchProportionPlanner.Plan(", feature);
            Assert.Contains("RouteCrossingPlanner.Plan(", feature);
            Assert.Contains("ArchBridgePolyline.TryPlanVertices(", feature);
            // 洞径沿用 U1SET 的 U1R 直径设置，最小可读长度 = 1.5×洞径。
            Assert.Contains("ConfigKeys.UnrDiameter", feature);
            Assert.Contains("diameter * 1.5", feature);
            // 交叉检测必须消费重排后的最终位置（先查 MovesById 再回退原端点）。
            Assert.Contains("frame.MovesById.TryGetValue", feature);
            // Line→Polyline 后重挂 UNCAD_U1LX 关联，COMPLETED 由存储层保留。
            Assert.Contains("QuickLineCadService.TryLinkLineAndLabel", feature);
            // 标注随线整体平移（保留原偏移），而不是重写文字。
            Assert.Contains("Matrix3d.Displacement", feature);
            // 单写事务：失败 Abort 整体回退（一次 U 撤销的来源）。
            Assert.Contains("tr.Abort()", feature);
        }

        [Fact]
        public void XAuto_NeverTouchesLabelTextFrameRecordsOrNetwork()
        {
            string feature = Source("src", "UNCAD", "Features", "XAuto",
                "XAutoFeature.cs");

            Assert.DoesNotContain("TextString =", feature);
            Assert.DoesNotContain("MTextAttribute", feature);
            Assert.DoesNotContain("FRAMEINFO", feature);
            Assert.DoesNotContain("CABLE_INFO", feature);
            Assert.DoesNotContain("CONDUIT_INFO", feature);
            Assert.DoesNotContain("http://", feature);
            Assert.DoesNotContain("https://", feature);
        }

        [Fact]
        public void UnrFeature_DelegatesHopGeometryToTheSharedBuilder()
        {
            string unr = Source("src", "UNCAD", "Features", "Unr", "UnrFeature.cs");
            string shared = Source("src", "UNCAD", "Cad", "QuickLine",
                "ArchBridgePolyline.cs");

            Assert.Contains("ArchBridgePolyline.TryPlanOpening", unr);
            Assert.Contains("ArchBridgePolyline.BuildMerged", unr);
            // 几何实现移入共享类，U1R 本体不再自带半圆数学。
            Assert.DoesNotContain("SemicircleGeometry", unr);
            Assert.Contains("SemicircleGeometry.Bulge", shared);
            Assert.Contains("SemicircleGeometry.CanSplit", shared);
            // 多洞规划：间距与端点留半径校验在共享层兜底。
            Assert.Contains("TryPlanVertices", shared);
        }

        private static string Source(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(parts.Aggregate(root, Path.Combine));
        }
    }
}

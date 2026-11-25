using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-059 锁死：楼层平面按台账上下游楼层画 + 跨层高度校验（2F→3F 应在
    /// 7.6~7.8 m）；上游柜（I-Line 落地 / 插座盘离地 1 m / 立管取底部基础点）；
    /// 桥架/线管整趟——标注长度是整趟总长，红线（含合并多段线、被缩短的红线）
    /// 拆段分别找最近电缆。
    /// </summary>
    public sealed class RouteFloorAndRecoveryTests
    {
        private static QuickLinePoint P(double x, double y) => new QuickLinePoint(x, y);

        // ---- 楼层 ---------------------------------------------------------------

        [Fact]
        public void Floors_ComeFromTheWorkbookBetweenUpstreamAndDevice()
        {
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            Assert.Equal(2, plan.Floors.Count);
            Assert.Equal("2F", plan.Floors[0].Label);
            Assert.Equal("3F", plan.Floors[1].Label);
            // 2F 地面在场景 0；3F 地面比它高 7.8 m。
            Assert.Equal(0.0, plan.Floors[0].SceneZ, 6);
            Assert.Equal(7800.0, plan.Floors[1].SceneZ, 6);
            Assert.Equal("台账", plan.Source);
            Assert.True(plan.CrossesFloors);
        }

        [Fact]
        public void Floors_OnlyTheDeviceFloor_WhenTheOtherEndIsUnreadable()
        {
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            PreviewFloorSlab slab = Assert.Single(plan.Floors);
            Assert.Equal("3F", slab.Label);
            Assert.Equal(0.0, slab.SceneZ, 6);   // 只有一层：它就是场景零点
            Assert.False(plan.CrossesFloors);
            Assert.StartsWith("台账", plan.Source, StringComparison.Ordinal);
        }

        [Fact]
        public void Floors_FallBackToTheBaseFloorDirective_WhenTheWorkbookHasNoFloors()
        {
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("", "", "本层=2F地面",
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            PreviewFloorSlab slab = Assert.Single(plan.Floors);
            Assert.Equal("2F", slab.Label);
            Assert.Equal("U1SET 本层", plan.Source);
        }

        [Fact]
        public void Floors_None_WhenNothingIsKnown()
        {
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("", "", null,
                Array.Empty<PreviewReferenceLevel>());
            Assert.Empty(plan.Floors);
            Assert.Equal("无", plan.Source);
            Assert.Null(plan.UpstreamSlab);
        }

        [Fact]
        public void Floors_UnknownFloorsFallBackToTheStandardFloorHeight()
        {
            // 清单里只有 2F 地面：3F 按层高推（7800+7800）。
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("3F", "2F", null,
                PreviewReferenceLevels.Parse("本层=2F地面;2F地面=7800=环氧地坪"));

            Assert.Equal(2, plan.Floors.Count);
            Assert.Equal(0.0, plan.Floors[0].SceneZ, 6);       // 2F
            Assert.Equal(7800.0, plan.Floors[1].SceneZ, 6);    // 3F = 15600 − 7800
            Assert.Equal(15600.0, plan.Floors[1].ElevationMillimetres, 6);
            Assert.False(plan.Floors[1].ElevationKnown);
        }

        [Theory]
        [InlineData("3F", 3)]
        [InlineData("3", 3)]
        [InlineData("f3", 3)]
        [InlineData("3层", 3)]
        [InlineData("B1", -1)]
        [InlineData("b2f", -2)]
        [InlineData("", null)]
        [InlineData("三层", null)]
        public void FloorNumber_ParsesTheWorkbookForms(string text, int? expected)
        {
            Assert.Equal(expected, PreviewFloorPlan.ParseFloorNumber(text));
        }

        [Fact]
        public void Rise_2FTo3F_AcceptsSevenSixToSevenEight()
        {
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            // 正好 7.8 m：合格。
            Assert.Null(plan.EvaluateRise(0.0, 7800.0));
            // 7.6 m（7.8 − 0.2）：合格的下限。
            Assert.Null(plan.EvaluateRise(0.0, 7600.0));
            // 7.59 m：超下限，报警，且文案里带区间与实际值。
            string tooLow = plan.EvaluateRise(0.0, 7590.0);
            Assert.NotNull(tooLow);
            Assert.Contains("7.6~7.8", tooLow, StringComparison.Ordinal);
            Assert.Contains("实际 7.59 m", tooLow, StringComparison.Ordinal);
            // 8.1 m：超高也报警。
            Assert.NotNull(plan.EvaluateRise(0.0, 8100.0));
            // 同层：不做跨层校验。
            Assert.Null(PreviewFloorPlan.Plan("2F", "2F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList))
                .EvaluateRise(0.0, 3000.0));
        }

        [Fact]
        public void Rise_MeasuresFromTheUpstreamFloor_AddingTheSocketPanelMetre()
        {
            // 3.8.0.3 现场（2026-11-25）：「上下游端点距离改成 2F 地面到设备的距离，
            // 在 7.6~7.8」。插座盘 2F→3F，线路竖向只有 6.2 m——以前直接拿两端高差
            // 去比 7.6~7.8，起算点不一致。现在从 2F 地面量：离地 1 m + 线路竖向。
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            string warning = plan.EvaluateRise(0.0, 6200.0, 1000.0);
            Assert.NotNull(warning);
            Assert.Contains("跨层高度不符：台账 2F→3F，2F地面到设备端应在 7.6~7.8 m",
                warning, StringComparison.Ordinal);
            // 7.6~7.8 的来源点名：U1SET 标高清单里 2F 地面到 3F 高架地板面差 7.8 m。
            Assert.Contains("按 U1SET 标高清单 2F地面 → 3F高架地板面 差 7.8 m",
                warning, StringComparison.Ordinal);
            Assert.Contains("实际 7.2 m（上游端离地 1 m + 线路竖向 6.2 m）", warning,
                StringComparison.Ordinal);
            // 线路竖向 6.6 m：1 + 6.6 = 7.6，落在区间里，不报警。
            Assert.Null(plan.EvaluateRise(0.0, 6600.0, 1000.0));
            Assert.Null(plan.EvaluateRise(0.0, 6800.0, 1000.0));
            // I-Line 盘不抬：线路竖向就是到设备的距离，6.2 照旧报警，7.6 起合格。
            string iLine = plan.EvaluateRise(0.0, 6200.0);
            Assert.Contains("实际 6.2 m（线路竖向 6.2 m）", iLine, StringComparison.Ordinal);
            Assert.Null(plan.EvaluateRise(0.0, 7600.0));
        }

        [Fact]
        public void Rise_UpstreamEndOnARiserTop_MeasuresFromTheRiserBottom()
        {
            // 用户口径：末端的未闭合端在立管上面，取下面那个点作基础点——
            // 插座盘在立管底（离地 1 m），线路竖向从立管底起算。
            var riserTop = new QuickLineSpatialPoint(0, 0, 3700);
            var riserBottom = new QuickLineSpatialPoint(0, 0, 1000);
            Assert.Equal(riserBottom, PreviewCabinet.BasePoint(riserTop, riserBottom));
            // 相接段是水平的：基础点就是上游末端本身。
            Assert.Equal(riserTop, PreviewCabinet.BasePoint(riserTop,
                new QuickLineSpatialPoint(4800, 0, 3700)));
            // 立管另一端在端点上面（末端在管底）：不是「端点在立管顶上」。
            Assert.Equal(riserBottom, PreviewCabinet.BasePoint(riserBottom, riserTop));
            // 没有相接段：基础点就是上游末端。
            Assert.Equal(riserTop, PreviewCabinet.BasePoint(riserTop, null));

            PreviewFloorPlan plan = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));
            // 设备端在 7.7 m：从立管底量是 6.7 m，加离地 1 m = 7.7 m，合格
            // （从立管顶量只有 4 m，会误报）。
            Assert.Null(plan.EvaluateRise(riserBottom.Z, 7700.0, 1000.0));
        }

        [Fact]
        public void Floors_NameTheLevelListEntryTheyWereTakenFrom()
        {
            PreviewFloorPlan plan = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));
            Assert.Equal("2F地面", plan.Floors[0].SourceLabel);
            // 设备那一层取高架地板面（设备站在它上面），不是 3F 地面。
            Assert.Equal("3F高架地板面", plan.Floors[1].SourceLabel);
            Assert.Equal(15600.0, plan.Floors[1].ElevationMillimetres, 6);

            // 清单里没有的楼层按层高推：条目名为空，校验文案里注明是推算的。
            PreviewFloorPlan guessed = PreviewFloorPlan.Plan("3F", "2F", null,
                PreviewReferenceLevels.Parse("本层=2F地面;2F地面=7800=环氧地坪"));
            Assert.Equal(string.Empty, guessed.Floors[1].SourceLabel);
            Assert.Contains("3F（标高清单里没有，按层高推算）", guessed.EvaluateRise(0.0, 1.0),
                StringComparison.Ordinal);
        }

        // ---- 上游柜 --------------------------------------------------------------

        [Fact]
        public void Cabinet_ILineStandsOnTheFloor_SocketPanelIsOneMetreAbove()
        {
            PreviewCabinet panel = PreviewCabinet.Plan(PreviewCabinetKind.ILinePanel,
                0, 0, 0, 4800, 0, 0, 1, 0, 0.0);
            Assert.Equal(0.0, panel.BaseZ, 6);
            Assert.Equal(2200.0, panel.Height, 6);
            Assert.True(panel.BaseX < 0.0, "柜子放在路线的反方向");

            PreviewCabinet socket = PreviewCabinet.Plan(PreviewCabinetKind.SocketPanel,
                0, 0, 0, 4800, 0, 0, 1, 0, 0.0);
            Assert.Equal(1000.0, socket.BaseZ, 6);
            Assert.Equal(1000.0, socket.Height, 6);
            Assert.Equal(0.0, socket.MountingFloorZ.Value, 6);
        }

        [Fact]
        public void Cabinet_RiserEndpointTakesTheRiserBottomAsItsBase()
        {
            // 上游端点在立管顶上（z=2200），立管底在 z=0、楼层定不出来：
            // 基础点取立管底，柜子落在楼层地面上而不是悬在管顶。
            PreviewCabinet riser = PreviewCabinet.Plan(PreviewCabinetKind.ILinePanel,
                0, 0, 2200, 0, 0, 0, null, null, null);
            Assert.True(riser.BasedOnRiserBottom);
            Assert.Equal(0.0, riser.BaseZ, 6);

            // 楼层定得出来时，以楼层地面为准（更深的地面也认）。
            PreviewCabinet onFloor = PreviewCabinet.Plan(PreviewCabinetKind.ILinePanel,
                0, 0, 2200, 0, 0, 0, null, null, 0.0);
            Assert.Equal(0.0, onFloor.BaseZ, 6);

            // 非立管端点（水平段相接）：基础点就是端点所在楼层。
            PreviewCabinet horizontal = PreviewCabinet.Plan(PreviewCabinetKind.ILinePanel,
                0, 0, 7800, 4800, 0, 7800, 1, 0, 7800.0);
            Assert.False(horizontal.BasedOnRiserBottom);
            Assert.Equal(7800.0, horizontal.BaseZ, 6);
        }

        [Fact]
        public void Cabinet_KindFollowsTheWorkbookPanelColumn()
        {
            Assert.Equal(PreviewCabinetKind.None, PreviewCabinet.KindOf(""));
            Assert.Equal(PreviewCabinetKind.None, PreviewCabinet.KindOf(null));
            Assert.Equal(PreviewCabinetKind.ILinePanel, PreviewCabinet.KindOf("I-Line盘"));
            Assert.Equal(PreviewCabinetKind.SocketPanel, PreviewCabinet.KindOf("插座盘"));
            Assert.Equal(PreviewCabinetKind.BusPlugBox, PreviewCabinet.KindOf("母线插接口"));
            Assert.Null(PreviewCabinet.Plan(PreviewCabinetKind.None,
                0, 0, 0, 1, 0, 0, null, null, null));
        }

        // ---- 3.8.0.2：插座盘的线路直接整体抬高 1 m；占位方块按场景放大 --------------

        [Fact]
        public void RouteLift_SocketPanelRaisesTheWholeRouteOneMetre()
        {
            // 用户口径：「不用管两端的高差，直接抬高就行」——盘的方块只是占位，
            // 插座盘的出线在楼层地面 + 1 m，所以整条线路抬高 1 m；其它盘不抬。
            Assert.Equal(1000.0, PreviewCabinet.RouteLiftMillimetres(
                PreviewCabinetKind.SocketPanel), 6);
            Assert.Equal(0.0, PreviewCabinet.RouteLiftMillimetres(
                PreviewCabinetKind.ILinePanel), 6);
            Assert.Equal(0.0, PreviewCabinet.RouteLiftMillimetres(
                PreviewCabinetKind.BusPlugBox), 6);
            Assert.Equal(0.0, PreviewCabinet.RouteLiftMillimetres(
                PreviewCabinetKind.None), 6);
        }

        [Fact]
        public void Cabinet_VisualScaleKeepsThePlaceholderReadable()
        {
            // 小场景（对角线 8 m）：真实尺寸就看得清，不放大。
            Assert.Equal(1.0, PreviewCabinet.VisualScale(
                PreviewCabinetKind.SocketPanel, 8000.0), 6);
            // 40 m 的楼层：插座盘宽度至少占取景对角线的 5% → 2 m。
            Assert.Equal(0.05 * 40000.0 / 600.0, PreviewCabinet.VisualScale(
                PreviewCabinetKind.SocketPanel, 40000.0), 6);
            // 再大的场景最多放大 4 倍；非法对角线不放大。
            Assert.Equal(4.0, PreviewCabinet.VisualScale(
                PreviewCabinetKind.SocketPanel, 200000.0), 6);
            Assert.Equal(1.0, PreviewCabinet.VisualScale(
                PreviewCabinetKind.SocketPanel, double.NaN), 6);

            // 只放大平面尺寸；高度（底边 +1000、顶边 +2000 的口径）不变。
            PreviewCabinet scaled = PreviewCabinet.Plan(PreviewCabinetKind.SocketPanel,
                0, 0, 1000, 4800, 0, 1000, 1, 0, 0.0, 3.0);
            Assert.Equal(1800.0, scaled.Width, 6);
            Assert.Equal(1200.0, scaled.Depth, 6);
            Assert.Equal(1000.0, scaled.Height, 6);
            Assert.Equal(1000.0, scaled.BaseZ, 6);

            // 楼层定不出来：柜底放在线路的基础点上（线路同样已经抬高 1 m），
            // 不再在基础点上又加 1 m。
            PreviewCabinet unknownFloor = PreviewCabinet.Plan(
                PreviewCabinetKind.SocketPanel, 0, 0, 1000, 4800, 0, 1000,
                1, 0, null);
            Assert.Equal(1000.0, unknownFloor.BaseZ, 6);
            Assert.Null(unknownFloor.MountingFloorZ);
            // 未闭合端在立管上面：基础点取立管下面那个点。
            PreviewCabinet riser = PreviewCabinet.Plan(PreviewCabinetKind.SocketPanel,
                0, 0, 3700, 0, 0, 1000, null, null, null);
            Assert.Equal(1000.0, riser.BaseZ, 6);
            Assert.True(riser.BasedOnRiserBottom);
        }

        [Fact]
        public void FromScene_SocketPanel_RaisesTheWholeRouteAboveTheFloor()
        {
            // 2F 插座盘 → 水平 14.4m → 立管上 3F → 3F 上走 9.6m。
            var graph = QuickLineGraph.Build(new[]
            {
                new QuickLineSegment("h", new QuickLinePoint(0, 0),
                    new QuickLinePoint(14400, 0)),
                new QuickLineSegment("v", new QuickLinePoint(14400, 0),
                    new QuickLinePoint(14400, 7800)),
                new QuickLineSegment("run", new QuickLinePoint(14400, 7800),
                    new QuickLinePoint(4800, 7800))
            }, 1.0);
            var distances = new Dictionary<string, double>
            {
                ["h"] = 14400.0, ["v"] = 7800.0, ["run"] = 9600.0
            };
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            QuickLineIsometricNode upstream = scene.Nodes.Single(node =>
                Math.Abs(node.Position.X) < 1e-6 && Math.Abs(node.Position.Y) < 1e-6);
            QuickLineIsometricNode device = scene.Nodes.Single(node =>
                Math.Abs(node.Position.X - 4800) < 1e-6);
            PreviewFloorPlan floors = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            QuickLineOrbitPreviewModel socket = QuickLineOrbitPreviewModel.FromScene(
                scene, "", upstreamEndpoint: upstream.Position,
                downstreamEndpoint: device.Position, directionResolved: true,
                floors: floors, cabinetKind: PreviewCabinetKind.SocketPanel);
            // 整条线路抬 1 m：最低点（上游末端）在 2F 地面 + 1 m，设备端跟着高 1 m。
            Assert.Equal(1000.0, socket.UpstreamEndpoint.Value.Z, 6);
            Assert.Equal(8800.0, socket.DownstreamEndpoint.Value.Z, 6);
            Assert.Equal(1000.0, socket.Segments.Min(segment =>
                Math.Min(segment.Start.Z, segment.End.Z)), 6);
            QuickLineOrbitPreviewEnvelope socketEnvelope =
                QuickLineOrbitPreviewEnvelope.Build(socket);
            Assert.Equal(1000.0, socketEnvelope.Cabinet.BaseZ, 6);
            Assert.Equal(0.0, socketEnvelope.Cabinet.MountingFloorZ.Value, 6);
            // 跨层高差不因为整体抬高而变（校验从基础点起量，这里就是上游末端）。
            Assert.Null(floors.EvaluateRise(socket.UpstreamEndpoint.Value.Z,
                socket.DownstreamEndpoint.Value.Z));
            // 占位方块按场景放大（平面尺寸），高度口径不变。
            Assert.True(socketEnvelope.Cabinet.SizeY > 600.0,
                "placeholder footprint is scaled up, got " + socketEnvelope.Cabinet.SizeY);
            Assert.Equal(1000.0, socketEnvelope.Cabinet.Height, 6);

            // 楼层定不出来：线路照样抬高 1 m，柜底就在线路的基础点上。
            QuickLineOrbitPreviewModel noFloors = QuickLineOrbitPreviewModel.FromScene(
                scene, "", upstreamEndpoint: upstream.Position,
                downstreamEndpoint: device.Position, directionResolved: true,
                cabinetKind: PreviewCabinetKind.SocketPanel);
            Assert.Equal(1000.0, noFloors.UpstreamEndpoint.Value.Z, 6);
            QuickLineOrbitPreviewEnvelope noFloorEnvelope =
                QuickLineOrbitPreviewEnvelope.Build(noFloors);
            Assert.Equal(1000.0, noFloorEnvelope.Cabinet.BaseZ, 6);
            Assert.Null(noFloorEnvelope.Cabinet.MountingFloorZ);

            // 同一条线路、I-Line 盘：基础点落在 2F 地面上，不抬。
            QuickLineOrbitPreviewModel panel = QuickLineOrbitPreviewModel.FromScene(
                scene, "", upstreamEndpoint: upstream.Position,
                downstreamEndpoint: device.Position, directionResolved: true,
                floors: floors, cabinetKind: PreviewCabinetKind.ILinePanel);
            Assert.Equal(0.0, panel.UpstreamEndpoint.Value.Z, 6);
            Assert.Equal(7800.0, panel.DownstreamEndpoint.Value.Z, 6);
            // 原场景不被修改。
            Assert.Equal(0.0, scene.Nodes.Min(node => node.Position.Z), 6);
        }

        [Fact]
        public void FloorSummary_SurvivesTheU1SetOnlyFloor()
        {
            // 3.8.0.2：台账两端楼层都认不出、只靠 U1SET「本层=2F地面」画出一层时，
            // 摘要不能空引用（以前会让整个 3D 预览构建失败）。
            PreviewFloorPlan directive = PreviewFloorPlan.Plan(null, null, "本层=2F地面",
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));
            Assert.Equal("U1SET 本层 2F", FillRouteInspectionStage.DescribeFloors(directive));
            Assert.Equal("楼层未识别",
                FillRouteInspectionStage.DescribeFloors(null));
            Assert.Equal("楼层未识别",
                FillRouteInspectionStage.DescribeFloors(PreviewFloorPlan.Plan(null, null,
                    null, null)));
            PreviewFloorPlan crossed = PreviewFloorPlan.Plan("2F", "3F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));
            Assert.Equal("台账 2F→3F", FillRouteInspectionStage.DescribeFloors(crossed));
            Assert.Equal("台账 3F", FillRouteInspectionStage.DescribeFloors(
                PreviewFloorPlan.Plan("3F", "3F", null,
                    PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList))));
        }

        [Fact]
        public void FromScene_SocketPanel_RiserEndTakesTheRiserBottom()
        {
            // 未闭合端在立管顶上：基础点取立管底——立管底落在 2F 地面 + 1 m。
            var graph = QuickLineGraph.Build(new[]
            {
                new QuickLineSegment("v", new QuickLinePoint(0, 0),
                    new QuickLinePoint(0, 2700)),
                new QuickLineSegment("h", new QuickLinePoint(0, 2700),
                    new QuickLinePoint(14400, 2700))
            }, 1.0);
            var distances = new Dictionary<string, double> { ["v"] = 2700.0, ["h"] = 14400.0 };
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, distances,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            QuickLineIsometricNode upstream = scene.Nodes.Single(node =>
                Math.Abs(node.Position.X) < 1e-6
                && Math.Abs(node.Position.Z - 2700) < 1e-6);
            QuickLineIsometricNode device = scene.Nodes.Single(node =>
                Math.Abs(node.Position.X - 14400) < 1e-6);
            PreviewFloorPlan floors = PreviewFloorPlan.Plan("2F", "2F", null,
                PreviewReferenceLevels.Parse(PreviewReferenceLevels.DefaultList));

            QuickLineOrbitPreviewModel model = QuickLineOrbitPreviewModel.FromScene(
                scene, "", upstreamEndpoint: upstream.Position,
                downstreamEndpoint: device.Position, directionResolved: true,
                floors: floors, cabinetKind: PreviewCabinetKind.SocketPanel);
            Assert.Equal(3700.0, model.UpstreamEndpoint.Value.Z, 6);
            Assert.Equal(1000.0, model.Segments.Min(segment =>
                Math.Min(segment.Start.Z, segment.End.Z)), 6);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model);
            Assert.Equal(1000.0, envelope.Cabinet.BaseZ, 6);
            Assert.True(envelope.Cabinet.Connection.Z > envelope.Cabinet.TopZ,
                "末端在立管顶上，高于柜顶");
        }

        // ---- 整趟：长度取标注、宿主按红线拆段找 --------------------------------------

        [Theory]
        [InlineData("桥架200*100 2500mm", 2500.0)]
        [InlineData("⌀20线管 2000mm", 2000.0)]
        [InlineData("梯形桥架400Wx200H\\P19200mm", 19200.0)]
        [InlineData("镀锌穿线管38mm\\P9600mm", 9600.0)]
        [InlineData("梯形桥架200Wx100H-1300mm", 1300.0)]
        [InlineData("桥架200*100", null)]
        [InlineData("桥架 2500mm 预留", null)]
        // 用户口径（2026-11-25）「共用桥架不用管」：共用桥架的长度照旧不读。
        [InlineData("共用桥架200*100 3000mm", null)]
        public void AnnotationLength_IsTheWholeRunTotalFromTheLabel(
            string text, double? expected)
        {
            Assert.Equal(expected, RouteAnnotationTextPlanner.ParseLengthMillimetres(text));
        }

        [Theory]
        [InlineData("桥架200*100 2500mm", 200.0)]
        [InlineData("梯形桥架200Wx100H\\P2500mm", 200.0)]
        // 3.8.0.3：U1Q 出标的「宽Wx高H」以前读不出宽度（一律按缺省 200 画），
        // 100 宽的桥架被画成 200 宽。
        [InlineData("梯形桥架100Wx100H\\P2500mm", 100.0)]
        [InlineData("梯形桥架200Wx100H-1300mm", 200.0)]
        [InlineData("线管25 3000mm", null)]
        [InlineData("桥架200*100 12格", 200.0)]
        // 「共用桥架不用管」：规格前面带「共用」的照旧读不出（走缺省并注明）。
        [InlineData("共用桥架200*100 3000mm", null)]
        public void TrayWidth_ReadsTheStatisticsSpecAndTheWidthHeightSpec(
            string text, double? expected)
        {
            Assert.Equal(expected.HasValue,
                RouteAnnotationTextPlanner.TryParseTrayWidthMillimetres(text,
                    out double width));
            if (expected.HasValue) Assert.Equal(expected.Value, width, 6);
        }

        [Fact]
        public void RedLinePieces_EachFindTheirNearestCable_OneRunManyHosts()
        {
            // 三根电缆平行排开；一根红线横跨它们上方（合并多段线）：
            // 拆成小段后各自找到最近的那根电缆，合起来是同一趟的宿主。
            var cables = new List<QuickLineSegment>
            {
                new QuickLineSegment("c1", P(0, 0), P(4000, 0)),
                new QuickLineSegment("c2", P(5000, 0), P(9000, 0)),
                new QuickLineSegment("c3", P(10000, 0), P(14000, 0))
            };
            var redLine = new List<IReadOnlyList<QuickLinePoint>>
            {
                new[] { P(0, 20), P(4000, 20), P(4000, 35), P(9000, 35), P(9000, 20), P(14000, 20) }
            };

            IReadOnlyList<string> hosts = RouteAnnotationHostRecovery.ResolveHosts(
                redLine, cables);
            Assert.Equal(new[] { "c1", "c2", "c3" }, hosts);
        }

        [Fact]
        public void RedLine_ShortenedButStillOnTheCable_IsRecognised()
        {
            // 红线被缩短了（只盖住电缆中间一小段），仍然认得出。
            IReadOnlyList<string> hosts = RouteAnnotationHostRecovery.ResolveHosts(
                new[] { (IReadOnlyList<QuickLinePoint>)new[] { P(1500, 20), P(2500, 20) } },
                new[] { new QuickLineSegment("c1", P(0, 0), P(4000, 0)) });
            Assert.Equal(new[] { "c1" }, hosts);
        }

        [Fact]
        public void RedLine_UpTo600Away_StillFindsTheHost()
        {
            // 3.8.0.1：红线与电缆的垂距放到 600（现场旧图框常见 400~600），
            // 再远就不猜。
            IReadOnlyList<string> hosts = RouteAnnotationHostRecovery.ResolveHosts(
                new[] { (IReadOnlyList<QuickLinePoint>)new[] { P(1500, 550), P(2500, 550) } },
                new[] { new QuickLineSegment("c1", P(0, 0), P(4000, 0)) });
            Assert.Equal(new[] { "c1" }, hosts);
            Assert.Empty(RouteAnnotationHostRecovery.ResolveHosts(
                new[] { (IReadOnlyList<QuickLinePoint>)new[] { P(1500, 700), P(2500, 700) } },
                new[] { new QuickLineSegment("c1", P(0, 0), P(4000, 0)) }));
        }

        [Fact]
        public void RedLine_NoCableBeside_ReportsNothing()
        {
            Assert.Empty(RouteAnnotationHostRecovery.ResolveHosts(
                new[] { (IReadOnlyList<QuickLinePoint>)new[] { P(0, 20), P(4000, 20) } },
                new[] { new QuickLineSegment("c1", P(0, 2000), P(4000, 2000)) }));
            // 不平行（斜着穿过）也不认。
            Assert.Empty(RouteAnnotationHostRecovery.ResolveHosts(
                new[] { (IReadOnlyList<QuickLinePoint>)new[] { P(0, 0), P(4000, 4000) } },
                new[] { new QuickLineSegment("c1", P(0, 0), P(4000, 0)) }));
        }

        [Fact]
        public void LegacyAnnotationText_FindsTheRedLineEvenWhenNotParallel()
        {
            // 旧图框：桥架文字竖排（90°），红线和电缆是 30°/水平的；红线还在
            // 文字外框中心下方。按外框中心找，不要求文字与红线平行。
            var stamp = new RouteAnnotationTextStamp("t1",
                "梯形桥架200Wx100H\\P3500mm", P(2000, 700), Math.PI / 2,
                false, RouteAnnotationTextKind.Tray, 200, P(2000, 500), 2500);
            var lines = new[]
            {
                new QuickLineSegment("red", P(0, 300), P(4000, 300)),
                new QuickLineSegment("far", P(0, -6000), P(4000, -6000))
            };
            IReadOnlyList<IReadOnlyList<QuickLineSegment>> chains =
                RouteAnnotationLineRecovery.FindRedLineChains(stamp, lines,
                    new HashSet<string>(), out bool besideRedLine);
            Assert.True(besideRedLine, "文字旁边有红线候选");
            Assert.Contains(chains, chain => chain.Any(line => line.Id == "red"));
            Assert.DoesNotContain(chains, chain => chain.Any(line => line.Id == "far"));
            // 找到红线后，红线旁边那根带毫米标的电缆就是宿主。
            IReadOnlyList<string> hosts = RouteAnnotationHostRecovery.ResolveHosts(
                chains[0], new[]
                {
                    new QuickLineSegment("cable", P(0, 0), P(4000, 0))
                });
            Assert.Equal(new[] { "cable" }, hosts);
        }

        // ---- U1UL：只靠位置匹配的毫米标 ------------------------------------------

        [Fact]
        public void Align_PositionMatch_AcceptsALabelMovedAlongOrAcrossTheLine()
        {
            // 沿线挪到 1/4 处、放在线下方、角度差 5°：都算这条线的毫米标。
            Assert.True(QuickLineLabelPlacementPlanner.SitsBesideLine(1000, 300, 180,
                Math.PI / 36, P(0, 0), P(4000, 0)));
            // 换到线下方同样是它的。
            Assert.True(QuickLineLabelPlacementPlanner.SitsBesideLine(2000, -300, 180,
                0, P(0, 0), P(4000, 0)));
        }

        [Fact]
        public void Align_PositionMatch_RejectsFarOrSkewedOrBeyondTheEnds()
        {
            // 离线太远（>4 倍字高）。
            Assert.False(QuickLineLabelPlacementPlanner.SitsBesideLine(2000, 900, 180,
                0, P(0, 0), P(4000, 0)));
            // 角度差 30°。
            Assert.False(QuickLineLabelPlacementPlanner.SitsBesideLine(2000, 100, 180,
                Math.PI / 6, P(0, 0), P(4000, 0)));
            // 落在线段两端之外。
            Assert.False(QuickLineLabelPlacementPlanner.SitsBesideLine(6000, 100, 180,
                0, P(0, 0), P(4000, 0)));
        }
    }
}

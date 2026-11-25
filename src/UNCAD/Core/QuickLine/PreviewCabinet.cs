using System;
using System.Collections.Generic;

namespace UNCAD.Core.QuickLine
{
    /// <summary>上游柜的类型（台账「盘柜类型」一列）。</summary>
    public enum PreviewCabinetKind
    {
        None = 0,
        /// <summary>I-Line 盘：立在该楼层的地面上。</summary>
        ILinePanel,
        /// <summary>插座盘：底边离该楼层地面 1 m。</summary>
        SocketPanel,
        /// <summary>母线插接口：挂在母线上，按路线端点的高度示意。</summary>
        BusPlugBox
    }

    /// <summary>上游柜在预览里的盒子（底面中心 + 三向尺寸 + 朝向）。</summary>
    public sealed class PreviewCabinet
    {
        private PreviewCabinet(PreviewCabinetKind kind, string label, double baseX,
            double baseY, double baseZ, double width, double depth, double height,
            double directionX, double directionY, bool basedOnRiserBottom,
            double? mountingFloorZ)
        {
            Kind = kind;
            Label = label;
            BaseX = baseX;
            BaseY = baseY;
            BaseZ = baseZ;
            Width = width;
            Depth = depth;
            Height = height;
            DirectionX = directionX;
            DirectionY = directionY;
            BasedOnRiserBottom = basedOnRiserBottom;
            MountingFloorZ = mountingFloorZ;
        }

        public PreviewCabinetKind Kind { get; }
        public string Label { get; }
        /// <summary>盒子底面中心（场景坐标，mm）。</summary>
        public double BaseX { get; }
        public double BaseY { get; }
        public double BaseZ { get; }
        /// <summary>垂直于路线方向的宽度（mm）。</summary>
        public double Width { get; }
        /// <summary>沿路线方向的进深（mm）。</summary>
        public double Depth { get; }
        public double Height { get; }
        /// <summary>从上游端点指向路线的水平单位向量（盒子放在它的反方向）。</summary>
        public double DirectionX { get; }
        public double DirectionY { get; }
        /// <summary>基础点取的是竖向段的下面那个点（端点段是向上的立管）。</summary>
        public bool BasedOnRiserBottom { get; }
        /// <summary>柜子所站楼层的地面高度（母线插接口、楼层定不出来时为空）。</summary>
        public double? MountingFloorZ { get; }

        /// <summary>I-Line 盘高度（mm）：从本层地面起算（3.4.0.2 口径）。</summary>
        public const double ILineHeightMillimetres = 2200.0;
        /// <summary>插座盘底边离地（mm）。</summary>
        public const double SocketPanelMountingMillimetres = 1000.0;
        /// <summary>插座盘盒高（mm）：底边 +1000、顶边 +2000（3.4.0.2 口径）。</summary>
        public const double SocketPanelHeightMillimetres = 1000.0;
        /// <summary>占位方块的基准平面尺寸（mm，按 VisualScale 放大前）。</summary>
        public const double ILineWidthMillimetres = 800.0;
        public const double ILineDepthMillimetres = 600.0;
        public const double SocketPanelWidthMillimetres = 600.0;
        public const double SocketPanelDepthMillimetres = 400.0;
        public const double BusPlugWidthMillimetres = 500.0;
        public const double BusPlugDepthMillimetres = 400.0;
        public const double BusPlugHeightMillimetres = 700.0;
        private const double GapMillimetres = 80.0;

        /// <summary>台账「盘柜类型」→ 柜子类型；空或认不出 = 不画。</summary>
        public static PreviewCabinetKind KindOf(string next)
        {
            string text = (next ?? string.Empty).Trim();
            if (text.Length == 0) return PreviewCabinetKind.None;
            if (text.IndexOf("母线", StringComparison.Ordinal) >= 0)
                return PreviewCabinetKind.BusPlugBox;
            if (text.IndexOf("插座", StringComparison.Ordinal) >= 0)
                return PreviewCabinetKind.SocketPanel;
            return PreviewCabinetKind.ILinePanel;
        }

        /// <summary>
        /// 规划上游柜。endpoint 是路线的上游端点；segmentOther 是与它相接的
        /// 那一段的另一个端点（用来认「端点段是向上的立管」）；direction 是从
        /// 端点指向路线的水平方向（没有水平段时传 null）；upstreamFloorZ 是上游
        /// 所在楼层平面在场景里的高度（定不出来时传 null）；visualScale 是占位
        /// 方块平面尺寸的放大倍数（高度不变，见 <see cref="VisualScale"/>）。
        ///
        /// 用户口径：I-Line 盘在该楼层的地面上，插座盘离地 1 m；不完全看上游端的
        /// 端点——端点段是向上的立管时，立管下面那个点才是设备的基础点。
        /// 3.8.0.2：插座盘的线路已经整体抬高 1 m（<see cref="RouteLiftMillimetres"/>），
        /// 所以柜底就落在楼层地面 + 1 m，也就是线路的基础点。
        /// </summary>
        public static PreviewCabinet Plan(PreviewCabinetKind kind,
            double endpointX, double endpointY, double endpointZ,
            double otherX, double otherY, double otherZ,
            double? directionX, double? directionY, double? upstreamFloorZ,
            double visualScale = 1.0)
        {
            if (kind == PreviewCabinetKind.None) return null;
            double scale = double.IsNaN(visualScale)
                || double.IsInfinity(visualScale) ? 1.0 : visualScale;
            if (scale < 1.0) scale = 1.0;
            if (scale > MaximumVisualScale) scale = MaximumVisualScale;
            double dx = 1.0;
            double dy = 0.0;
            if (directionX.HasValue && directionY.HasValue)
            {
                double length = Math.Sqrt(directionX.Value * directionX.Value
                    + directionY.Value * directionY.Value);
                if (length > 1e-9)
                {
                    dx = directionX.Value / length;
                    dy = directionY.Value / length;
                }
            }

            // 端点段是竖直线，而且另一端在端点下面 → 端点在立管顶上，
            // 基础点取立管底。
            bool riserBottom = IsRiserTop(endpointX, endpointY, endpointZ,
                otherX, otherY, otherZ);
            double referenceZ = riserBottom ? otherZ : endpointZ;
            double floorZ = upstreamFloorZ ?? referenceZ;

            double width;
            double depth;
            double height;
            double baseZ;
            double? mountingFloorZ;
            string label;
            switch (kind)
            {
                case PreviewCabinetKind.SocketPanel:
                    label = "插座盘";
                    width = SocketPanelWidthMillimetres * scale;
                    depth = SocketPanelDepthMillimetres * scale;
                    height = SocketPanelHeightMillimetres;
                    // 楼层已知：柜底在楼层地面 + 1 m。楼层定不出来：柜底放在线路的
                    // 基础点上（线路同样已经整体抬高 1 m）。
                    baseZ = upstreamFloorZ.HasValue
                        ? upstreamFloorZ.Value + SocketPanelMountingMillimetres
                        : referenceZ;
                    mountingFloorZ = upstreamFloorZ;
                    break;
                case PreviewCabinetKind.BusPlugBox:
                    label = "母线插接口";
                    width = BusPlugWidthMillimetres * scale;
                    depth = BusPlugDepthMillimetres * scale;
                    height = BusPlugHeightMillimetres;
                    baseZ = riserBottom ? otherZ + 0.5 * height : endpointZ - 0.5 * height;
                    mountingFloorZ = null;
                    break;
                default:
                    label = "I-Line盘";
                    width = ILineWidthMillimetres * scale;
                    depth = ILineDepthMillimetres * scale;
                    height = ILineHeightMillimetres;
                    baseZ = floorZ;
                    mountingFloorZ = floorZ;
                    break;
            }
            double standOff = depth / 2.0 + GapMillimetres * scale;
            return new PreviewCabinet(kind, label,
                endpointX - dx * standOff, endpointY - dy * standOff, baseZ,
                width, depth, height, dx, dy, riserBottom, mountingFloorZ);
        }

        /// <summary>
        /// 3.8.0.3：线路的基础点——上游端点本身；端点段是向上的立管（相接那一段
        /// 的另一端在端点正下方）时是立管底（用户口径「未闭合端在线段上面则取
        /// 下面这个点」）。柜子摆放在基础点上，跨层高度也从这里起算。
        /// segmentOtherEnd 是与上游端点相接那一段的另一个端点；没有相接段时传 null。
        /// </summary>
        public static QuickLineSpatialPoint BasePoint(QuickLineSpatialPoint endpoint,
            QuickLineSpatialPoint? segmentOtherEnd)
        {
            if (!segmentOtherEnd.HasValue
                || !IsRiserTop(endpoint.X, endpoint.Y, endpoint.Z,
                    segmentOtherEnd.Value.X, segmentOtherEnd.Value.Y,
                    segmentOtherEnd.Value.Z))
                return endpoint;
            return segmentOtherEnd.Value;
        }

        /// <summary>端点在立管顶上：相接段的另一端在端点正下方。</summary>
        private static bool IsRiserTop(double endpointX, double endpointY,
            double endpointZ, double otherX, double otherY, double otherZ)
        {
            double dx = endpointX - otherX;
            double dy = endpointY - otherY;
            return dx * dx + dy * dy <= 1e-6 && otherZ < endpointZ - 1e-6;
        }

        /// <summary>
        /// 3.8.0.2：插座盘的线路整体抬高多少——盘的方块只是占位，要抬的是线路
        /// 本身：图框里画不出地面，插座盘的出线在楼层地面 + 1 m，所以整条线路
        /// 直接抬高 1 m（不按两端高差算）。其它盘不抬。
        /// </summary>
        public static double RouteLiftMillimetres(PreviewCabinetKind kind)
            => kind == PreviewCabinetKind.SocketPanel
                ? SocketPanelMountingMillimetres : 0.0;

        /// <summary>
        /// 占位方块的宽度至少占取景对角线的这个比例：真实尺寸（0.6~0.8 m）放在
        /// 几十米的楼层里只有十来个像素，现场反馈「箱子太小」。
        /// </summary>
        public const double MinimumWidthViewDiagonalFraction = 0.05;
        /// <summary>占位方块平面尺寸最多放大的倍数（高度不放大）。</summary>
        public const double MaximumVisualScale = 4.0;

        /// <summary>占位方块的基准宽度（mm，放大前）。</summary>
        public static double BaseWidthMillimetres(PreviewCabinetKind kind)
        {
            switch (kind)
            {
                case PreviewCabinetKind.SocketPanel:
                    return SocketPanelWidthMillimetres;
                case PreviewCabinetKind.BusPlugBox:
                    return BusPlugWidthMillimetres;
                default:
                    return ILineWidthMillimetres;
            }
        }

        /// <summary>
        /// 占位方块的放大倍数：让宽度不小于取景对角线的
        /// <see cref="MinimumWidthViewDiagonalFraction"/>，不低于 1、不超过
        /// <see cref="MaximumVisualScale"/>。viewDiagonal 是楼层平面 + 线路的
        /// 包围盒对角线（mm）。
        /// </summary>
        public static double VisualScale(PreviewCabinetKind kind,
            double viewDiagonal)
        {
            if (double.IsNaN(viewDiagonal) || double.IsInfinity(viewDiagonal)
                || viewDiagonal <= 0.0) return 1.0;
            double baseWidth = BaseWidthMillimetres(kind);
            if (baseWidth <= 0.0) return 1.0;
            double scale = MinimumWidthViewDiagonalFraction * viewDiagonal
                / baseWidth;
            if (scale < 1.0) return 1.0;
            return scale > MaximumVisualScale ? MaximumVisualScale : scale;
        }

        /// <summary>
        /// 盒子沿场景 X / Y 的尺寸。重建出来的路线只走 X/Y/Z 三个方向，朝向
        /// 不是沿 X 就是沿 Y，盒子按轴对齐画即可。
        /// </summary>
        public double SizeX => Math.Abs(DirectionX) >= Math.Abs(DirectionY)
            ? Depth : Width;
        public double SizeY => Math.Abs(DirectionX) >= Math.Abs(DirectionY)
            ? Width : Depth;
    }
}

using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace UNCAD.UI
{
    /// <summary>
    /// 3.4.0：3D 预览场景的**哑渲染中间层**——两个构建器（U1U 信封构建器/
    /// U13D 批量信封适配器）把各自的数据源解析成这里的纯几何图元，视口控件
    /// （QuickLine3dViewportControl）只负责把它们画出来。分层铁律不变：
    /// 场景语义（建筑模型公式/路由配色/标签锚点）全在构建器，视口零推导。
    /// </summary>
    internal enum QuickLine3dSceneGroupKind
    {
        /// <summary>路由主体（管线/节点/端点钉）——常显。</summary>
        Routes,

        /// <summary>柱网（轴线/刻度/罗盘）——「柱网」开关。</summary>
        Grid,

        /// <summary>结构柱——U1U 的独立「柱子」开关。</summary>
        Columns,

        /// <summary>参照建筑模型（楼板/环氧/华夫/设备/母线玻璃/地面/标高线框）——「参照」开关。</summary>
        Reference,

        /// <summary>结构细节（工字钢梁阵/葡萄架格栅）——默认隐藏，「结构」开关。</summary>
        Detail,

        /// <summary>方向标记（U13D 上/下游标记）——常显。</summary>
        Direction
    }

    /// <summary>场景标签（叠加层渲染；Group 决定随哪个开关显隐）。</summary>
    internal sealed class QuickLine3dSceneLabel
    {
        public QuickLine3dSceneLabel(string text, double x, double y, double z,
            QuickLine3dSceneGroupKind group, string kind)
        {
            Text = text ?? string.Empty;
            X = x;
            Y = y;
            Z = z;
            Group = group;
            Kind = kind ?? "mm";
        }

        public string Text { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public QuickLine3dSceneGroupKind Group { get; }
        /// <summary>样式键（mm/axis/ref/pin/route/axisNorth…决定颜色字号）。</summary>
        public string Kind { get; }
    }

    /// <summary>图元种类。</summary>
    internal enum QuickLine3dPrimitiveKind
    {
        Tube,
        Box,
        Quad,
        WaffleCell,
        Sphere
    }

    /// <summary>一个几何图元（世界坐标 mm；颜色/透明度作用于整个图元）。</summary>
    internal sealed class QuickLine3dPrimitive
    {
        internal QuickLine3dPrimitive(QuickLine3dPrimitiveKind kind)
        {
            Kind = kind;
        }

        public QuickLine3dPrimitiveKind Kind { get; }
        public QuickLine3dSceneGroupKind Group { get; internal set; }

        // Tube/Quad：端点/角点。Sphere：A=球心，Radius。Box：A=中心，Size=尺寸。
        public double A1x, A1y, A1z;
        public double B1x, B1y, B1z;
        public double A2x, A2y, A2z;
        public double B2x, B2y, B2z;
        public double Radius;
        public double Sizex, Sizey, Sizez;
        public Color Tint;
        /// <summary>1=不透明；&lt;1 半透明（半透明图元双面渲染）。</summary>
        public double Opacity = 1.0;
        /// <summary>不透明盒顶面描边颜色（透明度 0=不描）。</summary>
        public Color EdgeTint;
    }

    /// <summary>一整个 3D 场景（图元 + 标签 + 相机取景参数）。</summary>
    internal sealed class QuickLine3dScene
    {
        internal QuickLine3dScene()
        {
            Primitives = new List<QuickLine3dPrimitive>();
            Labels = new List<QuickLine3dSceneLabel>();
        }

        public List<QuickLine3dPrimitive> Primitives { get; }
        public List<QuickLine3dSceneLabel> Labels { get; }

        /// <summary>世界对角线（mm）——线径/字号换算基准。</summary>
        public double Diagonal { get; internal set; } = 1.0;

        /// <summary>正交投影（U13D 批量页口径；false=透视，U1U 预览口径）。</summary>
        public bool Orthographic { get; internal set; }

        /// <summary>默认相机：场景中心 + 方位方向 × 距离（透视）/ 视口高度（正交）。</summary>
        public double Centerx { get; internal set; }
        public double Centery { get; internal set; }
        public double Centerz { get; internal set; }
        public double Orbitx { get; internal set; } = -1.0;
        public double Orbitsy { get; internal set; } = -1.0;
        public double Orbitz { get; internal set; } = 0.57735026918962573;
        public double CameraDistance { get; internal set; } = 2000.0;
        /// <summary>正交投影视口高度（mm）；透视投影忽略。</summary>
        public double OrthoViewHeight { get; internal set; } = 4000.0;
        public double Near { get; internal set; } = 1.0;
        public double Far { get; internal set; } = 100000.0;

        /// <summary>加一根圆柱管（6 棱柱，对齐 three.js CylinderGeometry radialSegments=6）。</summary>
        public void AddTube(QuickLine3dSceneGroupKind group,
            double ax, double ay, double az,
            double bx, double by, double bz,
            double radius, Color tint)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            if (dx * dx + dy * dy + dz * dz < 1e-18) return;
            var primitive = new QuickLine3dPrimitive(QuickLine3dPrimitiveKind.Tube)
            {
                Group = group,
                A1x = ax, A1y = ay, A1z = az,
                B1x = bx, B1y = by, B1z = bz,
                Radius = radius,
                Tint = tint
            };
            Primitives.Add(primitive);
        }

        /// <summary>加一个盒子（center=中心，size=三向尺寸；不透明盒可选顶面描边）。</summary>
        public void AddBox(QuickLine3dSceneGroupKind group,
            double centerx, double centery, double centerz,
            double sizex, double sizey, double sizez,
            Color tint, double opacity, Color edgeTint)
        {
            if (sizex <= 0 || sizey <= 0 || sizez <= 0) return;
            var primitive = new QuickLine3dPrimitive(QuickLine3dPrimitiveKind.Box)
            {
                Group = group,
                A1x = centerx, A1y = centery, A1z = centerz,
                Sizex = sizex, Sizey = sizey, Sizez = sizez,
                Tint = tint,
                Opacity = opacity,
                EdgeTint = edgeTint
            };
            Primitives.Add(primitive);
            if (opacity >= 1.0 && edgeTint.A > 0)
            {
                // 顶面描边：四根细条（对齐 three.js EdgesGeometry 的顶圈观感，
                // 全 12 棱在深底上太碎——板类模型只描顶圈即可读）。
                double halfx = sizex / 2.0, halfy = sizey / 2.0;
                double topz = centerz + sizez / 2.0;
                double edgeRadius = Math.Max(Math.Min(sizex, Math.Min(sizey, sizez))
                    * 0.06, Diagonal * 0.0004);
                AddRectOutline(group, centerx - halfx, centery - halfy,
                    centerx + halfx, centery + halfy, topz, edgeTint, edgeRadius);
            }
        }

        /// <summary>加一圈矩形描边（四根水平细条）。</summary>
        public void AddRectOutline(QuickLine3dSceneGroupKind group,
            double minx, double miny, double maxx, double maxy, double z,
            Color tint, double radius)
        {
            AddTube(group, minx, miny, z, maxx, miny, z, radius, tint);
            AddTube(group, maxx, miny, z, maxx, maxy, z, radius, tint);
            AddTube(group, maxx, maxy, z, minx, maxy, z, radius, tint);
            AddTube(group, minx, maxy, z, minx, miny, z, radius, tint);
        }

        /// <summary>加一块双面四边形板（a→b→c→d 逆时针；半透明板不写深度）。</summary>
        public void AddQuad(QuickLine3dSceneGroupKind group,
            double ax, double ay, double az,
            double bx, double by, double bz,
            double cx, double cy, double cz,
            double dx, double dy, double dz,
            Color tint, double opacity)
        {
            var primitive = new QuickLine3dPrimitive(QuickLine3dPrimitiveKind.Quad)
            {
                Group = group,
                A1x = ax, A1y = ay, A1z = az,
                B1x = bx, B1y = by, B1z = bz,
                A2x = cx, A2y = cy, A2z = cz,
                B2x = dx, B2y = dy, B2z = dz,
                Tint = tint,
                Opacity = opacity
            };
            Primitives.Add(primitive);
        }

        /// <summary>加一块带圆孔的华夫板单元；孔圆心居中，外沿按矩形单元裁切。</summary>
        public void AddWaffleCell(QuickLine3dSceneGroupKind group,
            double minx, double miny, double maxx, double maxy, double z,
            double holeRadius, Color tint, double opacity)
        {
            if (maxx <= minx || maxy <= miny || holeRadius <= 0
                || holeRadius >= Math.Min(maxx - minx, maxy - miny) / 2.0)
                return;
            Primitives.Add(new QuickLine3dPrimitive(
                QuickLine3dPrimitiveKind.WaffleCell)
            {
                Group = group,
                A1x = minx, A1y = miny, A1z = z,
                B1x = maxx, B1y = maxy,
                Radius = holeRadius,
                Tint = tint,
                Opacity = opacity
            });
        }

        /// <summary>加一个低模球。</summary>
        public void AddSphere(QuickLine3dSceneGroupKind group,
            double centerx, double centery, double centerz,
            double radius, Color tint)
        {
            if (radius <= 0) return;
            var primitive = new QuickLine3dPrimitive(QuickLine3dPrimitiveKind.Sphere)
            {
                Group = group,
                A1x = centerx, A1y = centery, A1z = centerz,
                Radius = radius,
                Tint = tint
            };
            Primitives.Add(primitive);
        }

        public void AddLabel(QuickLine3dSceneGroupKind group, string kind,
            string text, double x, double y, double z)
        {
            if (string.IsNullOrEmpty(text)) return;
            Labels.Add(new QuickLine3dSceneLabel(text, x, y, z, group, kind));
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;

namespace UNCAD.Cad
{
    /// <summary>一次 U1B 绘制的产出实体（均为新实体 ObjectId；标注可空=用户跳过）。</summary>
    public sealed class HoseDrawResult
    {
        public ObjectId OutlineId { get; set; }
        public ObjectId LeaderId { get; set; }
        public ObjectId TextId { get; set; }
        /// <summary>中心线折线长度（图形单位=毫米口径），供标注默认值取整。</summary>
        public double CenterlineLength { get; set; }
    }

    /// <summary>
    /// U1B 软管绘制服务（3.3.3，D-054 Amendment）。在调用方传入的写事务里生成
    /// 平面软管：闭合 LWPolyline 轮廓（HoseOutlinePlanner：双线偏移+外拐角圆弧
    /// +内拐角截断+首尾半圆端帽，凸度进顶点——**不用** ConstantWidth，管壁是
    /// 真实轮廓线）+ 可选引线（路径长度中点→点击点）+ 可选标注文字。
    /// 全部实体落在法定图层 U_HOSE（缺层幂等创建，色号 3 绿；建层失败回退当前
    /// 图层不阻断），实体色 ACI 3，挂 ParallelAnnotationMetadata KIND="U1B"
    /// （SOURCE=轮廓自身句柄，把一趟输出组在一起——图层矫正与归属链由此认得；
    /// 扫描器 TryRead 命中即跳过，管子/引线不会被当成电缆路由）。事务归调用方
    /// （一次 U 连同管子/引线/文本全回退）。
    /// </summary>
    public static class HoseCadService
    {
        /// <summary>软管实体色（ACI 3 绿，与 U_HOSE 图层默认色同族）。</summary>
        public const short HoseColorIndex = UncadLayerStandard.HoseColorIndex;

        /// <summary>
        /// 生成软管轮廓（不含标注）。path 至少 2 点；规划失败（退化路径/非法直径）
        /// 返回 null——调用方礼貌退出。事务归调用方。
        /// </summary>
        public static HoseDrawResult DrawOutline(CadContext ctx, Transaction transaction,
            IReadOnlyList<QuickLinePoint> path, double diameter)
        {
            if (ctx == null || transaction == null) return null;
            if (!HoseOutlinePlanner.TryPlan(path, diameter,
                    out List<HoseOutlineVertex> outline, out double length))
                return null;

            // U_HOSE 缺层幂等创建；失败回退当前图层（不让图层规范阻断绘制）。
            ObjectId hoseLayerId = LayerGovernorService.EnsureLayer(ctx, transaction,
                UncadLayerStandard.HoseLayerName);
            bool metadataReady =
                ParallelAnnotationMetadata.EnsureApplication(ctx.Db, transaction);

            var polyline = new Polyline(outline.Count);
            polyline.SetDatabaseDefaults(ctx.Db);
            for (int i = 0; i < outline.Count; i++)
                polyline.AddVertexAt(i,
                    new Point2d(outline[i].Point.X, outline[i].Point.Y),
                    outline[i].Bulge, 0.0, 0.0);
            polyline.Closed = true;
            polyline.ColorIndex = HoseColorIndex;
            if (!hoseLayerId.IsNull) polyline.LayerId = hoseLayerId;
            ObjectId outlineId = ctx.AddToCurrentSpace(transaction, polyline);

            // SOURCE=轮廓自身句柄：U1B 没有宿主电缆线，自引用把一趟输出组在
            // 一起；宿主解析（MapHostSegments）在扫描段池里找不到它，输出自然
            // 不属于任何图框——与「宿主已删/未知即跳过」同口径，不猜。
            if (metadataReady)
                ParallelAnnotationMetadata.Set(polyline,
                    UncadLayerStandard.HoseAnnotationKind,
                    outlineId.Handle.ToString());
            return new HoseDrawResult
            {
                OutlineId = outlineId,
                CenterlineLength = length
            };
        }

        /// <summary>
        /// 补画标注：引线（path 长度中点→labelPoint）+ 单行 DBText（底边中点贴
        /// labelPoint，可直接编辑）。文字内容调用方已定（默认值见
        /// HoseAnnotationDefaults）。与轮廓同事务。
        /// </summary>
        public static void DrawAnnotation(CadContext ctx, Transaction transaction,
            HoseDrawResult result, IReadOnlyList<QuickLinePoint> path,
            Point3d labelPoint, string labelText, double textHeight)
        {
            if (ctx == null || transaction == null || result == null
                || result.OutlineId.IsNull || path == null || path.Count < 2
                || string.IsNullOrWhiteSpace(labelText)) return;

            ObjectId hoseLayerId = LayerGovernorService.EnsureLayer(ctx, transaction,
                UncadLayerStandard.HoseLayerName);
            bool metadataReady =
                ParallelAnnotationMetadata.EnsureApplication(ctx.Db, transaction);
            ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, transaction);

            QuickLinePoint mid = HoseOutlinePlanner.PointAtDistance(path,
                result.CenterlineLength / 2.0);
            var leader = new Line(new Point3d(mid.X, mid.Y, 0), labelPoint)
            {
                ColorIndex = HoseColorIndex
            };
            if (!hoseLayerId.IsNull) leader.LayerId = hoseLayerId;
            result.LeaderId = ctx.AddToCurrentSpace(transaction, leader);

            var text = EntityFactory.DBText(ctx, labelText, labelPoint,
                textHeight, 0.0, AttachmentPoint.BottomCenter, HoseColorIndex,
                styleId);
            if (!hoseLayerId.IsNull) text.LayerId = hoseLayerId;
            result.TextId = ctx.AddToCurrentSpace(transaction, text);

            if (metadataReady)
            {
                string source = result.OutlineId.Handle.ToString();
                ParallelAnnotationMetadata.Set(leader,
                    UncadLayerStandard.HoseAnnotationKind, source);
                ParallelAnnotationMetadata.Set(text,
                    UncadLayerStandard.HoseAnnotationKind, source);
            }
        }
    }
}

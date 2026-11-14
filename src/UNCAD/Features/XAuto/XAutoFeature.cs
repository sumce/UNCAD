using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Contracts;
using UNCAD.Core.Fill;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Submit;
using UNCAD.Infra;

namespace UNCAD.Features.XAuto
{
    /// <summary>
    /// Xauto：框选完整图框，按毫米标注自动重排等测草图比例并自动开拱桥。
    ///
    /// 重排：复用 3D 预览的等测重建链路，把显示距离换成真实毫米值重新
    /// 展开，再投影回草图平面；短段钳到最小可读长度，包络中心锚定。
    /// 拱桥：交叉处靠近相机的一侧开半圆洞（直径沿用 U1SET 的 U1R 设置），
    /// 无法判定深度的交叉跳过并点名。整个命令一个写事务，一次 U 全回退。
    /// </summary>
    [Feature("xauto", "自动优化",
        Commands = CommandIds.XAutoFeatureCommands,
        Description = "框选图框，按毫米标注自动重排比例并自动开拱桥")]
    public sealed class XAutoFeature : CommandBase
    {
        [CommandMethod(CommandIds.XAuto, CommandFlags.UsePickSet)]
        public void AutoOptimize() => Run();

        private sealed class FramePlan
        {
            public string Name;
            public List<QuickLineCadSegment> Segments = new List<QuickLineCadSegment>();
            public SketchProportionPlan Proportion;
            public Dictionary<string, SketchSegmentMove> MovesById
                = new Dictionary<string, SketchSegmentMove>(
                    StringComparer.OrdinalIgnoreCase);
            public int MovedCount;
        }

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.XAuto);

            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n[Xauto] 框选完整图框: ", new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[Xauto] 未选择图框，未执行。");
                return;
            }

            FrameRegionCollection regions = FrameRegionCollector
                .CollectForLayout(ctx, selected);
            if (regions.Errors.Count > 0)
                throw new InvalidOperationException(
                    string.Join("\n", regions.Errors));
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[Xauto] 未找到完整的 frame/frame_20260812/xframe/xframe_v2 图框。");
                return;
            }

            double diameter = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0);
            // 最小可读长度 = 1.5×洞径：洞和毫米标在短段上放得下。
            double minFinalUnits = diameter * 1.5;

            IReadOnlyList<QuickLineCadSegment> scanned =
                QuickLineCadService.Scan(ctx, new QuickLineScanOptions
                {
                    IncludeUnlabelled = true
                });
            List<FramePlan> frames = BucketSegmentsByFrame(regions.Groups,
                scanned, ctx);
            if (frames.Count == 0)
            {
                ctx.Write("\n[Xauto] 选中的图框内没有 U1L 线段。");
                return;
            }

            var diagnostics = new List<string>();
            foreach (FramePlan frame in frames)
            {
                if (frame.Segments.Count == 0) continue;
                var inputs = frame.Segments.Select(item => new SketchProportionInput(
                    item.LineId.Handle.ToString(),
                    new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                    new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y),
                    item.LabelMillimetres, item.HasCompletionMarker,
                    QuickLineMillimeterText.IsPlaceholder(item.LabelText)))
                    .ToList();
                frame.Proportion = SketchProportionPlanner.Plan(inputs,
                    minFinalUnits);
                foreach (SketchSegmentMove move in frame.Proportion.Moves)
                    frame.MovesById[move.Id] = move;
                frame.MovedCount = frame.Proportion.Moves.Count;
                foreach (string diagnostic in frame.Proportion.Diagnostics)
                    diagnostics.Add(frame.Name + "：" + diagnostic);
            }

            // 交叉检测在全部图框的最终几何上一次完成（跨图框交叉也能识别）。
            var crossingSegments = new List<RouteCrossingSegment>();
            foreach (FramePlan frame in frames)
                foreach (QuickLineCadSegment segment in frame.Segments)
                    crossingSegments.Add(ToCrossingSegment(segment, frame));
            RouteCrossingPlan crossing = RouteCrossingPlanner.Plan(
                crossingSegments, diameter);
            diagnostics.AddRange(crossing.Skipped);
            diagnostics.AddRange(crossing.DroppedHops);

            int totalMoves = frames.Sum(item => item.MovedCount);
            ctx.Write("\n[Xauto] 计划：图框 " + frames.Count + " 个，重排 "
                + totalMoves + " 段，拱桥 " + crossing.HopCount + " 个，跳过 "
                + crossing.Skipped.Count + " 处交叉。");
            if (totalMoves == 0 && crossing.HopCount == 0)
            {
                if (diagnostics.Count > 0)
                    foreach (string diagnostic in diagnostics)
                        ctx.Write("\n[Xauto] " + diagnostic);
                ctx.Write("\n[Xauto] 没有需要处理的线路。");
                return;
            }
            if (diagnostics.Count > 0)
                ctx.Write("\n[Xauto] 有 " + diagnostics.Count
                    + " 条提示，确认后随结果一并列出。");
            if (!Confirm(ctx))
            {
                ctx.Write("\n[Xauto] 已取消，图纸未修改。");
                return;
            }

            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                try
                {
                    ApplyMoves(ctx, tr, frames);
                    ApplyHops(ctx, tr, frames, crossing, diameter);
                    tr.Commit();
                }
                catch
                {
                    tr.Abort();
                    throw;
                }
            }

            foreach (string diagnostic in diagnostics)
                ctx.Write("\n[Xauto] " + diagnostic);
            ctx.Write("\n[Xauto] 完成：重排 " + totalMoves + " 段，拱桥 "
                + crossing.HopCount + " 个（一次 U 可整体撤销）。");
        }

        private static bool Confirm(CadContext ctx)
        {
            var options = new PromptKeywordOptions(
                "\n[Xauto] 确认执行重排并开拱桥")
            {
                AllowNone = true
            };
            options.Keywords.Add("Proceed", "Proceed", "执行(Proceed)");
            options.Keywords.Add("Cancel", "Cancel", "取消(Cancel)");
            options.Keywords.Default = "Proceed";
            PromptResult choice = ctx.Ed.GetKeywords(options);
            return choice.Status == PromptStatus.OK
                && string.Equals(choice.StringResult, "Proceed",
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 把整份扫描结果按图框分桶（含未标注线，供"分量含未标注线整分量
        /// 跳过"的判定）；同一线段落入多个图框时只归第一个，避免重复处理。
        /// </summary>
        private static List<FramePlan> BucketSegmentsByFrame(
            List<FrameRegionGroup> groups,
            IReadOnlyList<QuickLineCadSegment> scanned, CadContext ctx)
        {
            var names = new List<string>(groups.Count);
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(tr);
                foreach (FrameRegionGroup group in groups)
                {
                    string name;
                    try
                    {
                        ExistingFillIdentity identity = FrameIdentityReader
                            .ReadIdentity(tr, group, definitions);
                        name = identity != null
                            && !string.IsNullOrWhiteSpace(identity.MachineId)
                            ? identity.MachineId + "-"
                                + (identity.DeviceName ?? string.Empty)
                            : "句柄 " + group.Handle;
                    }
                    catch
                    {
                        name = "句柄 " + group.Handle;
                    }
                    names.Add(name);
                }
                tr.Commit();
            }

            var frameIndexById = new Dictionary<ObjectId, int>();
            for (int index = 0; index < groups.Count; index++)
            {
                foreach (ObjectId id in groups[index].EntityIds)
                    if (!frameIndexById.ContainsKey(id))
                        frameIndexById[id] = index;
            }

            var result = new List<FramePlan>(groups.Count);
            for (int index = 0; index < groups.Count; index++)
                result.Add(new FramePlan { Name = names[index] });
            foreach (QuickLineCadSegment segment in scanned ?? 
                Array.Empty<QuickLineCadSegment>())
            {
                if (segment == null) continue;
                if (!frameIndexById.TryGetValue(segment.LineId, out int frameIndex))
                    continue;
                result[frameIndex].Segments.Add(segment);
            }
            return result;
        }

        private static RouteCrossingSegment ToCrossingSegment(
            QuickLineCadSegment segment, FramePlan frame)
        {
            QuickLinePoint start = new QuickLinePoint(segment.StartPoint.X,
                segment.StartPoint.Y);
            QuickLinePoint end = new QuickLinePoint(segment.EndPoint.X,
                segment.EndPoint.Y);
            double? closeness = null;
            QuickLineProjectionMode mode = QuickLineProjectionMode.Isometric;
            if (frame.MovesById.TryGetValue(segment.LineId.Handle.ToString(),
                    out SketchSegmentMove move))
            {
                start = move.NewStart;
                end = move.NewEnd;
            }
            if (frame.Proportion != null
                && frame.Proportion.Depths.TryGetValue(
                    segment.LineId.Handle.ToString(), out SketchSegmentDepth depth))
            {
                closeness = depth.Closeness;
                mode = depth.Mode;
            }
            return new RouteCrossingSegment(segment.LineId.Handle.ToString(),
                start, end, closeness, mode);
        }

        private static void ApplyMoves(CadContext ctx, Transaction tr,
            List<FramePlan> frames)
        {
            foreach (FramePlan frame in frames)
            {
                int labelSkipped = 0;
                foreach (QuickLineCadSegment segment in frame.Segments)
                {
                    string id = segment.LineId.Handle.ToString();
                    if (!frame.MovesById.TryGetValue(id, out SketchSegmentMove move))
                        continue;

                    var line = tr.GetObject(segment.LineId, OpenMode.ForWrite)
                        as Line;
                    if (line == null || line.IsErased) continue;
                    double startZ = line.StartPoint.Z;
                    double endZ = line.EndPoint.Z;
                    line.StartPoint = new Point3d(move.NewStart.X,
                        move.NewStart.Y, startZ);
                    line.EndPoint = new Point3d(move.NewEnd.X, move.NewEnd.Y,
                        endZ);

                    // 标注随线平移：保留原来的相对偏移（含 U1L 三种贴边位置）。
                    if (!segment.LabelId.IsNull && segment.LabelId.IsValid)
                    {
                        Entity label = tr.GetObject(segment.LabelId,
                            OpenMode.ForWrite, false) as Entity;
                        if (label is DBText || label is MText)
                        {
                            Point3d oldMid = segment.MidPoint;
                            var newMid = new Point3d(
                                (move.NewStart.X + move.NewEnd.X) / 2.0,
                                (move.NewStart.Y + move.NewEnd.Y) / 2.0,
                                oldMid.Z);
                            label.TransformBy(Matrix3d.Displacement(
                                newMid - oldMid));
                        }
                        else
                        {
                            labelSkipped++;
                        }
                    }
                }
                if (labelSkipped > 0)
                    ctx.Write("\n[Xauto] " + frame.Name + "：" + labelSkipped
                        + " 个尺寸/属性标注未随线移动。");
            }
        }

        private static void ApplyHops(CadContext ctx, Transaction tr,
            List<FramePlan> frames, RouteCrossingPlan crossing, double diameter)
        {
            var hopById = crossing.Hops.ToDictionary(item => item.SegmentId,
                StringComparer.OrdinalIgnoreCase);
            var segmentById = new Dictionary<string, QuickLineCadSegment>(
                StringComparer.OrdinalIgnoreCase);
            foreach (FramePlan frame in frames)
                foreach (QuickLineCadSegment segment in frame.Segments)
                    segmentById[segment.LineId.Handle.ToString()] = segment;

            foreach (KeyValuePair<string, RouteHopPlan> pair in hopById)
            {
                if (!segmentById.TryGetValue(pair.Key,
                        out QuickLineCadSegment segment)) continue;

                var line = tr.GetObject(segment.LineId, OpenMode.ForWrite) as Line;
                if (line == null || line.IsErased) continue;

                Point3d start = line.StartPoint;
                Point3d end = line.EndPoint;
                if (!ArchBridgePolyline.TryPlanVertices(start, end,
                        pair.Value.Centers, diameter,
                        out List<KeyValuePair<Point2d, double>> vertices,
                        out string warning))
                {
                    ctx.Write("\n[Xauto] 线段 " + pair.Key + " 开洞失败：" + warning);
                    continue;
                }
                Polyline merged = ArchBridgePolyline.BuildMerged(ctx.Db, line,
                    vertices);
                var owner = tr.GetObject(line.OwnerId, OpenMode.ForWrite)
                    as BlockTableRecord;
                if (owner == null)
                {
                    merged.Dispose();
                    ctx.Write("\n[Xauto] 线段 " + pair.Key + " 无法写入所在空间，开洞跳过。");
                    continue;
                }
                owner.AppendEntity(merged);
                tr.AddNewlyCreatedDBObject(merged, true);
                line.Erase();

                // Line→Polyline 后重挂 U1LX 关联（保留 COMPLETED 标记），
                // 下一次 U1LX/U1F 扫描仍能按元数据认领标注。
                if (!segment.LabelId.IsNull && segment.LabelId.IsValid)
                    QuickLineCadService.TryLinkLineAndLabel(ctx, tr, merged.Id,
                        segment.LabelId);
            }
        }
    }
}

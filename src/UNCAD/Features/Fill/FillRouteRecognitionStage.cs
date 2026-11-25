using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Excel;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// D-058：图框内的线路识别——给 U1U 的 3D 预览和问题清单供数，U1F/U1U
    /// 的连通性报告共用。识别口径（用户定义）：
    ///  - 电缆线段一定带毫米标，没有毫米标就是问题（记下来，不猜）；
    ///  - 桥架/线管是「旁边跟着的桥架/线管文字」——带归属元数据的按元数据
    ///    精确归属，没有元数据的文字就近绑定到平行的带毫米标线段；
    ///  - 紧贴带通道电缆线段的平行线是桥架/线管自己的标注线，不算漏标；
    ///  - 路线两端按连接块就近判定：离上游块最近的末端是上游端，离设备块
    ///    最近的是设备端。
    /// 全部只读，不改图纸、不进任何统计。
    /// </summary>
    internal static class FillRouteRecognitionStage
    {
        private const int HandlesShownPerIssue = 6;

        /// <summary>
        /// 体检之后做线路识别：补全通道（无元数据文字就近）、认出标注线、
        /// 列出没有毫米标的线段和其它问题。
        /// </summary>
        internal static void Recognize(FillRouteInspection inspection,
            List<QuickLineCadSegment> inScope,
            List<ParallelAnnotationOwnershipEntry> frameOutputs,
            FrameRectangle boundary,
            List<ParallelAnnotationOwnershipEntry> allOutputs,
            IReadOnlyList<QuickLineCadSegment> scanned,
            IReadOnlyList<RouteAnnotationTextEntity> annotationTexts)
        {
            if (inspection == null) return;
            List<QuickLineCadSegment> labelled = (inScope
                ?? new List<QuickLineCadSegment>())
                .Where(item => item != null && item.HasMillimetreLabel).ToList();
            List<QuickLineCadSegment> unlabelled = (inScope
                ?? new List<QuickLineCadSegment>())
                .Where(item => item != null && !item.HasMillimetreLabel).ToList();

            BindFreeAnnotationTexts(inspection, labelled, frameOutputs, boundary,
                allOutputs, scanned, annotationTexts);

            // 紧贴带通道电缆线段的平行线＝桥架/线管自己的标注线，不是漏标的电缆线。
            HashSet<string> offsetLines = RouteAnnotationLineClassifier.FindOffsetLines(
                unlabelled.Select(ToCoreSegment),
                labelled.Where(item => inspection.Channels.ContainsKey(
                    item.LineId.Handle.ToString())).Select(ToCoreSegment));
            foreach (QuickLineCadSegment line in unlabelled)
            {
                string handle = line.LineId.Handle.ToString();
                if (offsetLines.Contains(handle))
                    inspection.AnnotationLineHandles.Add(handle);
                else
                    inspection.UnlabelledSegmentHandles.Add(handle);
            }
            inspection.RouteIssues.AddRange(BuildDrawingIssues(inspection));
        }

        /// <summary>命令行输出识别结果与问题（U1U 单框）。</summary>
        internal static void WriteReport(CadContext ctx, string command,
            FillRouteInspection inspection)
        {
            if (inspection == null) return;
            if (inspection.PreviewFailure != null)
            {
                ctx.Write("\n[" + command + "] " + inspection.PreviewFailure
                    + "（不影响写入）。");
                return;
            }
            if (inspection.Preview == null)
            {
                ctx.Write("\n[" + command + "] 线路识别：图框内没有带毫米标的 U1L 线段，左侧无 3D 预览。");
            }
            else
            {
                ctx.Write("\n[" + command + "] 线路识别：" + inspection.RecognitionText + "。");
            }
            foreach (string issue in inspection.RouteIssues)
                ctx.Write("\n[" + command + "] 线路问题：" + issue);
        }

        /// <summary>
        /// 识别摘要一句话（3D 窗格底部与命令行共用）。
        /// </summary>
        internal static string BuildSummary(FillRouteInspection inspection,
            int sceneSegmentCount, string directionSource,
            QuickLineOrbitPreviewHose hose)
        {
            int tray = inspection.Channels.Values.Count(channel => channel.TrayWidthMillimetres.HasValue);
            int conduit = inspection.Channels.Values.Count(channel => channel.HasConduit);
            string summary = "识别 " + sceneSegmentCount + " 段带毫米标线段";
            if (inspection.Connectivity != null && inspection.Connectivity.GapJoins.Count > 0)
                summary += "（容错接上 " + inspection.Connectivity.GapJoins.Count + " 处断口）";
            summary += " · 桥架 " + tray + " 段 · 线管 " + conduit + " 段";
            if (hose != null)
                summary += " · 软管 " + Math.Round(hose.LengthMillimetres)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture) + "mm";
            else
                summary += " · 软管 —";
            return summary + " · " + directionSource;
        }

        /// <summary>
        /// 场景建好之后才能知道的两类问题：方向不是等测轴、进不了 3D 的线段，
        /// 以及上下游两端未能确认的原因。
        /// </summary>
        internal static void AddSceneIssues(FillRouteInspection inspection,
            List<QuickLineCadSegment> labelled, QuickLineIsometricScene scene,
            QuickLineRouteDirection direction)
        {
            var sceneIds = new HashSet<string>(scene.Segments.Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            List<string> skipped = labelled.Select(item => item.LineId.Handle.ToString())
                .Where(handle => !sceneIds.Contains(handle)).ToList();
            if (skipped.Count > 0)
                inspection.RouteIssues.Add(skipped.Count
                    + " 段带毫米标的线段方向不是等测轴方向，未进 3D："
                    + JoinHandles(skipped) + "——请把线段画成 30°/90°/150°（或正交）方向");
            if (!direction.IsResolved)
            {
                string reason = direction.Diagnostics.Count == 0
                    ? "路线末端不是两个" : string.Join("；", direction.Diagnostics);
                inspection.RouteIssues.Add("上下游两端未确认：" + reason);
            }
        }

        /// <summary>
        /// 路线两端：离上游块最近的末端是上游端，离设备块最近的末端是设备端
        /// （Core QuickLineRouteDirectionPlanner.ResolveFromAnchors）；两个块都
        /// 定位不到时 Core 自己退回工作簿轴位推断。
        /// </summary>
        internal static QuickLineRouteDirection ResolveDirection(
            QuickLineIsometricScene scene, QuickLineGraph graph,
            List<QuickLineCadSegment> labelled, Transaction transaction,
            FillSelection selection, MachineRow machine, out string source)
        {
            var segmentById = new Dictionary<string, QuickLineIsometricSegment>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
                if (!segmentById.ContainsKey(segment.Id)) segmentById[segment.Id] = segment;
            var terminals = new List<QuickLineRouteEndpointCandidate>();
            var allEndpoints = new List<QuickLineRouteEndpointCandidate>();
            foreach (QuickLineCadSegment line in labelled)
            {
                string handle = line.LineId.Handle.ToString();
                if (!segmentById.TryGetValue(handle, out QuickLineIsometricSegment segment))
                    continue;
                var start = new QuickLineRouteEndpointCandidate(segment.Id,
                    segment.StartNodeId,
                    new QuickLinePoint(line.StartPoint.X, line.StartPoint.Y));
                var end = new QuickLineRouteEndpointCandidate(segment.Id,
                    segment.EndNodeId,
                    new QuickLinePoint(line.EndPoint.X, line.EndPoint.Y));
                allEndpoints.Add(start);
                allEndpoints.Add(end);
                if (graph.GetConnections(segment.Id, QuickLineEndpoint.Start).Count == 0)
                    terminals.Add(start);
                if (graph.GetConnections(segment.Id, QuickLineEndpoint.End).Count == 0)
                    terminals.Add(end);
            }
            List<QuickLinePoint> upstream = ReadBlockPoints(transaction,
                selection?.UpstreamInfoBlockIds, selection?.UpstreamAxisBlockIds);
            List<QuickLinePoint> device = ReadBlockPoints(transaction,
                selection?.DeviceBlockIds, selection?.DownstreamAxisBlockIds);
            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, terminals, allEndpoints, upstream, device,
                    machine?.UpstreamAxis, machine?.DownstreamAxis);
            source = DescribeDirectionSource(direction.IsResolved,
                upstream.Count > 0, device.Count > 0);
            return direction;
        }

        /// <summary>
        /// 图框里的软管：长度先取 Ruanguan 软管块属性（与统计同源）；图框里
        /// 没有 Ruanguan 块、只有 U1B 软管标注文字时取文字（仅预览，不进统计）。
        /// </summary>
        internal static QuickLineOrbitPreviewHose ReadHose(Transaction transaction,
            FillSelection selection,
            List<ParallelAnnotationOwnershipEntry> frameOutputs)
        {
            try
            {
                double[] lengths = FillSelectionCollector.ReadRuanguanLengthMillimeters(
                    transaction, selection?.RuanguanBlockIds);
                if (lengths.Length > 0)
                {
                    double total = lengths.Sum();
                    if (total > 0 && !double.IsInfinity(total))
                        return new QuickLineOrbitPreviewHose(total,
                            ReadHoseDiameter(transaction, selection?.RuanguanBlockIds),
                            "Ruanguan 块", false);
                }
            }
            catch
            {
                // 块内长度不一致等错误由 U1U 主流程照常报告；预览里软管按未知处理。
            }
            foreach (ParallelAnnotationOwnershipEntry entry in frameOutputs
                ?? new List<ParallelAnnotationOwnershipEntry>())
            {
                if (!string.Equals(entry?.Kind, UncadLayerStandard.HoseAnnotationKind,
                        StringComparison.OrdinalIgnoreCase)) continue;
                string text = FillRouteInspectionStage.TryReadOutputText(transaction,
                    entry.EntityId);
                if (text != null && text.IndexOf("软管", StringComparison.Ordinal) >= 0
                    && RuanguanLengthParser.TryParseMillimetres(text, out double millimetres))
                {
                    RuanguanLengthParser.TryParseDiameterMillimetres(text, out double diameter);
                    return new QuickLineOrbitPreviewHose(millimetres, diameter,
                        text.Trim(), true);
                }
            }
            return null;
        }

        private static void BindFreeAnnotationTexts(FillRouteInspection inspection,
            List<QuickLineCadSegment> labelled,
            List<ParallelAnnotationOwnershipEntry> frameOutputs,
            FrameRectangle boundary,
            List<ParallelAnnotationOwnershipEntry> allOutputs,
            IReadOnlyList<QuickLineCadSegment> scanned,
            IReadOnlyList<RouteAnnotationTextEntity> annotationTexts)
        {
            if (boundary == null || annotationTexts == null) return;
            List<RouteAnnotationTextEntity> frameTexts = annotationTexts
                .Where(item => item.Stamp.Anchor != null
                    && boundary.Contains(item.Stamp.Anchor.X, item.Stamp.Anchor.Y))
                .ToList();
            if (frameTexts.Count == 0) return;

            // 带归属元数据、却不归属本框任何宿主线段的标注：宿主在整个图纸里
            // 都找不到时是「孤立标注」（源线被删除/合并），如实报出来。
            var attributed = new HashSet<string>(
                (frameOutputs ?? new List<ParallelAnnotationOwnershipEntry>())
                    .Select(entry => entry.EntityId.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> scannedHandles = null;
            foreach (RouteAnnotationTextEntity text in frameTexts)
            {
                if (!text.Stamp.HasOwnershipMetadata) continue;
                string handle = text.EntityId.Handle.ToString();
                if (attributed.Contains(handle)) continue;
                ParallelAnnotationOwnershipEntry entry = (allOutputs
                    ?? new List<ParallelAnnotationOwnershipEntry>())
                    .FirstOrDefault(item => item != null && item.EntityId == text.EntityId);
                if (entry == null || entry.HostHandles.Count == 0) continue;
                if (scannedHandles == null)
                    scannedHandles = new HashSet<string>((scanned
                        ?? Array.Empty<QuickLineCadSegment>())
                        .Select(item => item.LineId.Handle.ToString()),
                        StringComparer.OrdinalIgnoreCase);
                if (!entry.HostHandles.Any(host => scannedHandles.Contains(host)))
                    inspection.OrphanAnnotations.Add(FirstLine(text.Stamp.Text));
            }

            var owned = new HashSet<string>(inspection.Channels.Keys,
                StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<RouteAnnotationTextBinding> bindings =
                RouteAnnotationTextPlanner.Bind(
                    frameTexts.Where(item => !item.Stamp.HasOwnershipMetadata)
                        .Select(item => item.Stamp),
                    labelled.Select(ToCoreSegment), owned);
            foreach (RouteAnnotationTextBinding binding in bindings)
            {
                if (binding.Kind == RouteAnnotationTextKind.Tray)
                    inspection.Channels[binding.SegmentId] = new QuickLineOrbitPreviewChannel(
                        binding.TrayWidthMillimetres
                            ?? FillRouteInspectionStage.DefaultTrayWidthMillimetres,
                        binding.TrayWidthAssumedDefault, false);
                else if (inspection.Channels.TryGetValue(binding.SegmentId,
                    out QuickLineOrbitPreviewChannel tray))
                    inspection.Channels[binding.SegmentId] = new QuickLineOrbitPreviewChannel(
                        tray.TrayWidthMillimetres, tray.TrayWidthAssumedDefault, true);
                else
                    inspection.Channels[binding.SegmentId] = new QuickLineOrbitPreviewChannel(
                        null, false, true);
                inspection.TextBoundAnnotations.Add(binding);
            }
        }

        /// <summary>图纸层面就能判定的问题（不需要 3D 场景），逐条成句。</summary>
        internal static List<string> BuildDrawingIssues(FillRouteInspection inspection)
        {
            var issues = new List<string>();
            if (inspection.UnlabelledSegmentHandles.Count > 0)
                issues.Add(inspection.UnlabelledSegmentHandles.Count
                    + " 段线段没有毫米标（是电缆线请用 U1LX 补标；不是电缆线请忽略）："
                    + JoinHandles(inspection.UnlabelledSegmentHandles));
            IReadOnlyList<QuickLineGapJoin> gaps = inspection.Connectivity?.GapJoins;
            if (gaps != null && gaps.Count > 0)
                issues.Add("断口容差接上 " + gaps.Count + " 处（看着连着、放大有缝）："
                    + string.Join("、", gaps.Take(HandlesShownPerIssue).Select(join =>
                        join.SegmentId + "–" + join.ConnectedSegmentId + " 间隙 "
                        + Math.Round(join.Gap)))
                    + (gaps.Count > HandlesShownPerIssue ? " 等" : string.Empty));
            foreach (RouteAnnotationTextBinding binding in inspection.TextBoundAnnotations)
                issues.Add((binding.Kind == RouteAnnotationTextKind.Tray ? "桥架" : "线管")
                    + "文字「" + FirstLine(binding.Text) + "」没有归属元数据，已就近绑定到线段 "
                    + binding.SegmentId + "（只算这一段）");
            foreach (string text in inspection.OrphanAnnotations)
                issues.Add("桥架/线管标注「" + text + "」找不到宿主线段（源线被删除或合并），未显示");
            return issues;
        }

        private static string DescribeDirectionSource(bool resolved,
            bool hasUpstreamBlock, bool hasDeviceBlock)
        {
            if (!resolved) return "上下游两端未确认";
            if (hasUpstreamBlock && hasDeviceBlock)
                return "上游端/设备端＝离上游块/设备块最近的路线末端";
            if (hasUpstreamBlock) return "上游端＝离上游块最近的末端，设备端＝另一末端";
            if (hasDeviceBlock) return "设备端＝离设备块最近的末端，上游端＝另一末端";
            return "上下游两端按工作簿轴位推断（图框没有可定位的连接块）";
        }

        private static List<QuickLinePoint> ReadBlockPoints(Transaction transaction,
            IEnumerable<ObjectId> primary, IEnumerable<ObjectId> fallback)
        {
            var points = new List<QuickLinePoint>();
            if (transaction == null) return points;
            foreach (ObjectId id in (primary ?? Enumerable.Empty<ObjectId>())
                .Concat(fallback ?? Enumerable.Empty<ObjectId>()).Distinct())
            {
                BlockReference block;
                try
                {
                    block = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                }
                catch
                {
                    continue;
                }
                if (block == null || block.IsErased) continue;
                try
                {
                    points.Add(new QuickLinePoint(block.Position.X, block.Position.Y));
                }
                catch
                {
                    // 坏掉的代理块不影响其余锚点。
                }
            }
            return points;
        }

        private static double? ReadHoseDiameter(Transaction transaction,
            IEnumerable<ObjectId> blockIds)
        {
            if (transaction == null) return null;
            foreach (ObjectId id in blockIds ?? Enumerable.Empty<ObjectId>())
            {
                BlockReference block;
                try
                {
                    block = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                    if (block == null || block.IsErased) continue;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        if (!(transaction.GetObject(attributeId, OpenMode.ForRead, true)
                            is AttributeReference attribute) || attribute.IsErased) continue;
                        if (RuanguanLengthParser.TryParseDiameterMillimetres(
                            attribute.TextString, out double diameter))
                            return diameter;
                    }
                }
                catch
                {
                    // 读不到口径时软管只报长度。
                }
            }
            return null;
        }

        private static QuickLineSegment ToCoreSegment(QuickLineCadSegment item)
            => new QuickLineSegment(item.LineId.Handle.ToString(),
                new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y));

        private static string FirstLine(string text)
        {
            List<string> lines = TextParser.SplitMTextLines(text ?? string.Empty);
            return lines.Count > 0 ? lines[0] : string.Empty;
        }

        private static string JoinHandles(IEnumerable<string> handles)
        {
            List<string> list = handles.ToList();
            string shown = string.Join("、", list.Take(HandlesShownPerIssue));
            return list.Count > HandlesShownPerIssue
                ? shown + " 等 " + list.Count + " 段" : shown;
        }
    }
}

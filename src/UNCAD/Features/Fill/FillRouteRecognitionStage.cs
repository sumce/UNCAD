using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Cad.Frame;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>一趟桥架/线管标注（识别阶段的数据，进预览模型前再折算）。</summary>
    internal sealed class FillAnnotationRun
    {
        public RouteAnnotationTextKind Kind { get; set; }
        /// <summary>标注首行（型号/规格）。</summary>
        public string Label { get; set; } = "";
        /// <summary>整趟长度（mm）；标注里读不出为空。</summary>
        public double? LengthMillimetres { get; set; }
        public double? TrayWidthMillimetres { get; set; }
        /// <summary>这一趟盖住的电缆线段句柄（去重）。</summary>
        public List<string> HostHandles { get; } = new List<string>();
        /// <summary>元数据 / 红线就近 / 文字就近。</summary>
        public string RecoveredBy { get; set; } = "";
    }

    /// <summary>
    /// D-058/D-059：图框内的线路识别——给 U1U 的 3D 预览和问题清单供数，U1F/U1U
    /// 的连通性报告共用。识别口径（用户定义）：
    ///  - 电缆线段一定带毫米标；没有毫米标的线段、没有归属线段的毫米标都点名；
    ///  - 桥架/线管是「旁边跟着的红线 + 标注文字」：带归属元数据的按元数据精确
    ///    归属；对不上时把红线（一根或多根相连的多段线，可能被缩短）拆成小段，
    ///    每小段找旁边最近、平行、带毫米标的电缆；实在找不到照样报警。标注上的
    ///    长度是整趟的总长，不是每段各这么长；
    ///  - 上游/下游轴位块缺一不可，缺了报警；
    ///  - 路线两端按连接块就近判定：离上游块最近的末端是上游端，离设备块最近的
    ///    是设备端。
    /// 全部只读，不改图纸、不进任何统计。
    /// </summary>
    internal static class FillRouteRecognitionStage
    {
        private const int HandlesShownPerIssue = 6;
        private const double RedLineChainTolerance = 50.0;
        private const string ByMetadata = "元数据";
        private const string ByRedLine = "红线就近";

        /// <summary>
        /// 体检之后做线路识别：整趟清单（元数据 → 红线就近 → 文字就近）、认出
        /// 标注线、列出没有毫米标的线段 / 没有归属的毫米标 / 缺失的轴位块和其它问题。
        /// </summary>
        internal static void Recognize(FillRouteInspection inspection,
            List<QuickLineCadSegment> inScope,
            List<ParallelAnnotationOwnershipEntry> frameOutputs,
            FrameRectangle boundary,
            List<ParallelAnnotationOwnershipEntry> allOutputs,
            IReadOnlyList<RouteAnnotationTextEntity> annotationTexts,
            IReadOnlyList<QuickLineUnboundLabel> unboundLabels,
            Transaction readTransaction,
            FillSelection selection)
        {
            if (inspection == null) return;
            inScope = inScope ?? new List<QuickLineCadSegment>();
            List<QuickLineCadSegment> labelled = inScope
                .Where(item => item != null && item.HasMillimetreLabel).ToList();
            List<QuickLineCadSegment> unlabelled = inScope
                .Where(item => item != null && !item.HasMillimetreLabel).ToList();

            // ① 元数据直接归属上的整趟（通道已由 BuildChannels 建好，这里只登清单）。
            foreach (var group in (frameOutputs ?? new List<ParallelAnnotationOwnershipEntry>())
                .Where(entry => entry != null && IsTrayOrConduit(entry.Kind))
                .GroupBy(Signature))
                RegisterRun(inspection, group.ToList(), ByMetadata, labelled,
                    readTransaction);

            // ② 元数据对不上的整趟：红线就近恢复（恢复不了的进孤立清单）。
            RecoverByRedLines(inspection, labelled, frameOutputs, allOutputs,
                boundary, readTransaction);

            // ③ 没有元数据的标注文字：先找旁边的红线，再退回就近电缆。
            BindFreeAnnotationTexts(inspection, labelled, unlabelled, boundary,
                annotationTexts);

            // 紧贴带通道电缆线段的平行线＝桥架/线管自己的标注线，不是漏标的电缆线。
            HashSet<string> offsetLines = RouteAnnotationLineClassifier.FindOffsetLines(
                unlabelled.Select(ToCoreSegment),
                labelled.Where(item => inspection.Channels.ContainsKey(
                    item.Key)).Select(ToCoreSegment));
            var knownAnnotationLines = new HashSet<string>(
                inspection.AnnotationLineHandles, StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineCadSegment line in unlabelled)
            {
                string handle = line.Key;
                if (offsetLines.Contains(handle) || knownAnnotationLines.Contains(handle))
                    inspection.AnnotationLineHandles.Add(handle);
                else
                    inspection.UnlabelledSegmentHandles.Add(handle);
            }

            inspection.RouteIssues.AddRange(BuildDrawingIssues(inspection));
            inspection.RouteIssues.AddRange(BuildUnboundLabelIssues(unboundLabels));
            inspection.RouteIssues.AddRange(BuildAxisBlockIssues(selection));
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
        /// 识别摘要一句话（3D 窗格底部与命令行共用）：桥架/线管按「趟」计，
        /// 长度是各趟标注上的整趟总长之和。
        /// </summary>
        internal static string BuildSummary(FillRouteInspection inspection,
            int sceneSegmentCount, string directionSource,
            QuickLineOrbitPreviewHose hose)
        {
            string summary = "识别 " + sceneSegmentCount + " 段带毫米标线段";
            if (inspection.Connectivity != null && inspection.Connectivity.GapJoins.Count > 0)
                summary += "（容错接上 " + inspection.Connectivity.GapJoins.Count + " 处断口）";
            summary += " · " + DescribeRuns(inspection.AnnotationRuns,
                RouteAnnotationTextKind.Tray, "桥架");
            summary += " · " + DescribeRuns(inspection.AnnotationRuns,
                RouteAnnotationTextKind.Conduit, "线管");
            if (hose != null)
                summary += " · 软管 " + PreviewFloorPlan.Meters(hose.LengthMillimetres) + "m";
            else
                summary += " · 软管 —";
            if (!string.IsNullOrEmpty(inspection.FloorSummary))
                summary += " · " + inspection.FloorSummary;
            return summary + " · " + directionSource;
        }

        /// <summary>「桥架 2 趟 共 45m」「线管 1 趟 共 9.6m（1 趟未标长度）」。</summary>
        internal static string DescribeRuns(IEnumerable<FillAnnotationRun> runs,
            RouteAnnotationTextKind kind, string name)
        {
            List<FillAnnotationRun> mine = runs
                .Where(run => run.Kind == kind).ToList();
            if (mine.Count == 0) return name + " 0 趟";
            List<double> lengths = mine.Where(run => run.LengthMillimetres.HasValue)
                .Select(run => run.LengthMillimetres.Value).ToList();
            string text = name + " " + mine.Count + " 趟";
            if (lengths.Count > 0)
                text += " 共 " + PreviewFloorPlan.Meters(lengths.Sum()) + "m";
            if (lengths.Count < mine.Count)
                text += "（" + (mine.Count - lengths.Count) + " 趟未标长度）";
            return text;
        }

        internal static List<QuickLineOrbitPreviewAnnotationRun> ToPreviewRuns(
            FillRouteInspection inspection)
            => inspection.AnnotationRuns.Select(run => new QuickLineOrbitPreviewAnnotationRun(
                run.Kind, run.Label, run.LengthMillimetres, run.TrayWidthMillimetres,
                run.HostHandles, run.RecoveredBy)).ToList();

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
            List<string> skipped = labelled.Select(item => item.Key)
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
        /// 定位不到时 Core 自己退回工作簿轴位推断。上游端优先用上游轴位块、
        /// 设备端优先用下游轴位块（没有才退回信息块/设备块）。
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
                string handle = line.Key;
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
                selection?.UpstreamAxisBlockIds, selection?.UpstreamInfoBlockIds);
            List<QuickLinePoint> device = ReadBlockPoints(transaction,
                selection?.DownstreamAxisBlockIds, selection?.DeviceBlockIds);
            QuickLineRouteDirection direction = QuickLineRouteDirectionPlanner
                .ResolveFromAnchors(scene, terminals, allEndpoints, upstream, device,
                    machine?.UpstreamAxis, machine?.DownstreamAxis);
            source = DescribeDirectionSource(direction.IsResolved,
                upstream.Count > 0, device.Count > 0);
            return direction;
        }

        /// <summary>
        /// 图框里的软管：长度先取 Ruanguan 软管块属性（与统计同源）；图框里
        /// 没有 Ruanguan 块时取 U1B 软管标注——一条标注是一趟，长度是整趟的，
        /// 多条标注相加。
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
            double sum = 0;
            int count = 0;
            string source = "";
            foreach (ParallelAnnotationOwnershipEntry entry in frameOutputs
                ?? new List<ParallelAnnotationOwnershipEntry>())
            {
                if (!string.Equals(entry?.Kind, UncadLayerStandard.HoseAnnotationKind,
                        StringComparison.OrdinalIgnoreCase)) continue;
                string text = FillRouteInspectionStage.TryReadOutputText(transaction,
                    entry.EntityId);
                if (text == null || text.IndexOf("软管", StringComparison.Ordinal) < 0
                    || !RuanguanLengthParser.TryParseMillimetres(text, out double millimetres))
                    continue;
                sum += millimetres;
                count++;
                if (source.Length == 0) source = text.Trim();
            }
            if (count == 0) return null;
            RuanguanLengthParser.TryParseDiameterMillimetres(source, out double diameter);
            return new QuickLineOrbitPreviewHose(sum, diameter,
                count == 1 ? source : source + " 等 " + count + " 处", true);
        }

        // ---- 整趟清单 ---------------------------------------------------------------

        private static bool IsTrayOrConduit(string kind)
            => string.Equals(kind, UncadLayerStandard.TrayAnnotationKind,
                   StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, UncadLayerStandard.ConduitAnnotationKind,
                   StringComparison.OrdinalIgnoreCase);

        /// <summary>同一趟输出的签名：种类 + 宿主句柄集（SOURCE/RUN）。</summary>
        private static string Signature(ParallelAnnotationOwnershipEntry entry)
            => (entry?.Kind ?? "").ToUpperInvariant() + "|"
                + string.Join("|", (entry?.HostHandles ?? new List<string>())
                    .OrderBy(handle => handle, StringComparer.OrdinalIgnoreCase));

        private static RouteAnnotationTextKind KindOf(string kind)
            => string.Equals(kind, UncadLayerStandard.TrayAnnotationKind,
                StringComparison.OrdinalIgnoreCase)
                ? RouteAnnotationTextKind.Tray : RouteAnnotationTextKind.Conduit;

        private static string Handle(ParallelAnnotationOwnershipEntry entry)
            => entry.EntityId.Handle.ToString();

        private static string OutputText(Transaction transaction,
            ParallelAnnotationOwnershipEntry entry)
            => FillRouteInspectionStage.TryReadOutputText(transaction, entry.EntityId);

        /// <summary>
        /// 登记一趟。forcedHosts 为空时宿主＝这趟元数据里落在本框带毫米标线段上的
        /// 句柄；非空时是按红线恢复出来的宿主。
        /// </summary>
        private static void RegisterRun(FillRouteInspection inspection,
            List<ParallelAnnotationOwnershipEntry> entries, string recoveredBy,
            List<QuickLineCadSegment> labelled, Transaction transaction,
            IReadOnlyList<string> forcedHosts = null)
        {
            var run = new FillAnnotationRun
            {
                Kind = KindOf(entries[0].Kind),
                RecoveredBy = recoveredBy
            };
            string labelText = entries.Select(entry => OutputText(transaction, entry))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            if (labelText != null)
            {
                run.Label = FirstLine(labelText);
                run.LengthMillimetres = RouteAnnotationTextPlanner
                    .ParseLengthMillimetres(labelText);
                if (run.Kind == RouteAnnotationTextKind.Tray
                    && RouteAnnotationTextPlanner.TryParseTrayWidthMillimetres(
                        labelText, out double parsed))
                    run.TrayWidthMillimetres = parsed;
            }
            // 宿主句柄 → 本框里的线段键。带拐角的多段线拆成了几段、共用一个句柄：
            // 元数据指向这个句柄时，拆出的每一段都是这一趟的宿主。
            Dictionary<string, List<string>> keysByHandle = labelled
                .GroupBy(item => item.LineId.Handle.ToString(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key,
                    group => group.Select(item => item.Key).ToList(),
                    StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> hosts = forcedHosts ?? entries
                .SelectMany(entry => entry.HostHandles)
                .Where(handle => keysByHandle.ContainsKey(handle))
                .SelectMany(handle => keysByHandle[handle]);
            run.HostHandles.AddRange(hosts.Distinct(StringComparer.OrdinalIgnoreCase));
            if (run.HostHandles.Count == 0) return;
            // 元数据直接归属的通道已由 BuildChannels 建好（宽度取最宽），这里
            // 只给靠红线/文字恢复的趟补通道。
            if (recoveredBy != ByMetadata) ApplyChannel(inspection, run);
            inspection.AnnotationRuns.Add(run);
            if (recoveredBy == ByRedLine)
                inspection.RouteIssues.Add(RunName(run.Kind) + "标注「" + run.Label
                    + "」的宿主按红线就近恢复：" + run.HostHandles.Count + " 段电缆（"
                    + JoinHandles(run.HostHandles) + "），整趟 " + LengthText(run));
        }

        private static string LengthText(FillAnnotationRun run)
            => run.LengthMillimetres.HasValue
                ? PreviewFloorPlan.Meters(run.LengthMillimetres.Value) + "m"
                : "长度未标注";

        private static void ApplyChannel(FillRouteInspection inspection, FillAnnotationRun run)
        {
            foreach (string host in run.HostHandles)
            {
                inspection.Channels.TryGetValue(host,
                    out QuickLineOrbitPreviewChannel existing);
                if (run.Kind == RouteAnnotationTextKind.Tray)
                {
                    inspection.Channels[host] = new QuickLineOrbitPreviewChannel(
                        Math.Max(run.TrayWidthMillimetres
                            ?? FillRouteInspectionStage.DefaultTrayWidthMillimetres,
                            existing?.TrayWidthMillimetres ?? 0.0),
                        !run.TrayWidthMillimetres.HasValue
                            && !(existing?.TrayWidthMillimetres.HasValue ?? false),
                        existing?.HasConduit ?? false);
                }
                else
                {
                    inspection.Channels[host] = new QuickLineOrbitPreviewChannel(
                        existing?.TrayWidthMillimetres,
                        existing?.TrayWidthAssumedDefault ?? false, true);
                }
            }
        }

        // ---- ② 红线就近恢复 ----------------------------------------------------------

        /// <summary>
        /// 元数据宿主对不上的趟（源线被合并/删除）：读它自己的红线，把相邻的、
        /// 没有标注的同类红线也并进来（一个标注代表几趟），拆成小段逐段找最近的
        /// 带毫米标电缆。一段都找不到的进孤立清单。
        /// </summary>
        private static void RecoverByRedLines(FillRouteInspection inspection,
            List<QuickLineCadSegment> labelled,
            List<ParallelAnnotationOwnershipEntry> frameOutputs,
            List<ParallelAnnotationOwnershipEntry> allOutputs, FrameRectangle boundary,
            Transaction transaction)
        {
            if (boundary == null || transaction == null || allOutputs == null) return;
            var attributed = new HashSet<string>((frameOutputs
                    ?? new List<ParallelAnnotationOwnershipEntry>())
                .Where(entry => entry != null).Select(Handle),
                StringComparer.OrdinalIgnoreCase);
            List<IGrouping<string, ParallelAnnotationOwnershipEntry>> groups = allOutputs
                .Where(entry => entry != null && IsTrayOrConduit(entry.Kind))
                .GroupBy(Signature).ToList();

            // 没有自己标注的同类红线：留给相邻那一趟并入。
            var labelLessCurves = new List<IReadOnlyList<QuickLinePoint>>();
            var labelLessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, ParallelAnnotationOwnershipEntry> group in groups)
            {
                if (group.Any(entry => attributed.Contains(Handle(entry)))) continue;
                if (group.Any(entry => CurveOf(transaction, entry) == null)) continue;
                foreach (ParallelAnnotationOwnershipEntry entry in group)
                    AddCurve(labelLessCurves, CurveOf(transaction, entry));
                labelLessKeys.Add(group.Key);
            }

            List<QuickLineSegment> cables = labelled.Select(ToCoreSegment).ToList();
            foreach (IGrouping<string, ParallelAnnotationOwnershipEntry> group in groups)
            {
                if (labelLessKeys.Contains(group.Key)
                    || group.Any(entry => attributed.Contains(Handle(entry)))) continue;
                var pieces = new List<IReadOnlyList<QuickLinePoint>>();
                var labels = new List<ParallelAnnotationOwnershipEntry>();
                foreach (ParallelAnnotationOwnershipEntry entry in group)
                {
                    IReadOnlyList<QuickLinePoint> piece = CurveOf(transaction, entry);
                    if (piece != null) AddCurve(pieces, piece);
                    else labels.Add(entry);
                }
                if (labels.Count == 0) continue;
                // 这趟得跟本框有关：红线或标注文字落在图框边界里。
                bool touches = pieces.Any(piece => piece.Any(point =>
                    boundary.Contains(point.X, point.Y)));
                if (!touches)
                    touches = labels.Any(entry =>
                    {
                        Point3d? anchor = RouteAnnotationTextReader.TryReadTextAnchor(
                            transaction, entry.EntityId);
                        return anchor.HasValue
                            && boundary.Contains(anchor.Value.X, anchor.Value.Y);
                    });
                if (!touches) continue;

                var merged = new List<IReadOnlyList<QuickLinePoint>>(pieces);
                foreach (IReadOnlyList<QuickLinePoint> candidate in labelLessCurves)
                    if (TouchesAny(candidate, merged, RedLineChainTolerance))
                        merged.Add(candidate);
                IReadOnlyList<string> hosts = RouteAnnotationHostRecovery.ResolveHosts(
                    merged, cables);
                if (hosts.Count > 0)
                {
                    RegisterRun(inspection, group.ToList(), ByRedLine, labelled,
                        transaction, hosts);
                    continue;
                }
                foreach (ParallelAnnotationOwnershipEntry entry in labels)
                    inspection.OrphanAnnotations.Add(FirstLine(
                        OutputText(transaction, entry) ?? string.Empty));
            }
        }

        /// <summary>一条输出实体的折线：红线=顶点序列；文字/其它=null。</summary>
        private static IReadOnlyList<QuickLinePoint> CurveOf(Transaction transaction,
            ParallelAnnotationOwnershipEntry entry)
            => entry == null || entry.EntityId.IsNull ? null
                : RouteAnnotationTextReader.ReadCurvePoints(transaction, entry.EntityId);

        private static void AddCurve(List<IReadOnlyList<QuickLinePoint>> target,
            IReadOnlyList<QuickLinePoint> piece)
        {
            if (piece != null && piece.Count >= 2) target.Add(piece);
        }

        private static bool TouchesAny(IReadOnlyList<QuickLinePoint> candidate,
            List<IReadOnlyList<QuickLinePoint>> accepted, double tolerance)
        {
            if (candidate == null || candidate.Count == 0 || accepted.Count == 0)
                return false;
            QuickLinePoint a = candidate[0];
            QuickLinePoint b = candidate[candidate.Count - 1];
            foreach (IReadOnlyList<QuickLinePoint> piece in accepted)
                foreach (QuickLinePoint end in new[] { piece[0], piece[piece.Count - 1] })
                    if (end.DistanceTo(a) <= tolerance || end.DistanceTo(b) <= tolerance)
                        return true;
            return false;
        }

        // ---- ③ 没有元数据的标注文字 ------------------------------------------------------

        private static void BindFreeAnnotationTexts(FillRouteInspection inspection,
            List<QuickLineCadSegment> labelled, List<QuickLineCadSegment> unlabelled,
            FrameRectangle boundary,
            IReadOnlyList<RouteAnnotationTextEntity> annotationTexts)
        {
            if (boundary == null || annotationTexts == null) return;
            List<RouteAnnotationTextEntity> frameTexts = annotationTexts
                .Where(item => InBoundary(boundary, item.Stamp.Anchor)
                    || InBoundary(boundary, item.Stamp.Center))
                .ToList();
            if (frameTexts.Count == 0) return;
            var taken = new HashSet<string>(inspection.Channels.Keys,
                StringComparer.OrdinalIgnoreCase);
            var usedLines = new HashSet<string>(inspection.AnnotationLineHandles,
                StringComparer.OrdinalIgnoreCase);
            List<QuickLineSegment> cables = labelled.Select(ToCoreSegment).ToList();
            List<QuickLineSegment> unlabelledLines = unlabelled
                .Select(ToCoreSegment).ToList();
            foreach (RouteAnnotationTextEntity text in frameTexts
                .Where(item => !item.Stamp.HasOwnershipMetadata
                    && item.Stamp.Kind != RouteAnnotationTextKind.None)
                .OrderBy(item => item.Stamp.EntityId, StringComparer.OrdinalIgnoreCase))
            {
                // 先找标注旁边的红线：没有元数据的红线会被扫描成「没有毫米标的
                // 普通线段」，它紧贴标注；旧图框的文字可能和红线不平行，所以按
                // 文字外框中心找、按距离排序，再逐条候选链验证宿主。
                IReadOnlyList<IReadOnlyList<QuickLineSegment>> chains =
                    RouteAnnotationLineRecovery.FindRedLineChains(text.Stamp,
                        unlabelledLines, usedLines, out bool besideRedLine);
                List<string> hosts = new List<string>();
                IReadOnlyList<QuickLineSegment> acceptedChain = null;
                foreach (IReadOnlyList<QuickLineSegment> chain in chains)
                {
                    List<string> found = RouteAnnotationHostRecovery.ResolveHosts(
                        chain, cables)
                        .Where(host => !taken.Contains(host)).ToList();
                    if (found.Count == 0) continue;
                    hosts = found;
                    acceptedChain = chain;
                    break;
                }
                if (hosts.Count > 0)
                {
                    // 这条（这些）红线就是这趟桥架/线管的标注线，不能又当成
                    // 「没有毫米标的电缆线」再报一次。
                    foreach (QuickLineSegment line in acceptedChain)
                    {
                        if (usedLines.Add(line.Id))
                            inspection.AnnotationLineHandles.Add(line.Id);
                    }
                    var run = new FillAnnotationRun
                    {
                        Kind = text.Stamp.Kind,
                        Label = FirstLine(text.Stamp.Text),
                        RecoveredBy = ByRedLine,
                        LengthMillimetres = RouteAnnotationTextPlanner
                            .ParseLengthMillimetres(text.Stamp.Text)
                    };
                    if (run.Kind == RouteAnnotationTextKind.Tray
                        && RouteAnnotationTextPlanner.TryParseTrayWidthMillimetres(
                            text.Stamp.Text, out double width))
                        run.TrayWidthMillimetres = width;
                    run.HostHandles.AddRange(hosts);
                    ApplyChannel(inspection, run);
                    foreach (string host in hosts) taken.Add(host);
                    inspection.AnnotationRuns.Add(run);
                    inspection.RouteIssues.Add(RunName(run.Kind) + "标注「" + run.Label
                        + "」没有归属元数据，按旁边的红线就近归属到 " + hosts.Count
                        + " 段电缆（" + JoinHandles(hosts) + "），整趟 " + LengthText(run));
                    continue;
                }
                // 标注旁边根本没有红线：退回就近的一根带毫米标电缆（只算这一段）。
                // 有红线但找不到宿主电缆的，照旧进孤立清单（BuildDrawingIssues 点名）。
                if (besideRedLine) { NoteOrphan(inspection, text.Stamp.Text); continue; }
                foreach (RouteAnnotationTextBinding binding in RouteAnnotationTextPlanner.Bind(
                    new[] { text.Stamp }, cables, taken))
                {
                    var run = new FillAnnotationRun
                    {
                        Kind = binding.Kind,
                        Label = FirstLine(binding.Text),
                        RecoveredBy = "文字就近",
                        LengthMillimetres = RouteAnnotationTextPlanner
                            .ParseLengthMillimetres(binding.Text),
                        TrayWidthMillimetres = binding.TrayWidthMillimetres
                    };
                    run.HostHandles.Add(binding.SegmentId);
                    ApplyChannel(inspection, run);
                    taken.Add(binding.SegmentId);
                    inspection.AnnotationRuns.Add(run);
                    inspection.TextBoundAnnotations.Add(binding);
                }
            }
        }

        private static void NoteOrphan(FillRouteInspection inspection, string text)
        {
            string label = FirstLine(text);
            if (inspection.OrphanAnnotations.Contains(label)) return;
            inspection.OrphanAnnotations.Add(label);
        }

        private static bool InBoundary(FrameRectangle boundary, QuickLinePoint point)
            => boundary != null && boundary.Contains(point.X, point.Y);

        // ---- 问题清单 ---------------------------------------------------------------

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
                issues.Add(RunName(binding.Kind) + "标注「" + FirstLine(binding.Text)
                    + "」没有归属元数据，已就近绑定到线段 "
                    + binding.SegmentId + "（只算这一段）");
            foreach (string text in inspection.OrphanAnnotations)
                issues.Add("桥架/线管标注「" + text
                    + "」找不到宿主线段（旁边没有红线，或红线旁边没有带毫米标的电缆线），未显示");
            return issues;
        }

        /// <summary>D-059：没有归属线段的毫米标点名（设备预留标记不算）。</summary>
        internal static List<string> BuildUnboundLabelIssues(
            IReadOnlyList<QuickLineUnboundLabel> unboundLabels)
        {
            var issues = new List<string>();
            var listed = new List<string>();
            foreach (QuickLineUnboundLabel label in unboundLabels
                ?? Array.Empty<QuickLineUnboundLabel>())
            {
                if (!IsReportableUnbound(label)) continue;
                listed.Add("「" + label.Text.Trim() + "」(" + label.Handle + ")");
            }
            if (listed.Count == 0) return issues;
            issues.Add(listed.Count + " 个毫米标没有归属线段（每个毫米标必须归属一条线段；"
                + "请检查是否漏画线段，或把毫米标挪回它的线段旁）："
                + string.Join("、", listed.Take(HandlesShownPerIssue))
                + (listed.Count > HandlesShownPerIssue ? " 等 " + listed.Count + " 个" : ""));
            return issues;
        }

        /// <summary>
        /// 这个没有归属的毫米标要不要点名：设备预留标记（D-043：横排、整数结尾 0
        /// 的毫米文字，且没有悬空关联）不是电缆毫米标，不报。
        /// </summary>
        internal static bool IsReportableUnbound(QuickLineUnboundLabel label)
            => label != null && (label.AssociationDangling
                || !DeviceReserveMarker.IsHorizontal(label.Rotation)
                || !DeviceReserveMarker.TryParseMillimeters(label.Text, out _));

        /// <summary>D-059：上游/下游轴位块缺一不可。</summary>
        internal static List<string> BuildAxisBlockIssues(FillSelection selection)
        {
            var issues = new List<string>();
            if (selection == null) return issues;
            if ((selection.UpstreamAxisBlockIds?.Length ?? 0) == 0)
                issues.Add("图框缺少上游轴位块（上游/下游轴位块缺一不可），请补上后再 U1F/U1U");
            if ((selection.DownstreamAxisBlockIds?.Length ?? 0) == 0)
                issues.Add("图框缺少下游轴位块（上游/下游轴位块缺一不可），请补上后再 U1F/U1U");
            return issues;
        }

        private static string RunName(RouteAnnotationTextKind kind)
            => kind == RouteAnnotationTextKind.Tray ? "桥架" : "线管";

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
            => new QuickLineSegment(item.Key,
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

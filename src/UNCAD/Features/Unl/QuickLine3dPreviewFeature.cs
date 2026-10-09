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
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;
using UNCAD.Features.Fill;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace UNCAD.Features.Unl
{
    /// <summary>
    /// Collects one complete frame, reconstructs its U1L routes and shows the
    /// last refreshed machine axes in a read-only 3D preview.
    /// </summary>
    [Feature("unl-3d-preview", "U1L 三维预览",
        Commands = CommandIds.QuickLine3dPreviewFeatureCommands,
        Description = "框选完整图框并预览其中的 U1L 等测路线、机台上下游轴位。")]
    public sealed class QuickLine3dPreviewFeature : CommandBase
    {
        private static readonly Lazy<AnnotationModelIndex> AnnotationModels =
            new Lazy<AnnotationModelIndex>(
                () => AnnotationModelIndex.Build(ListItemReader.ReadEmbedded()), true);

        [CommandMethod(CommandIds.Line3dPreview, CommandFlags.UsePickSet)]
        public void Preview() => Run();

        [CommandMethod(CommandIds.Line3dBatchPreview, CommandFlags.UsePickSet)]
        public void BatchPreview() => Run(true);

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Line3dPreview);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n[U1L3D] 框选一个完整图框: ", new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0) return;

            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx,
                selected);
            if (regions.Errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", regions.Errors));
            if (regions.SelectedFrameCount != 1 || regions.Groups.Count != 1)
            {
                ctx.Write("\n[U1L3D] 请只框选一个完整的 frame/frame_20260812/xframe 图框，"
                    + "并确保图框块本身包含在选择集中。");
                return;
            }

            FrameRegionGroup region = regions.Groups[0];
            ExistingFillIdentity identity;
            Point2d? downstreamAnchor;
            Point2d? upstreamAnchor;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(transaction);
                identity = FrameIdentityReader.ReadIdentity(transaction, region,
                    definitions);
                FillSelection selection = FillSelectionCollector.Split(transaction,
                    region.EntityIds.ToArray(), true, definitions);
                downstreamAnchor = ReadAnchorPoint(transaction,
                    selection.DeviceBlockIds, selection.DownstreamAxisBlockIds);
                upstreamAnchor = ReadAnchorPoint(transaction,
                    selection.UpstreamInfoBlockIds, selection.UpstreamAxisBlockIds);
                transaction.Commit();
            }

            HashSet<ObjectId> frameEntityIds = new HashSet<ObjectId>(region.EntityIds);
            IReadOnlyList<QuickLineCadSegment> scanned = QuickLineCadService.Scan(ctx,
                new QuickLineScanOptions { IncludeUnlabelled = true });
            List<QuickLineCadSegment> frameLines = scanned
                .Where(item => frameEntityIds.Contains(item.LineId)).ToList();
            List<QuickLineCadSegment> labelled = frameLines
                .Where(item => item.HasMillimetreLabel).ToList();
            if (labelled.Count == 0)
            {
                ctx.Write("\n[U1L3D] 当前图框内没有带毫米标注的 U1L 线段。");
                return;
            }

            QuickLineIsometricScene scene = BuildScene(labelled);
            var anchorDiagnostics = new List<string>();
            IReadOnlyDictionary<string, QuickLineSpatialPoint> routeAnchors =
                ResolveRouteAnchorPositions(labelled, scene, downstreamAnchor,
                    upstreamAnchor, anchorDiagnostics);
            MachineRow machine = ReadMachine(identity, out List<string> diagnostics);
            diagnostics.AddRange(anchorDiagnostics);
            QuickLineAxisGrid axes = QuickLineAxisGridPlanner.Plan(
                machine?.UpstreamAxis, machine?.DownstreamAxis);
            Dictionary<string, object> envelope = BuildEnvelope(scene, axes,
                identity, machine, diagnostics, routeAnchors);
            using (var form = new U1L3DPreviewForm(envelope))
                AcApplication.ShowModalDialog(form);
        }

        protected override void Execute(CadContext ctx, object state)
        {
            if (state is bool && (bool)state)
            {
                ExecuteBatch(ctx);
                return;
            }
            Execute(ctx);
        }

        private static void ExecuteBatch(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Line3dBatchPreview);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n[U13D] 框选多个完整图框: ", new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0) return;

            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx,
                selected);
            if (regions.Errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", regions.Errors));
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[U13D] 未找到完整的 frame/frame_20260812/xframe 图框。");
                return;
            }

            IReadOnlyList<QuickLineCadSegment> scanned = QuickLineCadService.Scan(ctx,
                new QuickLineScanOptions { IncludeUnlabelled = true });
            var frames = new List<BatchFrameData>();
            var ordered = regions.Groups
                .OrderBy(item => item.Boundary,
                    Comparer<FrameRectangle>.Create(FrameRectangle.CompareReadingOrder))
                .ThenBy(item => item.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(transaction);
                foreach (FrameRegionGroup region in ordered)
                {
                    var frame = new BatchFrameData
                    {
                        Id = string.IsNullOrWhiteSpace(region.Handle)
                            ? "FRAME-" + frames.Count.ToString()
                            : region.Handle,
                        Boundary = region.Boundary,
                        Diagnostics = new List<string>()
                    };
                    try
                    {
                        frame.Identity = FrameIdentityReader.ReadIdentity(transaction,
                            region, definitions);
                    }
                    catch (System.Exception ex)
                    {
                        frame.Diagnostics.Add("图框身份读取失败：" + ex.Message);
                    }

                    var frameIds = new HashSet<ObjectId>(region.EntityIds);
                    frame.Labelled = scanned.Where(item => frameIds.Contains(item.LineId)
                        && item.HasMillimetreLabel).ToList();
                    FillSelection selection = FillSelectionCollector.Split(transaction,
                        region.EntityIds.ToArray(), true, definitions);
                    frame.DownstreamAnchor = ReadAnchorPoint(transaction,
                        selection.DeviceBlockIds, selection.DownstreamAxisBlockIds);
                    frame.UpstreamAnchor = ReadAnchorPoint(transaction,
                        selection.UpstreamInfoBlockIds, selection.UpstreamAxisBlockIds);
                    frame.Bridges = ReadBridges(transaction, region);
                    frames.Add(frame);
                }
                transaction.Commit();
            }

            foreach (BatchFrameData frame in frames)
            {
                if (frame.Labelled.Count == 0)
                {
                    frame.Diagnostics.Add("当前图框内没有带毫米标注的 U1L 线段。");
                    continue;
                }
                try
                {
                    frame.Scene = BuildScene(frame.Labelled);
                }
                catch (System.Exception ex)
                {
                    frame.Diagnostics.Add("U1L 路由重建失败：" + ex.Message);
                }
                if (frame.Scene != null)
                    frame.RouteAnchors = ResolveRouteAnchorPositions(
                        frame.Labelled, frame.Scene, frame.DownstreamAnchor,
                        frame.UpstreamAnchor, frame.Diagnostics);
                var machineDiagnostics = new List<string>();
                if (frame.Identity != null)
                    frame.Machine = ReadMachine(frame.Identity, out machineDiagnostics);
                else
                    frame.Machine = null;
                if (frame.Machine == null) frame.Diagnostics.AddRange(machineDiagnostics);
            }

            if (frames.All(item => item.Scene == null))
            {
                ctx.Write("\n[U13D] 选中的图框内没有可识别的等测 U1L 路由。");
                return;
            }

            Dictionary<string, object> envelope = BuildBatchEnvelope(frames);
            using (var form = new U1L3DPreviewForm(envelope))
                AcApplication.ShowModalDialog(form);
        }

        private static QuickLineIsometricScene BuildScene(
            IReadOnlyList<QuickLineCadSegment> labelled)
        {
            var coreSegments = labelled.Select(item => new QuickLineSegment(
                item.LineId.Handle.ToString(),
                new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))).ToList();
            QuickLineGraph graph = QuickLineGraph.Build(coreSegments, 1.0);
            var distances = labelled.ToDictionary(
                item => item.LineId.Handle.ToString(),
                item => item.LabelMillimetres.Value,
                StringComparer.OrdinalIgnoreCase);
            var displayDistances = labelled.ToDictionary(
                item => item.LineId.Handle.ToString(),
                item => !item.HasCompletionMarker
                    && QuickLineMillimeterText.IsPlaceholder(item.LabelText)
                        ? item.Length : item.LabelMillimetres.Value,
                StringComparer.OrdinalIgnoreCase);
            var completed = new HashSet<string>(labelled
                .Where(item => item.HasCompletionMarker)
                .Select(item => item.LineId.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
            return QuickLineIsometricSceneBuilder.BuildAll(graph, distances,
                displayDistances, completed);
        }

        private static Dictionary<string, string> BuildRouteGroupIds(
            IReadOnlyList<QuickLineCadSegment> labelled)
        {
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var coreSegments = (labelled ?? Array.Empty<QuickLineCadSegment>())
                .Where(item => item != null)
                .Select(item => new QuickLineSegment(
                    item.LineId.Handle.ToString(),
                    new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                    new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y)))
                .ToList();
            QuickLineGraph graph = QuickLineGraph.Build(coreSegments, 1.0);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int routeNumber = 0;
            foreach (QuickLineSegment segment in graph.Segments)
            {
                if (!visited.Add(segment.Id)) continue;
                string group = "R" + (++routeNumber).ToString();
                var queue = new Queue<string>();
                queue.Enqueue(segment.Id);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    result[current] = group;
                    foreach (QuickLineConnection connection in graph.GetConnections(current))
                        if (visited.Add(connection.ConnectedSegmentId))
                            queue.Enqueue(connection.ConnectedSegmentId);
                }
            }
            return result;
        }

        private static Dictionary<string, string> BuildSceneRouteGroupIds(
            QuickLineIsometricScene scene)
        {
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (scene == null) return result;
            var byNode = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                AddSceneSegment(byNode, segment.StartNodeId, segment.Id);
                AddSceneSegment(byNode, segment.EndNodeId, segment.Id);
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int routeNumber = 0;
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                if (!visited.Add(segment.Id)) continue;
                string group = "R" + (++routeNumber).ToString();
                var queue = new Queue<string>();
                queue.Enqueue(segment.Id);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    result[current] = group;
                    QuickLineIsometricSegment currentSegment = scene.Segments
                        .First(item => string.Equals(item.Id, current,
                            StringComparison.OrdinalIgnoreCase));
                    foreach (string nodeId in new[]
                    {
                        currentSegment.StartNodeId, currentSegment.EndNodeId
                    })
                    {
                        if (!byNode.TryGetValue(nodeId,
                                out List<string> neighbours)) continue;
                        foreach (string neighbour in neighbours)
                            if (visited.Add(neighbour)) queue.Enqueue(neighbour);
                    }
                }
            }
            return result;
        }

        private static void AddSceneSegment(
            IDictionary<string, List<string>> byNode,
            string nodeId, string segmentId)
        {
            if (!byNode.TryGetValue(nodeId, out List<string> values))
            {
                values = new List<string>();
                byNode[nodeId] = values;
            }
            values.Add(segmentId);
        }

        private static MachineRow ReadMachine(ExistingFillIdentity identity,
            out List<string> diagnostics)
        {
            diagnostics = new List<string>();
            if (identity == null)
            {
                diagnostics.Add("图框没有可用的机台身份。");
                return null;
            }
            string source = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            if (source.Length == 0)
            {
                diagnostics.Add("尚未配置机台工作簿；请在 U1SET 中配置并刷新数据。");
                return null;
            }
            try
            {
                if (!MachineWorkbookSource.TryGetSnapshot(source, out _))
                {
                    diagnostics.Add("机台工作簿尚无成功的 SQLite 快照；请执行 U1DATA 刷新。");
                    return null;
                }
                List<MachineRow> rows = MachineWorkbookSnapshotStore.Default
                    .ReadRowsForMachines(source, new[] { identity.MachineId });
                MachineRow machine = ExistingFillIdentityResolver.MatchMachine(
                    identity, rows, out string error);
                if (machine == null) diagnostics.Add(error);
                return machine;
            }
            catch (System.Exception ex)
            {
                diagnostics.Add("读取机台 SQLite 快照失败：" + ex.Message);
                return null;
            }
        }

        private static List<BridgePreviewInfo> ReadBridges(Transaction transaction,
            FrameRegionGroup region)
        {
            var bySource = new Dictionary<string, BridgePreviewInfo>(
                StringComparer.OrdinalIgnoreCase);
            var unlinked = new List<BridgePreviewInfo>();
            if (transaction == null || region == null) return unlinked;

            foreach (ObjectId id in region.EntityIds.Distinct())
            {
                Entity entity;
                try
                {
                    entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                }
                catch
                {
                    continue;
                }
                if (entity == null || entity.IsErased) continue;

                ParallelAnnotationMetadata.TryRead(entity, out string kind,
                    out string sourceHandle);
                bool generated = string.Equals(kind, "U1Q",
                    StringComparison.OrdinalIgnoreCase);
                if (generated && entity is Curve curve)
                {
                    BridgePreviewInfo bridge = GetOrCreateBridge(bySource,
                        sourceHandle);
                    try
                    {
                        bridge.GeometryLengthMm = Math.Max(bridge.GeometryLengthMm,
                            Math.Abs(curve.GetDistanceAtParameter(curve.EndParam)
                                - curve.GetDistanceAtParameter(curve.StartParam)));
                    }
                    catch { }
                    continue;
                }

                if (!(entity is DBText) && !(entity is MText)) continue;
                string raw = entity is DBText dbText
                    ? dbText.TextString : ((MText)entity).Contents;
                if (!TryReadBridgeLength(raw, out string spec,
                    out double millimetres)) continue;

                BridgePreviewInfo label = string.IsNullOrWhiteSpace(sourceHandle)
                    ? new BridgePreviewInfo()
                    : GetOrCreateBridge(bySource, sourceHandle);
                label.Spec = spec;
                label.LabelLengthMm = millimetres;
                label.SourceHandle = sourceHandle ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceHandle))
                    unlinked.Add(label);
            }

            var result = bySource.Values
                .Concat(unlinked)
                .Where(item => item.LabelLengthMm > 0 || item.GeometryLengthMm > 0)
                .ToList();
            foreach (BridgePreviewInfo bridge in result)
                if (bridge.LabelLengthMm <= 0)
                    bridge.LabelLengthMm = bridge.GeometryLengthMm;
            return result;
        }

        private static BridgePreviewInfo GetOrCreateBridge(
            IDictionary<string, BridgePreviewInfo> values, string sourceHandle)
        {
            string key = sourceHandle ?? string.Empty;
            if (!values.TryGetValue(key, out BridgePreviewInfo result))
            {
                result = new BridgePreviewInfo { SourceHandle = key };
                values[key] = result;
            }
            return result;
        }

        private static bool TryReadBridgeLength(string raw, out string spec,
            out double millimetres)
        {
            spec = null;
            millimetres = 0;
            string clean = TextParser.CleanMText(raw ?? string.Empty);
            List<string> lines = TextParser.SplitMTextLines(clean);
            List<string> collapsed = AnnotationLabelPair.CollapseStatisticsLines(lines);
            foreach (string line in collapsed)
            {
                if (TextParser.TryExtractBridgeLabel(line, 250.0,
                    out spec, out double grids))
                {
                    millimetres = grids * 250.0;
                    return millimetres > 0;
                }
                MatchBoqBridgeModel(line, out spec, out millimetres);
                if (!string.IsNullOrWhiteSpace(spec) && millimetres > 0)
                    return true;
            }
            return false;
        }

        private static void MatchBoqBridgeModel(string line, out string spec,
            out double millimetres)
        {
            spec = null;
            millimetres = 0;
            string value = (line ?? string.Empty).Trim();
            int split = value.LastIndexOf(' ');
            if (split <= 0) return;
            string model = value.Substring(0, split).Trim();
            string length = value.Substring(split + 1).Trim();
            if (!TextParser.IsBareLengthToken(length)) return;
            if (!AnnotationModels.Value.TryCollapse(model, length,
                out string legacy)
                || !TextParser.TryExtractBridgeLabel(legacy, 250.0,
                    out spec, out double grids)) return;
            millimetres = grids * 250.0;
        }

        private static Dictionary<string, object> BuildBatchEnvelope(
            IReadOnlyList<BatchFrameData> frames)
        {
            var inputs = frames.Select(item => new QuickLineAxisPlacementInput(
                item.Id, item.Machine?.DownstreamAxis ?? string.Empty,
                item.Machine?.UpstreamAxis ?? string.Empty)).ToList();
            IReadOnlyList<QuickLineAxisPlacement> placements =
                QuickLineAxisPlacementPlanner.Plan(inputs);
            var placementById = placements.ToDictionary(item => item.Id,
                StringComparer.OrdinalIgnoreCase);
            var nodes = new List<object>();
            var segments = new List<object>();
            var bridgeValues = new List<object>();
            var frameValues = new List<object>();
            var directionMarkers = new List<object>();
            var diagnostics = new List<string>();
            int routeFrameCount = 0;
            int frameIndex = 0;

            foreach (BatchFrameData frame in frames)
            {
                if (!placementById.TryGetValue(frame.Id,
                    out QuickLineAxisPlacement placement))
                    continue;
                string circuitName = frame.Machine?.CircuitName
                    ?? frame.Identity?.DeviceName ?? string.Empty;
                diagnostics.AddRange(frame.Diagnostics.Select(item =>
                    "图框 " + frame.Id + "：" + item));
                diagnostics.AddRange(placement.Diagnostics.Select(item =>
                    "图框 " + frame.Id + "：" + item));
                QuickLineAxisGrid axes = QuickLineAxisGridPlanner.Plan(
                    frame.Machine?.UpstreamAxis, frame.Machine?.DownstreamAxis);
                diagnostics.AddRange(axes.Diagnostics.Select(item =>
                    "图框 " + frame.Id + "：" + item));

                string framePrefix = "F" + (++frameIndex) + "_";
                Dictionary<string, string> routeGroups =
                    BuildRouteGroupIds(frame.Labelled);
                var nodeMap = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                if (frame.Scene != null)
                {
                    routeFrameCount++;
                    foreach (QuickLineIsometricNode node in frame.Scene.Nodes)
                    {
                        string nodeId = framePrefix + node.Id;
                        nodeMap[node.Id] = nodeId;
                        nodes.Add(new Dictionary<string, object>
                        {
                            ["id"] = nodeId,
                            ["position"] = new Dictionary<string, object>
                            {
                                ["x"] = node.Position.X + placement.OffsetXMillimetres,
                                ["y"] = node.Position.Y + placement.OffsetYMillimetres,
                                ["z"] = node.Position.Z
                            }
                        });
                    }
                    foreach (QuickLineIsometricSegment segment in frame.Scene.Segments)
                    {
                        string routeId = framePrefix + segment.Id;
                        segments.Add(new Dictionary<string, object>
                        {
                            ["id"] = routeId,
                            ["frameId"] = frame.Id,
                            ["frameLabel"] = string.IsNullOrWhiteSpace(
                                frame.Identity?.MachineId)
                                    ? frame.Id : frame.Identity.MachineId,
                            ["circuitName"] = circuitName,
                            ["startNodeId"] = nodeMap[segment.StartNodeId],
                            ["endNodeId"] = nodeMap[segment.EndNodeId],
                            ["routeGroupId"] = framePrefix
                                + (routeGroups.TryGetValue(segment.Id,
                                    out string routeGroup) ? routeGroup : "R0"),
                            ["axis"] = segment.Axis.ToString(),
                            ["distanceMm"] = segment.DistanceMillimetres,
                            ["displayDistanceMm"] = segment.DisplayDistanceMillimetres
                        });
                    }
                }

                frameValues.Add(new Dictionary<string, object>
                {
                    ["id"] = frame.Id,
                    ["machineId"] = frame.Identity?.MachineId ?? string.Empty,
                    ["deviceName"] = circuitName,
                    ["upstreamAxis"] = frame.Machine?.UpstreamAxis ?? string.Empty,
                    ["downstreamAxis"] = frame.Machine?.DownstreamAxis ?? string.Empty,
                    ["offsetXMillimetres"] = placement.OffsetXMillimetres,
                    ["offsetYMillimetres"] = placement.OffsetYMillimetres,
                    ["resolvedAxis"] = placement.Resolved
                });
                directionMarkers.AddRange(BuildDirectionMarkers(
                    frame.Id,
                    string.IsNullOrWhiteSpace(frame.Identity?.MachineId)
                        ? frame.Id : frame.Identity.MachineId,
                    circuitName,
                    axes,
                    placement.OffsetXMillimetres,
                    placement.OffsetYMillimetres,
                    frame.RouteAnchors));

                foreach (BridgePreviewInfo bridge in frame.Bridges)
                {
                    string routeId = string.Empty;
                    bool associated = frame.Scene != null
                        && frame.Scene.Segments.Any(item =>
                            string.Equals(item.Id, bridge.SourceHandle,
                                StringComparison.OrdinalIgnoreCase));
                    if (associated)
                        routeId = framePrefix + bridge.SourceHandle;
                    bridgeValues.Add(new Dictionary<string, object>
                    {
                        ["frameId"] = frame.Id,
                        ["spec"] = bridge.Spec ?? "桥架",
                        ["lengthMm"] = bridge.LengthMm,
                        ["lengthM"] = bridge.LengthMm / 1000.0,
                        ["sourceLineHandle"] = bridge.SourceHandle ?? string.Empty,
                        ["routeId"] = routeId,
                        ["associated"] = associated
                    });
                }
            }

            var sceneValue = new Dictionary<string, object>
            {
                ["rootNodeId"] = nodes.Count > 0
                    ? Convert.ToString(((Dictionary<string, object>)nodes[0])["id"])
                    : "N1",
                ["projectionMode"] = "Auto",
                ["nodes"] = nodes.ToArray(),
                ["segments"] = segments.ToArray(),
                ["directionMarkers"] = directionMarkers.ToArray(),
                ["diagnostics"] = diagnostics.ToArray()
            };
            return new Dictionary<string, object>
            {
                ["type"] = "initialize",
                ["schemaVersion"] = 2,
                ["mode"] = "batch",
                ["scene"] = sceneValue,
                ["frames"] = frameValues.ToArray(),
                ["bridges"] = bridgeValues.ToArray(),
                ["axisGrid"] = new Dictionary<string, object>
                {
                    ["spacingMillimetres"] =
                        QuickLineAxisGridPlanner.DefaultSpacingMillimetres,
                    ["diagnostics"] = diagnostics.ToArray()
                },
                ["metadata"] = new Dictionary<string, object>
                {
                    ["frameCount"] = frames.Count,
                    ["routeFrameCount"] = routeFrameCount
                },
                ["diagnostics"] = diagnostics.ToArray()
            };
        }

        private sealed class BatchFrameData
        {
            public string Id { get; set; }
            public FrameRectangle Boundary { get; set; }
            public ExistingFillIdentity Identity { get; set; }
            public MachineRow Machine { get; set; }
            public List<QuickLineCadSegment> Labelled { get; set; }
                = new List<QuickLineCadSegment>();
            public QuickLineIsometricScene Scene { get; set; }
            public Point2d? DownstreamAnchor { get; set; }
            public Point2d? UpstreamAnchor { get; set; }
            public IReadOnlyDictionary<string, QuickLineSpatialPoint> RouteAnchors
                { get; set; }
            public List<BridgePreviewInfo> Bridges { get; set; }
                = new List<BridgePreviewInfo>();
            public List<string> Diagnostics { get; set; }
                = new List<string>();
        }

        private sealed class BridgePreviewInfo
        {
            public string SourceHandle { get; set; } = string.Empty;
            public string Spec { get; set; } = string.Empty;
            public double LabelLengthMm { get; set; }
            public double GeometryLengthMm { get; set; }
            public double LengthMm => LabelLengthMm > 0
                ? LabelLengthMm : GeometryLengthMm;
        }

        private static Dictionary<string, object> BuildEnvelope(
            QuickLineIsometricScene scene, QuickLineAxisGrid axes,
            ExistingFillIdentity identity, MachineRow machine,
            IEnumerable<string> diagnostics,
            IReadOnlyDictionary<string, QuickLineSpatialPoint> routeAnchors = null)
        {
            var sceneDiagnostics = scene.Diagnostics.ToList();
            sceneDiagnostics.AddRange(axes.Diagnostics);

            var nodes = scene.Nodes.Select(item => (object)new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = item.Position.X,
                    ["y"] = item.Position.Y,
                    ["z"] = item.Position.Z
                }
            }).ToArray();
            Dictionary<string, string> routeGroups =
                BuildSceneRouteGroupIds(scene);
            var segments = scene.Segments.Select(item => (object)new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["circuitName"] = machine?.CircuitName
                    ?? identity?.DeviceName ?? string.Empty,
                ["startNodeId"] = item.StartNodeId,
                ["endNodeId"] = item.EndNodeId,
                ["routeGroupId"] = routeGroups.TryGetValue(item.Id,
                    out string routeGroup) ? routeGroup : "R0",
                ["axis"] = item.Axis.ToString(),
                ["distanceMm"] = item.DistanceMillimetres,
                ["displayDistanceMm"] = item.DisplayDistanceMillimetres
            }).ToArray();
            var sceneValue = new Dictionary<string, object>
            {
                ["rootNodeId"] = scene.RootNodeId,
                ["projectionMode"] = scene.ProjectionMode.ToString(),
                ["nodes"] = nodes,
                ["segments"] = segments,
                ["directionMarkers"] = BuildDirectionMarkers(
                    "single",
                    identity?.MachineId ?? string.Empty,
                    machine?.CircuitName ?? identity?.DeviceName ?? string.Empty,
                    axes,
                    0.0,
                    0.0,
                    routeAnchors).ToArray(),
                ["diagnostics"] = sceneDiagnostics
            };
            var axisValue = new Dictionary<string, object>
            {
                ["upstreamAxis"] = axes.UpstreamAxis,
                ["downstreamAxis"] = axes.DownstreamAxis,
                ["spacingMillimetres"] = axes.SpacingMillimetres,
                ["numericMarks"] = axes.NumericMarks.Select(item => (object)
                    new Dictionary<string, object>
                    {
                        ["label"] = item.Label,
                        ["offsetMillimetres"] = item.OffsetMillimetres
                    }).ToArray(),
                ["alphabeticMarks"] = axes.AlphabeticMarks.Select(item => (object)
                    new Dictionary<string, object>
                    {
                        ["label"] = item.Label,
                        ["offsetMillimetres"] = item.OffsetMillimetres
                    }).ToArray(),
                ["diagnostics"] = axes.Diagnostics
            };
            var metadata = new Dictionary<string, object>
            {
                ["machineId"] = identity.MachineId,
                ["deviceName"] = machine?.CircuitName
                    ?? identity?.DeviceName ?? string.Empty,
                ["deviceFloor"] = machine?.DeviceFloor ?? "",
                ["panelFloor"] = machine?.PanelFloor ?? ""
            };
            return new Dictionary<string, object>
            {
                ["type"] = "initialize",
                ["schemaVersion"] = 1,
                ["scene"] = sceneValue,
                ["axisGrid"] = axisValue,
                ["metadata"] = metadata,
                ["diagnostics"] = (diagnostics ?? Enumerable.Empty<string>()).ToArray()
            };
        }

        private static IReadOnlyList<object> BuildDirectionMarkers(
            string frameId, string frameLabel, string circuitName,
            QuickLineAxisGrid axes, double offsetX, double offsetY,
            IReadOnlyDictionary<string, QuickLineSpatialPoint> routeAnchors = null)
        {
            var result = new List<object>();
            if (axes == null
                || !QuickLineAxisGridPlanner.TryParseAxis(axes.DownstreamAxis,
                    out QuickLineAxisCoordinate downstream)
                || !QuickLineAxisGridPlanner.TryParseAxis(axes.UpstreamAxis,
                    out QuickLineAxisCoordinate upstream))
                return result;

            double downstreamX = FindAxisOffset(axes.NumericMarks,
                downstream.Number.ToString());
            double upstreamX = FindAxisOffset(axes.NumericMarks,
                upstream.Number.ToString());
            double downstreamY = FindAxisOffset(axes.AlphabeticMarks,
                downstream.Letter);
            double upstreamY = FindAxisOffset(axes.AlphabeticMarks,
                upstream.Letter);
            double downstreamZ = 0.0;
            double upstreamZ = 0.0;
            if (routeAnchors != null
                && routeAnchors.TryGetValue("downstream",
                    out QuickLineSpatialPoint downstreamPoint))
            {
                downstreamX = downstreamPoint.X;
                downstreamY = downstreamPoint.Y;
                downstreamZ = downstreamPoint.Z;
            }
            if (routeAnchors != null
                && routeAnchors.TryGetValue("upstream",
                    out QuickLineSpatialPoint upstreamPoint))
            {
                upstreamX = upstreamPoint.X;
                upstreamY = upstreamPoint.Y;
                upstreamZ = upstreamPoint.Z;
            }
            result.Add(DirectionMarker("downstream", "下游",
                axes.DownstreamAxis, frameId, frameLabel, circuitName,
                offsetX + downstreamX, offsetY + downstreamY, downstreamZ));
            result.Add(DirectionMarker("upstream", "上游",
                axes.UpstreamAxis, frameId, frameLabel, circuitName,
                offsetX + upstreamX, offsetY + upstreamY, upstreamZ));
            return result;
        }

        private static double FindAxisOffset(
            IReadOnlyList<QuickLineAxisMark> marks, string label)
        {
            QuickLineAxisMark mark = (marks ?? Array.Empty<QuickLineAxisMark>())
                .FirstOrDefault(item => string.Equals(item.Label, label,
                    StringComparison.OrdinalIgnoreCase));
            return mark?.OffsetMillimetres ?? 0.0;
        }

        private static object DirectionMarker(string kind, string title,
            string axis, string frameId, string frameLabel, string circuitName,
            double x, double y, double z = 0.0)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["title"] = title,
                ["axis"] = axis ?? string.Empty,
                ["frameId"] = frameId ?? string.Empty,
                ["frameLabel"] = frameLabel ?? string.Empty,
                ["circuitName"] = circuitName ?? string.Empty,
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["z"] = z
                }
            };
        }

        private static Point2d? ReadAnchorPoint(Transaction transaction,
            IEnumerable<ObjectId> primary, IEnumerable<ObjectId> fallback)
        {
            if (transaction == null) return null;
            IEnumerable<ObjectId> ids = (primary ?? Enumerable.Empty<ObjectId>())
                .Concat(fallback ?? Enumerable.Empty<ObjectId>())
                .Distinct();
            foreach (ObjectId id in ids)
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
                    return new Point2d(block.Position.X, block.Position.Y);
                }
                catch
                {
                    // A damaged proxy block should not prevent route preview.
                }
            }
            return null;
        }

        private static IReadOnlyDictionary<string, QuickLineSpatialPoint>
            ResolveRouteAnchorPositions(
                IReadOnlyList<QuickLineCadSegment> labelled,
                QuickLineIsometricScene scene,
                Point2d? downstreamAnchor,
                Point2d? upstreamAnchor,
                ICollection<string> diagnostics)
        {
            var result = new Dictionary<string, QuickLineSpatialPoint>(
                StringComparer.OrdinalIgnoreCase);
            if (scene == null || labelled == null || labelled.Count == 0)
                return result;

            if (downstreamAnchor.HasValue)
            {
                string nodeId = FindNearestRouteNode(labelled, scene,
                    downstreamAnchor.Value, diagnostics);
                if (!string.IsNullOrWhiteSpace(nodeId)
                    && TryFindNode(scene, nodeId, out QuickLineSpatialPoint point))
                    result["downstream"] = point;
                else
                    diagnostics?.Add("未找到靠近设备块的 U1L 端点，下游标记按轴位显示。");
            }
            else
                diagnostics?.Add("图框没有可定位的设备块，下游标记按轴位显示。");

            if (upstreamAnchor.HasValue)
            {
                string nodeId = FindNearestRouteNode(labelled, scene,
                    upstreamAnchor.Value, diagnostics);
                if (!string.IsNullOrWhiteSpace(nodeId)
                    && TryFindNode(scene, nodeId, out QuickLineSpatialPoint point))
                    result["upstream"] = point;
                else
                    diagnostics?.Add("未找到靠近上游块的 U1L 端点，上游标记按轴位显示。");
            }
            else
                diagnostics?.Add("图框没有可定位的上游块，上游标记按轴位显示。");

            if (result.TryGetValue("downstream",
                    out QuickLineSpatialPoint downstream)
                && result.TryGetValue("upstream",
                    out QuickLineSpatialPoint upstream)
                && downstream.DistanceTo(upstream) < 1.0)
                diagnostics?.Add("上下游定位到了同一个 U1L 端点，请检查图框内设备块和上游块。");
            return result;
        }

        private static string FindNearestRouteNode(
            IReadOnlyList<QuickLineCadSegment> labelled,
            QuickLineIsometricScene scene, Point2d anchor,
            ICollection<string> diagnostics)
        {
            var coreSegments = labelled.Where(item => item != null)
                .Select(item => new QuickLineSegment(
                    item.LineId.Handle.ToString(),
                    new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                    new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y)))
                .ToList();
            QuickLineGraph graph = QuickLineGraph.Build(coreSegments, 1.0);
            var terminalCandidates = new List<QuickLineRouteEndpointCandidate>();
            var allCandidates = new List<QuickLineRouteEndpointCandidate>();
            foreach (QuickLineCadSegment line in labelled)
            {
                if (line == null) continue;
                QuickLineIsometricSegment segment = scene.Segments
                    .FirstOrDefault(item => string.Equals(item.Id,
                        line.LineId.Handle.ToString(),
                        StringComparison.OrdinalIgnoreCase));
                if (segment == null) continue;
                var start = new QuickLineRouteEndpointCandidate(
                    segment.Id, segment.StartNodeId,
                    new QuickLinePoint(line.StartPoint.X, line.StartPoint.Y));
                allCandidates.Add(start);
                if (graph.GetConnections(segment.Id, QuickLineEndpoint.Start).Count == 0)
                    terminalCandidates.Add(start);

                var end = new QuickLineRouteEndpointCandidate(
                    segment.Id, segment.EndNodeId,
                    new QuickLinePoint(line.EndPoint.X, line.EndPoint.Y));
                allCandidates.Add(end);
                if (graph.GetConnections(segment.Id, QuickLineEndpoint.End).Count == 0)
                    terminalCandidates.Add(end);
            }
            if (terminalCandidates.Count == 0)
            {
                diagnostics?.Add("路由没有可识别的末端，已按全部线段端点回退。");
                terminalCandidates = allCandidates;
            }
            return QuickLineRouteAnchorPlanner.FindNearestNode(terminalCandidates,
                new QuickLinePoint(anchor.X, anchor.Y));
        }

        private static bool TryFindNode(QuickLineIsometricScene scene,
            string nodeId, out QuickLineSpatialPoint point)
        {
            QuickLineIsometricNode node = scene.Nodes.FirstOrDefault(item =>
                string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            if (node == null)
            {
                point = default(QuickLineSpatialPoint);
                return false;
            }
            point = node.Position;
            return true;
        }

    }
}

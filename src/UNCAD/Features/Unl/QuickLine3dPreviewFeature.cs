using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.QuickLine;
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
        [CommandMethod(CommandIds.Line3dPreview, CommandFlags.UsePickSet)]
        public void Preview() => Run();

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
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                identity = FrameIdentityReader.ReadIdentity(transaction, region,
                    new CadBlockDefinitionReader(transaction));
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

            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                graph, distances, displayDistances, completed);
            MachineRow machine = ReadMachine(identity, out List<string> diagnostics);
            QuickLineAxisGrid axes = QuickLineAxisGridPlanner.Plan(
                machine?.UpstreamAxis, machine?.DownstreamAxis);
            Dictionary<string, object> envelope = BuildEnvelope(scene, axes,
                identity, machine, diagnostics);
            using (var form = new U1L3DPreviewForm(envelope))
                AcApplication.ShowModalDialog(form);
        }

        private static MachineRow ReadMachine(ExistingFillIdentity identity,
            out List<string> diagnostics)
        {
            diagnostics = new List<string>();
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

        private static Dictionary<string, object> BuildEnvelope(
            QuickLineIsometricScene scene, QuickLineAxisGrid axes,
            ExistingFillIdentity identity, MachineRow machine,
            IEnumerable<string> diagnostics)
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
            var segments = scene.Segments.Select(item => (object)new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["startNodeId"] = item.StartNodeId,
                ["endNodeId"] = item.EndNodeId,
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
                ["deviceName"] = identity.DeviceName,
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Contracts;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;
using UNCAD.UI;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using CadQuickLineSegment = UNCAD.Cad.QuickLine.QuickLineSegment;

namespace UNCAD.Features.Unl
{
    /// <summary>
    /// U1LX edits a selected connected U1L route. U1X opens a blank
    /// southeast-isometric drawing workspace without selecting CAD geometry.
    /// </summary>
    [Feature("unl-quick", "3D 快速绘图",
        Commands = CommandIds.QuickLineFeatureCommands,
        Description = "U1LX 选择已有线段编辑；U1X 直接打开东南等轴侧 3D 正交绘图器。")]
    public sealed class QuickLineFeature : CommandBase
    {
        [CommandMethod(CommandIds.LineQuick)]
        public void QuickLine() => Run();

        [CommandMethod(CommandIds.Line3d)]
        public void QuickLine3d() => Run("draw");

        protected override void Execute(CadContext ctx)
            => ExecuteSelected(ctx);

        protected override void Execute(CadContext ctx, object state)
        {
            if (string.Equals(state as string, "draw",
                    StringComparison.OrdinalIgnoreCase))
            {
                ExecuteBlank(ctx);
                return;
            }
            ExecuteSelected(ctx);
        }

        private static void ExecuteBlank(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Line3d);
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder
                .CreateDrawingScene();
            using (var form = new QuickLine3dEditorForm(scene))
            {
                DialogResult result = AcApplication.ShowModalDialog(form);
                if (result != DialogResult.OK)
                {
                    ctx.Write("\n[U1X] 已取消，图纸未修改。");
                    return;
                }
                if (form.CreatedSegments == null || form.CreatedSegments.Count == 0)
                {
                    ctx.Write("\n[U1X] 没有可写回的 3D 线段，图纸未修改。");
                    return;
                }
                PromptPointResult insertion;
                try
                {
                    insertion = ctx.Ed.GetPoint(
                        new PromptPointOptions("\n请点击平面图插入点: "));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    when (ex.ErrorStatus == ErrorStatus.UserBreak)
                {
                    ctx.Write("\n[U1X] 已取消插入，图纸未修改。");
                    return;
                }
                if (insertion == null || insertion.Status != PromptStatus.OK)
                {
                    ctx.Write("\n[U1X] 已取消插入，图纸未修改。");
                    return;
                }
                if (!QuickLineCadService.TryCreatePlanRoute(ctx,
                    form.CreatedSegments, insertion.Value, out int createdCount))
                {
                    ctx.Write("\n[U1X] 3D 路线写回失败，图纸未修改。");
                    return;
                }
                ctx.Ed.Regen();
                ctx.Write("\n[U1X] 完成：已按标准等轴测展开为平面，在插入点创建 "
                    + createdCount + " 根线段，长度与标注保留真实毫米值。");
            }
        }

        private static void ExecuteSelected(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.LineQuick);
            IReadOnlyList<CadQuickLineSegment> allScanned =
                QuickLineCadService.Scan(ctx,
                    new QuickLineScanOptions { IncludeUnlabelled = true });
            IReadOnlyList<CadQuickLineSegment> labelled = allScanned
                .Where(item => item.HasMillimetreLabel).ToList();
            if (labelled.Count == 0)
            {
                ctx.Write("\n[U1LX] 当前空间没有带毫米标注的 U1L 线段。");
                return;
            }
            PromptEntityResult selected;
            try
            {
                var options = new PromptEntityOptions(
                    "\n[U1LX] 选择要载入 3D 编辑器的 LINE 线段: ");
                options.SetRejectMessage("\n[U1LX] 只能选择 LINE 线段。");
                options.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Line), false);
                selected = ctx.Ed.GetEntity(options);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
                when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
                return;
            }
            if (selected == null || selected.Status != PromptStatus.OK) return;
            CadQuickLineSegment selectedCad = allScanned.FirstOrDefault(item =>
                item.LineId == selected.ObjectId && item.HasMillimetreLabel);
            if (selectedCad == null)
            {
                ctx.Write("\n[U1LX] 选中的 LINE 没有可识别毫米标注。");
                return;
            }
            try
            {
                var coreSegments = labelled.Select(item =>
                    new UNCAD.Core.QuickLine.QuickLineSegment(
                        item.LineId.Handle.ToString(),
                        new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                        new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))).ToList();
                QuickLineGraph graph = QuickLineGraph.Build(coreSegments, 1.0);
                var distances = labelled.ToDictionary(item => item.LineId.Handle.ToString(),
                    item => item.LabelMillimetres.Value, StringComparer.OrdinalIgnoreCase);
                var displayDistances = labelled.ToDictionary(
                    item => item.LineId.Handle.ToString(),
                    item => IsUnconfirmedPlaceholder(item)
                        ? item.Length : item.LabelMillimetres.Value,
                    StringComparer.OrdinalIgnoreCase);
                var completed = new HashSet<string>(labelled
                    .Where(item => !IsUnconfirmedPlaceholder(item))
                    .Select(item => item.LineId.Handle.ToString()),
                    StringComparer.OrdinalIgnoreCase);
                var scene = QuickLineIsometricSceneBuilder.Build(graph,
                    selectedCad.LineId.Handle.ToString(), distances,
                    displayDistances, completed);
                using (var form = new QuickLine3dEditorForm(scene))
                {
                    if (AcApplication.ShowModalDialog(form) != DialogResult.OK)
                        return;
                    var actual = new Dictionary<string, double>(distances,
                        StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, double> update in form.Updates)
                        actual[update.Key] = update.Value;
                    var completedIds = new HashSet<string>(scene.Segments
                        .Where(item => item.Completed).Select(item => item.Id),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (string id in form.Updates.Keys) completedIds.Add(id);
                    if (completedIds.Count < scene.Segments.Count)
                    {
                        ctx.Write("\n[U1LX] 仍有 "
                            + (scene.Segments.Count - completedIds.Count)
                            + " 根线段未填写，暂不写回 CAD。");
                        return;
                    }
                    QuickLineSchematicLayout layout =
                        QuickLineSchematicLayoutBuilder.Build(graph, scene, actual);
                    var byId = labelled.ToDictionary(item => item.LineId.Handle.ToString(),
                        StringComparer.OrdinalIgnoreCase);
                    var layoutById = layout.Segments.ToDictionary(item => item.Id,
                        StringComparer.OrdinalIgnoreCase);
                    var updates = scene.Segments.Select(item =>
                    {
                        CadQuickLineSegment cad = byId[item.Id];
                        QuickLineSchematicSegment schematic = layoutById[item.Id];
                        return new QuickLineSchematicUpdate(cad.LineId, cad.LabelId,
                            actual[item.Id], new Autodesk.AutoCAD.Geometry.Point3d(
                                schematic.Start.X, schematic.Start.Y, cad.StartPoint.Z),
                            new Autodesk.AutoCAD.Geometry.Point3d(
                                schematic.End.X, schematic.End.Y, cad.StartPoint.Z));
                    }).ToList();
                    if (QuickLineCadService.TryApplySchematicLayout(ctx, updates))
                    {
                        ctx.Ed.Regen();
                        ctx.Write("\n[U1LX] 完成：已更新 " + updates.Count
                            + " 根线段，布局范围不超过 78021×78021。");
                    }
                    else
                        ctx.Write("\n[U1LX] CAD 布局写回失败，图纸未修改。");
                }
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1LX] 无法建立 3D 编辑路线：" + ex.Message);
            }
        }

        private static bool IsUnconfirmedPlaceholder(CadQuickLineSegment segment)
            => segment != null && segment.LabelMillimetres.HasValue
                && Math.Abs(segment.LabelMillimetres.Value - 2000.0) <= 1e-7
                && string.Equals(segment.LabelKind, "DBText",
                    StringComparison.OrdinalIgnoreCase)
                && !segment.HasCompletionMarker;

    }
}

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
    /// Opens one connected U1L route as an orthogonal 3D scene. Browser edits
    /// remain in memory until the modal editor returns OK, then every changed
    /// annotation is committed in one CAD transaction.
    /// </summary>
    [Feature("unl-quick", "3D 快速绘图",
        Commands = CommandIds.QuickLineFeatureCommands,
        Description = "选择 U1L 线段后反算相连等轴测路线，在 3D 窗口中框选并批量配置毫米距离。")]
    public sealed class QuickLineFeature : CommandBase
    {
        private const double EndpointTolerance = 1.0;

        [CommandMethod(CommandIds.LineQuick)]
        public void QuickLine() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.LineQuick);

            IReadOnlyList<CadQuickLineSegment> allScanned =
                QuickLineCadService.Scan(ctx,
                    new QuickLineScanOptions { IncludeUnlabelled = true });
            IReadOnlyList<CadQuickLineSegment> labelled = allScanned
                .Where(item => item.HasMillimetreLabel)
                .ToList();
            int dimensions = labelled.Count(item => string.Equals(item.LabelKind,
                "Dimension", StringComparison.OrdinalIgnoreCase));
            int mtexts = labelled.Count(item => string.Equals(item.LabelKind,
                "MText", StringComparison.OrdinalIgnoreCase));
            int attributes = labelled.Count(item => string.Equals(item.LabelKind,
                "Attribute", StringComparison.OrdinalIgnoreCase));
            int placeholders = labelled.Count(item => item.LabelMillimetres.HasValue
                && Math.Abs(item.LabelMillimetres.Value - 2000.0) <= 1e-7
                && string.Equals(item.LabelKind, "DBText",
                    StringComparison.OrdinalIgnoreCase)
                && !item.HasCompletionMarker);
            ctx.Write("\n[U1LX] 检测到 " + allScanned.Count + " 条 LINE，其中 "
                + labelled.Count + " 条带可识别毫米标注。");
            if (dimensions + mtexts + attributes > 0)
                ctx.Write("\n[U1LX] 标注来源：DIM " + dimensions + "，MTEXT "
                    + mtexts + "，块属性 " + attributes + "。");
            if (placeholders > 0)
                ctx.Write("\n[U1LX] 提示：其中 " + placeholders
                    + " 条仍是未确认的 2000mm；这是 U1L 的默认占位值。"
                    + "若旁边有 DIM/MTEXT，本命令会优先使用其真实值。");
            if (labelled.Count == 0)
            {
                ctx.Write("\n[U1LX] 当前空间没有可用于 3D 编辑的 U1L 线段。");
                return;
            }

            PromptEntityResult selected = SelectSeedLine(ctx);
            if (selected == null || selected.Status != PromptStatus.OK) return;

            CadQuickLineSegment selectedCad = allScanned.FirstOrDefault(item =>
                item.LineId == selected.ObjectId);
            if (selectedCad == null || !selectedCad.HasMillimetreLabel)
            {
                ctx.Write("\n[U1LX] 选中的 LINE 没有可识别毫米标注，操作已停止。");
                return;
            }

            var bySegmentId = labelled.ToDictionary(ToCoreId,
                StringComparer.OrdinalIgnoreCase);
            QuickLineIsometricScene scene;
            try
            {
                QuickLineGraph graph = QuickLineGraph.Build(
                    labelled.Select(ToCoreSegment), EndpointTolerance);
                var distances = labelled.ToDictionary(ToCoreId,
                    item => item.LabelMillimetres.Value,
                    StringComparer.OrdinalIgnoreCase);
                scene = QuickLineIsometricSceneBuilder.Build(graph,
                    ToCoreId(selectedCad), distances);
            }
            catch (ArgumentException ex)
            {
                ctx.Write("\n[U1LX] 无法建立 3D 路线：" + ex.Message);
                return;
            }

            IReadOnlyDictionary<string, double> acceptedUpdates;
            using (var form = new QuickLine3dEditorForm(scene))
            {
                DialogResult result = AcApplication.ShowModalDialog(form);
                if (result != DialogResult.OK)
                {
                    ctx.Write("\n[U1LX] 已取消，图纸未修改。");
                    return;
                }
                acceptedUpdates = form.Updates;
            }

            if (acceptedUpdates.Count == 0)
            {
                ctx.Write("\n[U1LX] 已确认，没有距离发生变化。");
                return;
            }

            var cadUpdates = new List<QuickLineLabelUpdate>(acceptedUpdates.Count);
            foreach (KeyValuePair<string, double> update in acceptedUpdates)
            {
                if (!bySegmentId.TryGetValue(update.Key,
                        out CadQuickLineSegment segment)
                    || !segment.HasMillimetreLabel
                    || double.IsNaN(update.Value)
                    || double.IsInfinity(update.Value)
                    || update.Value < 0.0)
                {
                    ctx.Write("\n[U1LX] 编辑结果包含无效线段，图纸未修改。");
                    return;
                }
                cadUpdates.Add(new QuickLineLabelUpdate(segment.LabelId,
                    update.Value));
            }

            if (!QuickLineCadService.TryUpdateMillimetreLabels(ctx, cadUpdates))
            {
                ctx.Write("\n[U1LX] 写回失败，全部修改已回滚。");
                return;
            }

            ctx.Ed.Regen();
            ctx.Write("\n[U1LX] 完成：已原子更新 " + cadUpdates.Count
                + " 根线段的毫米标注。");
        }

        private static PromptEntityResult SelectSeedLine(CadContext ctx)
        {
            var options = new PromptEntityOptions(
                "\n[U1LX] 选择要载入 3D 编辑器的线段或按回车结束: ")
            {
                AllowNone = true
            };
            options.SetRejectMessage("\n[U1LX] 只能选择 LINE 线段。");
            options.AddAllowedClass(
                typeof(Autodesk.AutoCAD.DatabaseServices.Line), false);
            try
            {
                return ctx.Ed.GetEntity(options);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
                when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
                return null;
            }
        }

        private static UNCAD.Core.QuickLine.QuickLineSegment ToCoreSegment(
            CadQuickLineSegment segment)
            => new UNCAD.Core.QuickLine.QuickLineSegment(ToCoreId(segment),
                new QuickLinePoint(segment.StartPoint.X, segment.StartPoint.Y),
                new QuickLinePoint(segment.EndPoint.X, segment.EndPoint.Y));

        private static string ToCoreId(CadQuickLineSegment segment)
            => segment.LineId.Handle.ToString();
    }
}

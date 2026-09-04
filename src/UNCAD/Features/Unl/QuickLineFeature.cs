using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Contracts;
using UNCAD.Core.QuickLine;
using UNCAD.Infra;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace UNCAD.Features.Unl
{
    /// <summary>
    /// U1LX 快速标注:无 GUI、纯命令行。点选一根 LINE,自动从点击端起
    /// 沿连通路线逐段填写毫米标注;每输入一段,视角中心跳到下一段,
    /// 直到走完。点击落在段中部时询问向哪端走。
    /// </summary>
    [Feature("unl-quick", "快速标注距离",
        Commands = CommandIds.QuickLineFeatureCommands,
        Description = "点选线段后逐段快速填写毫米标注(兼容命令 UNLX)。")]
    public sealed class QuickLineFeature : CommandBase
    {
        [CommandMethod(CommandIds.LineQuick)]
        public void QuickLine() => Run();

        protected override void Execute(CadContext ctx) => FastAnnotate(ctx);

        private static void FastAnnotate(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.LineQuick);
            PromptEntityResult selected;
            try
            {
                var options = new PromptEntityOptions(
                    "\n[U1LX] 选择开始填写的 LINE 线段(点选位置靠近哪端就从哪端开始): ");
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
            IReadOnlyList<QuickLineCadSegment> scanned = QuickLineCadService.Scan(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    RootLineId = selected.ObjectId,
                    EndpointTolerance = 1.0
                });
            if (scanned.Count == 0)
            {
                ctx.Write("\n[U1LX] 选中的线段无效或不在当前空间。");
                return;
            }
            QuickLineCadSegment start = scanned.FirstOrDefault(item =>
                item.LineId == selected.ObjectId);
            if (start == null)
            {
                ctx.Write("\n[U1LX] 该线段不在扫描结果中,请重试。");
                return;
            }

            var coreSegments = scanned.Select(item =>
                new UNCAD.Core.QuickLine.QuickLineSegment(
                    item.LineId.Handle.ToString(),
                    new QuickLinePoint(item.StartPoint.X, item.StartPoint.Y),
                    new QuickLinePoint(item.EndPoint.X, item.EndPoint.Y))).ToList();
            QuickLineGraph graph = QuickLineGraph.Build(coreSegments, 1.0);
            string startId = start.LineId.Handle.ToString();

            QuickLineTraversalPlan plan = BuildPlan(graph, startId, start,
                selected.PickedPoint, ctx);
            if (plan == null || plan.Steps.Count == 0) return;

            ctx.Write("\n[U1LX] 共 "
                + plan.Steps.Count + " 段待填写;输入距离回车即写标注并跳到下一段,"
                + "直接回车保留原值,Esc 结束。");
            var byId = scanned.ToDictionary(
                item => item.LineId.Handle.ToString(), StringComparer.OrdinalIgnoreCase);
            int done = 0;
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                QuickLineTraversalStep step = plan.Steps[i];
                QuickLineCadSegment segment = byId[step.SegmentId];
                double? value = PromptMillimetres(ctx, segment, i + 1, plan.Steps.Count);
                if (value == null) break; // Esc / 取消:保留已完成部分
                if (!QuickLineCadService.TryWriteSegmentMillimetre(ctx, segment, value.Value))
                {
                    ctx.Write("\n[U1LX] 该段标注写入失败,已跳过。");
                }
                else
                {
                    done++;
                    if (i + 1 < plan.Steps.Count)
                        QuickLineCadService.TryCenterView(ctx, byId[plan.Steps[i + 1].SegmentId]);
                }
            }
            // Entity edits already refresh their own graphics; a full Regen()
            // only costs seconds on dense drawings for nothing.
            ctx.Write("\n[U1LX] 完成:已填写 " + done + " / " + plan.Steps.Count + " 段。");
        }

        /// <summary>构建遍历计划;点击在段中部时询问用户向哪端走。</summary>
        private static QuickLineTraversalPlan BuildPlan(QuickLineGraph graph,
            string startId, QuickLineCadSegment start, Point3d pickedPoint, CadContext ctx)
        {
            var click = new QuickLinePoint(pickedPoint.X, pickedPoint.Y);
            QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(graph, startId, click);
            if (plan.ClickRegion != QuickLineClickRegion.Interior) return plan;

            var options = new PromptKeywordOptions(
                "\n[U1LX] 点击位置在线段中部,向哪端开始填写? ");
            options.Keywords.Add("Start", "Start", "起点端(Start)");
            options.Keywords.Add("End", "End", "终点端(End)");
            PromptResult choice = ctx.Ed.GetKeywords(options);
            if (choice == null || choice.Status != PromptStatus.OK)
                return null;
            var anchor = choice != null
                    && string.Equals(choice.StringResult, "End", StringComparison.OrdinalIgnoreCase)
                ? new QuickLinePoint(start.EndPoint.X, start.EndPoint.Y)
                : new QuickLinePoint(start.StartPoint.X, start.StartPoint.Y);
            return QuickLineTraversal.CreatePlan(graph, startId, anchor);
        }

        /// <summary>
        /// 提示输入距离;回车 = null 表示保留原值,Esc 取消遍历。
        /// 格式输错(Error)只重试本段,不再退出整个遍历——否则用户一次
        /// 手滑就要重新扫描整张图。
        /// </summary>
        private static double? PromptMillimetres(CadContext ctx,
            QuickLineCadSegment segment, int index, int total)
        {
            double current = segment.LabelMillimetres ?? segment.Length;
            var options = new PromptDoubleOptions(
                $"\n[U1LX] ({index}/{total}) 线段 "
                + segment.LineId.Handle + " 距离(mm) <回车=保留 "
                + QuickLineCadService.FormatMillimetres(current) + ">")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = current,
                UseDefaultValue = true
            };
            PromptDoubleResult result = ctx.Ed.GetDouble(options);
            while (result.Status == PromptStatus.Error)
            {
                ctx.Write("\n[U1LX] 请输入非负数字距离(mm),Esc 结束本命令。");
                result = ctx.Ed.GetDouble(options);
            }
            if (result.Status != PromptStatus.OK) return null;
            return result.Value;
        }
    }
}

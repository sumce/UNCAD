using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Contracts;
using UNCAD.Core.Geometry;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Unl
{
    /// <summary>
    /// D-058：U1UL 毫米标归位。把毫米标「离线的距离」和「角度」按 U1SET 的偏移
    /// 归到线段上：距离＝偏移值，角度＝与线段平行；毫米标现在在哪一侧就留在
    /// 哪一侧（用户手动挪到另一侧的不挪回去），沿线方向的位置也保持。
    /// 只处理识别得出绑定关系的毫米标（带 U1L/U1LX 关联元数据）。
    /// 选图框＝处理整框；选线段或毫米标＝只处理它们。一个事务，一次 U 整体撤销。
    /// </summary>
    [Feature("unl-label-align", "毫米标归位",
        Commands = CommandIds.LineLabelAlignFeatureCommands,
        Description = "毫米标离线的距离和角度随线段归位，所在侧保持不变")]
    public sealed class QuickLineLabelAlignFeature : CommandBase
    {
        private const int HandlesShown = 6;

        [CommandMethod(CommandIds.LineLabelAlign)]
        public void AlignLabels() => Run();

        protected override void Execute(CadContext ctx) => Align(ctx);

        private static void Align(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.LineLabelAlign);
            FillDrawingStyleSettings settings = FillDrawingStyleSettings.Capture();
            bool centered = settings.CableTextPosition == "0";
            bool defaultAbove = settings.CableTextPosition == "2";
            ObjectId[] selected;
            try
            {
                selected = SelectionService.PickFirstOrPrompt(ctx,
                    "\n[U1UL] 选择线段、毫米标或图框（选图框＝处理整框）: ",
                    new TypedValue(0, "LINE,LWPOLYLINE,TEXT,MTEXT,INSERT"));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
                when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
                return;
            }
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[U1UL] 未选择对象，图纸未修改。");
                return;
            }

            // 选了图框就按图框边界取线段；否则按直接选中的线段、以及选中毫米标
            // 所关联的线段。预选集不过滤类型，这里逐个认。
            FrameRectangle[] boundaries = null;
            var ownerHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, selected);
                if (regions.Groups.Count > 0)
                    boundaries = regions.Groups.Where(group => group.Boundary != null)
                        .Select(group => group.Boundary).ToArray();
                foreach (ObjectId id in selected)
                {
                    Entity entity;
                    try
                    {
                        entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    }
                    catch
                    {
                        continue;
                    }
                    if (entity == null || entity.IsErased) continue;
                    if (entity is Curve)
                        ownerHandles.Add(entity.Handle.ToString());
                    else if (entity is DBText || entity is MText)
                    {
                        string owner = QuickLineMetadataStore.ReadAssociatedLineHandle(entity);
                        if (!string.IsNullOrWhiteSpace(owner)) ownerHandles.Add(owner);
                    }
                }
                transaction.Commit();
            }
            bool wholeFrames = boundaries != null && boundaries.Length > 0;

            IReadOnlyList<QuickLineCadSegment> scanned = QuickLineCadService.Scan(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = wholeFrames ? boundaries : null
                });
            List<QuickLineCadSegment> targets = wholeFrames
                ? scanned.ToList()
                : scanned.Where(item => ownerHandles.Contains(
                    item.LineId.Handle.ToString())).ToList();
            if (targets.Count == 0)
            {
                ctx.Write("\n[U1UL] 选中的对象里没有可归位的 U1L 线段，图纸未修改。");
                return;
            }

            ConfigPrinter.Print(ctx, "U1UL",
                ("离线距离", centered ? "居中压线"
                    : TextFormatter.FormatNum(settings.CableTextOffset)),
                ("角度", "与线段平行"),
                ("所在侧", "保持毫米标现状"));
            QuickLineLabelAlignResult result = QuickLineLabelAlignService.Apply(ctx,
                targets, settings.CableTextOffset, centered, defaultAbove);
            if (result == null)
            {
                ctx.Write("\n[U1UL] 有线段的位置无法计算，图纸未修改。");
                return;
            }
            ctx.Write("\n[U1UL] 已归位 " + result.Moved + " 个毫米标（离线距离、角度随线段，"
                + "所在侧保持不变）；另有 " + result.Unchanged + " 个已在位未动。"
                + "一次 U 可整体撤销。");
            if (result.SkippedHandles.Count > 0)
            {
                string detail = result.WithoutAssociatedLabel > 0
                    ? result.WithoutAssociatedLabel + " 段没有毫米标" : string.Empty;
                if (result.WithoutMetadata > 0)
                    detail += (detail.Length > 0 ? "，" : string.Empty)
                        + result.WithoutMetadata + " 段的毫米标没有指向本线段的关联元数据"
                        + "（旧图纸只按位置匹配，或线段被合并过；不猜）";
                ctx.Write("\n[U1UL] " + result.SkippedHandles.Count + " 段线段未处理（"
                    + detail + "）：句柄 "
                    + string.Join("、", result.SkippedHandles.Take(HandlesShown))
                    + (result.SkippedHandles.Count > HandlesShown
                        ? " 等 " + result.SkippedHandles.Count + " 段" : string.Empty));
            }
        }
    }
}

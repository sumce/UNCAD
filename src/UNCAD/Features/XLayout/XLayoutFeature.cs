using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Dwg;
using UNCAD.Core.Submission;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.XLayout
{
    /// <summary>Arranges selected frame_20260812 blocks by machine and shows circuit totals.</summary>
    [Feature("xlayout", "图框自动排版", Commands = CommandIds.XLayoutFeatureCommands,
        Description = "按机台ID将图框分行排版，并统计每个机台的回路数量")]
    public sealed class XLayoutFeature : CommandBase
    {
        [CommandMethod(CommandIds.XLayout, CommandFlags.UsePickSet)]
        public void ArrangeFrames() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.XLayout);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要排版的 frame_20260812 图框: ",
                new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[XLAYOUT] 未选择图框，未执行排版。");
                return;
            }

            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx, selected);
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors)
                    ctx.Write("\n[XLAYOUT] 图框识别失败: " + error);
                return;
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[XLAYOUT] 未找到有效的 frame_20260812 图框。");
                return;
            }

            var items = new List<XLayoutFrameItem>();
            var groupsByItem = new Dictionary<XLayoutFrameItem, FrameRegionGroup>();
            var summaries = new Dictionary<string, XLayoutMachineSummary>(
                StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (FrameRegionGroup group in regions.Groups)
            {
                index++;
                SubmissionRecord record;
                try
                {
                    record = SubmissionRecordExtractor.Extract(CadSubmissionReader.Read(ctx,
                        group.EntityIds.ToArray()));
                }
                catch (System.Exception ex)
                {
                    ctx.Write("\n[XLAYOUT] 图框 " + group.Handle + " 身份读取失败: " + ex.Message);
                    return;
                }
                string machineId = (record.MachineId ?? "").Trim();
                if (machineId.Length == 0)
                {
                    ctx.Write("\n[XLAYOUT] 图框 " + group.Handle
                        + " 缺少机台ID，未执行排版。");
                    return;
                }
                string deviceName = (record.DeviceName ?? "").Trim();
                var item = new XLayoutFrameItem(machineId, deviceName, group.Boundary, group.Handle);
                items.Add(item);
                groupsByItem[item] = group;
                if (!summaries.TryGetValue(machineId, out XLayoutMachineSummary summary))
                {
                    summary = new XLayoutMachineSummary(machineId);
                    summaries.Add(machineId, summary);
                }
                summary.CircuitCount++;
            }

            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items);
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var moved = new HashSet<ObjectId>();
                foreach (XLayoutPlacement placement in placements)
                {
                    FrameRegionGroup group = groupsByItem[placement.Item];
                    Vector3d displacement = new Vector3d(
                        placement.TranslationX, placement.TranslationY, 0d);
                    foreach (ObjectId id in group.EntityIds)
                    {
                        if (!moved.Add(id)) continue;
                        Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
                        if (entity == null || entity.IsErased) continue;
                        entity.TransformBy(Matrix3d.Displacement(displacement));
                    }
                }
                transaction.Commit();
            }

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[XLAYOUT] 排版完成：" + regions.Groups.Count
                + " 个图框，" + summaries.Count + " 个机台；图框间距 10000。");
            using (var form = new XLayoutSummaryForm(summaries.Values.ToList(), regions.Groups.Count))
                form.ShowDialog(new WindowWrapper(
                    Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle));
        }
    }
}

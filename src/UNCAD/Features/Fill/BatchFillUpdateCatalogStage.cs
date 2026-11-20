using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.UI;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// Applies batch U1U catalog choices. Cable substitutions run first because they
    /// can change the derived flexible-conduit diameter and its required catalog row.
    /// </summary>
    internal static class BatchFillUpdateCatalogStage
    {
        internal static bool Resolve(CadContext ctx,
            IReadOnlyList<BatchFillUpdatePlan> plans,
            IReadOnlyList<BatchCatalogRequest> catalogRequests,
            FillWorkbookSnapshot workbook, FillPlanningOptions planning)
        {
            var cableReplacementPlans = new HashSet<BatchFillUpdatePlan>();
            if (catalogRequests != null && catalogRequests.Count > 0)
            {
                var picker = new BatchCatalogSelectionWindow(catalogRequests);
                ModalWindowHost.Show(picker);
                if (!picker.Confirmed) return false;

                foreach (BatchFillUpdatePlan plan in plans)
                {
                    foreach (BatchCatalogAssignment assignment in
                        plan.CatalogAssignments.Where(item =>
                            item.Item?.Category == TableFillCategory.Cable))
                    {
                        if (!picker.Selections.TryGetValue(assignment.RequestKey,
                                out ListItem selected))
                            continue;
                        plan.Review.ReplaceWithCatalogItem(assignment.Item, selected,
                            workbook.Catalog);
                        cableReplacementPlans.Add(plan);
                    }
                    foreach (BatchCatalogAssignment assignment in
                        plan.CatalogAssignments.Where(item =>
                            item.Item?.Category != TableFillCategory.Cable
                                && !(cableReplacementPlans.Contains(plan)
                                    && item.Item?.Category
                                        == TableFillCategory.FlexibleConduit)))
                    {
                        if (!picker.Selections.TryGetValue(assignment.RequestKey,
                                out ListItem selected))
                            continue;
                        plan.Review.ReplaceWithCatalogItem(assignment.Item, selected,
                            workbook.Catalog);
                    }
                    if (cableReplacementPlans.Contains(plan)
                        && plan.HoseWasMissingBeforeCableChoice
                        && plan.Review.FlexibleConduitItem() != null
                        && !FillFeature.ApplyRuanguanLength(ctx, plan.Selection,
                            plan.Review, workbook.Catalog, planning, true))
                        return false;
                }
            }

            var hoseRequests = new List<BatchCatalogRequest>();
            var hoseAssignments =
                new List<Tuple<BatchFillUpdatePlan, BatchCatalogAssignment>>();
            foreach (BatchFillUpdatePlan plan in cableReplacementPlans)
            {
                foreach (FillReviewItem item in plan.Review.Items.Where(item =>
                    item.Category == TableFillCategory.FlexibleConduit
                        && item.RequiresCatalogConfirmation))
                {
                    string category = FillReviewData.CategoryName(item.Category);
                    string key = plan.Region.Handle + ":" + category + ":"
                        + plan.Review.Items.IndexOf(item);
                    var request = new BatchCatalogRequest(key,
                        plan.Machine.MachineId, plan.Machine.CircuitName,
                        category, item.Description, workbook.Catalog.SelectableItems);
                    if (request.Candidates.Count == 0)
                    {
                        ctx.Write("\n[U1U] 电缆替代后软管规格“" + item.Description
                            + "”在固定清单中没有可选择的替代项，图纸未修改。");
                        MessageBox.Show(Owner(),
                            "固定清单没有可选择的软管规格。图纸未修改。",
                            "U1U 批量替代", MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return false;
                    }
                    hoseRequests.Add(request);
                    hoseAssignments.Add(Tuple.Create(plan,
                        new BatchCatalogAssignment
                        {
                            RequestKey = key,
                            Item = item
                        }));
                }
            }
            if (hoseRequests.Count == 0) return true;

            var hosePicker = new BatchCatalogSelectionWindow(hoseRequests);
            ModalWindowHost.Show(hosePicker);
            if (!hosePicker.Confirmed)
            {
                ctx.Write("\n[U1U] 已取消软管替代选择，图纸未修改。");
                return false;
            }
            foreach (Tuple<BatchFillUpdatePlan, BatchCatalogAssignment> entry
                in hoseAssignments)
            {
                BatchCatalogAssignment assignment = entry.Item2;
                if (!hosePicker.Selections.TryGetValue(assignment.RequestKey,
                        out ListItem selected))
                    continue;
                entry.Item1.Review.ReplaceWithCatalogItem(assignment.Item,
                    selected, workbook.Catalog);
            }
            return true;
        }

        private static WindowWrapper Owner()
        {
            IntPtr handle = AcApplication.MainWindow == null
                ? IntPtr.Zero : AcApplication.MainWindow.Handle;
            return new WindowWrapper(handle);
        }
    }
}

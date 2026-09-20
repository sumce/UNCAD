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
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Cad.QuickLine;
using UNCAD.Features.Submit;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.XLayout
{
    /// <summary>Arranges selected legacy/xframe blocks by machine and shows circuit totals.</summary>
    [Feature("xlayout", "图框自动排版", Commands = CommandIds.XLayoutFeatureCommands,
        Description = "按机台ID将图框分行排版，并统计每个机台的回路数量")]
    public sealed class XLayoutFeature : CommandBase
    {
        [CommandMethod(CommandIds.XLayout, CommandFlags.UsePickSet)]
        public void ArrangeFrames() => Run();

        private const double MachineLabelTextHeight = 25000d;
        // Keep one fixed left column for every row; text grows right without alignment recalculation.
        private const double MachineLabelLeftDistance = 300000d;
        private const short MachineCompleteColorIndex = 3;
        private const short MachineIncompleteColorIndex = 1;
        private const short MachineUnknownColorIndex = 8;
        private const short DuplicateMarkerColorIndex = 2;

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.XLayout);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要排版的图框（" + FrameRegionCollector.SupportedFrameDescription + "）: ",
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
                ctx.Write("\n[XLAYOUT] 未找到有效图框（" + FrameRegionCollector.SupportedFrameDescription + "）。");
                return;
            }

            var items = new List<XLayoutFrameItem>();
            var groupsByItem = new Dictionary<XLayoutFrameItem, FrameRegionGroup>();
            var summaries = new Dictionary<string, XLayoutMachineSummary>(
                IdentityTextNormalizer.Comparer);
            var identityErrors = new List<string>();
            FrameRegionGroup firstIdentityError = null;
            int index = 0;
            using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(readTransaction);
                foreach (FrameRegionGroup group in regions.Groups)
                {
                    index++;
                    ExistingFillIdentity identity;
                    try
                    {
                        identity = FrameIdentityReader.ReadIdentity(readTransaction, group,
                            definitions);
                    }
                    catch (System.Exception ex)
                    {
                        string error = DescribeFrameIdentityFailure(group, index,
                            regions.Groups.Count, ex.Message);
                        identityErrors.Add(error);
                        if (firstIdentityError == null) firstIdentityError = group;
                        continue;
                    }
                    string machineId = (identity.MachineId ?? "").Trim();
                    if (machineId.Length == 0)
                    {
                        string error = DescribeFrameIdentityFailure(group, index,
                            regions.Groups.Count, "缺少机台ID。");
                        identityErrors.Add(error);
                        if (firstIdentityError == null) firstIdentityError = group;
                        continue;
                    }
                    string deviceName = (identity.DeviceName ?? "").Trim();
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
                readTransaction.Commit();
            }

            if (identityErrors.Count > 0)
            {
                const int maxReportedErrors = 20;
                ctx.Write("\n[XLAYOUT] 发现 " + identityErrors.Count
                    + " 个图框身份异常，未执行排版：");
                foreach (string error in identityErrors.Take(maxReportedErrors))
                    ctx.Write("\n  " + error);
                if (identityErrors.Count > maxReportedErrors)
                    ctx.Write("\n  其余 " + (identityErrors.Count - maxReportedErrors)
                        + " 个异常未在命令行展开。");
                if (firstIdentityError?.Boundary != null)
                {
                    Point3d center = new Point3d(
                        (firstIdentityError.Boundary.MinX + firstIdentityError.Boundary.MaxX) / 2d,
                        (firstIdentityError.Boundary.MinY + firstIdentityError.Boundary.MaxY) / 2d,
                        0d);
                    if (QuickLineCadService.TryCenterView(ctx, center))
                        ctx.Write("\n[XLAYOUT] 视图已定位到第一个异常图框。");
                }
                return;
            }

            Dictionary<string, HashSet<string>> expectedCircuits =
                ReadExpectedCircuits(ctx, items.Select(item => item.MachineId));
            HashSet<string> duplicateHandles = FindDuplicateCircuits(items, summaries,
                expectedCircuits);

            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items);
            if (!TryGetLayoutOrigin(ctx, out Point3d layoutOrigin))
            {
                SelectionService.ClearPickFirst(ctx);
                ctx.Write("\n[XLAYOUT] 已取消位置选择，未执行排版。");
                return;
            }

            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                HashSet<string> targetHandles = new HashSet<string>(
                    placements.Select(placement => placement.Item.Handle ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                RemoveDuplicateMarkers(ctx, transaction, targetHandles);
                var moved = new HashSet<ObjectId>();
                foreach (XLayoutPlacement placement in placements)
                {
                    FrameRegionGroup group = groupsByItem[placement.Item];
                    Vector3d displacement = new Vector3d(
                        placement.TranslationX + layoutOrigin.X,
                        placement.TranslationY + layoutOrigin.Y,
                        layoutOrigin.Z);
                    foreach (ObjectId id in group.EntityIds)
                    {
                        if (!moved.Add(id)) continue;
                        Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
                        if (entity == null || entity.IsErased) continue;
                        entity.TransformBy(Matrix3d.Displacement(displacement));
                    }
                }

                ObjectId textStyle = StyleManager.GetDrawingStandardStyle(ctx, transaction);
                foreach (IGrouping<int, XLayoutPlacement> row in placements.GroupBy(
                    placement => placement.RowIndex))
                {
                    // TranslationX is relative to each source frame's original MinX. It is
                    // not a comparable world coordinate when source frames start at different
                    // X positions; compare the final placed left edge instead.
                    XLayoutPlacement first = row.OrderBy(placement =>
                            placement.TranslationX + placement.Item.Boundary.MinX)
                        .ThenBy(placement => placement.Item.Handle,
                            StringComparer.OrdinalIgnoreCase).First();
                    Point3d firstFrameMin = new Point3d(
                        first.Item.Boundary.MinX + first.TranslationX + layoutOrigin.X,
                        first.Item.Boundary.MinY + first.TranslationY + layoutOrigin.Y,
                        layoutOrigin.Z);
                    Point3d labelAnchor = new Point3d(
                        firstFrameMin.X - MachineLabelLeftDistance,
                        firstFrameMin.Y + first.Item.Boundary.Height / 2d,
                        firstFrameMin.Z);
                    DBText label = EntityFactory.DBText(ctx, first.Item.MachineId,
                        labelAnchor, MachineLabelTextHeight, 0d,
                        AttachmentPoint.BaseLeft,
                        colorIndex: MachineLabelColor(summaries, first.Item.MachineId),
                        textStyleId: textStyle);
                    ctx.AddToCurrentSpace(transaction, label);
                }
                XLayoutDuplicateMarker.EnsureApplication(ctx.Db, transaction);
                foreach (XLayoutPlacement placement in placements)
                {
                    if (!duplicateHandles.Contains(placement.Item.Handle)) continue;
                    DrawDuplicateMarker(ctx, transaction, placement, layoutOrigin);
                }
                transaction.Commit();
            }

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[XLAYOUT] 排版完成：" + regions.Groups.Count
                + " 个图框，" + summaries.Count + " 个机台；图框间距 10000。");
            using (var form = new XLayoutSummaryForm(summaries.Values.ToList(), regions.Groups.Count))
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        }

        private static short MachineLabelColor(
            IDictionary<string, XLayoutMachineSummary> summaries, string machineId)
        {
            if (summaries != null && summaries.TryGetValue(machineId ?? "",
                out XLayoutMachineSummary summary))
            {
                if (summary.DuplicateCircuitCount > 0
                    || (summary.StatusKnown && !summary.IsComplete))
                    return MachineIncompleteColorIndex;
                if (!summary.StatusKnown) return MachineUnknownColorIndex;
            }
            return MachineCompleteColorIndex;
        }

        internal static string DescribeFrameIdentityFailure(FrameRegionGroup group,
            int index, int total, string reason)
        {
            return XLayoutDiagnostics.DescribeFrameIdentityFailure(group?.Handle,
                group?.Boundary, index, total, group?.EntityIds?.Count ?? 0, reason);
        }

        private static HashSet<string> FindDuplicateCircuits(
            IReadOnlyList<XLayoutFrameItem> items,
            IDictionary<string, XLayoutMachineSummary> summaries,
            IDictionary<string, HashSet<string>> expectedCircuits)
        {
            var duplicateHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, XLayoutFrameItem> duplicate in (items ??
                Array.Empty<XLayoutFrameItem>()).Where(item => item != null)
                .GroupBy(item => (item.MachineId ?? "").Trim() + "\u001f"
                    + (item.DeviceName ?? "").Trim(), IdentityTextNormalizer.Comparer)
                .Where(group => group.Count() > 1))
            {
                foreach (XLayoutFrameItem item in duplicate)
                    duplicateHandles.Add(item.Handle ?? "");
                string machineId = (duplicate.First().MachineId ?? "").Trim();
                if (summaries != null && summaries.TryGetValue(machineId,
                    out XLayoutMachineSummary summary)) summary.DuplicateCircuitCount++;
            }

            foreach (IGrouping<string, XLayoutFrameItem> machine in (items ??
                Array.Empty<XLayoutFrameItem>()).Where(item => item != null)
                .GroupBy(item => (item.MachineId ?? "").Trim(),
                    IdentityTextNormalizer.Comparer))
            {
                if (summaries == null || !summaries.TryGetValue(machine.Key,
                    out XLayoutMachineSummary summary)) continue;
                var selected = new HashSet<string>(machine.Select(item =>
                    (item.DeviceName ?? "").Trim()).Where(value => value.Length > 0),
                    IdentityTextNormalizer.Comparer);
                if (expectedCircuits != null && expectedCircuits.TryGetValue(machine.Key,
                    out HashSet<string> expected))
                {
                    summary.ExpectedCircuitCount = expected.Count;
                    summary.StatusKnown = true;
                    summary.IsComplete = expected.SetEquals(selected)
                        && summary.DuplicateCircuitCount == 0;
                }
            }
            return duplicateHandles;
        }

        private static Dictionary<string, HashSet<string>> ReadExpectedCircuits(
            CadContext ctx, IEnumerable<string> selectedMachineIds)
        {
            string configured = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            if (configured.Length == 0) return null;
            try
            {
                if (!MachineWorkbookSource.TryGetSnapshot(configured,
                    out MachineWorkbookSnapshotInfo snapshot)) return null;
                var result = new Dictionary<string, HashSet<string>>(
                    IdentityTextNormalizer.Comparer);
                foreach (MachineRow row in MachineWorkbookSnapshotStore.Default
                    .ReadRowsForMachines(configured, selectedMachineIds))
                {
                    string machineId = (row.MachineId ?? "").Trim();
                    string circuit = (row.CircuitName ?? "").Trim();
                    if (machineId.Length == 0 || circuit.Length == 0) continue;
                    if (!result.TryGetValue(machineId, out HashSet<string> values))
                    {
                        values = new HashSet<string>(IdentityTextNormalizer.Comparer);
                        result.Add(machineId, values);
                    }
                    values.Add(circuit);
                }
                return result;
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[XLAYOUT] SQLite 机台快照读取失败，机台状态暂不判断: " + ex.Message);
                return null;
            }
        }

        private static void RemoveDuplicateMarkers(CadContext ctx, Transaction transaction,
            ISet<string> targetHandles)
        {
            // XData filter selects only entities carrying the marker RegApp;
            // a plain space sweep would open and inspect every entity.
            var filter = new SelectionFilter(new TypedValue[]
            {
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,
                    XLayoutDuplicateMarker.ApplicationName)
            });
            Editor editor = ctx.Ed;
            PromptSelectionResult matches = editor.SelectAll(filter);
            if (matches.Status != PromptStatus.OK) return;
            foreach (SelectedObject selected in matches.Value)
            {
                if (selected == null || selected.ObjectId.IsNull) continue;
                Entity entity = transaction.GetObject(selected.ObjectId,
                    OpenMode.ForRead, true) as Entity;
                if (entity == null
                    || !XLayoutDuplicateMarker.TryGetSourceHandle(entity,
                        out string sourceHandle)
                    || targetHandles == null
                    || !targetHandles.Contains(sourceHandle)) continue;
                entity.UpgradeOpen();
                entity.Erase();
            }
        }

        private static void DrawDuplicateMarker(CadContext ctx, Transaction transaction,
            XLayoutPlacement placement, Point3d origin)
        {
            FrameRectangle boundary = placement.Item.Boundary;
            double minX = boundary.MinX + placement.TranslationX + origin.X;
            double minY = boundary.MinY + placement.TranslationY + origin.Y;
            double maxX = boundary.MaxX + placement.TranslationX + origin.X;
            double maxY = boundary.MaxY + placement.TranslationY + origin.Y;
            double inset = Math.Max(1000d, Math.Min(boundary.Width, boundary.Height) * 0.01d);
            Point3d lowerLeft = new Point3d(minX - inset, minY - inset, origin.Z);
            Point3d lowerRight = new Point3d(maxX + inset, minY - inset, origin.Z);
            Point3d upperRight = new Point3d(maxX + inset, maxY + inset, origin.Z);
            Point3d upperLeft = new Point3d(minX - inset, maxY + inset, origin.Z);
            Point3d[] points = { lowerLeft, lowerRight, upperRight, upperLeft };
            for (int index = 0; index < points.Length; index++)
            {
                Line line = EntityFactory.Line(ctx, points[index],
                    points[(index + 1) % points.Length], DuplicateMarkerColorIndex);
                ctx.AddToCurrentSpace(transaction, line);
                XLayoutDuplicateMarker.Set(line, placement.Item.Handle);
            }
        }

        private static bool TryGetLayoutOrigin(CadContext ctx, out Point3d origin)
        {
            var options = new PromptPointOptions("\n请点击指定排版左上角位置 <原点>: ")
            {
                AllowNone = true
            };
            PromptPointResult result = ctx.Ed.GetPoint(options);
            if (result.Status == PromptStatus.OK)
            {
                origin = result.Value;
                return true;
            }
            if (result.Status == PromptStatus.None)
            {
                origin = Point3d.Origin;
                return true;
            }
            origin = Point3d.Origin;
            return false;
        }
    }
}

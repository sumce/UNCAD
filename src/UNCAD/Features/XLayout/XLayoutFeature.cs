using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const short MachineWarningColorIndex = 2;
        private const short MachineMissingColorIndex = 1;
        /// <summary>本次 XLAYOUT 的排列方案（默认取上次记忆值，选择窗可改并记住）。</summary>
        private static Core.Dwg.XLayoutArrangeMode _arrangeMode = LoadArrangeMode();

        private static Core.Dwg.XLayoutArrangeMode LoadArrangeMode()
        {
            Core.Dwg.XLayoutArrangeModes.TryParseValue(
                Settings.Get(ConfigKeys.XLayoutArrangeMode, ""),
                out Core.Dwg.XLayoutArrangeMode mode);
            return mode;
        }

        /// <summary>
        /// D-045：排列对照数据。就绪等级来自快照对照结果（完整 0/重复或多出 1/
        /// 缺回路 2/无基准 3）；Excel 行序与上游编号读同一份 SQLite 快照——
        /// 快照不可用时优雅降级（行内退回按图上位置排，上游模式全部归“未配上游”）。
        /// </summary>
        private static Core.Dwg.XLayoutArrangeContext BuildArrangeContext(
            IDictionary<string, XLayoutMachineSummary> summaries,
            IReadOnlyList<XLayoutFrameItem> items, CadContext ctx)
        {
            var context = new Core.Dwg.XLayoutArrangeContext();
            foreach (XLayoutMachineSummary summary in summaries.Values)
            {
                switch (summary.LabelStatus)
                {
                    case XLayoutLabelStatus.Complete:
                        context.ReadinessRank[summary.MachineId] = 0;
                        break;
                    case XLayoutLabelStatus.Missing:
                        context.ReadinessRank[summary.MachineId] = 2;
                        break;
                    default:
                        // 警告（重复/多出）与未判断分开：未判断排最后。
                        context.ReadinessRank[summary.MachineId] =
                            summary.StatusKnown ? 1 : 3;
                        break;
                }
            }
            string configured = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            if (configured.Length == 0) return context;
            try
            {
                if (!MachineWorkbookSource.TryGetSnapshot(configured,
                        out MachineWorkbookSnapshotInfo snapshot))
                    return context;
                int order = 0;
                foreach (MachineRow row in MachineWorkbookSnapshotStore.Default
                    .ReadRowsForMachines(configured, items.Select(item => item.MachineId)))
                {
                    string key = Core.Dwg.XLayoutArrangeContext.MakeKey(
                        row.MachineId, row.CircuitName);
                    if (!context.RowOrder.ContainsKey(key))
                        context.RowOrder[key] = order;
                    context.UpstreamOf[key] = (row.Fr ?? "").Trim();
                    order++;
                }
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[XLAYOUT] SQLite 机台快照读取失败，行序/上游排列退回图上位置: "
                    + ex.Message);
            }
            return context;
        }
        /// <summary>
        /// 机台号（归一化）→ 台账 U_区域。失败返回 null：选择窗隐藏区域下拉，
        /// 行为与旧版一致（3.2.0.10）。
        /// </summary>
        private static IReadOnlyDictionary<string, string> LoadRegionByMachine(
            IReadOnlyList<Core.Dwg.XLayoutFrameItem> items)
        {
            try
            {
                string configured = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
                if (configured.Length == 0) return null;
                if (!MachineWorkbookSource.TryGetSnapshot(configured, out _)) return null;
                var region = new Dictionary<string, string>(
                    Core.Text.IdentityTextNormalizer.Comparer);
                foreach (MachineRow row in MachineWorkbookSnapshotStore.Default
                    .ReadRowsForMachines(configured, items.Select(item => item.MachineId)))
                {
                    string key = Core.Text.IdentityTextNormalizer.Key(row.MachineId ?? "");
                    if (key.Length == 0 || region.ContainsKey(key)) continue;
                    region[key] = (row.Region ?? "").Trim();
                }
                return region.Count > 0 ? region : null;
            }
            catch (System.Exception ex)
            {
                Log.Info("[XLAYOUT] 区域信息读取失败（区域筛选不可用）: " + ex.Message);
                return null;
            }
        }

        private const short DuplicateMarkerColorIndex = 2;
        // 只在明显变慢时输出一行 [PERF]；用户交互/对话框时间不计入。
        private const long PerfThresholdMilliseconds = 500;

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

            var phaseWatch = Stopwatch.StartNew();
            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx, selected);
            long collectMilliseconds = phaseWatch.ElapsedMilliseconds;
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

            phaseWatch.Restart();
            var items = new List<XLayoutFrameItem>();
            var groupsByItem = new Dictionary<XLayoutFrameItem, FrameRegionGroup>();
            var summaries = new Dictionary<string, XLayoutMachineSummary>(
                IdentityTextNormalizer.Comparer);
            var identityErrors = new List<string>();
            FrameRegionGroup firstIdentityError = null;
            int index = 0;
            var identityProgress = new ProgressWindow("XLAYOUT 读取机台身份");
            int identityStep = Math.Max(1, regions.Groups.Count / 100);
            try
            {
            using (Transaction readTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(readTransaction);
                foreach (FrameRegionGroup group in regions.Groups)
                {
                    index++;
                    if (index % identityStep == 0 || index == regions.Groups.Count)
                        identityProgress.Update("识别图框机台身份", index, regions.Groups.Count);
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
            }
            finally { identityProgress.CloseByOwner(); }

            long identityMilliseconds = phaseWatch.ElapsedMilliseconds;
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

            phaseWatch.Restart();
            Dictionary<string, HashSet<string>> expectedCircuits =
                ReadExpectedCircuits(ctx, items.Select(item => item.MachineId));
            HashSet<string> duplicateHandles = FindDuplicateCircuits(items, summaries,
                expectedCircuits);
            // D-045：排列对照数据（机台就绪等级 + Excel 行序 + 上游编号）。
            Core.Dwg.XLayoutArrangeContext arrangeContext = BuildArrangeContext(
                summaries, items, ctx);
            long prepareMilliseconds = phaseWatch.ElapsedMilliseconds;

            // 排版前让用户挑选机台/回路：树形勾选 + 关键词筛选（空格/逗号分隔，AND）。
            // 只有"勾选且命中当前筛选"的回路参与重排，其余图框保持原位。
            IReadOnlyList<XLayoutFrameItem> arrangedItems = items;
            if (items.Count > 1)
            {
                // 3.0 WPF 窗体（D-038）：确认与否用 Confirmed 表达，取消/关闭/Esc 一律 false。
                // 3.2.0.10：带上台账区域（U_区域）→ 选择窗可按区域筛选（现场：
                // 手拼 AMHS 机台清单防不住同号/近名混入，区域才是可靠口径）。
                var selector = new XLayoutSelectionWindow(items, _arrangeMode,
                    LoadRegionByMachine(items));
                UNCAD.UI.ModalWindowHost.Show(selector);
                if (!selector.Confirmed)
                {
                    SelectionService.ClearPickFirst(ctx);
                    ctx.Write("\n[XLAYOUT] 已取消，未执行排版。");
                    return;
                }
                arrangedItems = selector.SelectedItems;
                _arrangeMode = selector.ArrangeMode;
                Settings.Set(ConfigKeys.XLayoutArrangeMode,
                    Core.Dwg.XLayoutArrangeModes.Value(_arrangeMode));
            }
            if (arrangedItems.Count == 0)
            {
                SelectionService.ClearPickFirst(ctx);
                ctx.Write("\n[XLAYOUT] 没有选择任何回路，未执行排版。");
                return;
            }
            Dictionary<string, XLayoutMachineSummary> arrangedSummaries =
                BuildArrangedSummaries(arrangedItems, expectedCircuits);
            phaseWatch.Restart();
            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(
                arrangedItems, arrangeContext, _arrangeMode);
            long arrangeMilliseconds = phaseWatch.ElapsedMilliseconds;
            if (!TryGetLayoutOrigin(ctx, out Point3d layoutOrigin))
            {
                SelectionService.ClearPickFirst(ctx);
                ctx.Write("\n[XLAYOUT] 已取消位置选择，未执行排版。");
                return;
            }

            phaseWatch.Restart();
            long commitMilliseconds = 0;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                HashSet<string> targetHandles = new HashSet<string>(
                    placements.Select(placement => placement.Item.Handle ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                RemoveDuplicateMarkers(ctx, transaction, targetHandles);
                var moved = new HashSet<ObjectId>();
                int written = 0, skippedInPlace = 0;
                // 千/万级图框：进度窗 + 边写边刷，替代“无响应到最后一次性蹦完”。
                var progress = new ProgressWindow("XLAYOUT 排版写入");
                progress.Show();
                int progressStep = Math.Max(1, placements.Count / 100);
                try
                {
                foreach (XLayoutPlacement placement in placements)
                {
                    FrameRegionGroup group = groupsByItem[placement.Item];
                    Vector3d displacement = new Vector3d(
                        placement.TranslationX + layoutOrigin.X,
                        placement.TranslationY + layoutOrigin.Y,
                        layoutOrigin.Z);
                    // 已在目标位的图框整框跳过：重复排版时避免十几万次无谓的
                    // 实体改写（每次改写都产生 undo 记录，这是重复 XLAYOUT
                    // 又慢又肿的主因之一）。
                    if (displacement.Length <= 1e-9)
                    {
                        skippedInPlace++;
                        continue;
                    }
                    written++;
                    if (written % progressStep == 0 || written == placements.Count)
                        progress.Update("排版写入图框（已跳过 "
                            + skippedInPlace + " 个在位图框）", written, placements.Count);
                    foreach (ObjectId id in group.EntityIds)
                    {
                        if (!moved.Add(id)) continue;
                        Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
                        if (entity == null || entity.IsErased) continue;
                        entity.TransformBy(Matrix3d.Displacement(displacement));
                    }
                    // 大批量时边写边刷：定期冲刷图形，
                    // 替代“憋在事务里 30 秒无响应、最后一次性蹦完”。
                    if (written % 50 == 0)
                    {
                        ctx.Db.TransactionManager.QueueForGraphicsFlush();
                        Autodesk.AutoCAD.ApplicationServices.Application
                            .DocumentManager.MdiActiveDocument.Editor.UpdateScreen();
                    }
                }
                // 提交段（AutoCAD 统一重生成被移动实体 + 写撤销记录）才是真正的
                // 重活：1537 框实测约 50s 全在 Commit。进度窗转不确定态罩住它，
                // 不再“进度条飞完后死画面”。
                progress.SetIndeterminate("提交变更：AutoCAD 正在重生成 "
                    + written + " 个图框（大图可能需要几十秒）…");
                if (skippedInPlace > 0)
                    ctx.Write("\n[XLAYOUT] " + skippedInPlace
                        + " 个图框已在目标位，未重复改写。");

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
                    string rowLabelText = string.IsNullOrWhiteSpace(first.RowLabel)
                        ? first.Item.MachineId : first.RowLabel;
                    DBText label = EntityFactory.DBText(ctx, rowLabelText,
                        labelAnchor, MachineLabelTextHeight, 0d,
                        AttachmentPoint.BaseLeft,
                        colorIndex: MachineLabelColor(arrangedSummaries, first.Item.MachineId),
                        textStyleId: textStyle);
                    ctx.AddToCurrentSpace(transaction, label);
                }
                XLayoutDuplicateMarker.EnsureApplication(ctx.Db, transaction);
                foreach (XLayoutPlacement placement in placements)
                {
                    if (!duplicateHandles.Contains(placement.Item.Handle)) continue;
                    DrawDuplicateMarker(ctx, transaction, placement, layoutOrigin);
                }
                var commitWatch = System.Diagnostics.Stopwatch.StartNew();
                transaction.Commit();
                commitWatch.Stop();
                commitMilliseconds = commitWatch.ElapsedMilliseconds;
            }
            finally { progress.CloseByOwner(); }
            }
            long writeMilliseconds = phaseWatch.ElapsedMilliseconds;
            long perfTotal = collectMilliseconds + identityMilliseconds
                + prepareMilliseconds + arrangeMilliseconds + writeMilliseconds;
            if (perfTotal >= PerfThresholdMilliseconds)
            {
                Log.Info("[PERF] XLAYOUT total=" + perfTotal + "ms"
                    + " collect=" + collectMilliseconds + "ms"
                    + " identity=" + identityMilliseconds + "ms"
                    + " prepare=" + prepareMilliseconds + "ms"
                    + " arrange=" + arrangeMilliseconds + "ms"
                    + " write=" + writeMilliseconds + "ms"
                    + " commit=" + commitMilliseconds + "ms"
                    + " frames=" + regions.SelectedFrameCount
                    + " machines=" + summaries.Count
                    + " arranged=" + arrangedItems.Count);
            }

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[XLAYOUT] 排版完成：" + arrangedItems.Count
                + " 个图框，" + arrangedSummaries.Count + " 个机台；图框间距 10000。");
            // 与 XSTS 同样的可诊断性：非完整机台逐台写日志，命令行点名列出
            // 缺少/多出/重复（最多 10 台），避免"红标签但不知道缺什么"。
            int attention = 0;
            foreach (XLayoutMachineSummary summary in arrangedSummaries.Values
                .Where(item => !item.IsComplete))
            {
                attention++;
                Log.Info("[XLAYOUT] 机台 " + summary.MachineId + "：应有 "
                    + summary.ExpectedCircuitCount + " 回路，本次参与 "
                    + summary.CircuitCount + " 框，"
                    + summary.StatusText
                    + (summary.DetailText.Length > 0
                        ? "（" + summary.DetailText + "）" : ""));
                if (attention <= 10)
                    ctx.Write("\n[XLAYOUT] 机台 " + summary.MachineId + " "
                        + summary.StatusText
                        + (summary.DetailText.Length > 0
                            ? "（" + summary.DetailText + "）" : ""));
            }
            if (attention > 10)
                ctx.Write("\n[XLAYOUT] 其余 " + (attention - 10)
                    + " 个机台详见统计窗。");
            if (attention > 0)
                ctx.Write("\n[XLAYOUT] 红色=缺回路（应有的回路本次没见到）；黄色=重复或多出，不算缺。");

            // 3.0 WPF 窗体（D-038）：只读统计窗。
            var summaryWindow = new XLayoutSummaryWindow(arrangedSummaries.Values.ToList(),
                arrangedItems.Count);
            UNCAD.UI.ModalWindowHost.Show(summaryWindow);
        }

        /// <summary>
        /// 按"实际参与排版"的回路重建机台统计：机台标签颜色与完成统计窗只反映本次
        /// 重排的图框。直接用本次参与集合对照 Excel 基准重算缺少/多出/重复——
        /// 只排一部分时缺的是"没排的那些回路"（红，正确信号）；重复与多出走黄，
        /// 不再被误标成"缺回路"。
        /// </summary>
        private static Dictionary<string, XLayoutMachineSummary> BuildArrangedSummaries(
            IReadOnlyList<XLayoutFrameItem> arrangedItems,
            IDictionary<string, HashSet<string>> expectedCircuits)
        {
            var result = new Dictionary<string, XLayoutMachineSummary>(
                IdentityTextNormalizer.Comparer);
            if (arrangedItems == null) return result;
            foreach (IGrouping<string, XLayoutFrameItem> machine in arrangedItems
                .Where(item => item != null)
                .GroupBy(item => (item.MachineId ?? "").Trim(),
                    IdentityTextNormalizer.Comparer))
            {
                var summary = new XLayoutMachineSummary(machine.Key)
                {
                    CircuitCount = machine.Count()
                };
                FillMachineStatus(summary, machine.ToList(), expectedCircuits);
                result[machine.Key] = summary;
            }
            return result;
        }

        /// <summary>按机台回路集合对照 Excel 基准填充统计：缺少/多出的数量与
        /// 回路名、重复组数、IsComplete（三者全零）。基准缺失时保持未判断。</summary>
        private static void FillMachineStatus(XLayoutMachineSummary summary,
            IReadOnlyList<XLayoutFrameItem> machineItems,
            IDictionary<string, HashSet<string>> expectedCircuits)
        {
            summary.DuplicateCircuitCount = machineItems
                .GroupBy(item => (item.DeviceName ?? "").Trim(),
                    IdentityTextNormalizer.Comparer)
                .Count(group => group.Count() > 1);
            var selected = new HashSet<string>(
                machineItems.Select(item => (item.DeviceName ?? "").Trim())
                    .Where(value => value.Length > 0),
                IdentityTextNormalizer.Comparer);
            if (expectedCircuits == null || !expectedCircuits.TryGetValue(
                    summary.MachineId, out HashSet<string> expected))
                return;
            summary.ExpectedCircuitCount = expected.Count;
            summary.StatusKnown = true;
            List<string> missing = expected.Except(selected,
                    IdentityTextNormalizer.Comparer)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            List<string> extra = selected.Except(expected,
                    IdentityTextNormalizer.Comparer)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            summary.MissingCircuitCount = missing.Count;
            summary.MissingCircuitNames.AddRange(missing);
            summary.ExtraCircuitCount = extra.Count;
            summary.ExtraCircuitNames.AddRange(extra);
            summary.IsComplete = missing.Count == 0 && extra.Count == 0
                && summary.DuplicateCircuitCount == 0;
        }

        private static short MachineLabelColor(
            IDictionary<string, XLayoutMachineSummary> summaries, string machineId)
        {
            // 现场口径（2026-11-02）：绿=完整无异常；黄=完整但有重复或未知；红=缺回路。
            if (summaries != null && summaries.TryGetValue(machineId ?? "",
                out XLayoutMachineSummary summary))
            {
                switch (summary.LabelStatus)
                {
                    case XLayoutLabelStatus.Missing: return MachineMissingColorIndex;
                    case XLayoutLabelStatus.Warning: return MachineWarningColorIndex;
                }
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
                FillMachineStatus(summary, machine.ToList(), expectedCircuits);
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

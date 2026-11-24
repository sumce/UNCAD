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
using UNCAD.Core.Report;
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

        // Keep one fixed left column for every row; text grows right without alignment recalculation.
        // D-057：标签字高/离框距离与图框间距改为 U1SET「图框与排版」可配
        //（XLayoutRuntimeOptions），默认值即旧常量 25000 / 300000。
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

        private const short DuplicateMarkerColorIndex = 2;
        // 只在明显变慢时输出一行 [PERF]；用户交互/对话框时间不计入。
        private const long PerfThresholdMilliseconds = 500;

        protected override void Execute(CadContext ctx)
        {
            try
            {
                ExecuteLayout(ctx);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
                when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                ReportException(ctx, ex);
            }
        }

        private void ExecuteLayout(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.XLayout);
            // D-057：排版参数一次快照（间距/标签字高/标签距离/黄框开关）。
            XLayoutRuntimeOptions layout = XLayoutSettings.Current();
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要排版的图框（" + FrameRegionCollector.SupportedFrameDescription + "）: ",
                new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Operation?.Cancel("未选择图框");
                Log.Info("[XLAYOUT] cancelled: no frames selected");
                return;
            }

            var phaseWatch = Stopwatch.StartNew();
            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx, selected);
            long collectMilliseconds = phaseWatch.ElapsedMilliseconds;
            if (regions.Errors.Count > 0)
            {
                ReportFailure(ctx, "图框识别失败",
                    XLayoutDiagnostics.FormatFailures(regions.Errors));
                return;
            }
            if (regions.Groups.Count == 0)
            {
                ReportFailure(ctx, "未找到有效图框",
                    "所选对象不包含受支持的图框（"
                        + FrameRegionCollector.SupportedFrameDescription + "）。");
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
                        // D-045 修订（2026-11-22）：布局身份只读图框自身属性，
                        // 设备块 DEVICENAME 不参与——全图/单机选择必须读出同一身份。
                        identity = FrameIdentityReader.ReadLayoutIdentity(readTransaction,
                            group, definitions);
                    }
                    catch (System.Exception ex)
                    {
                        string error = DescribeFrameIdentityFailure(group, index,
                            regions.Groups.Count, ex.Message);
                        identityErrors.Add(error);
                        Log.Error("[XLAYOUT] 图框身份读取异常：" + error, ex);
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
                    ctx.Operation?.Machine(machineId, deviceName);
                    // D-057：图框带回上游编号与回路身份键（frameinfo_json 明文），
                    // 同机台同名回路靠它们区分；老图框两者为空。
                    var item = new XLayoutFrameItem(machineId, deviceName, group.Boundary,
                        group.Handle, identity.UpstreamId ?? "", identity.CircuitKey ?? "");
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
                if (firstIdentityError?.Boundary != null)
                {
                    Point3d center = new Point3d(
                        (firstIdentityError.Boundary.MinX + firstIdentityError.Boundary.MaxX) / 2d,
                        (firstIdentityError.Boundary.MinY + firstIdentityError.Boundary.MaxY) / 2d,
                        0d);
                    try
                    {
                        QuickLineCadService.TryCenterView(ctx, center);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error("[XLAYOUT] 无法定位到首个异常图框。", ex);
                    }
                }
                ReportFailure(ctx, "发现 " + identityErrors.Count
                    + " 个图框身份异常，未执行排版",
                    XLayoutDiagnostics.FormatFailures(identityErrors));
                return;
            }

            phaseWatch.Restart();
            // D-057：台账快照一次读取（基准/行序/上游/区域共用）；回路状态走
            // 统一配对内核——同名回路按身份键区分，不再按回路名判重/塌缩。
            XLayoutWorkbookContext workbook = XLayoutWorkbookContext.Load(
                items.Select(item => item.MachineId));
            var warningHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, XLayoutMachineSummary> allSummaries =
                XLayoutCircuitStatus.Evaluate(items, workbook, warningHandles);
            // D-045：排列对照数据（机台就绪等级 + Excel 行序 + 上游编号）。
            Core.Dwg.XLayoutArrangeContext arrangeContext = XLayoutCircuitStatus
                .BuildArrangeContext(workbook, allSummaries);
            long prepareMilliseconds = phaseWatch.ElapsedMilliseconds;

            // 排版前让用户挑选机台/回路：树形勾选 + 关键词筛选（空格/逗号分隔，AND）。
            // 只有"勾选且命中当前筛选"的回路参与重排，其余图框保持原位。
            IReadOnlyList<XLayoutFrameItem> arrangedItems = items;
            // D-055：导入的机台/回路清单（选择窗「导入清单…」按钮；单框路径
            // 不开选择窗，清单为 null，结果导出退化为全部参与图框为纲）。
            XLayoutFilterList importedFilter = null;
            if (items.Count > 1)
            {
                // 3.0 WPF 窗体（D-038）：确认与否用 Confirmed 表达，取消/关闭/Esc 一律 false。
                // 3.2.0.10：带上台账区域（U_区域）→ 选择窗可按区域筛选（现场：
                // 手拼 AMHS 机台清单防不住同号/近名混入，区域才是可靠口径）。
                var selector = new XLayoutSelectionWindow(items, _arrangeMode,
                    workbook.Available && workbook.RegionByMachine.Count > 0
                        ? workbook.RegionByMachine : null);
                selector.ImportListRequested = ImportFilterList;
                UNCAD.UI.ModalWindowHost.Show(selector);
                if (!selector.Confirmed)
                {
                    SelectionService.ClearPickFirst(ctx);
                    ctx.Operation?.Cancel("用户取消机台/回路选择");
                    Log.Info("[XLAYOUT] cancelled in machine/circuit selection");
                    return;
                }
                arrangedItems = selector.SelectedItems;
                importedFilter = selector.ImportedFilter;
                _arrangeMode = selector.ArrangeMode;
                Settings.Set(ConfigKeys.XLayoutArrangeMode,
                    Core.Dwg.XLayoutArrangeModes.Value(_arrangeMode));
            }
            if (arrangedItems.Count == 0)
            {
                SelectionService.ClearPickFirst(ctx);
                ctx.Operation?.Cancel("没有选择参与排版的回路");
                Log.Info("[XLAYOUT] cancelled: no circuits selected for arrangement");
                return;
            }
            // 就绪度取“本次参与集”——与标签颜色、统计窗同一口径。旧实现用全图
            // 就绪度排行：取消勾选几条回路后，行序仍按完整机台排，颜色却是红的。
            Dictionary<string, XLayoutMachineSummary> arrangedSummaries =
                XLayoutCircuitStatus.Evaluate(arrangedItems, workbook, null);
            arrangeContext = XLayoutCircuitStatus.BuildArrangeContext(workbook,
                arrangedSummaries);
            phaseWatch.Restart();
            IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(
                arrangedItems, arrangeContext, _arrangeMode, layout.Spacing);
            long arrangeMilliseconds = phaseWatch.ElapsedMilliseconds;
            if (!TryGetLayoutOrigin(ctx, out Point3d layoutOrigin))
            {
                SelectionService.ClearPickFirst(ctx);
                ctx.Operation?.Cancel("用户取消位置选择");
                Log.Info("[XLAYOUT] cancelled while selecting layout origin");
                return;
            }

            phaseWatch.Restart();
            long commitMilliseconds = 0;
            int skippedInPlace = 0;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                HashSet<string> targetHandles = new HashSet<string>(
                    placements.Select(placement => placement.Item.Handle ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                if (layout.WarningMarker) RemoveDuplicateMarkers(ctx, transaction, targetHandles);
                var moved = new HashSet<ObjectId>();
                int written = 0;
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
                        firstFrameMin.X - layout.LabelLeftDistance,
                        firstFrameMin.Y + first.Item.Boundary.Height / 2d,
                        firstFrameMin.Z);
                    string rowLabelText = string.IsNullOrWhiteSpace(first.RowLabel)
                        ? first.Item.MachineId : first.RowLabel;
                    DBText label = EntityFactory.DBText(ctx, rowLabelText,
                        labelAnchor, layout.LabelTextHeight, 0d,
                        AttachmentPoint.BaseLeft,
                        // D-057：颜色取整行最差机台——上游模式一行跨多台机台，
                        // 旧行为取最左图框的机台，颜色随图框位置随机。
                        colorIndex: RowLabelColor(arrangedSummaries, row),
                        textStyleId: textStyle);
                    ctx.AddToCurrentSpace(transaction, label);
                }
                XLayoutDuplicateMarker.EnsureApplication(ctx.Db, transaction);
                // 重复图框与同名回路待确认的图框都画黄框（都是黄标签口径）；U1SET 可关。
                if (layout.WarningMarker)
                {
                    foreach (XLayoutPlacement placement in placements)
                    {
                        if (!warningHandles.Contains(placement.Item.Handle)) continue;
                        DrawDuplicateMarker(ctx, transaction, placement, layoutOrigin);
                    }
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
            Log.Info("[XLAYOUT] 排版完成：参与 " + arrangedItems.Count
                + " 个图框，" + arrangedSummaries.Count + " 个机台；原位跳过 "
                + skippedInPlace + " 个。");
            // 统计窗展示所有非完整机台，文件日志保留逐机台诊断。
            foreach (XLayoutMachineSummary summary in arrangedSummaries.Values
                .Where(item => !item.IsComplete))
            {
                Log.Info("[XLAYOUT] 机台 " + summary.MachineId + "：应有 "
                    + summary.ExpectedCircuitCount + " 回路，本次参与 "
                    + summary.CircuitCount + " 框，"
                    + summary.StatusText
                    + (summary.DetailText.Length > 0
                        ? "（" + summary.DetailText + "）" : ""));
            }

            // 3.0 WPF 窗体（D-038）：统计窗 + 结果导出（D-055：导入清单时以清单
            // 为纲逐条 Done/找不到；未导入时导出全部参与图框）。
            XLayoutResultReport resultReport = XLayoutResultReport.Build(
                importedFilter, items, arrangedItems, arrangedSummaries,
                workbook.Available ? workbook.RowsInOrder : null);
            // D-057：统计窗“定位图框”用的最终位置（排版原点 + 平移量，含原位
            // 跳过的图框）。只平移视图，不改图形。
            var frameCenters = new Dictionary<string, Point3d>(StringComparer.OrdinalIgnoreCase);
            foreach (XLayoutPlacement placement in placements)
            {
                FrameRectangle boundary = placement.Item.Boundary;
                frameCenters[placement.Item.Handle ?? ""] = new Point3d(
                    (boundary.MinX + boundary.MaxX) / 2d + placement.TranslationX + layoutOrigin.X,
                    (boundary.MinY + boundary.MaxY) / 2d + placement.TranslationY + layoutOrigin.Y,
                    layoutOrigin.Z);
            }
            var summaryWindow = new XLayoutSummaryWindow(arrangedSummaries.Values.ToList(),
                arrangedItems.Count, skippedInPlace);
            summaryWindow.ExportRequested = () => ExportResultReport(ctx, resultReport);
            summaryWindow.LocateRequested = handle => frameCenters.TryGetValue(
                    handle ?? "", out Point3d center)
                && QuickLineCadService.TryCenterView(ctx, center);
            UNCAD.UI.ModalWindowHost.Show(summaryWindow);
        }

        /// <summary>
        /// 选择窗「导入清单…」回调（D-055）：文件对话框 + Core 解析都在 Features
        /// （UI 不读文件）。用户关掉对话框 = Cancelled（窗口静默）。
        /// </summary>
        private static XLayoutFilterImportResult ImportFilterList()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择机台/回路清单（Excel 或 CSV：机台ID 必需，回路名称可选）",
                Filter = "机台/回路清单 (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt"
                    + "|Excel 工作簿 (*.xlsx)|*.xlsx|CSV 文本 (*.csv;*.txt)|*.csv;*.txt"
            };
            if (dialog.ShowDialog() != true)
                return new XLayoutFilterImportResult(null, "");
            if (!XLayoutFilterListReader.TryRead(dialog.FileName,
                    out XLayoutFilterList list, out string error))
            {
                Log.Error("[XLAYOUT] 清单解析失败：" + error);
                return new XLayoutFilterImportResult(null, error);
            }
            Log.Info("[XLAYOUT] 已导入清单 " + dialog.FileName + "："
                + list.Entries.Count + " 条。");
            return new XLayoutFilterImportResult(list, "");
        }

        /// <summary>结果报表路径（XSTS 同款 D-035 口径：直接放输出根目录 + 时间戳）。</summary>
        internal static string ResolveResultOutputFile(string root, DateTime now)
        {
            return Path.Combine(root, "UNCAD_XLAYOUT_"
                + now.ToString("yyyyMMdd_HHmmss_fff") + ".xlsx");
        }

        /// <summary>
        /// 写出结果报表（D-055）：目录创建 + 默认根写回 + 写占用预检 + Core 原子写。
        /// 失败抛出带中文原因的异常（由结果窗显示），不影响已完成的排版。
        /// </summary>
        private string ExportResultReport(CadContext ctx, XLayoutResultReport report)
        {
            string configuredRoot = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            string root = OutputLayout.NormalizeRoot(configuredRoot);
            Directory.CreateDirectory(root);
            if (!string.Equals(configuredRoot, root, StringComparison.OrdinalIgnoreCase))
                Settings.Set(ConfigKeys.SubmitFolder, root);
            string path = ResolveResultOutputFile(root, DateTime.Now);
            OutputLayout.ValidateNotLocked(new[] { path });
            XLayoutResultExcelReporter.WriteToFile(report, path);
            ctx.Operation?.Output("XLAYOUT结果", path);
            Log.Info("[XLAYOUT] 结果已导出：" + path);
            return path;
        }

        private static void ReportFailure(CadContext ctx, string heading, string details)
        {
            string report = heading + Environment.NewLine
                + (details ?? "未提供诊断详情。");
            ctx.Operation?.Fail(report);
            Log.Error("[XLAYOUT] " + report
                .Replace("\r\n", " | ").Replace("\n", " | "));
            ShowFailureWindow(heading, report);
        }

        private static void ReportException(CadContext ctx, System.Exception ex)
        {
            ctx.Operation?.Fail("XLAYOUT 执行失败", ex);
            Log.Error("[XLAYOUT] 执行失败。", ex);
            ShowFailureWindow("XLAYOUT 执行失败",
                ex?.ToString() ?? "未提供异常详情。");
        }

        private static void ShowFailureWindow(string heading, string details)
        {
            try
            {
                UNCAD.UI.ModalWindowHost.Show(
                    new XLayoutFailureWindow(heading, details));
            }
            catch (System.Exception ex)
            {
                Log.Error("[XLAYOUT] 无法显示诊断窗口。", ex);
                try
                {
                    System.Windows.MessageBox.Show(details, heading,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
                catch { }
            }
        }

        /// <summary>
        /// 行首标签颜色：绿=完整无异常；黄=重复、待确认或未知；红=缺回路
        /// （现场口径 2026-11-02）。取整行**最差**的机台——上游模式一行跨多台
        /// 机台，只看某一台会把缺回路的机台染成绿。
        /// </summary>
        internal static short RowLabelColor(
            IDictionary<string, XLayoutMachineSummary> summaries,
            IEnumerable<XLayoutPlacement> row)
        {
            XLayoutLabelStatus worst = XLayoutLabelStatus.Complete;
            foreach (XLayoutPlacement placement in row ?? Enumerable.Empty<XLayoutPlacement>())
            {
                if (summaries != null && summaries.TryGetValue(
                        placement.Item.MachineId ?? "",
                        out XLayoutMachineSummary summary)
                    && (int)summary.LabelStatus > (int)worst)
                    worst = summary.LabelStatus;
            }
            switch (worst)
            {
                case XLayoutLabelStatus.Missing: return MachineMissingColorIndex;
                case XLayoutLabelStatus.Warning: return MachineWarningColorIndex;
                default: return MachineCompleteColorIndex;
            }
        }

        internal static string DescribeFrameIdentityFailure(FrameRegionGroup group,
            int index, int total, string reason)
        {
            return XLayoutDiagnostics.DescribeFrameIdentityFailure(group?.Handle,
                group?.Boundary, index, total, group?.EntityIds?.Count ?? 0, reason);
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

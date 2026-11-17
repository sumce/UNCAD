using System;
using System.IO;
using System.Linq;
using System.Text;
using UNCAD.Core.ConfigShare;
using UNCAD.Core.Fill;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.2.0.6 六路深审（Fill 链 / QuickLine+Xauto / 导出提交 / Infra 授权 /
    /// UI 层 / 决策文档+测试盲点）确认缺陷的修复回归锁。行为可纯单测的直接
    /// 测行为；接线在 CAD/UI 边界无法单测的，按仓库惯例锁源码契约（标识符
    /// 存在性 + 关键顺序），防止修复被回退。
    /// </summary>
    public sealed class AuditRoundContractTests
    {
        // ---------------- 行为测试 ----------------

        [Fact]
        public void DeviceReserveMarker_RejectsZeroValuedCandidates()
        {
            // D-043 Amendment"0mm 不是候选"被同形写法绕过："0mm" 不匹配正则
            //（至少两位），但 "00mm"/"000mm" 匹配且解析为 0——吸收成 0 还删
            // 图上文字，纯丢数据（3.2.0.6 修复：值 ≤ 0 一律拒绝）。
            Assert.False(DeviceReserveMarker.TryParseMillimeters("00mm", out _));
            Assert.False(DeviceReserveMarker.TryParseMillimeters("000mm", out _));
            // 合法候选不受影响（含前导零但值 > 0 的写法）。
            Assert.True(DeviceReserveMarker.TryParseMillimeters("010mm", out double ten));
            Assert.Equal(10d, ten);
        }

        [Fact]
        public void Rollback_KeepsBackupForEntryItFailedToRestore()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_batch_keep_bak_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "target.txt");
            try
            {
                File.WriteAllText(path, "original");
                using (var batch = new FileBatchRollback(new[] { path }))
                {
                    batch.BeginWrite(path);
                    File.WriteAllText(path, "uncad");
                    batch.MarkWritten(path);

                    // 独占锁住目标文件 → 恢复（File.Copy 覆盖）必然失败。
                    using (var lockStream = new FileStream(path, FileMode.Open,
                        FileAccess.Read, FileShare.None))
                    {
                        AggregateException error = Assert.Throws<AggregateException>(
                            () => batch.Rollback());
                        Assert.Contains("无法恢复输出文件", error.ToString());
                    }
                }

                // 3.2.0.6 修复断言：恢复失败的条目必须保留 .bak（旧版唯一副本，
                // 此前无论成败一律删除备份）。
                string[] backups = Directory.GetFiles(folder, "*.bak");
                Assert.NotEmpty(backups);
                Assert.Equal("original", File.ReadAllText(backups[0]));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void ConfigShareCodec_RejectsNewerVersion_AndAcceptsKnownOlder()
        {
            // 未来版本：拒绝并提示升级（3.2.0.6 前向兼容口径的第一半）。
            string future = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"v\":99}"));
            ConfigShareFormatException futureError = Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode(future));
            Assert.Contains("升级", futureError.Message);
            // 未知旧版本：拒绝并提示重新导出（不误导用户"升级"）。
            string unknown = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"v\":0}"));
            ConfigShareFormatException unknownError = Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode(unknown));
            Assert.Contains("无法识别", unknownError.Message);
            // 当前版本照常可解（完整往返）。
            var payload = new ConfigSharePayload();
            payload.Items["UNL_HEIGHT"] = "180";
            ConfigSharePayload decoded = ConfigShareCodec.Decode(
                ConfigShareCodec.Encode(payload));
            Assert.Equal("180", decoded.Items["UNL_HEIGHT"]);
        }

        // ---------------- 源码契约：Fill 链 ----------------

        [Fact]
        public void MarkerReader_ExcludesU1LAssociatedLabels()
        {
            // U1L/U1LX 生成的毫米标（水平线如正交草图 "2000mm"）与 D-043 判据
            // 同形——读侧必须按"带 U1LX 线关联"排除，否则标注被当预留标吸收：
            // 文字被删、长度从米数统计剔除（3.2.0.6）。
            // 3.3.3.1 双锚定升级（进度窗 3.2.0.8 同款教训：本测试旧版锁的
            // "非空即排除"正是现场病灶——垃圾关联 "0"/悬空/复制残留把真预留标
            // 全拦在门外，用户报告"识别不到横着的毫米标"，两日日志版本指纹
            // 定位 3.2.0.6 排除口随升级生效）：排除口改为 AssociatedLineIsLive
            // （关联解析到本图活着的 Line/Polyline 才排除），且该判定必须仍在
            // 判据 switch 之前。运行时行为由 UNCAD_MARKER_SELFTEST 真机锁死
            // （三种关联状态三根横标）。
            string source = Read("src", "UNCAD", "Features", "Fill",
                "DeviceReserveMarkerReader.cs");
            int gate = source.IndexOf("if (AssociatedLineIsLive(source, transaction))",
                StringComparison.Ordinal);
            int helper = source.IndexOf(
                "private static bool AssociatedLineIsLive(DBObject source,",
                StringComparison.Ordinal);
            int liveness = source.IndexOf("resolved is Line || resolved is Polyline",
                StringComparison.Ordinal);
            int firstCase = source.IndexOf("case DBText", StringComparison.Ordinal);
            Assert.True(gate >= 0 && helper > gate && liveness > helper,
                "排除口必须是 AssociatedLineIsLive（关联解析到活线才排除），实现不得退化回字符串非空判");
            Assert.True(firstCase < 0 || gate < firstCase,
                "活性排除必须在判据 switch 之前");
        }

        [Fact]
        public void U1F_EmbeddedMergeIsGated_And_BatchPreflightRunsGuard()
        {
            // 3.2.0.5 只关了独立块暗道；内嵌 FRAMEINFO 合并同样要在 U1F 关闭
            // （无时间戳的会话合成记录在 Merge 的时间戳规则下恒败给旧记录）。
            string writer = Read("src", "UNCAD", "Features", "Fill",
                "FrameInfoJsonBlockWriter.cs");
            int gate = writer.IndexOf(
                "embedded != null && embedded.BelongsToMachine(machine.MachineId)",
                StringComparison.Ordinal);
            int inherit = writer.IndexOf("&& inheritLegacyStandaloneRecord",
                StringComparison.Ordinal);
            Assert.True(gate >= 0 && inherit > gate,
                "内嵌旧记录合并必须与独立块继承共用 U1F 闸门");

            // 批量链补 Guard：幽灵记录（物理身份是本机台、JSON 记录指向别机）
            // 此前只在单框链被防御，批量会照单采用并把旧机台预留洗白进新 JSON。
            string preflight = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs");
            Assert.Contains("FrameRedefineGuard.Apply", preflight, StringComparison.Ordinal);
        }

        [Fact]
        public void FillFeature_TableWriteFailure_ThrowsInsteadOfSilentReturn()
        {
            // 单框写表 -1 此前静默 return（用户不知道整体已回滚），批量同条件
            // 抛异常——统一为抛出+点名回滚（3.2.0.6）。
            string source = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            int failure = source.IndexOf("if (filled < 0)", StringComparison.Ordinal);
            int throwSite = source.IndexOf("throw new InvalidOperationException",
                failure, StringComparison.Ordinal);
            Assert.True(failure >= 0 && throwSite >= 0,
                "写表失败必须抛出（与批量链同口径），不得静默 return");
            // U1U 可见消息不再硬编码 [U1F]。
            Assert.DoesNotContain("\n[U1F] 已选到统计文字", source, StringComparison.Ordinal);
        }

        [Fact]
        public void WorkbookStage_UsesModeAwarePrefix()
        {
            string source = Read("src", "UNCAD", "Features", "Fill",
                "FillWorkbookStage.cs");
            Assert.DoesNotContain("\n[U1F] 机台数据", source, StringComparison.Ordinal);
            Assert.Contains("(updateMode ? \"U1U\" : \"U1F\")", source,
                StringComparison.Ordinal);
        }

        // ---------------- 源码契约：Xauto / QuickLine ----------------

        [Fact]
        public void XAuto_HandlesParallelAnnotationOutputs()
        {
            // D-053×D-050 交界：重排随线平移 U1Q/U1C 旧输出、开洞把 SOURCE/RUN
            // 改挂到新 Polyline——否则旧输出孤儿化，重跑 U1Q/U1C 也无法回收。
            string feature = Read("src", "UNCAD", "Features", "XAuto", "XAutoFeature.cs");
            Assert.Contains("CollectBySourceHandle", feature, StringComparison.Ordinal);
            Assert.Contains("TranslateOutputs", feature, StringComparison.Ordinal);
            Assert.Contains("RetargetHandles", feature, StringComparison.Ordinal);

            string annotator = Read("src", "UNCAD", "Cad", "ParallelCurveAnnotator.cs");
            Assert.Contains("CollectBySourceHandle", annotator, StringComparison.Ordinal);
            Assert.Contains("RetargetHandles", annotator, StringComparison.Ordinal);
        }

        [Fact]
        public void AddRuns_ErasesPreviousOnlyForSuccessfulRuns()
        {
            // A 趟成功、B 趟失败时，B 趟旧标注此前也被整批删除（无新输出顶上
            // = 数据丢失）。分桶后只删成功趟的旧输出（3.2.0.6）。
            string source = Read("src", "UNCAD", "Cad", "ParallelCurveAnnotator.cs");
            Assert.Contains("previousByMember", source, StringComparison.Ordinal);
            Assert.Contains("previousSeen", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FitRetryLoop_TruncatesDiagnosticsBeforeReapply()
        {
            // 截断时序：上一轮诊断段的清理必须发生在本轮 ApplyComponent 之前
            // ——原实现在其后，第二轮自己的诊断也被切掉（3.2.0.6）。
            string source = Read("src", "UNCAD", "Core", "QuickLine",
                "SketchProportionPlanner.cs");
            int loopStart = source.IndexOf("for (int attempt = 0;",
                StringComparison.Ordinal);
            int removeRange = source.IndexOf("diagnostics.RemoveRange",
                loopStart, StringComparison.Ordinal);
            int apply = source.IndexOf("ApplyComponent(graph", loopStart,
                StringComparison.Ordinal);
            Assert.True(loopStart >= 0 && removeRange >= 0 && apply >= 0
                && removeRange < apply,
                "收缩重试环必须先清上一轮诊断再装配本轮");
        }

        [Fact]
        public void TryLinkLineAndLabel_AcceptsMTextLabels()
        {
            string source = Read("src", "UNCAD", "Cad", "QuickLine",
                "QuickLineCadService.cs");
            int methodStart = source.IndexOf("public static bool TryLinkLineAndLabel",
                StringComparison.Ordinal);
            int methodEnd = source.IndexOf("public static", methodStart + 1,
                StringComparison.Ordinal);
            Assert.True(methodStart >= 0 && methodEnd > methodStart);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            Assert.Contains("as Entity", body, StringComparison.Ordinal);
            Assert.DoesNotContain("as DBText", body, StringComparison.Ordinal);
        }

        // ---------------- 源码契约：导出链 ----------------

        [Fact]
        public void PdfExport_CreatesMachineDirectory_AndSkipsInvalidMachineIds()
        {
            string source = Read("src", "UNCAD", "Features", "PdfExport",
                "PdfExportFeature.cs");
            Assert.Contains("GetInvalidFileNameChars", source, StringComparison.Ordinal);
            Assert.Contains("CreateDirectory", source, StringComparison.Ordinal);

            // 失败清理必须在会话释放之后（此前在 using 内 return，引擎仍持有
            // PDF 句柄时 TryDelete 被吞，半个 PDF 留在机台文件夹）。
            string service = Read("src", "UNCAD", "Features", "PdfExport",
                "PdfExportService.cs");
            int dispose = service.IndexOf("session.Dispose()", StringComparison.Ordinal);
            int delete = service.IndexOf("TryDelete(file)", dispose,
                StringComparison.Ordinal);
            Assert.True(dispose >= 0 && delete > dispose,
                "残件清理必须先释放出图会话");
        }

        [Fact]
        public void ExportWriters_AreAtomicOrBackupSafe()
        {
            // XSTS 改临时文件+替换的原子写（此前全仓唯一一个直写目标的导出器）。
            string xsts = Read("src", "UNCAD", "Core", "Report", "XstsExcelReporter.cs");
            Assert.Contains(".tmp-", xsts, StringComparison.Ordinal);
            Assert.Contains("File.Replace", xsts, StringComparison.Ordinal);

            // DWG 替换成功后备份删除改 TryDelete——File.Delete 抛 IOException 会
            // 落入降级分支，把已正确写盘的批次误报失败并整体回滚。
            string dwg = Read("src", "UNCAD", "Features", "DwgExport",
                "DwgFrameExportService.cs");
            int replace = dwg.IndexOf("File.Replace(temporary, target, backup, true)",
                StringComparison.Ordinal);
            int tryDelete = dwg.IndexOf("TryDelete(backup)", replace,
                StringComparison.Ordinal);
            Assert.True(replace >= 0 && tryDelete > replace,
                "DWG 替换成功后备份清理必须吞错，不得参与成败判定");

            string rollback = Read("src", "UNCAD", "Infra", "FileBatchRollback.cs");
            Assert.Contains("DeleteBackups(failedTargets)", rollback,
                StringComparison.Ordinal);
        }

        // ---------------- 源码契约：UI / Infra ----------------

        [Fact]
        public void OnlineLicenseWindow_ResetsValidationGuardBeforeClose()
        {
            // 验证成功的 Close() 此前被自己的 Closing 守卫 100% 取消（_validating
            // 在 finally 才复位），窗口不自动关；用户点取消还会把 Saved 翻回 false。
            string source = Read("src", "UNCAD", "UI", "Wpf",
                "OnlineLicenseWindow.xaml.cs");
            int success = source.IndexOf("授权验证成功", StringComparison.Ordinal);
            int reset = source.IndexOf("_validating = false;", success,
                StringComparison.Ordinal);
            int close = source.IndexOf("Close();", success, StringComparison.Ordinal);
            Assert.True(success >= 0 && reset >= 0 && close >= 0 && reset < close,
                "验证成功路径必须先复位 _validating 再 Close");
        }

        [Fact]
        public void Splash_StopsRenderTimer_OnClose()
        {
            string source = Read("src", "UNCAD", "UI", "Wpf", "StartupSplashWindow.cs");
            Assert.Contains("Closed += OnClosedStopTimer", source, StringComparison.Ordinal);
            Assert.Contains("_timer.Stop()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ProgressWindow_CannotBeClosedByUser_ButOwnerCanClose()
        {
            string source = Read("src", "UNCAD", "UI", "Wpf", "ProgressWindow.xaml.cs");
            int closing = source.IndexOf("Closing +=", StringComparison.Ordinal);
            int cancel = source.IndexOf("args.Cancel = true", closing,
                StringComparison.Ordinal);
            Assert.True(closing >= 0 && cancel > closing,
                "进度窗必须取消用户关闭（无取消语义，点 X 只会制造卡死错觉）");
            // 3.2.0.8 现场回归锁：用户关闭的拦截必须带"命令收尾出口"——3.2.0.7
            // 的无条件 Cancel 把 finally 里的 Close() 也取消了，XLAYOUT 排版
            // 完成后进度窗永久滞留（用户报告"都排好了还在显示"）。
            int gate = source.IndexOf("if (!_closeByOwner) args.Cancel = true",
                StringComparison.Ordinal);
            int ownerClose = source.IndexOf("public void CloseByOwner()",
                StringComparison.Ordinal);
            Assert.True(gate > closing && ownerClose > gate,
                "进度窗必须仅拦用户关闭，命令收尾经 CloseByOwner() 放行");
        }

        [Fact]
        public void UnrReplaceLine_RetargetsMetadataAfterErase()
        {
            // 3.3.3.2（审计 #2）：手工 U1R 开洞换线必须照 Xauto ApplyHops 收尾
            // 改挂元数据——毫米标 UNCAD_U1LX 关联重挂新 Polyline、U1Q/U1C 输出
            // UNCAD_PARALLEL SOURCE/RUN 改挂新句柄。旧代码两者都不做，旧句柄
            // 悬空：重跑 U1Q/U1C 认不到旧输出（重复残留）、3.3.3.1 起横排毫米标
            // 还会被 U1U/U1F 的 D-043 排除口判"不在役"误当设备预留吸收。
            // 运行时行为由 UNCAD_UNR_SELFTEST 真机锁死（倒排表命中+改挂+排除）。
            string source = Read("src", "UNCAD", "Features", "Unr", "UnrFeature.cs");
            int erase = source.IndexOf("line.Erase()", StringComparison.Ordinal);
            int retarget = source.IndexOf("ParallelCurveAnnotator.RetargetHandles",
                StringComparison.Ordinal);
            int relink = source.IndexOf("QuickLineCadService.TryLinkLineAndLabel",
                StringComparison.Ordinal);
            Assert.True(erase >= 0 && retarget > erase && relink > erase,
                "U1R 换线在擦除旧线后必须改挂平行标注 SOURCE/RUN 与毫米标 U1LX 关联（镜像 Xauto）");
            // 命令入口必须先一次收集两张倒排表再进点击循环（逐点击全空间走查）。
            int collect = source.IndexOf(
                "ParallelCurveAnnotator.CollectBySourceHandle", StringComparison.Ordinal);
            int labels = source.IndexOf(
                "QuickLineCadService.CollectLabelsByLineHandle", StringComparison.Ordinal);
            Assert.True(collect >= 0 && labels >= 0 && collect < erase && labels < erase,
                "U1R 必须在点击循环前一次收集平行标注与毫米标倒排表");
        }

        [Fact]
        public void BatchPreflight_ParallelOwnershipCollectedOncePerBatch()
        {
            // 3.3.3.2（审计 #4）：平行标注归属收集是全空间走查，批量 U1U 必须
            // 整图只收一次下传复用（3.3.0.1 起逐框 Inspect 内各收一遍是
            // O(图框×实体)，与 routeScanned 提批外同一教训）。
            string coordinator = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string preflight = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs");
            string stage = Read("src", "UNCAD", "Features", "Fill",
                "FillRouteInspectionStage.cs");
            Assert.Equal(1, CountOccurrences(coordinator,
                "ParallelAnnotationOwnership.Collect("));
            Assert.Equal(0, CountOccurrences(preflight,
                "ParallelAnnotationOwnership.Collect("));
            Assert.Contains("precollectedOutputs", preflight,
                StringComparison.Ordinal);
            // 单框路径不传时 Inspect 内自收（保持 3.3.0.1 行为）。
            Assert.Contains("precollectedOutputs", stage, StringComparison.Ordinal);
            Assert.Contains("?? ParallelAnnotationOwnership.Collect(", stage,
                StringComparison.Ordinal);
        }

        [Fact]
        public void MachinePickerPreview_DegradesInsteadOfCrashing()
        {
            // 3.3.3.2（审计 #5）：机台选择窗的预览构建链会主动抛（设备块状态
            // 冲突 InvalidOperationException/软管长度非法 InvalidDataException——
            // 主流程同因是优雅中止）。预览是展示件：WPF 事件处理器必须包住
            // 降级成文案，同 3.3.0.1 路由预览口径，绝不炸选择窗。
            string source = Read("src", "UNCAD", "UI", "Wpf",
                "MachinePickerWindow.xaml.cs");
            int call = source.IndexOf("_previewBuilder(row)", StringComparison.Ordinal);
            int guard = source.IndexOf("catch (Exception ex)", call,
                StringComparison.Ordinal);
            int fallback = source.IndexOf("预览生成失败", call, StringComparison.Ordinal);
            Assert.True(call >= 0 && guard > call && fallback > call,
                "机台选择窗预览必须 try/catch 降级（构建失败出文案，不炸 WPF 事件）");
        }

        private static int CountOccurrences(string source, string fragment)
        {
            int count = 0, index = 0;
            while ((index = source.IndexOf(fragment, index,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += fragment.Length;
            }
            return count;
        }

        // ---------------- 帮助 ----------------

        private static string Read(params string[] parts)
        {
            return File.ReadAllText(Repo(parts));
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}

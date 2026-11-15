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
            string source = Read("src", "UNCAD", "Features", "Fill",
                "DeviceReserveMarkerReader.cs");
            int association = source.IndexOf("QuickLineMetadataStore",
                StringComparison.Ordinal);
            int firstCase = source.IndexOf("case DBText", StringComparison.Ordinal);
            Assert.True(association >= 0 && firstCase >= 0 && association < firstCase,
                "读取器必须在判据 switch 之前按 U1LX 线关联排除 UNCAD 自己的毫米标");
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
        public void ProgressWindow_CannotBeClosedByUser()
        {
            string source = Read("src", "UNCAD", "UI", "Wpf", "ProgressWindow.xaml.cs");
            int closing = source.IndexOf("Closing +=", StringComparison.Ordinal);
            int cancel = source.IndexOf("args.Cancel = true", closing,
                StringComparison.Ordinal);
            Assert.True(closing >= 0 && cancel > closing,
                "进度窗必须取消用户关闭（无取消语义，点 X 只会制造卡死错觉）");
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

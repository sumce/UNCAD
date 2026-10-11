using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// U1PDF：框选图框 → **每个图框（= 一个回路）出一份彩色 PDF** → 上传到服务端，
    /// 供回路详情页在线预览。
    ///
    /// 与 U1DWG 的粒度差异：**DWG 按机台**（一份 DWG 覆盖多个回路），
    /// **PDF 按回路**（一个图框一份，上传时 circuits 必须且只能一个）。
    ///
    /// 回路名的推导与 U1F/U1U 上报清单时**完全同一套**
    /// （`FrameIdentityReader` 读图框持久身份 + `ExistingFillIdentityResolver.MatchMachine`），
    /// 避免"PDF 挂到错误的回路"。
    ///
    /// 出图在主线程逐份执行并打印进度（批量较慢，必须让用户看到进度）；
    /// **上传在后台排队**，失败只记日志与命令行提示，绝不影响图纸与出图结果。
    /// </summary>
    [Feature("pdf-export", "PDF 出图", Commands = CommandIds.PdfExportFeatureCommands,
        Description = "框选图框，按回路导出彩色 PDF 并上传到服务端供在线预览")]
    public sealed class PdfExportFeature : CommandBase
    {
        [CommandMethod(CommandIds.PdfExport, CommandFlags.UsePickSet)]
        public void ExportPdf() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.PdfExport);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要输出 PDF 的图框（" + FrameRegionCollector.SupportedFrameDescription + "）: ",
                new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[U1PDF] 未选择图框，未生成 PDF。");
                return;
            }

            FrameRegionCollection regions = FrameRegionCollector.CollectForExport(ctx, selected);
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors)
                    ctx.Write("\n[U1PDF] 图框识别失败: " + error);
                return;
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[U1PDF] 未找到有效图框（"
                    + FrameRegionCollector.SupportedFrameDescription + "）。");
                return;
            }

            if (!PdfExportService.TryGetDeviceError(out string deviceError))
            {
                ctx.Write("\n[U1PDF] " + deviceError);
                return;
            }

            // 图框 →（机台ID, 回路名）：与 U1F/U1U 同一套推导。
            List<PdfExportTarget> targets;
            Dictionary<string, FrameRegionGroup> regionsByHandle;
            if (!TryResolveTargets(ctx, regions.Groups, out targets, out regionsByHandle,
                out List<string> resolveProblems, out string resolveError))
            {
                ctx.Write("\n[U1PDF] " + resolveError);
                return;
            }
            foreach (string problem in resolveProblems)
                ctx.Write("\n[U1PDF] 已跳过：" + problem);

            string directory = Path.Combine(Path.GetTempPath(), "UNCAD",
                "pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            // 渲染是主线程同步执行（要逐份进度）；上传排队到后台。
            var queued = new List<KeyValuePair<PdfExportTarget, string>>();
            var coordinator = new PdfExportCoordinator(
                target => regionsByHandle.TryGetValue(target.FrameHandle, out FrameRegionGroup region)
                    ? PdfExportService.Render(ctx, region, target.CircuitName, directory)
                    : PdfRenderResult.Fail("找不到图框 " + target.FrameHandle + "。"),
                (target, file) => queued.Add(
                    new KeyValuePair<PdfExportTarget, string>(target, file)),
                message => ctx.Write("\n[U1PDF] " + message));

            PdfExportRun run = coordinator.Run(targets);
            if (queued.Count > 0) QueueUploads(queued, directory);
            // 一份都没排上队（全部出图失败或被跳过）时也要清理临时目录，
            // 否则每失败一次就在 %TEMP% 里留下一个 pdf-<guid> 目录。
            else TryDeleteDirectory(directory);
            ctx.Write("\n[U1PDF] " + run.BuildSummary());
            if (run.Failed > 0 && run.FirstFailureDetails.Count > 0)
            {
                ctx.Write("\n[U1PDF] 首个失败诊断（" + run.FirstFailureLabel + "，详见日志）：");
                foreach (string line in run.FirstFailureDetails)
                    ctx.Write("\n[U1PDF]   " + line);
            }
            if (queued.Count > 0)
                ctx.Write("\n[U1PDF] 已排队上传 " + queued.Count
                    + " 份 PDF（后台执行，失败不影响图纸）。");
        }

        /// <summary>
        /// 逐图框解析（机台ID, 回路名）。复用 `FillFeature.TryResolveFrameCircuit`，
        /// 与 U1F/U1U 的回路名口径完全一致。
        /// </summary>
        private static bool TryResolveTargets(CadContext ctx,
            IReadOnlyList<FrameRegionGroup> regions,
            out List<PdfExportTarget> targets,
            out Dictionary<string, FrameRegionGroup> regionsByHandle,
            out List<string> problems, out string error)
        {
            targets = new List<PdfExportTarget>();
            regionsByHandle = new Dictionary<string, FrameRegionGroup>(StringComparer.OrdinalIgnoreCase);
            problems = new List<string>();
            error = "";

            FillRuntimeOptions options = FillSettings.Current();
            string path = FillFeature.ResolveMachineWorkbookPath(ctx, options.MachineWorkbookPath);
            if (path == null)
            {
                error = "未配置机台数据源，无法确定回路名；请先在 U1SET 选择并刷新机台 Excel。";
                return false;
            }

            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
            }
            catch (System.Exception ex)
            {
                Log.Error("U1PDF 读取机台 SQLite 快照失败", ex);
                error = "读取机台 SQLite 快照失败：" + ex.Message;
                return false;
            }

            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(transaction);
                foreach (FrameRegionGroup region in regions)
                {
                    if (!FillFeature.TryResolveFrameCircuit(transaction, region, workbook,
                        definitions, out string machineId, out string circuitName,
                        out string frameError))
                    {
                        problems.Add("图框 " + region.Handle + "：" + frameError);
                        continue;
                    }
                    targets.Add(new PdfExportTarget
                    {
                        MachineId = machineId,
                        CircuitName = circuitName,
                        FrameHandle = region.Handle
                    });
                    regionsByHandle[region.Handle] = region;
                }
                transaction.Commit();
            }

            if (targets.Count == 0)
            {
                error = "所有图框都无法识别机台/回路，未生成 PDF"
                    + (problems.Count == 0 ? "。" : "：" + string.Join("；", problems));
                return false;
            }
            return true;
        }

        /// <summary>
        /// 后台逐份上传 PDF（kind=pdf，circuits 恰好一个）：失败只记日志+命令行提示
        /// （由 Bootstrap 的 Idle 循环输出），随后删除临时文件与目录。
        /// </summary>
        /// <summary>删除本次出图的临时目录；失败只记日志（残留目录不影响出图结果）。</summary>
        private static void TryDeleteDirectory(string directory)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (System.Exception ex) { Log.Warn("删除 PDF 临时目录失败: " + ex.Message); }
        }

        private static void QueueUploads(List<KeyValuePair<PdfExportTarget, string>> queued,
            string directory)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (KeyValuePair<PdfExportTarget, string> item in queued)
                {
                    try
                    {
                        ClientSubmissionUploader.Default.UploadPdf(item.Key.MachineId,
                            item.Value, item.Key.CircuitName);
                    }
                    catch (System.Exception ex)
                    {
                        // 上传链路的任何异常都不得影响出图结果。
                        Log.Warn("U1PDF 上传异常（已忽略）: " + ex.Message);
                    }
                    finally
                    {
                        try { if (File.Exists(item.Value)) File.Delete(item.Value); }
                        catch (System.Exception ex) { Log.Warn("删除临时 PDF 失败: " + ex.Message); }
                    }
                }
                TryDeleteDirectory(directory);
            });
        }
    }
}

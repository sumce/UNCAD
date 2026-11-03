using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.UI;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Features.Fill;
using UNCAD.Features.Submit;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// U1PDF：框选图框 → **每个图框（= 一个回路）出一份彩色 PDF**，
    /// 按输出布局落到 `<输出根>\<图纸>\PDF\<机台>_<回路>.pdf`（同批同名冲突加 -2/-3），
    /// 写盘前做占用预检。
    ///
    /// 与 U1DWG 的粒度差异：**DWG 按机台**（一份 DWG 覆盖多个回路），
    /// **PDF 按回路**（一个图框一份）。
    ///
    /// 回路名的推导与 U1F/U1U 上报清单时**完全同一套**
    /// （`FrameIdentityReader` 读图框持久身份 + `ExistingFillIdentityResolver.MatchMachine`），
    /// 避免"PDF 挂到错误的回路"。
    ///
    /// 出图在主线程逐份执行并打印进度（批量较慢，必须让用户看到进度）；
    /// 失败的份只跳过该份并在命令行给出中文原因，绝不影响图纸与其余份。
    /// </summary>
    [Feature("pdf-export", "PDF 出图", Commands = CommandIds.PdfExportFeatureCommands,
        Description = "框选图框，按回路导出彩色 PDF 到输出目录")]
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

            // 输出目录：统一走 OutputLayout（<root>\<图纸>\PDF），不再使用 %TEMP%。
            if (!TryPrepareOutputDirectory(ctx, out string root, out string outputDirectory,
                out string directoryError))
            {
                ctx.Write("\n[U1PDF] " + directoryError);
                return;
            }

            // 预先分配每份 PDF 的文件名与最终路径：同批同名冲突加 -2/-3，
            // 这样出图前就能做"文件被占用"预检，也不会让后出的图覆盖前一份。
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PdfExportTarget target in targets)
            {
                string fileName = PdfExportNaming.AllocateFileName(target.MachineId,
                    target.CircuitName, usedNames);
                target.FilePath = OutputLayout.ComposePdfPath(root, target.MachineId,
                    fileName);
            }
            try
            {
                OutputLayout.ValidateNotLocked(targets.Select(target => target.FilePath));
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[U1PDF] PDF 输出文件被占用或被锁定，已中止（未出图）：");
                ctx.Write("\n[U1PDF]   " + ex.Message);
                return;
            }

            // 渲染在主线程同步逐份执行；进度在进度窗（命令行不再逐份刷屏），
            // PDF 直接写最终输出目录，不做任何上传。
            PdfExportRun run;
            var rendered = new List<PdfExportTarget>();
            var renderProgress = new ProgressWindow("U1PDF 出图");
            renderProgress.Show();
            try
            {
            var coordinator = new PdfExportCoordinator(
                target =>
                {
                    PdfRenderResult result = regionsByHandle.TryGetValue(target.FrameHandle,
                        out FrameRegionGroup region)
                        ? PdfExportService.Render(ctx, region, target.CircuitName, target.FilePath)
                        : PdfRenderResult.Fail("找不到图框 " + target.FrameHandle + "。");
                    if (result.Ok) rendered.Add(target);
                    return result;
                },
                message => Log.Info("[U1PDF] " + message));

                run = coordinator.Run(targets, (done, total, label) =>
                    renderProgress.Update("出图 " + label, done, total));
            }
            finally { renderProgress.Close(); }
            // 操作记录：只登记真正出图成功的回路与 PDF 文件。
            foreach (PdfExportTarget target in rendered)
            {
                ctx.Operation?.Machine(target.MachineId, target.CircuitName);
                ctx.Operation?.Output("PDF", target.FilePath);
            }
            // D-042 UI 信息完整性：每份 PDF 一行的结果窗（状态/原因/文件），
            // 命令行只留一行摘要；失败明细在结果窗与日志里。
            ctx.Write("\n[U1PDF] " + run.BuildSummary() + "；明细见结果窗。");
            Log.Info("[U1PDF] 输出目录：" + outputDirectory
                + (run.Failed > 0
                    ? "；首失败 " + run.FirstFailureLabel + "：" + string.Join(" | ",
                        run.FirstFailureDetails)
                    : ""));
            var resultWindow = new UNCAD.UI.PdfExportResultWindow(targets,
                run.Rendered, run.Failed, outputDirectory);
            Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(resultWindow);
        }

        /// <summary>
        /// 解析 PDF 输出目录（&lt;配置根&gt;\&lt;图纸&gt;\PDF），空根时默认
        /// &lt;我的文档&gt;\UNCAD输出 并写回 UNC_SUBMIT_FOLDER。失败返回中文原因。
        /// </summary>
        private static bool TryPrepareOutputDirectory(CadContext ctx, out string root,
            out string outputDirectory, out string error)
        {
            root = "";
            outputDirectory = "";
            error = "";
            try
            {
                string configured = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
                root = OutputLayout.NormalizeRoot(configured);
                Directory.CreateDirectory(root);
                if (!Directory.Exists(root))
                    throw new DirectoryNotFoundException("输出文件夹不存在：" + root);
                if (!string.Equals(configured, root, StringComparison.OrdinalIgnoreCase))
                    Settings.Set(ConfigKeys.SubmitFolder, root);
                // D-042：PDF 与 BOQ/DWG 一样落在 <root><机台> 文件夹里，
                // 每份 PDF 的目录在分配路径时按机台创建。
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error("U1PDF 准备输出目录失败", ex);
                error = "无法准备 PDF 输出目录：" + ex.Message;
                return false;
            }
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
    }
}

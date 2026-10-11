using System;
using System.Collections.Generic;
using System.Text;

namespace UNCAD.Features.PdfExport
{
    /// <summary>一份 PDF 的目标：**一个图框 = 一个回路 = 一份 PDF**（与按机台的 DWG 不同）。</summary>
    internal sealed class PdfExportTarget
    {
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        /// <summary>图框块参照句柄：用于把出图任务映射回具体图框，也便于日志定位。</summary>
        public string FrameHandle { get; set; } = "";

        internal string Label
            => (CircuitName ?? "").Length > 0 ? CircuitName : "(未识别回路)";
    }

    internal sealed class PdfRenderResult
    {
        public bool Ok { get; private set; }
        public string FilePath { get; private set; } = "";
        public string Error { get; private set; } = "";

        internal static PdfRenderResult Success(string filePath)
            => new PdfRenderResult { Ok = true, FilePath = filePath ?? "" };

        internal static PdfRenderResult Fail(string error)
            => new PdfRenderResult { Ok = false, Error = (error ?? "").Trim() };
    }

    /// <summary>一次 U1PDF 的执行结果与中文汇总。</summary>
    internal sealed class PdfExportRun
    {
        public int Total { get; set; }
        public int Rendered { get; set; }
        public int Queued { get; set; }
        public int Failed { get; set; }
        public List<string> Failures { get; } = new List<string>();

        internal void AddFailure(string label, string reason)
        {
            Failed++;
            Failures.Add((label ?? "") + "：" + (reason ?? ""));
        }

        /// <summary>汇总文案：成功 N / 失败 M + 失败原因（最多列 3 条，其余指向日志）。</summary>
        internal string BuildSummary()
        {
            var text = new StringBuilder();
            text.Append("出图完成：成功 ").Append(Rendered).Append(" 份 / 失败 ")
                .Append(Failed).Append(" 份；已排队上传 ").Append(Queued)
                .Append(" 份（后台执行，失败只记日志与命令行提示，不影响图纸）。");
            if (Failures.Count > 0)
            {
                text.Append(" 失败原因：")
                    .Append(string.Join("；", Failures.GetRange(0, Math.Min(3, Failures.Count))));
                if (Failures.Count > 3)
                    text.Append(" 等 ").Append(Failures.Count).Append(" 项，详见日志。");
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// U1PDF 的出图编排（不依赖 AutoCAD，可单测）：
    /// 逐份出图 → 每份**成功后立刻单独排队上传**（一个图框一个回路一份 PDF）。
    ///
    /// 关键语义：
    /// - **某一份失败只跳过该份并继续处理其余份**，最后给出"成功 N / 失败 M + 原因"；
    /// - 出图失败的份**绝不上传**（半成品没有 %PDF- 头，会被服务端 400 拒绝）；
    /// - 上传在后台执行，失败不影响图纸，也不会中断其余份。
    /// </summary>
    internal sealed class PdfExportCoordinator
    {
        private readonly Func<PdfExportTarget, PdfRenderResult> _render;
        private readonly Action<PdfExportTarget, string> _enqueueUpload;
        private readonly Action<string> _report;

        internal PdfExportCoordinator(Func<PdfExportTarget, PdfRenderResult> render,
            Action<PdfExportTarget, string> enqueueUpload, Action<string> report)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _enqueueUpload = enqueueUpload ?? throw new ArgumentNullException(nameof(enqueueUpload));
            _report = report ?? (_ => { });
        }

        internal PdfExportRun Run(IReadOnlyList<PdfExportTarget> targets)
        {
            var run = new PdfExportRun { Total = targets?.Count ?? 0 };
            if (targets == null || targets.Count == 0) return run;

            for (int index = 0; index < targets.Count; index++)
            {
                PdfExportTarget target = targets[index];
                _report("正在出图 " + (index + 1) + "/" + targets.Count + "：" + target.Label + "…");
                if (string.IsNullOrWhiteSpace(target.MachineId)
                    || string.IsNullOrWhiteSpace(target.CircuitName))
                {
                    run.AddFailure(target.Label, "缺少机台ID或回路名，未出图");
                    continue;
                }

                PdfRenderResult rendered;
                try
                {
                    rendered = _render(target) ?? PdfRenderResult.Fail("出图没有返回结果。");
                }
                catch (System.Exception ex)
                {
                    rendered = PdfRenderResult.Fail(ex.Message);
                }
                if (!rendered.Ok)
                {
                    run.AddFailure(target.Label, "出图失败：" + rendered.Error);
                    continue;
                }
                run.Rendered++;
                try
                {
                    _enqueueUpload(target, rendered.FilePath);
                    run.Queued++;
                }
                catch (System.Exception ex)
                {
                    run.AddFailure(target.Label, "排队上传失败：" + ex.Message);
                }
            }
            return run;
        }
    }
}

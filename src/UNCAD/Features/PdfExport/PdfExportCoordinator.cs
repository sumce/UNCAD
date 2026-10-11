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
        /// <summary>成功时的生效设置摘要（布局/设备/介质/窗口/大小），或失败时的逐步诊断。</summary>
        public IReadOnlyList<string> Details { get; private set; } = Array.Empty<string>();

        internal static PdfRenderResult Success(string filePath)
            => Success(filePath, "");

        internal static PdfRenderResult Success(string filePath, string note)
            => new PdfRenderResult
            {
                Ok = true,
                FilePath = filePath ?? "",
                Details = string.IsNullOrWhiteSpace(note)
                    ? Array.Empty<string>()
                    : new[] { note }
            };

        internal static PdfRenderResult Fail(string error)
            => Fail(error, null);

        internal static PdfRenderResult Fail(string error, IEnumerable<string> details)
            => new PdfRenderResult
            {
                Ok = false,
                Error = (error ?? "").Trim(),
                Details = ToList(details)
            };

        private static IReadOnlyList<string> ToList(IEnumerable<string> details)
        {
            var items = new List<string>();
            foreach (string item in details ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(item)) items.Add(item);
            return items;
        }
    }

    /// <summary>一次 U1PDF 的执行结果与中文汇总。</summary>
    internal sealed class PdfExportRun
    {
        /// <summary>命令行里最多回显的诊断行数（防止刷屏）。</summary>
        internal const int MaximumReportedDetailLines = 4;

        public int Total { get; set; }
        public int Rendered { get; set; }
        public int Queued { get; set; }
        public int Failed { get; set; }
        public List<string> Failures { get; } = new List<string>();

        /// <summary>首个失败图框的名称（用于回显诊断）。</summary>
        internal string FirstFailureLabel { get; private set; } = "";

        /// <summary>首个失败的诊断明细（已截断到 <see cref="MaximumReportedDetailLines"/> 行）。</summary>
        internal IReadOnlyList<string> FirstFailureDetails { get; private set; } = Array.Empty<string>();

        internal void AddFailure(string label, string reason)
            => AddFailure(label, reason, null);

        internal void AddFailure(string label, string reason, IEnumerable<string> details)
        {
            Failed++;
            string text = (label ?? "") + "：" + (reason ?? "");
            Failures.Add(text);
            if (FirstFailureLabel.Length > 0) return;
            FirstFailureLabel = label ?? "";
            var lines = new List<string>();
            foreach (string detail in details ?? Array.Empty<string>())
            {
                if (lines.Count >= MaximumReportedDetailLines) break;
                if (string.IsNullOrWhiteSpace(detail)) continue;
                lines.Add(detail.Trim());
            }
            FirstFailureDetails = lines;
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
    /// - 出图失败的份**绝不上传**（半成品没有 %PDF- 头/%%EOF 尾，会被服务端拒绝或预览空白）；
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
            bool reportedSettings = false;

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
                    run.AddFailure(target.Label, "出图失败：" + rendered.Error, rendered.Details);
                    continue;
                }
                run.Rendered++;
                if (!reportedSettings && rendered.Details.Count > 0)
                {
                    reportedSettings = true;
                    _report("出图设置：" + rendered.Details[0]);
                }
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

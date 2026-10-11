using System;
using System.Collections.Generic;

namespace UNCAD.Features.PdfExport
{
    /// <summary>出图结束后的收尾决定：提交事务，还是回滚。</summary>
    internal sealed class PdfPlotCommitDecision
    {
        internal PdfPlotCommitDecision(bool commit, string reason)
        {
            Commit = commit;
            Reason = reason ?? "";
        }

        /// <summary>true = 提交本次事务；false = 回滚。</summary>
        internal bool Commit { get; }

        /// <summary>回滚（或提交）的原因，用于日志；提交且无异常时为空。</summary>
        internal string Reason { get; }
    }

    /// <summary>
    /// U1PDF 出图结束后的收尾策略（纯逻辑，可单测）。
    ///
    /// 出图只需要**写一个 PDF 文件**，对布局打印设置的改动一律不该留在用户图纸里。
    /// 正常路径下我们已经把用户设置逐字段还原（<see cref="PdfPlotSettingsSnapshot"/>），
    /// 还原干净就可以提交；**还原不干净时必须整体回滚** —— 靠事务回滚把布局恢复原样，
    /// 而不是带着半套临时设置（A3、DWG To PDF.pc3、窗口范围）提交给用户。
    ///
    /// 真机探针 UNCAD_TXPROBE 证明了这里依赖的前提：不 Commit 直接 Dispose（或 Abort）
    /// 就是回滚，不会落盘。
    /// </summary>
    internal static class PdfPlotCommitPolicy
    {
        internal static PdfPlotCommitDecision Decide(bool settingsRestored,
            IReadOnlyList<string> problems)
        {
            if (settingsRestored) return new PdfPlotCommitDecision(true, "");

            string detail = problems != null && problems.Count > 0
                ? string.Join("；", problems)
                : "还原过程没有报告具体原因";
            return new PdfPlotCommitDecision(false,
                "还原用户打印设置未全部成功，已回滚本次出图对布局的改动（PDF 已生成，不影响上传）："
                + detail);
        }
    }
}

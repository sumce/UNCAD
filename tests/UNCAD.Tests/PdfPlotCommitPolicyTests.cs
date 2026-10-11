using System;
using System.IO;
using UNCAD.Features.PdfExport;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1PDF 收尾策略契约：**用户的布局打印设置绝不能被这次出图改掉**。
    ///
    /// 正常路径逐字段还原后提交；还原不干净必须整体回滚（UNCAD_TXPROBE 已在真机上
    /// 证明"不提交就是回滚"）。这里同时锁住"提交点唯一且只在还原干净的那一条路径"，
    /// 防止以后有人在失败分支里又加回一个 Commit —— 那正是 2.6.2 修掉的 P1。
    /// </summary>
    public sealed class PdfPlotCommitPolicyTests
    {
        [Fact]
        public void Decide_CommitsWhenTheUsersSettingsWereFullyRestored()
        {
            PdfPlotCommitDecision decision = PdfPlotCommitPolicy.Decide(true, null);
            Assert.True(decision.Commit);
            Assert.Equal("", decision.Reason);
        }

        [Fact]
        public void Decide_RollsBackAndNamesTheProblemsWhenRestoreWasIncomplete()
        {
            PdfPlotCommitDecision decision = PdfPlotCommitPolicy.Decide(false,
                new[] { "还原“打印设备/介质”失败：eInvalidInput", "还原“打印窗口”失败：eInvalidInput" });

            Assert.False(decision.Commit);
            Assert.Contains("回滚", decision.Reason);
            Assert.Contains("还原“打印设备/介质”失败", decision.Reason);
            Assert.Contains("还原“打印窗口”失败", decision.Reason);
            // 回滚只丢弃布局改动，不影响已经出好的 PDF 与上传。
            Assert.Contains("PDF 已生成", decision.Reason);
        }

        [Fact]
        public void Decide_RollsBackEvenWhenNoProblemDetailWasReported()
        {
            PdfPlotCommitDecision decision = PdfPlotCommitPolicy.Decide(false, null);
            Assert.False(decision.Commit);
            Assert.Contains("没有报告具体原因", decision.Reason);
            Assert.Contains("没有报告具体原因",
                PdfPlotCommitPolicy.Decide(false, Array.Empty<string>()).Reason);
        }

        [Fact]
        public void Service_HasExactlyOneCommitPointAndItFollowsThePolicy()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportService.cs"));

            // 源码里 "transaction.Commit()" 只允许出现一次（注释里的历史说明不算）。
            int commitCount = 0;
            int index = 0;
            while (true)
            {
                index = source.IndexOf("transaction.Commit()", index, StringComparison.Ordinal);
                if (index < 0) break;
                // 行首是注释的跳过（真机教训那段说明）。
                int lineStart = source.LastIndexOf('\n', index) + 1;
                string line = source.Substring(lineStart, index - lineStart);
                if (line.IndexOf("//", StringComparison.Ordinal) < 0) commitCount++;
                index += "transaction.Commit()".Length;
            }
            Assert.Equal(1, commitCount);

            int decide = source.IndexOf("PdfPlotCommitPolicy.Decide", StringComparison.Ordinal);
            int commit = source.LastIndexOf("transaction.Commit()", StringComparison.Ordinal);
            int abort = source.IndexOf("transaction.Abort()", StringComparison.Ordinal);
            Assert.True(decide >= 0, "必须走 PdfPlotCommitPolicy 决定提交还是回滚");
            Assert.True(commit > decide, "唯一的提交点必须在策略判断之后");
            Assert.True(abort > decide, "还原不干净时必须回滚");
        }

        [Fact]
        public void Session_RestoresUserSettingsBeforeDisposeAndReportsFailures()
        {
            string session = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "AutoCadPlotSession.cs"));
            Assert.Contains("internal bool RestoreUserSettings()", session);
            Assert.Contains("_settingsRestored = _original.Problems.Count == 0;", session);
            // Dispose 也走同一个幂等还原，失败路径（不提交）同样能保证设置被还原。
            Assert.Contains("RestoreUserSettings();", session);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            var all = new string[parts.Length + 1];
            all[0] = root;
            Array.Copy(parts, 0, all, 1, parts.Length);
            return Path.Combine(all);
        }
    }
}

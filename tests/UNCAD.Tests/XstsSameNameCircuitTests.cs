using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-057：XSTS 对「一个机台两条同名回路（上游编号不同）」的口径。
    /// 3.5.0 之前按回路名 Distinct——合法的同名回路被误报成“图框重复”、
    /// 应有个数塌缩、只画一条也判完整。
    /// </summary>
    public sealed class XstsSameNameCircuitTests
    {
        private const string Machine = "MEDPC01";
        private const string Circuit = "AC Rack";

        private static string Key(string circuit, string upstream)
            => CircuitIdentityKey.Of(new MachineRow
                { MachineId = Machine, CircuitName = circuit, Fr = upstream });

        /// <summary>图框记录：上游编号与身份键来自 frameinfo_json 明文。</summary>
        private static XstsCircuitRecord Frame(string circuit, string upstream,
            string handle, bool keyed = true)
            => new XstsCircuitRecord(Machine, circuit)
            {
                FrameHandle = handle,
                UpstreamId = upstream ?? "",
                CircuitKey = keyed ? Key(circuit, upstream) : ""
            };

        /// <summary>台账行：上游编号与身份键来自快照行。</summary>
        private static XstsCircuitRecord Baseline(string circuit, string upstream)
            => new XstsCircuitRecord(Machine, circuit, "B1", "7")
            {
                UpstreamId = upstream,
                CircuitKey = Key(circuit, upstream)
            };

        [Fact]
        public void SameNameBothDrawn_CountsSeparatelyAndIsNotADuplicate()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    Frame(Circuit, "3N-11B1A-PP-02", "h1"),
                    Frame(Circuit, "3N-31D2A-PP-03", "h2")
                },
                new[] { Baseline(Circuit, "3N-11B1A-PP-02"), Baseline(Circuit, "3N-31D2A-PP-03") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(2, machine.SelectedCircuitCount);
            Assert.Equal(2, machine.ExpectedCircuitCount);
            Assert.Equal(0, machine.MissingCircuitCount);
            Assert.Empty(machine.MissingCircuits);
            Assert.Empty(machine.UnexpectedCircuits);
            Assert.Equal(0, machine.DuplicateFrameCount);
            Assert.Equal(0, machine.UnresolvedCircuitCount);
            Assert.Empty(report.Issues);
            Assert.Equal(1, report.CompleteMachineCount);
            Assert.Equal(2, report.SelectedCircuitCount);
            // 已选回路带上游编号后缀，两条同名回路一眼分得开。
            Assert.Contains("AC Rack（上游 3N-11B1A-PP-02）", machine.SelectedCircuits);
            Assert.Contains("AC Rack（上游 3N-31D2A-PP-03）", machine.SelectedCircuits);
            // 图框明细带上上游编号。
            Assert.Equal(new[] { "3N-11B1A-PP-02", "3N-31D2A-PP-03" },
                report.Frames.Select(frame => frame.UpstreamId));
        }

        [Fact]
        public void SameNameOnlyOneDrawn_ReportsTheOtherMissingWithItsUpstream()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { Frame(Circuit, "3N-31D2A-PP-03", "h2") },
                new[] { Baseline(Circuit, "3N-11B1A-PP-02"), Baseline(Circuit, "3N-31D2A-PP-03") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(1, machine.SelectedCircuitCount);
            Assert.Equal(2, machine.ExpectedCircuitCount);
            Assert.Equal(1, machine.MissingCircuitCount);
            Assert.Equal(new[] { "AC Rack（上游 3N-11B1A-PP-02）" }, machine.MissingCircuits);
            Assert.Equal(0, report.CompleteMachineCount);
            Assert.Equal(1, report.MachinesWithMissingCount);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void SameNameFrameWithoutUpstream_IsPendingNotCompleteAndNamedInTheIssues()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { Frame(Circuit, null, "h1", keyed: false) },
                new[] { Baseline(Circuit, "3N-11B1A-PP-02"), Baseline(Circuit, "3N-31D2A-PP-03") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            // 数量上缺一张，但说不出缺的是哪一条——不冒充具体行。
            Assert.Equal(1, machine.MissingCircuitCount);
            string missing = Assert.Single(machine.MissingCircuits);
            Assert.Contains("2 条同名回路", missing, StringComparison.Ordinal);
            Assert.Contains("h1", missing, StringComparison.Ordinal);
            Assert.Contains("其中缺 1 张", missing, StringComparison.Ordinal);
            Assert.DoesNotContain("上游 3N-", missing, StringComparison.Ordinal);
            Assert.Equal(1, machine.UnresolvedCircuitCount);
            Assert.Equal(0, report.CompleteMachineCount);
            Assert.Equal(1, report.MachinesWithUnresolvedCount);
            Assert.Equal(1, report.UnresolvedCircuitCount);
            Assert.Contains("同名回路待确认 1 台", report.MachineStatusText, StringComparison.Ordinal);
            // 异常图框页一图框一条：点名图框与处理办法。
            XstsFrameIssue issue = Assert.Single(report.Issues);
            Assert.Equal("h1", issue.FrameHandle);
            Assert.Contains("U1F 或 U1U", issue.Description, StringComparison.Ordinal);
        }

        [Fact]
        public void SameCircuitDrawnTwice_FlagsTheSecondFrameAsDuplicate()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    Frame(Circuit, "3N-11B1A-PP-02", "h1"),
                    Frame(Circuit, "3N-11B1A-PP-02", "h2")
                },
                new[] { Baseline(Circuit, "3N-11B1A-PP-02"), Baseline(Circuit, "3N-31D2A-PP-03") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(1, machine.SelectedCircuitCount);
            Assert.Equal(1, machine.DuplicateFrameCount);
            // 重复的那张不能顶替另一条同名回路。
            Assert.Equal(new[] { "AC Rack（上游 3N-31D2A-PP-03）" }, machine.MissingCircuits);
            XstsFrameIssue issue = Assert.Single(report.Issues);
            Assert.Equal("h2", issue.FrameHandle);
            Assert.Contains("h1", issue.Description, StringComparison.Ordinal);
            Assert.Contains("重复", issue.Description, StringComparison.Ordinal);
        }

        [Fact]
        public void UpstreamAnnotationDoesNotSplitIdentity()
        {
            // 图框记的是“编号 槽位(轴位)”，台账是纯编号：同一条回路。
            XstsReport report = XstsReportBuilder.Build(
                new[] { Frame(Circuit, "3U-UPS10D1-PP-03 36(43/AC)", "h1") },
                new[] { Baseline(Circuit, "3U-UPS10D1-PP-03") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(0, machine.MissingCircuitCount);
            Assert.Equal(1, report.CompleteMachineCount);
            Assert.Empty(report.Issues);
        }
    }
}

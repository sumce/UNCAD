using System.Linq;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XstsReportTests
    {
        [Fact]
        public void Build_DeduplicatesSelectedCircuitsAndReportsMissingExpected()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B")
                },
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B"),
                    new XstsCircuitRecord("M1", "C")
                });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(2, machine.SelectedCircuitCount);
            Assert.Equal(3, machine.ExpectedCircuitCount);
            Assert.Equal("C", Assert.Single(machine.MissingCircuits));
            Assert.Equal(1, report.MissingCircuitCount);
        }

        [Fact]
        public void Build_IsCaseInsensitiveAndIgnoresIncompleteRows()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("m1", " a "), new XstsCircuitRecord("", "B") },
                new[] { new XstsCircuitRecord("M1", "A") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal("m1", machine.MachineId);
            Assert.Equal(1, machine.SelectedCircuitCount);
            Assert.Empty(machine.MissingCircuits);
        }

        [Fact]
        public void Build_DoesNotAddMachinesThatWereNotSelected()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M2", "B")
                });

            Assert.Single(report.Machines);
            Assert.Equal("M1", report.Machines[0].MachineId);
        }

        [Fact]
        public void Build_WithoutExpectedDataDoesNotClaimZeroMissing()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new XstsCircuitRecord[0], XstsExpectedDataStatus.NotConfigured,
                "未配置机台 Excel");

            Assert.False(report.ExpectedDataAvailable);
            Assert.Equal("无法判断", report.ExpectedCircuitText);
            Assert.Equal("无法判断", report.MissingCircuitText);
            Assert.Equal("无法判断", report.Machines[0].MissingText);
        }

        [Fact]
        public void Build_ReportsIncompleteDuplicateAndUnknownMachineFrames()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("m1", "a"),
                    new XstsCircuitRecord("", "B")
                },
                new[] { new XstsCircuitRecord("M2", "C") });

            Assert.Equal(3, report.FrameCount);
            Assert.Contains(report.Issues, issue => issue.Description.Contains("重复"));
            Assert.Contains(report.Issues, issue => issue.Description == "缺少机台ID");
            Assert.Contains(report.Issues, issue => issue.Description.Contains("Excel基准"));
            Assert.Equal("A", Assert.Single(report.Machines[0].UnexpectedCircuits));
        }

        [Fact]
        public void Build_MachineAbsentFromReadableWorkbookIsUnknownPerRow()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("MISSING", "A") },
                new[] { new XstsCircuitRecord("OTHER", "B") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.True(report.ExpectedDataAvailable);
            Assert.False(machine.ExpectedDataAvailable);
            Assert.Equal("无法判断", machine.ExpectedCircuitText);
            Assert.Equal("无法判断", machine.MissingText);
            Assert.Contains(report.Issues, issue => issue.Description.Contains("Excel基准"));
        }

        [Fact]
        public void Build_PreservesFrameReadErrorsAsIssues()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("", "", "图框 42 身份读取失败") },
                new XstsCircuitRecord[0]);

            XstsFrameIssue issue = Assert.Single(report.Issues);
            Assert.Equal(1, issue.FrameNumber);
            Assert.Equal("图框 42 身份读取失败", issue.Description);
            Assert.Empty(report.Machines);
        }
    }
}

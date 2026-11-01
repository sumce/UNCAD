using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// XSTS 报表构建的性能回归：机台元数据必须按机台一次分组，不能对每个
    /// 机台重扫整张行表。断言用行访问次数与结果等价，不用脆弱的时间断言。
    /// </summary>
    public sealed class XstsReportPerformanceTests
    {
        [Fact]
        public void Build_LargeSelection_VisitsEachRowOnce()
        {
            const int machines = 800;
            const int circuits = 10;
            var selected = new List<XstsCircuitRecord>();
            var expected = new List<XstsCircuitRecord>();
            for (int machine = 0; machine < machines; machine++)
            {
                string machineId = "M-" + machine.ToString("D4");
                for (int circuit = 0; circuit < circuits; circuit++)
                {
                    string circuitName = "回路-" + circuit;
                    selected.Add(new XstsCircuitRecord(machineId, circuitName,
                        "B-2026", "1"));
                    expected.Add(new XstsCircuitRecord(machineId, circuitName,
                        "B-2026", "1"));
                }
            }

            XstsReport report = XstsReportBuilder.Build(selected, expected);

            Assert.Equal(machines, report.Machines.Count);
            Assert.Equal(selected.Count, report.SelectedCircuitCount);
            Assert.Equal(0, report.MissingCircuitCount);
            Assert.Equal(0, report.UnexpectedCircuitCount);
            // 线性实现把每行归属一次；旧的“每机台全表扫描”是 机台×行 次访问。
            Assert.True(XstsReportBuilder.LastBuildRowVisits <= selected.Count + expected.Count,
                "row visits " + XstsReportBuilder.LastBuildRowVisits);
        }

        [Fact]
        public void Build_MatchesIndependentGroupingForMixedIdentities()
        {
            // 大小写/空白差异、重复行、缺失机台基准与多批次值都要与逐条重算一致。
            var selected = new List<XstsCircuitRecord>
            {
                new XstsCircuitRecord("M Q-01", " A "),
                new XstsCircuitRecord("MQ-01", "a"),
                new XstsCircuitRecord("MQ-01", "B"),
                new XstsCircuitRecord("M2", "C"),
                new XstsCircuitRecord("M3", "D")
            };
            var expected = new List<XstsCircuitRecord>
            {
                new XstsCircuitRecord("MQ-01", "A", "B-1", "7"),
                new XstsCircuitRecord("MQ-01", "B", "B-1", "7"),
                new XstsCircuitRecord("MQ-01", "E", "B-1", "7"),
                new XstsCircuitRecord("M2", "C", "B-2", "8"),
                new XstsCircuitRecord("M9", "X", "B-3", "9")
            };

            XstsReport report = XstsReportBuilder.Build(selected, expected);

            Assert.Equal(new[] { "M Q-01", "M2", "M3" },
                report.Machines.Select(item => item.MachineId));
            XstsMachineSummary first = report.Machines[0];
            Assert.Equal(2, first.SelectedCircuitCount);
            Assert.Equal(new[] { "A", "B" }, first.SelectedCircuits.OrderBy(v => v,
                StringComparer.OrdinalIgnoreCase));
            Assert.Equal(3, first.ExpectedCircuitCount);
            Assert.Equal(new[] { "E" }, first.MissingCircuits);
            Assert.Empty(first.UnexpectedCircuits);
            Assert.Equal("B-1", first.Batch);
            Assert.Equal("7", first.Seq);
            Assert.True(first.ExpectedDataAvailable);

            XstsMachineSummary second = report.Machines[1];
            Assert.Equal("B-2", second.Batch);
            Assert.Equal("8", second.Seq);
            Assert.Empty(second.MissingCircuits);

            XstsMachineSummary third = report.Machines[2];
            Assert.False(third.ExpectedDataAvailable);
            Assert.Equal("无法判断", third.ExpectedCircuitText);
            Assert.Contains(report.Issues, issue =>
                issue.Description == "机台ID在Excel基准中不存在");
        }

        [Fact]
        public void Build_UsesSelectedRowsForMetadataWhenBaselineHasNoMachine()
        {
            // 基准里没有该机台时，批次/序号仍来自本次已选行（保持历史行为）。
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("MISSING", "A", "B-9", "3"),
                    new XstsCircuitRecord("MISSING", "B", "B-9", "3")
                },
                new[] { new XstsCircuitRecord("OTHER", "X") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.False(machine.ExpectedDataAvailable);
            Assert.Equal("B-9", machine.Batch);
            Assert.Equal("3", machine.Seq);
        }

        [Fact]
        public void Build_IsDeterministicAcrossRuns()
        {
            var selected = new List<XstsCircuitRecord>();
            var expected = new List<XstsCircuitRecord>();
            for (int machine = 0; machine < 50; machine++)
            {
                for (int circuit = 0; circuit < 3; circuit++)
                {
                    selected.Add(new XstsCircuitRecord("M" + machine, "C" + circuit));
                    expected.Add(new XstsCircuitRecord("m" + machine, "c" + circuit));
                }
            }

            XstsReport first = XstsReportBuilder.Build(selected, expected);
            XstsReport second = XstsReportBuilder.Build(selected, expected);

            Assert.Equal(first.Machines.Count, second.Machines.Count);
            for (int index = 0; index < first.Machines.Count; index++)
            {
                Assert.Equal(first.Machines[index].MachineId,
                    second.Machines[index].MachineId);
                Assert.Equal(first.Machines[index].Batch,
                    second.Machines[index].Batch);
                Assert.Equal(first.Machines[index].SelectedCircuits,
                    second.Machines[index].SelectedCircuits);
                Assert.Equal(first.Machines[index].MissingCircuits,
                    second.Machines[index].MissingCircuits);
            }
        }
    }
}

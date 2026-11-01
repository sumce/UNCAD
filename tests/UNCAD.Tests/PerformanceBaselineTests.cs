using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Report;
using UNCAD.Core.Stat;
using Xunit;
using Xunit.Abstractions;

namespace UNCAD.Tests
{
    /// <summary>
    /// 关键路径性能基线/回归测量（U1L3D 场景、XSTS 报告、XLAYOUT 编排）。
    ///
    /// 该测试只打印测量值，不做脆弱的时间断言：耗时随机器变化，真正防止
    /// 算法退化的是同一文件里的调用次数/复杂度不变量测试。基线命令：
    ///   dotnet test UNCAD.slnx --filter "FullyQualifiedName~PerformanceBaseline"
    ///   --logger "console;verbosity=detailed"
    /// </summary>
    public sealed class PerformanceBaselineTests
    {
        private readonly ITestOutputHelper _output;

        public PerformanceBaselineTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Baseline_XstsReport_1000MachinesX5Circuits()
        {
            const int machines = 1000;
            const int circuits = 5;
            var selected = new List<XstsCircuitRecord>(machines * circuits);
            var expected = new List<XstsCircuitRecord>(machines * circuits);
            for (int machine = 0; machine < machines; machine++)
            {
                string machineId = "M-" + machine.ToString("D4");
                for (int circuit = 0; circuit < circuits; circuit++)
                {
                    string circuitName = "回路-" + circuit;
                    selected.Add(new XstsCircuitRecord(machineId, circuitName, "B-2026", "1"));
                    expected.Add(new XstsCircuitRecord(machineId, circuitName, "B-2026", "1"));
                }
            }

            Measure("XSTS Build 1000x5", 3, () =>
            {
                XstsReport report = XstsReportBuilder.Build(selected, expected);
                Require(report.Machines.Count == machines, "machines");
            });
            _output.WriteLine("[PERF] XSTS 1000x5 row visits: "
                + XstsReportBuilder.LastBuildRowVisits);
        }

        [Fact]
        public void Baseline_XstsReport_1000MachinesX20Circuits()
        {
            const int machines = 1000;
            const int circuits = 20;
            var selected = new List<XstsCircuitRecord>(machines * circuits);
            var expected = new List<XstsCircuitRecord>(machines * circuits);
            for (int machine = 0; machine < machines; machine++)
            {
                string machineId = "M-" + machine.ToString("D4");
                for (int circuit = 0; circuit < circuits; circuit++)
                {
                    string circuitName = "回路-" + circuit;
                    selected.Add(new XstsCircuitRecord(machineId, circuitName));
                    expected.Add(new XstsCircuitRecord(machineId, circuitName));
                }
            }

            Measure("XSTS Build 1000x20", 3, () =>
            {
                XstsReport report = XstsReportBuilder.Build(selected, expected);
                Require(report.Machines.Count == machines, "machines");
            });
            _output.WriteLine("[PERF] XSTS 1000x20 row visits: "
                + XstsReportBuilder.LastBuildRowVisits);
        }

        [Fact]
        public void Baseline_XstsWorkbook_1000MachineRows()
        {
            const int machines = 1000;
            var records = new List<XstsCircuitRecord>(machines);
            for (int machine = 0; machine < machines; machine++)
                records.Add(new XstsCircuitRecord("M-" + machine.ToString("D4"),
                    "回路-1", "B-2026", "1"));
            XstsReport report = XstsReportBuilder.Build(records, records);
            string path = Path.Combine(Path.GetTempPath(),
                "uncad-perf-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                Measure("XSTS xlsx 1000 rows", 2, () =>
                    XstsExcelReporter.WriteToFile(report, path));
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void Baseline_QuickLineScene_1000SegmentRoute()
        {
            List<QuickLineSegment> segments = Chain(1000);
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);
            Dictionary<string, double> distances = distancesOf(segments);
            Measure("Scene BuildAll 1000 segments", 3, () =>
            {
                QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder.BuildAll(
                    graph, distances, distances, new HashSet<string>());
                Require(scene.Segments.Count == segments.Count, "segments");
            });
        }

        [Fact]
        public void Baseline_QuickLineTraversal_500SegmentChain()
        {
            List<QuickLineSegment> segments = Chain(500);
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);
            QuickLinePoint click = new QuickLinePoint(0, 0);
            Measure("Traversal plan 500-segment chain", 2, () =>
            {
                QuickLineTraversalPlan plan = QuickLineTraversal.CreatePlan(graph,
                    segments[0].Id, click);
                Require(plan.Steps.Count > 0, "steps");
            });
        }

        [Fact]
        public void Baseline_QuickLineGraph_Lookups()
        {
            List<QuickLineSegment> segments = Chain(1000);
            QuickLineGraph graph = QuickLineGraph.Build(segments, 1.0);
            Measure("Graph GetConnections x200k", 2, () =>
            {
                int count = 0;
                for (int index = 0; index < 200000; index++)
                    count += graph.GetConnections(segments[index % segments.Count].Id).Count;
                Require(count > 0, "connections");
            });
        }

        [Fact]
        public void Baseline_XLayoutArrange_1000Frames()
        {
            var items = new List<XLayoutFrameItem>(1000);
            for (int index = 0; index < 1000; index++)
                items.Add(new XLayoutFrameItem("M-" + (index % 100).ToString("D3"),
                    "C" + index, new FrameRectangle("h" + index, index * 2000, 0,
                        index * 2000 + 1000, 500), "h" + index));
            Measure("XLAYOUT Arrange 1000 frames", 3, () =>
            {
                IReadOnlyList<XLayoutPlacement> placements = XLayoutLayout.Arrange(items);
                Require(placements.Count == items.Count, "placements");
            });
        }

        private void Measure(string name, int repeats, Action action)
        {
            var samples = new List<long>(repeats);
            for (int index = 0; index < repeats; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                action();
                stopwatch.Stop();
                samples.Add(stopwatch.ElapsedMilliseconds);
            }
            samples.Sort();
            string line = "[PERF] " + name + ": " + samples[samples.Count / 2]
                + " ms (median of " + repeats + ")";
            _output.WriteLine(line);
            // 可选落盘：环境变量只在本机基线采集时设置，不影响正常测试。
            string output = Environment.GetEnvironmentVariable("UNCAD_PERF_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                try { File.AppendAllText(output, line + Environment.NewLine); }
                catch { }
            }
        }

        private static List<QuickLineSegment> Chain(int count)
        {
            var segments = new List<QuickLineSegment>(count);
            for (int index = 0; index < count; index++)
            {
                // 30° isometric chain: every segment shares an endpoint with the next.
                double x = index * 100.0 * Math.Cos(Math.PI / 6.0);
                double y = index * 100.0 * Math.Sin(Math.PI / 6.0);
                segments.Add(new QuickLineSegment("S" + index,
                    new QuickLinePoint(x, y),
                    new QuickLinePoint(x + 100.0 * Math.Cos(Math.PI / 6.0),
                        y + 100.0 * Math.Sin(Math.PI / 6.0))));
            }
            return segments;
        }

        private static Dictionary<string, double> distancesOf(
            IEnumerable<QuickLineSegment> segments)
        {
            var distances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineSegment segment in segments) distances[segment.Id] = 1000.0;
            return distances;
        }

        private static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Performance fixture: " + name);
        }
    }
}

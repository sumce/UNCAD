using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Stat
{
    public enum XstsExpectedDataStatus
    {
        Available = 0,
        NotConfigured = 1,
        FileNotFound = 2,
        ReadFailed = 3
    }

    /// <summary>One machine row in the XSTS circuit coverage report.</summary>
    public sealed class XstsMachineSummary
    {
        public XstsMachineSummary(string machineId)
        {
            MachineId = (machineId ?? "").Trim();
        }

        public string MachineId { get; }
        /// <summary>机台级元数据；同一机台在报表中只占一个批次字段。</summary>
        public string Batch { get; set; } = "";
        /// <summary>机台级元数据；同一机台在报表中只占一个序号字段。</summary>
        public string Seq { get; set; } = "";
        public int SelectedCircuitCount { get; set; }
        public int ExpectedCircuitCount { get; set; }
        public bool ExpectedDataAvailable { get; set; }
        public List<string> SelectedCircuits { get; } = new List<string>();
        public List<string> MissingCircuits { get; } = new List<string>();
        public List<string> UnexpectedCircuits { get; } = new List<string>();

        public string ExpectedCircuitText => ExpectedDataAvailable
            ? ExpectedCircuitCount.ToString()
            : "无法判断";

        public string MissingText => !ExpectedDataAvailable
            ? "无法判断"
            : MissingCircuits.Count == 0
            ? "无"
            : string.Join("、", MissingCircuits);

        public string UnexpectedText => !ExpectedDataAvailable
            ? "无法判断"
            : UnexpectedCircuits.Count == 0
                ? "无"
                : string.Join("、", UnexpectedCircuits);
    }

    public sealed class XstsFrameIssue
    {
        public int FrameNumber { get; set; }
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>Complete XSTS report, independent of AutoCAD and Excel I/O.</summary>
    public sealed class XstsReport
    {
        public List<XstsMachineSummary> Machines { get; } = new List<XstsMachineSummary>();
        public List<XstsFrameIssue> Issues { get; } = new List<XstsFrameIssue>();
        /// <summary>Physical selected frame count; machines are reported separately.</summary>
        public int FrameCount { get; set; }
        public XstsExpectedDataStatus ExpectedDataStatus { get; set; }
            = XstsExpectedDataStatus.Available;
        public string ExpectedDataDetail { get; set; } = "";
        public bool ExpectedDataAvailable
            => ExpectedDataStatus == XstsExpectedDataStatus.Available;
        public int SelectedCircuitCount => Machines.Sum(item => item.SelectedCircuitCount);
        public int ExpectedCircuitCount => Machines.Sum(item => item.ExpectedCircuitCount);
        public int MissingCircuitCount => Machines.Sum(item => item.MissingCircuits.Count);
        public int UnexpectedCircuitCount => Machines.Sum(item => item.UnexpectedCircuits.Count);
        public int UnknownMachineCount => Machines.Count(item => !item.ExpectedDataAvailable);
        public bool HasUnknownMachineBaseline
            => ExpectedDataAvailable && UnknownMachineCount > 0;
        public string ExpectedCircuitText => ExpectedDataAvailable
            ? HasUnknownMachineBaseline ? "部分无法判断"
            : ExpectedCircuitCount.ToString()
            : "无法判断";
        public string MissingCircuitText => ExpectedDataAvailable
            ? HasUnknownMachineBaseline ? "部分无法判断"
            : MissingCircuitCount.ToString()
            : "无法判断";
        public string UnexpectedCircuitText => ExpectedDataAvailable
            ? HasUnknownMachineBaseline ? "部分无法判断"
            : UnexpectedCircuitCount.ToString()
            : "无法判断";
    }

    public static class XstsReportBuilder
    {
        /// <summary>
        /// Builds a deterministic coverage report.  Circuit names are compared
        /// case-insensitively after trimming; duplicate frame names count once in
        /// the selected set while frame count remains the physical count.
        /// </summary>
        public static XstsReport Build(IEnumerable<XstsCircuitRecord> selected,
            IEnumerable<XstsCircuitRecord> expected,
            XstsExpectedDataStatus expectedDataStatus = XstsExpectedDataStatus.Available,
            string expectedDataDetail = null)
        {
            var selectedRows = (selected ?? Enumerable.Empty<XstsCircuitRecord>()).ToList();
            var expectedRows = (expected ?? Enumerable.Empty<XstsCircuitRecord>()).ToList();
            var report = new XstsReport
            {
                FrameCount = selectedRows.Count,
                ExpectedDataStatus = expectedDataStatus,
                ExpectedDataDetail = expectedDataDetail ?? ""
            };
            AddFrameIssues(selectedRows, report);
            var selectedGroups = Group(selectedRows);
            var expectedGroups = Group(expectedRows);
            // The report is selection-scoped: expected-only machines are deliberately
            // omitted. XSTS answers which circuits are missing from the machines the
            // user framed, not which circuits are missing from the whole workbook.
            foreach (string machineId in selectedGroups.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var row = new XstsMachineSummary(machineId);
                List<XstsCircuitRecord> metadataRows = expectedRows
                    .Where(item => string.Equals((item?.MachineId ?? "").Trim(),
                        machineId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (metadataRows.Count == 0)
                    metadataRows = selectedRows
                        .Where(item => string.Equals((item?.MachineId ?? "").Trim(),
                            machineId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                row.Batch = CollapseField(metadataRows.Select(item => item.Batch));
                row.Seq = CollapseField(metadataRows.Select(item => item.Seq));
                // A globally readable workbook still cannot provide a baseline for a
                // machine ID that is absent from it. Keep that row explicitly unknown
                // instead of presenting an empty expected set as "0 missing".
                row.ExpectedDataAvailable = report.ExpectedDataAvailable
                    && expectedGroups.ContainsKey(machineId);
                HashSet<string> selectedNames = selectedGroups.TryGetValue(machineId,
                    out HashSet<string> selectedSet) ? selectedSet : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> expectedNames = expectedGroups.TryGetValue(machineId,
                    out HashSet<string> expectedSet) ? expectedSet : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                row.SelectedCircuitCount = selectedNames.Count;
                row.SelectedCircuits.AddRange(selectedNames.OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase));
                if (row.ExpectedDataAvailable)
                {
                    row.ExpectedCircuitCount = expectedNames.Count;
                    row.MissingCircuits.AddRange(expectedNames.Except(selectedNames,
                        StringComparer.OrdinalIgnoreCase).OrderBy(value => value,
                        StringComparer.OrdinalIgnoreCase));
                    row.UnexpectedCircuits.AddRange(selectedNames.Except(expectedNames,
                        StringComparer.OrdinalIgnoreCase).OrderBy(value => value,
                        StringComparer.OrdinalIgnoreCase));
                }
                if (report.ExpectedDataAvailable && !expectedGroups.ContainsKey(machineId))
                {
                    report.Issues.Add(new XstsFrameIssue
                    {
                        MachineId = machineId,
                        Description = "机台ID在Excel基准中不存在"
                    });
                }
                report.Machines.Add(row);
            }
            return report;
        }

        private static void AddFrameIssues(IReadOnlyList<XstsCircuitRecord> selected,
            XstsReport report)
        {
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < selected.Count; index++)
            {
                XstsCircuitRecord item = selected[index];
                string machine = (item?.MachineId ?? "").Trim();
                string circuit = (item?.CircuitName ?? "").Trim();
                string description = machine.Length == 0 && circuit.Length == 0
                    ? "缺少机台ID和回路名称"
                    : machine.Length == 0
                        ? "缺少机台ID"
                        : circuit.Length == 0 ? "缺少回路名称" : "";
                if (!string.IsNullOrWhiteSpace(item?.Error))
                    description = item.Error.Trim();
                if (description.Length == 0
                    && !identities.Add(machine + "\u001f" + circuit))
                    description = "机台ID/回路名称与已选图框重复";
                if (description.Length == 0) continue;
                report.Issues.Add(new XstsFrameIssue
                {
                    FrameNumber = index + 1,
                    MachineId = machine,
                    CircuitName = circuit,
                    Description = description
                });
            }
        }

        private static Dictionary<string, HashSet<string>> Group(
            IEnumerable<XstsCircuitRecord> source)
        {
            var result = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (XstsCircuitRecord item in source ?? Enumerable.Empty<XstsCircuitRecord>())
            {
                string machine = (item?.MachineId ?? "").Trim();
                string circuit = (item?.CircuitName ?? "").Trim();
                if (machine.Length == 0 || circuit.Length == 0) continue;
                if (!result.TryGetValue(machine, out HashSet<string> names))
                {
                    names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[machine] = names;
                }
                names.Add(circuit);
            }
            return result;
        }

        private static string CollapseField(IEnumerable<string> values)
        {
            List<string> distinct = (values ?? Enumerable.Empty<string>())
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // The workbook contract is one batch/sequence per machine. If a
            // damaged workbook disagrees across circuits, keep one cell and
            // expose every distinct value instead of silently discarding data.
            return string.Join("、", distinct);
        }
    }

    public sealed class XstsCircuitRecord
    {
        public XstsCircuitRecord(string machineId, string circuitName)
            : this(machineId, circuitName, null, "", "")
        {
        }

        public XstsCircuitRecord(string machineId, string circuitName, string error)
            : this(machineId, circuitName, error, "", "")
        {
        }

        public XstsCircuitRecord(string machineId, string circuitName,
            string batch, string seq)
            : this(machineId, circuitName, null, batch, seq)
        {
        }

        public XstsCircuitRecord(string machineId, string circuitName, string error,
            string batch, string seq)
        {
            MachineId = machineId ?? "";
            CircuitName = circuitName ?? "";
            Error = error ?? "";
            Batch = batch ?? "";
            Seq = seq ?? "";
        }

        public string MachineId { get; }
        public string CircuitName { get; }
        public string Error { get; }
        public string Batch { get; }
        public string Seq { get; }
    }
}

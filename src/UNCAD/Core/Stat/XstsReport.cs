using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Core.Stat
{
    /// <summary>One machine row in the XSTS circuit coverage report.</summary>
    public sealed class XstsMachineSummary
    {
        public XstsMachineSummary(string machineId)
        {
            MachineId = (machineId ?? "").Trim();
        }

        public string MachineId { get; }
        public int SelectedCircuitCount { get; set; }
        public int ExpectedCircuitCount { get; set; }
        public List<string> SelectedCircuits { get; } = new List<string>();
        public List<string> MissingCircuits { get; } = new List<string>();

        public string MissingText => MissingCircuits.Count == 0
            ? "无"
            : string.Join("、", MissingCircuits);
    }

    /// <summary>Complete XSTS report, independent of AutoCAD and Excel I/O.</summary>
    public sealed class XstsReport
    {
        public List<XstsMachineSummary> Machines { get; } = new List<XstsMachineSummary>();
        /// <summary>Physical selected frame count; machines are reported separately.</summary>
        public int FrameCount { get; set; }
        public int SelectedCircuitCount => Machines.Sum(item => item.SelectedCircuitCount);
        public int ExpectedCircuitCount => Machines.Sum(item => item.ExpectedCircuitCount);
        public int MissingCircuitCount => Machines.Sum(item => item.MissingCircuits.Count);
    }

    public static class XstsReportBuilder
    {
        /// <summary>
        /// Builds a deterministic coverage report.  Circuit names are compared
        /// case-insensitively after trimming; duplicate frame names count once in
        /// the selected set while frame count remains the physical count.
        /// </summary>
        public static XstsReport Build(IEnumerable<XstsCircuitRecord> selected,
            IEnumerable<XstsCircuitRecord> expected)
        {
            var report = new XstsReport();
            var selectedGroups = Group(selected);
            var expectedGroups = Group(expected);
            // The report is selection-scoped: expected-only machines are deliberately
            // omitted. XSTS answers which circuits are missing from the machines the
            // user framed, not which circuits are missing from the whole workbook.
            foreach (string machineId in selectedGroups.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var row = new XstsMachineSummary(machineId);
                HashSet<string> selectedNames = selectedGroups.TryGetValue(machineId,
                    out HashSet<string> selectedSet) ? selectedSet : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> expectedNames = expectedGroups.TryGetValue(machineId,
                    out HashSet<string> expectedSet) ? expectedSet : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                row.SelectedCircuitCount = selectedNames.Count;
                row.ExpectedCircuitCount = expectedNames.Count;
                row.SelectedCircuits.AddRange(selectedNames.OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase));
                row.MissingCircuits.AddRange(expectedNames.Except(selectedNames,
                    StringComparer.OrdinalIgnoreCase).OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase));
                report.Machines.Add(row);
            }
            report.FrameCount = (selected ?? Enumerable.Empty<XstsCircuitRecord>()).Count();
            return report;
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
    }

    public sealed class XstsCircuitRecord
    {
        public XstsCircuitRecord(string machineId, string circuitName)
        {
            MachineId = machineId ?? "";
            CircuitName = circuitName ?? "";
        }

        public string MachineId { get; }
        public string CircuitName { get; }
    }
}

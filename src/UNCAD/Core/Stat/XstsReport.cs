using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

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
        public int UnexpectedCircuitCount => UnexpectedCircuits.Count;
        public bool ExpectedDataAvailable { get; set; }
        public List<string> SelectedCircuits { get; } = new List<string>();
        public List<string> MissingCircuits { get; } = new List<string>();
        public List<string> UnexpectedCircuits { get; } = new List<string>();
        /// <summary>D-057：没画的回路张数。同名回路各算一条；同名组里有待确认
        /// 图框时该组在 <see cref="MissingCircuits"/> 里只有一条合并说明，
        /// 所以张数不等于文案条数。</summary>
        public int MissingCircuitCount { get; set; }
        /// <summary>
        /// D-057 待确认：机台内有同名回路，而图框没有可用的回路身份键
        /// （未记录上游编号），无法判定对应哪一条。不算核实，也不算缺——
        /// 对该图框执行 U1F/U1U 指认后即消除。
        /// </summary>
        public int UnresolvedCircuitCount { get; set; }
        public List<string> UnresolvedCircuits { get; } = new List<string>();
        /// <summary>同一回路画了不止一张图框（机台/回路/上游编号相同）。</summary>
        public int DuplicateFrameCount { get; set; }

        public string ExpectedCircuitText => ExpectedDataAvailable
            ? ExpectedCircuitCount.ToString()
            : "无法判断";

        public string MissingText => !ExpectedDataAvailable
            ? "无法判断"
            : MissingCircuitCount == 0
            ? "无"
            : string.Join("、", MissingCircuits);

        public string UnexpectedText => !ExpectedDataAvailable
            ? "无法判断"
            : UnexpectedCircuits.Count == 0
                ? "无"
                : string.Join("、", UnexpectedCircuits);

        public string UnresolvedText => UnresolvedCircuitCount == 0
            ? "无"
            : string.Join("、", UnresolvedCircuits);
    }

    public sealed class XstsFrameIssue
    {
        public int FrameNumber { get; set; }
        public string FrameHandle { get; set; } = "";
        public string FrameIdText => !string.IsNullOrWhiteSpace(FrameHandle)
            ? FrameHandle : FrameNumber > 0 ? FrameNumber.ToString() : "";
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        /// <summary>图框记录的上游编号（frameinfo_json 明文；老图框可能为空）。</summary>
        public string UpstreamId { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// One physical frame, including empty or unreadable frames. Items retain
    /// source order; Cells align with the report's material columns without CAD access.
    /// </summary>
    public sealed class XstsFrameDetail
    {
        public string FrameHandle { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        /// <summary>D-057：图框记录的上游编号——同名回路靠它区分。</summary>
        public string UpstreamId { get; set; } = "";
        public List<XstsFrameItem> Items { get; } = new List<XstsFrameItem>();
        public List<XstsFrameItem> Cells { get; } = new List<XstsFrameItem>();
    }

    /// <summary>One cleaned BOQ item; text and quantities are never merged or inferred.</summary>
    public sealed class XstsFrameItem
    {
        public string Number { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Code { get; set; } = "";
        public string Model
        {
            get
            {
                string model = BoqFeatureName.Extract(Description);
                return model.Length > 0 ? model : Description;
            }
        }
        public string DetailText => "序号：" + Number + "\n项目名称：" + Name
            + "\n项目特征：" + Description + "\n单位：" + Unit
            + "\n数量：" + Quantity + "\n编码：" + Code;
    }

    /// <summary>One material's adjacent model and quantity columns, shared by UI and Excel.</summary>
    public sealed class XstsMaterialColumn
    {
        internal XstsMaterialColumn(string name, string unit, int occurrence)
        {
            string label = (name.Length == 0 ? "未命名项目" : name)
                + (occurrence > 1 ? occurrence.ToString() : "");
            ModelHeader = label + "型号";
            QuantityHeader = label + (unit == "m" ? "米数" : "数量"
                + (unit.Length == 0 ? "" : "（" + unit + "）"));
        }

        public string ModelHeader { get; }
        public string QuantityHeader { get; }
    }

    /// <summary>Complete XSTS report, independent of AutoCAD and Excel I/O.</summary>
    public sealed class XstsReport
    {
        public List<XstsMachineSummary> Machines { get; } = new List<XstsMachineSummary>();
        public List<XstsFrameDetail> Frames { get; } = new List<XstsFrameDetail>();
        public List<XstsMaterialColumn> MaterialColumns { get; } = new List<XstsMaterialColumn>();
        public List<XstsFrameIssue> Issues { get; } = new List<XstsFrameIssue>();
        /// <summary>Physical selected frame count; machines are reported separately.</summary>
        public int FrameCount { get; set; }
        public XstsExpectedDataStatus ExpectedDataStatus { get; set; }
            = XstsExpectedDataStatus.Available;
        public string ExpectedDataDetail { get; set; } = "";
        public bool ExpectedDataAvailable
            => ExpectedDataStatus == XstsExpectedDataStatus.Available;
        /// <summary>已选回路张数（D-057：同名回路各算一条，含待确认的图框）。</summary>
        public int SelectedCircuitCount => Machines.Sum(item => item.SelectedCircuitCount);
        public int ExpectedCircuitCount => Machines.Sum(item => item.ExpectedCircuitCount);
        public int MissingCircuitCount => Machines.Sum(item => item.MissingCircuitCount);
        public int UnexpectedCircuitCount => Machines.Sum(item => item.UnexpectedCircuits.Count);
        /// <summary>D-057：同名回路里对应关系待确认的图框数。</summary>
        public int UnresolvedCircuitCount => Machines.Sum(item => item.UnresolvedCircuitCount);
        public int DuplicateFrameCount => Machines.Sum(item => item.DuplicateFrameCount);
        public int UnknownMachineCount => Machines.Count(item => !item.ExpectedDataAvailable);
        /// <summary>有同名回路待确认图框的机台数。</summary>
        public int MachinesWithUnresolvedCount => Machines.Count(item =>
            item.ExpectedDataAvailable && item.UnresolvedCircuitCount > 0);
        /// <summary>回路齐全（无缺、无多出、无待确认）的机台数——只统计有 Excel 基准的机台。</summary>
        public int CompleteMachineCount => Machines.Count(item =>
            item.ExpectedDataAvailable && item.MissingCircuitCount == 0
            && item.UnexpectedCircuits.Count == 0 && item.UnresolvedCircuitCount == 0);
        /// <summary>缺回路的机台数（哪怕同时有多出也算缺）。</summary>
        public int MachinesWithMissingCount => Machines.Count(item =>
            item.ExpectedDataAvailable && item.MissingCircuitCount > 0);
        /// <summary>只有多出回路（图纸有、Excel 基准没有）的机台数。</summary>
        public int MachinesWithExtraOnlyCount => Machines.Count(item =>
            item.ExpectedDataAvailable && item.MissingCircuitCount == 0
            && item.UnexpectedCircuits.Count > 0);
        /// <summary>机台状态分布一句话（徽章行/命令行摘要共用口径）。</summary>
        public string MachineStatusText
            => "完整 " + CompleteMachineCount + " 台 · 缺回路 "
                + MachinesWithMissingCount + " 台 · 多出 "
                + MachinesWithExtraOnlyCount + " 台 · 无基准 "
                + UnknownMachineCount + " 台"
                + (UnresolvedCircuitCount > 0
                    ? " · 同名回路待确认 " + MachinesWithUnresolvedCount + " 台" : "");
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
        // 性能回归探针：本次 Build 归组的机台行数。线性实现等于
        // selected+expected 行数；旧的“每机台对全表 Where 过滤”实现等于
        // 机台数×行数。测试用它断言复杂度，不断言脆弱的时间。
        [ThreadStatic] private static long _lastBuildRowVisits;

        internal static long LastBuildRowVisits => _lastBuildRowVisits;

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
            _lastBuildRowVisits = 0;
            var selectedRows = (selected ?? Enumerable.Empty<XstsCircuitRecord>()).ToList();
            var expectedRows = (expected ?? Enumerable.Empty<XstsCircuitRecord>()).ToList();
            var report = new XstsReport
            {
                FrameCount = selectedRows.Count,
                ExpectedDataStatus = expectedDataStatus,
                ExpectedDataDetail = expectedDataDetail ?? ""
            };
            var selectedGroups = Group(selectedRows);
            var expectedGroups = Group(expectedRows);
            // 机台元数据按机台分组一次。旧实现每台机台都对整张 selected/expected
            // 行表做一次 Where 过滤，机台数与行数同时增长时是 O(机台×行)：
            // 1000 机台 × 20 回路实测 3.7s。分组后每行只归属一次。
            Dictionary<string, List<XstsCircuitRecord>> expectedByMachine =
                GroupRowsByMachine(expectedRows);
            Dictionary<string, List<XstsCircuitRecord>> selectedByMachine =
                GroupRowsByMachine(selectedRows);
            // D-057：每个机台走统一配对内核（CircuitMatcher）——同名回路按身份键
            // （上游编号）一一配对，不再按回路名 Distinct 塌缩。反查表让重复/
            // 待确认的问题行找回自己的图框序号与句柄。
            var sources = new Dictionary<CircuitSelection, XstsCircuitRecord>();
            var matches = new Dictionary<string, CircuitMatchResult>(
                IdentityTextNormalizer.Comparer);
            // The report is selection-scoped: expected-only machines are deliberately
            // omitted. XSTS answers which circuits are missing from the machines the
            // user framed, not which circuits are missing from the whole workbook.
            foreach (string machineId in selectedGroups.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var row = new XstsMachineSummary(machineId);
                if (!expectedByMachine.TryGetValue(machineId,
                        out List<XstsCircuitRecord> metadataRows))
                {
                    selectedByMachine.TryGetValue(machineId,
                        out metadataRows);
                }
                metadataRows = metadataRows ?? new List<XstsCircuitRecord>();
                row.Batch = CollapseField(metadataRows.Select(item => item.Batch));
                row.Seq = CollapseField(metadataRows.Select(item => item.Seq));
                // A globally readable workbook still cannot provide a baseline for a
                // machine ID that is absent from it. Keep that row explicitly unknown
                // instead of presenting an empty expected set as "0 missing".
                row.ExpectedDataAvailable = report.ExpectedDataAvailable
                    && expectedGroups.ContainsKey(machineId);
                List<CircuitSelection> drawn = ToSelections(selectedByMachine,
                    machineId, sources);
                List<CircuitSelection> baseline = row.ExpectedDataAvailable
                    ? ToSelections(expectedByMachine, machineId, sources)
                    : new List<CircuitSelection>();
                CircuitMatchResult match = CircuitMatcher.Pair(machineId, baseline,
                    drawn, row.ExpectedDataAvailable);
                matches[machineId] = match;
                row.SelectedCircuitCount = match.DistinctDrawnCount;
                row.SelectedCircuits.AddRange(CircuitMatchText.Selected(match));
                row.DuplicateFrameCount = match.Duplicates.Count(
                    duplicate => duplicate.Certain);
                row.UnresolvedCircuits.AddRange(CircuitMatchText.Unresolved(match));
                if (row.ExpectedDataAvailable)
                {
                    row.ExpectedCircuitCount = match.Expected.Count;
                    row.MissingCircuits.AddRange(CircuitMatchText.Missing(match));
                    row.MissingCircuitCount = CircuitMatchText.MissingCount(match);
                    row.UnexpectedCircuits.AddRange(CircuitMatchText.Extra(match));
                    row.UnresolvedCircuitCount = match.Unresolved.Count;
                }
                else
                {
                    // 无基准：同键 = 确定重复；同名且都无键 = 只能确定“同名”。
                    row.UnresolvedCircuitCount = match.Duplicates.Count(
                        duplicate => !duplicate.Certain);
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
            AddFrameDetails(selectedRows, report, matches.Values, sources);
            return report;
        }

        /// <summary>把一个机台的报表行转成配对条目，并登记反查表。</summary>
        private static List<CircuitSelection> ToSelections(
            Dictionary<string, List<XstsCircuitRecord>> rows, string machineId,
            Dictionary<CircuitSelection, XstsCircuitRecord> sources)
        {
            var result = new List<CircuitSelection>();
            if (!rows.TryGetValue(machineId, out List<XstsCircuitRecord> records))
                return result;
            foreach (XstsCircuitRecord record in records)
            {
                if (string.IsNullOrWhiteSpace(record.CircuitName)) continue;
                var selection = new CircuitSelection
                {
                    MachineId = record.MachineId.Trim(),
                    CircuitName = record.CircuitName.Trim(),
                    UpstreamId = record.UpstreamId ?? "",
                    CircuitKey = record.CircuitKey ?? "",
                    Handle = record.FrameHandle ?? ""
                };
                sources[selection] = record;
                result.Add(selection);
            }
            return result;
        }

        private static void AddFrameDetails(IReadOnlyList<XstsCircuitRecord> selected,
            XstsReport report, IEnumerable<CircuitMatchResult> matches,
            IReadOnlyDictionary<CircuitSelection, XstsCircuitRecord> sources)
        {
            var numbers = new Dictionary<XstsCircuitRecord, int>();
            var columns = new Dictionary<Tuple<string, string, int>, int>();
            foreach (string name in new[] { "电缆", "桥架", "软管", "线管" })
                AddColumn(name, "m", 1);
            // Use declared catalog categories only for exact, unambiguous project names.
            var categories = ListItemReader.ReadEmbedded()
                .Where(item => !string.IsNullOrWhiteSpace(item.Category))
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .Where(group => group.Select(item => item.Category).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Category,
                    StringComparer.Ordinal);
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
                if (description.Length > 0)
                    report.Issues.Add(new XstsFrameIssue
                    {
                        FrameNumber = index + 1,
                        FrameHandle = item?.FrameHandle ?? "",
                        MachineId = machine,
                        CircuitName = circuit,
                        Description = description
                    });
                if (item != null) numbers[item] = index + 1;
                var frame = new XstsFrameDetail
                {
                    FrameHandle = item?.FrameHandle ?? "",
                    MachineId = machine,
                    CircuitName = circuit,
                    UpstreamId = (item?.UpstreamId ?? "").Trim()
                };
                // Frame identity, not its material count, determines the number of rows.
                report.Frames.Add(frame);
                var occurrences = new Dictionary<Tuple<string, string>, int>();
                foreach (List<string> sourceRow in item?.TableRows
                    ?? Enumerable.Empty<List<string>>())
                {
                    if (sourceRow == null || sourceRow.Count < 5) continue;
                    string[] cells = Enumerable.Range(0, 6).Select(column =>
                        TextParser.CleanMText(column < sourceRow.Count
                            ? sourceRow[column] : "").Trim()).ToArray();
                    if ((TableLayoutClassifier.IsHeaderLike(cells[0], cells[1], cells[5])
                            && (cells[3] == "单位" || cells[4] == "数量"))
                        || cells.Skip(1).All(string.IsNullOrWhiteSpace)) continue;
                    var detail = new XstsFrameItem
                    {
                        Number = cells[0],
                        Name = cells[1],
                        Description = cells[2],
                        Unit = cells[3],
                        Quantity = cells[4],
                        Code = cells[5]
                    };
                    frame.Items.Add(detail);
                    string name = categories.TryGetValue(detail.Name, out string category)
                        ? category : detail.Name;
                    string unit = detail.Unit.Equals("m", StringComparison.OrdinalIgnoreCase)
                        || detail.Unit == "米" ? "m" : detail.Unit;
                    var material = Tuple.Create(name, unit);
                    occurrences.TryGetValue(material, out int occurrence);
                    occurrences[material] = ++occurrence;
                    int column = AddColumn(name, unit, occurrence);
                    while (frame.Cells.Count <= column) frame.Cells.Add(null);
                    frame.Cells[column] = detail;
                }
            }
            foreach (XstsFrameDetail frame in report.Frames)
                while (frame.Cells.Count < report.MaterialColumns.Count) frame.Cells.Add(null);

            // D-057：重复图框/同名回路待确认来自按身份键的配对结果。旧口径按
            // “机台ID+回路名”判重，把同名不同上游的合法回路误报成重复，而
            // “同名双回路只画一条”又被 Distinct 塌缩成完整。问题行按图框
            // 先后并入，异常页里一图框一条、顺序与图框明细一致。
            var keyedIssues = new List<XstsFrameIssue>();
            foreach (CircuitMatchResult match in matches)
            {
                foreach (CircuitDuplicate duplicate in match.Duplicates)
                {
                    if (!sources.TryGetValue(duplicate.Drawn, out XstsCircuitRecord record))
                        continue;
                    string originalHandle = duplicate.Original != null
                        && sources.TryGetValue(duplicate.Original, out XstsCircuitRecord first)
                        ? (first.FrameHandle ?? "") : "";
                    keyedIssues.Add(new XstsFrameIssue
                    {
                        FrameNumber = numbers.TryGetValue(record, out int number)
                            ? number : 0,
                        FrameHandle = record.FrameHandle ?? "",
                        MachineId = record.MachineId.Trim(),
                        CircuitName = record.CircuitName.Trim(),
                        UpstreamId = (record.UpstreamId ?? "").Trim(),
                        Description = CircuitMatchText.DuplicateFrameNote(match,
                            duplicate, originalHandle)
                    });
                }
                foreach (CircuitSelection item in match.Unresolved)
                {
                    if (!sources.TryGetValue(item, out XstsCircuitRecord record))
                        continue;
                    keyedIssues.Add(new XstsFrameIssue
                    {
                        FrameNumber = numbers.TryGetValue(record, out int number)
                            ? number : 0,
                        FrameHandle = record.FrameHandle ?? "",
                        MachineId = record.MachineId.Trim(),
                        CircuitName = record.CircuitName.Trim(),
                        UpstreamId = (record.UpstreamId ?? "").Trim(),
                        Description = CircuitMatchText.UnresolvedFrameNote(match, item)
                    });
                }
            }
            if (keyedIssues.Count > 0)
            {
                int tail = report.Issues.Count;
                var merged = report.Issues
                    .Select((issue, position) => new { issue, position })
                    .Concat(keyedIssues.Select((issue, position) =>
                        new { issue, position = tail + position }))
                    .OrderBy(entry => entry.issue.FrameNumber == 0
                        ? int.MaxValue : entry.issue.FrameNumber)
                    .ThenBy(entry => entry.position)
                    .Select(entry => entry.issue)
                    .ToList();
                report.Issues.Clear();
                report.Issues.AddRange(merged);
            }

            int AddColumn(string name, string unit, int occurrence)
            {
                var key = Tuple.Create(name, unit, occurrence);
                if (!columns.TryGetValue(key, out int column))
                {
                    column = report.MaterialColumns.Count;
                    columns.Add(key, column);
                    report.MaterialColumns.Add(new XstsMaterialColumn(name, unit, occurrence));
                }
                return column;
            }
        }

        private static Dictionary<string, HashSet<string>> Group(
            IEnumerable<XstsCircuitRecord> source)
        {
            var result = new Dictionary<string, HashSet<string>>(
                IdentityTextNormalizer.Comparer);
            foreach (XstsCircuitRecord item in source ?? Enumerable.Empty<XstsCircuitRecord>())
            {
                string machine = (item?.MachineId ?? "").Trim();
                string circuit = (item?.CircuitName ?? "").Trim();
                if (machine.Length == 0 || circuit.Length == 0) continue;
                if (!result.TryGetValue(machine, out HashSet<string> names))
                {
                    names = new HashSet<string>(IdentityTextNormalizer.Comparer);
                    result[machine] = names;
                }
                names.Add(circuit);
            }
            return result;
        }

        /// <summary>
        /// 按机台把报表行分组一次；机台 ID 比较沿用 IdentityTextNormalizer
        /// 的“忽略大小写与空白”语义，保证与旧的 Equals 过滤一致。
        /// </summary>
        private static Dictionary<string, List<XstsCircuitRecord>> GroupRowsByMachine(
            IEnumerable<XstsCircuitRecord> source)
        {
            var result = new Dictionary<string, List<XstsCircuitRecord>>(
                IdentityTextNormalizer.Comparer);
            foreach (XstsCircuitRecord item in source ?? Enumerable.Empty<XstsCircuitRecord>())
            {
                string machine = (item?.MachineId ?? "").Trim();
                if (machine.Length == 0) continue;
                if (!result.TryGetValue(machine, out List<XstsCircuitRecord> rows))
                {
                    rows = new List<XstsCircuitRecord>();
                    result[machine] = rows;
                }
                rows.Add(item);
                _lastBuildRowVisits++;
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
        /// <summary>Selected-frame provenance; empty for workbook baseline rows.</summary>
        public string FrameHandle { get; set; } = "";
        /// <summary>
        /// D-057：上游编号。图框侧取自 frameinfo_json 明文，台账侧取自快照行
        /// 的 U_上游编号——同名回路靠它区分。
        /// </summary>
        public string UpstreamId { get; set; } = "";
        /// <summary>D-057：回路身份键（台账行必有；老图框可能为空串）。</summary>
        public string CircuitKey { get; set; } = "";
        public List<List<string>> TableRows { get; } = new List<List<string>>();
    }
}

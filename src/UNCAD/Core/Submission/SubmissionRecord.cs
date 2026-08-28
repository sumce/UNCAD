using System;
using System.Collections.Generic;

namespace UNCAD.Core.Submission
{
    public sealed class SubmissionRecord
    {
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string PanelType { get; set; } = "";
        // Preserved from the device block when the edited table uses a BOQ substitute.
        public string OriginalCable { get; set; } = "";
        public string Cable { get; set; } = "";
        public string CableMeters { get; set; } = "";
        public string Fr { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Diameter { get; set; } = "";
        public string FlexibleConduitMeters { get; set; } = "";
        public string BridgeInfo { get; set; } = "";
        public string BridgeMeters { get; set; } = "";
        public string ConduitInfo { get; set; } = "";
        public string ConduitMeters { get; set; } = "";
        public string DownstreamAxis { get; set; } = "";
        public string UpstreamAxis { get; set; } = "";
        public List<SubmissionMaterial> Materials { get; set; } = new List<SubmissionMaterial>();
        // 框选读取到的表格总行数（供 UNC_SUBMIT 诊断：区分“没框到表”与“表是空的”）。
        public int TableRowsRead { get; set; }
        // 框选到的文字/多行文字实体数（诊断：用户框到的可能是文字画的表，而不是 AutoCAD 表格）。
        public int TextEntityCount { get; set; }
    }

    public sealed class SubmissionMaterial
    {
        public string Number { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public sealed class SubmissionSourceData
    {
        public SubmissionSourceData()
        {
            Attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, List<string>> Attributes { get; }
        public List<string> DynamicValues { get; } = new List<string>();
        public List<string> TableValues { get; } = new List<string>();
        public int TextEntityCount { get; set; }
        public List<List<string>> TableRows { get; } = new List<List<string>>();

        public void AddTableRow(params string[] values)
        {
            var row = new List<string>();
            foreach (string value in values ?? new string[0])
            {
                string text = (value ?? "").Trim();
                row.Add(text);
                if (text.Length > 0) TableValues.Add(text);
            }
            TableRows.Add(row);
        }

        public void AddAttribute(string tag, string value)
        {
            string key = (tag ?? "").Trim();
            string text = (value ?? "").Trim();
            if (key.Length == 0 || text.Length == 0) return;
            if (!Attributes.TryGetValue(key, out List<string> values))
            {
                values = new List<string>();
                Attributes[key] = values;
            }
            values.Add(text);
        }
    }

    public sealed class SubmissionWriteResult
    {
        public bool ReplacedExisting { get; set; }
        public int RemovedDuplicates { get; set; }
        public string FilePath { get; set; }
        public string SubmittedAt { get; set; }
        public string UpdatedAt { get; set; }
    }

    /// <summary>Results from one lock/load/write cycle containing several frame records.</summary>
    public sealed class SubmissionBatchWriteResult
    {
        public string FilePath { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public List<SubmissionWriteResult> Records { get; } = new List<SubmissionWriteResult>();
        public int AddedCount { get; set; }
        public int ReplacedCount { get; set; }
        public int RemovedDuplicates { get; set; }
    }
}

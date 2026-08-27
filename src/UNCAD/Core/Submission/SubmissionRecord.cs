using System;
using System.Collections.Generic;

namespace UNCAD.Core.Submission
{
    public sealed class SubmissionRecord
    {
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string PanelType { get; set; } = "";
        public string Cable { get; set; } = "";
        public string Fr { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Diameter { get; set; } = "";
        public string DownstreamAxis { get; set; } = "";
        public string UpstreamAxis { get; set; } = "";
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
}

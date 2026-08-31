using System;
using System.Collections.Generic;

namespace UNCAD.Core.Fill
{
    /// <summary>Persisted metadata for one drawing frame's frameinfo_json block.</summary>
    public sealed class FrameInfoJsonRecord
    {
        public string SchemaVersion { get; set; } = "1";
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Region { get; set; } = "";
        public string OriginalCableModel { get; set; } = "";
        public string BoqCableModel { get; set; } = "";
        public string Fr { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Seq { get; set; } = "";
        public string HoseDiameter { get; set; } = "";
        public string Next { get; set; } = "";
        public string UpstreamAxis { get; set; } = "";
        public string DownstreamAxis { get; set; } = "";
        public string LastModifiedUtc { get; set; } = "";
        public List<FrameInfoJsonChange> Changes { get; set; }
            = new List<FrameInfoJsonChange>();
    }

    public sealed class FrameInfoJsonChange
    {
        public string TimestampUtc { get; set; } = "";
        public string Command { get; set; } = "";
        public string Field { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public string Note { get; set; } = "";
    }
}

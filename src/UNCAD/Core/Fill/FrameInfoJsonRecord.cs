using System;
using System.Collections.Generic;

namespace UNCAD.Core.Fill
{
    /// <summary>Persisted metadata for one drawing frame's frameinfo_json block.</summary>
    /// <remarks>
    /// Schema v2 (see <see cref="FrameInfoJsonCodec"/>): the persisted JSON is a nested
    /// document with identity/upstream/downstream/cable/drawing/meta/changes sections.
    /// This record stays flat because CAD-side consumers read the fields directly.
    /// v1 flat payloads are still accepted by <see cref="FrameInfoJsonCodec.Parse"/>.
    /// </remarks>
    public sealed class FrameInfoJsonRecord
    {
        public string SchemaVersion { get; set; } = FrameInfoJsonCodec.CurrentSchemaVersion;
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Region { get; set; } = "";
        public string OriginalCableModel { get; set; } = "";
        public string BoqCableModel { get; set; } = "";
        public string BoqBusPlugBoxCode { get; set; } = "";
        public string Fr { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Seq { get; set; } = "";
        public string HoseDiameter { get; set; } = "";
        public string Next { get; set; } = "";
        public string UpstreamAxis { get; set; } = "";
        public string DownstreamAxis { get; set; } = "";
        public string DeviceFloor { get; set; } = "";
        public string PanelFloor { get; set; } = "";
        public string FacilitySwitch { get; set; } = "";
        public string LastModifiedUtc { get; set; } = "";
        public string LastModifiedUser { get; set; } = "";

        // v2 cable reserve fields (all persisted as strings, empty when unknown).
        public string MeasuredCableM { get; set; } = "";
        public string PanelReserveMm { get; set; } = "";
        public string DeviceReserveMm { get; set; } = "";
        public string TotalCableM { get; set; } = "";

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

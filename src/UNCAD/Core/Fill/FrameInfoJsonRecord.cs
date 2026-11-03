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

        /// <summary>
        /// 写入时所属图框的句柄（meta.frameHandle）。老 JSON 没有该字段（空串），
        /// 读取方必须按“无绑定=不校验”的兼容语义处理。用于防止图框移动后
        /// 遗留的 frameinfo_json 块被几何归属错误地认领（幽灵机台，2026-11-02）。
        /// </summary>
        public string FrameHandle { get; set; } = "";

        /// <summary>
        /// 该记录是否属于给定机台（用于写入前的继承守卫）：
        /// 记录无机台号（老/空记录）或与目标机台一致才算自己的；
        /// 他机的遗留记录（复制图框带走的旧 JSON）不得被继承。
        /// </summary>
        public bool BelongsToMachine(string machineId)
        {
            string mine = (MachineId ?? "").Trim();
            if (mine.Length == 0) return true;
            return UNCAD.Core.Text.IdentityTextNormalizer.Comparer
                .Equals(mine, (machineId ?? "").Trim());
        }

        /// <summary>
        /// 该记录是否明显属于另一个图框：双方句柄都非空且不等才算外块；
        /// 任一侧为空（老 JSON 或调用方未提供）一律放行，保持旧图兼容。
        /// </summary>
        public bool IsForeignTo(string expectedFrameHandle)
        {
            string mine = (FrameHandle ?? "").Trim();
            string theirs = (expectedFrameHandle ?? "").Trim();
            if (mine.Length == 0 || theirs.Length == 0) return false;
            return !string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase);
        }

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

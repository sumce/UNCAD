using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// D-056：整机回路校验的输入条目——机台内一条回路的身位。
    /// <see cref="CircuitKey"/> 来自 <see cref="CircuitIdentityKey.Of(MachineRow)"/>
    /// 或 frameinfo_json；老图框没有键（空串），校验退回按名匹配。
    /// <see cref="UpstreamId"/> 仅用于同名回路的显示区分，不参与比较。
    /// </summary>
    public sealed class CircuitSelection
    {
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        public string CircuitKey { get; set; } = "";
        public string UpstreamId { get; set; } = "";
        /// <summary>来源图框句柄（仅用于提示里点名定位，不参与比较；台账行为空）。</summary>
        public string Handle { get; set; } = "";

        public static CircuitSelection Of(MachineRow row)
        {
            if (row == null) return new CircuitSelection();
            return new CircuitSelection
            {
                MachineId = (row.MachineId ?? "").Trim(),
                CircuitName = (row.CircuitName ?? "").Trim(),
                CircuitKey = CircuitIdentityKey.Of(row),
                UpstreamId = (row.Fr ?? "").Trim()
            };
        }
    }
}

namespace UNCAD.Core.Submission
{
    /// <summary>一份 PDF 的目标：**一个图框 = 一个回路 = 一份 PDF**（与按机台的 DWG 不同）。</summary>
    public sealed class PdfExportTarget
    {
        public string MachineId { get; set; } = "";
        public string CircuitName { get; set; } = "";
        /// <summary>
        /// D-057：回路的上游编号。机台内有同名回路（上游编号不同）时非空——文件名
        /// 带上它区分两条回路；名字唯一时为空，文件名不变。
        /// </summary>
        public string UpstreamId { get; set; } = "";
        /// <summary>图框块参照句柄：用于把出图任务映射回具体图框，也便于日志定位。</summary>
        public string FrameHandle { get; set; } = "";
        /// <summary>本份 PDF 的最终输出路径（命令层按输出布局 + 唯一文件名预先分配）。</summary>
        public string FilePath { get; set; } = "";
        /// <summary>本份出图失败时的原因（结果窗展示用；成功为空）。</summary>
        public string LastError { get; set; } = "";

        public string Label
            => (CircuitName ?? "").Length > 0 ? CircuitName : "(未识别回路)";
    }
}

using System.Collections.Generic;

namespace UNCAD.Core.Dwg
{
    /// <summary>一个机台的导出结果：DWG 文件路径 + 覆盖的回路（回路 = 一个图框的设备名）。</summary>
    public sealed class DwgExportMachine
    {
        public string MachineId { get; set; }
        public string FilePath { get; set; }
        public List<string> Circuits { get; set; } = new List<string>();
        /// <summary>该机台本次导出的图框数量（结果窗展示用）。</summary>
        public int Frames { get; set; }
    }
}

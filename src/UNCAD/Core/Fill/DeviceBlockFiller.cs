using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    public static class DeviceBlockFiller
    {
        public const string TagDeviceName = "DEVICENAME";

        /// <summary>设备动态块显示所选 Excel 回路名称。</summary>
        public static string BuildValue(MachineRow row)
        {
            return (row?.CircuitName ?? "").Trim();
        }
    }
}

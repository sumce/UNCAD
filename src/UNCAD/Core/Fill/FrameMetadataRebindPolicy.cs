using System;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// D-052（2026-11-10）搬场重绑判定：frameinfo_json 句柄校验失配时的身份判别。
    /// 「整机台搬场」（跨文件复制/移动到新位置，机台身份未变）与「复制出新机台」
    /// （复制图框去定义另一台设备）从数据上区分：搬场后本图中不再存在 JSON 记录的
    /// 原句柄，且记录身份与本框当前身份一致 → 判定搬场，收下全部记录（预留/替代/
    /// 历史），写入时盖成本框句柄；原句柄仍在本图（= 同文件复制，源框还在，复制
    /// 框是另一台设备）或身份不符（= 幽灵记录/复制后已改号）→ 维持 3.0.0.6/3.0.0.8
    /// 的外来拒绝语义。判定保持纯逻辑（句柄是否存在由调用方注入），Core 不依赖
    /// AutoCAD。
    /// </summary>
    public static class FrameMetadataRebindPolicy
    {
        /// <summary>
        /// 句柄失配的记录是否属于「整机台搬场」：记录身份与本框身份完全一致，
        /// 且记录的原句柄已不存在于本图（原框不在 = 不是同文件复制）。
        /// handleStillInDrawing 返回 false 表示句柄在本图中不存在（或已删除）。
        /// </summary>
        public static bool IsMovedFrame(FrameInfoJsonRecord record,
            string expectedMachineId, string expectedDeviceName,
            Func<string, bool> handleStillInDrawing)
        {
            if (record == null || handleStillInDrawing == null) return false;
            string machine = (record.MachineId ?? "").Trim();
            string device = (record.DeviceName ?? "").Trim();
            string expectedMachine = (expectedMachineId ?? "").Trim();
            string expectedDevice = (expectedDeviceName ?? "").Trim();
            // 任一侧身份不完整都不判搬场（保守：宁可拒绝交回 3.0.0.6 语义）。
            if (machine.Length == 0 || device.Length == 0
                || expectedMachine.Length == 0 || expectedDevice.Length == 0)
                return false;
            if (!IdentityTextNormalizer.Equals(machine, expectedMachine)
                || !IdentityTextNormalizer.Equals(device, expectedDevice))
                return false;
            // 记录没有句柄绑定就不判搬场（生产侧 IsForeignTo 对空句柄放行，
            // 这里做防御：空句柄无从证明“原框已不在”，保持拒绝语义）。
            string recordedHandle = (record.FrameHandle ?? "").Trim();
            if (recordedHandle.Length == 0) return false;
            // 原句柄仍在本图 = 源框还在，这是同文件复制出的新图框 → 不是搬场。
            return !handleStillInDrawing(recordedHandle);
        }
    }
}

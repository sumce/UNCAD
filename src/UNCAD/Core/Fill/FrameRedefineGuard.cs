using UNCAD.Core.Excel;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 3.0.0.17 机台身份守卫（现场报告：U1F 会把复制过来之前的预留米数算进
    /// 新机台）。图框携带的旧 frameinfo_json 记录与 CABLE_INFO 预留建议值属于
    /// 「图框原来的那台机」；U1F 允许改选机台（复制改号 / 换机台重定义），
    /// 选了别的机台时旧记录与建议值必须整体丢弃，不得流进新机台的清单、
    /// 电缆公式与 JSON。同机台场景不受影响：重跑 U1F（复用记录）、同机台
    /// 第二只框（保留建议值预填）、跨文件搬场（D-052-C 收下的记录身份一致）。
    /// 纯 Core 判定，无 CAD/IO；调用时机=机台选定之后、清单规划之前。
    /// </summary>
    public static class FrameRedefineGuard
    {
        /// <summary>按所选机台过滤旧记录与建议值：不匹配返回 null 并清空建议值；
        /// note 非空时由调用方写命令行说明丢弃了什么（用户看得见，不是静默丢弃）。</summary>
        public static FrameInfoJsonRecord Apply(ExistingFillIdentity frameIdentity,
            FrameInfoJsonRecord previous, MachineRow picked,
            ref double? deviceReserveHintMm, out string note)
        {
            note = "";
            picked = picked ?? new MachineRow();
            // 没有物理身份 = 全新图框：外来记录早已被句柄校验拒绝，无可丢弃。
            if (frameIdentity == null
                || string.IsNullOrWhiteSpace(frameIdentity.MachineId))
                return previous;

            bool frameOwnsPicked = IdentityTextNormalizer.Equals(
                frameIdentity.MachineId, picked.MachineId)
                && (string.IsNullOrWhiteSpace(frameIdentity.DeviceName)
                    || string.IsNullOrWhiteSpace(picked.CircuitName)
                    || IdentityTextNormalizer.Equals(frameIdentity.DeviceName,
                        picked.CircuitName));
            if (frameOwnsPicked)
            {
                // 物理身份就是所选机台：记录仍指向别的机台（幽灵残留）时同样丢弃，
                // 不能让 A 机的记录给 A 机的框写 B 机的历史。
                if (previous != null && !RecordMatches(previous, picked))
                {
                    note = "图框 JSON 记录属于机台 " + previous.MachineId
                        + "，与本次选择的 " + picked.MachineId + " 不符，"
                        + "已丢弃其设备内预留/替代型号/更新历史。";
                    return null;
                }
                return previous;
            }

            // 改选了别的机台：旧记录与源框 CABLE_INFO 建议值全部丢弃。
            deviceReserveHintMm = null;
            if (previous != null)
                note = "本图框原属机台 " + frameIdentity.MachineId
                    + "，已按 " + picked.MachineId + " " + picked.CircuitName
                    + " 重新定义：原机台的设备内预留/替代型号/更新历史与"
                    + " CABLE_INFO 建议值不继承，请按新机台重新确认。";
            else
                note = "本图框原属机台 " + frameIdentity.MachineId
                    + "，已按 " + picked.MachineId + " " + picked.CircuitName
                    + " 重新定义：源框 CABLE_INFO 建议值不继承，"
                    + "设备内预留请按新机台重新填写。";
            return null;
        }

        /// <summary>记录身份与所选机台一致（旧记录的 DeviceName 可为空=v1 遗留）。</summary>
        private static bool RecordMatches(FrameInfoJsonRecord record, MachineRow picked)
            => IdentityTextNormalizer.Equals(record.MachineId, picked.MachineId)
                && (string.IsNullOrWhiteSpace(record.DeviceName)
                    || IdentityTextNormalizer.Equals(record.DeviceName,
                        picked.CircuitName));
    }
}

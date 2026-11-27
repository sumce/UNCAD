using System;
using System.Collections.Generic;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// JSON 身份与图框物理标注的仲裁（幽灵机台修复第二轮，2026-11-02）。
    ///
    /// frameinfo_json 是"上次填表时的缓存"；图框设备块/机台标注是"这个框现在
    /// 是谁"的物理事实。图框被复制/挪用后 JSON 过期、物理标注已被用户改新——
    /// 两者分歧时信物理标注（JSON 会在下次 U1F/U1U 被刷新）。一致或一侧为空
    /// 时维持原优先级（JSON 优先），不打扰既有语义。
    ///
    /// 3.8.0.11（D-056 Amendment）：同一机台内复制图框时，机台号相同并不代表
    /// 是同一个回路——回路名或上游编号（物理标注取自上游块的 UPSTREAM_INFO）
    /// 不同就是另一条回路，必须丢掉源框的旧身份键，否则同名回路会被静默判成
    /// 台账第一条。
    /// </summary>
    public static class FrameIdentityArbiter
    {
        /// <summary>
        /// 仲裁最终身份。physicalWins=true 表示发生分歧并采纳了物理标注
        /// （divergenceNote 同时给出双方身份，供调用方提示用户）。
        /// </summary>
        public static ExistingFillIdentity Decide(
            ExistingFillIdentity jsonIdentity, ExistingFillIdentity physicalIdentity,
            out bool physicalWins, out string divergenceNote)
        {
            physicalWins = false;
            divergenceNote = "";
            string jsonMachine = (jsonIdentity?.MachineId ?? "").Trim();
            string physicalMachine = (physicalIdentity?.MachineId ?? "").Trim();

            if (jsonMachine.Length == 0 && physicalMachine.Length == 0) return null;
            if (jsonMachine.Length == 0) return physicalIdentity;
            if (physicalMachine.Length == 0) return jsonIdentity;
            if (IdentityTextNormalizer.Comparer.Equals(jsonMachine, physicalMachine))
            {
                string jsonDevice = (jsonIdentity.DeviceName ?? "").Trim();
                string physicalDevice = (physicalIdentity.DeviceName ?? "").Trim();
                string jsonUpstream = CircuitIdentityKey.UpstreamDisplay(
                    jsonIdentity.UpstreamId);
                string physicalUpstream = CircuitIdentityKey.UpstreamDisplay(
                    physicalIdentity.UpstreamId);
                bool deviceDiffers = jsonDevice.Length > 0 && physicalDevice.Length > 0
                    && !IdentityTextNormalizer.Equals(jsonDevice, physicalDevice);
                // 上游按 D-056 口径比较：去掉槽位/轴位标注，只比编号本身。
                bool upstreamDiffers = jsonUpstream.Length > 0
                    && physicalUpstream.Length > 0
                    && !string.Equals(
                        CircuitIdentityKey.UpstreamKey(jsonIdentity.UpstreamId),
                        CircuitIdentityKey.UpstreamKey(physicalIdentity.UpstreamId),
                        StringComparison.Ordinal);
                if (!deviceDiffers && !upstreamDiffers) return jsonIdentity;

                // 物理标注已改成另一条回路：源框的身份键不能再用于匹配。
                physicalWins = true;
                physicalIdentity.CircuitKey = "";
                divergenceNote = "身份分歧：frameinfo_json 记录 " + Describe(jsonIdentity)
                    + "，图框标注 " + Describe(physicalIdentity)
                    + "——已按图框标注处理；该图框的 JSON 已过期（多为复制图框遗留），"
                    + "重跑 U1F/U1U 会自动刷新。";
                return physicalIdentity;
            }

            physicalWins = true;
            physicalIdentity.CircuitKey = "";
            divergenceNote = "身份分歧：frameinfo_json 记录机台 " + jsonMachine
                + "，图框标注机台 " + physicalMachine
                + "——已按图框标注处理；该图框的 JSON 已过期（多为复制图框遗留），"
                + "重跑 U1F/U1U 会自动刷新。";
            return physicalIdentity;
        }

        private static string Describe(ExistingFillIdentity identity)
        {
            var parts = new List<string>();
            string machine = (identity?.MachineId ?? "").Trim();
            string device = (identity?.DeviceName ?? "").Trim();
            string upstream = (identity?.UpstreamId ?? "").Trim();
            if (machine.Length > 0) parts.Add("机台 " + machine);
            if (device.Length > 0) parts.Add("回路 " + device);
            if (upstream.Length > 0) parts.Add("上游 " + upstream);
            return parts.Count > 0 ? string.Join(" / ", parts) : "（空）";
        }
    }
}

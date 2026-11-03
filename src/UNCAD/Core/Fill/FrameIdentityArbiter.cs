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
    /// </summary>
    public static class FrameIdentityArbiter
    {
        /// <summary>
        /// 仲裁最终身份。physicalWins=true 表示发生分歧并采纳了物理标注
        /// （divergenceNote 同时给出双方机台号，供调用方提示用户）。
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
                return jsonIdentity;
            }

            physicalWins = true;
            divergenceNote = "身份分歧：frameinfo_json 记录机台 " + jsonMachine
                + "，图框标注机台 " + physicalMachine
                + "——已按图框标注处理；该图框的 JSON 已过期（多为复制图框遗留），"
                + "重跑 U1F/U1U 会自动刷新。";
            return physicalIdentity;
        }
    }
}

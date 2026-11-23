using System.Collections.Generic;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// D-056：一个待指认的同名回路案例（批量预检在读事务里只记录，
    /// 事务关闭后由协调器弹窗，不在事务中途阻塞）。
    /// </summary>
    internal sealed class CircuitAmbiguityCase
    {
        public FrameRegionGroup Region { get; set; }
        public ExistingFillIdentity Identity { get; set; }
        public IReadOnlyList<MachineRow> Candidates { get; set; }
    }

    /// <summary>
    /// D-056 同名回路指认弹窗的唯一入口：单框 U1U 与批量 U1U 共用，
    /// 避免两处窗口调用口径漂移。用户确认返回所选行，取消返回 null。
    /// </summary>
    internal static class CircuitAmbiguityPrompt
    {
        internal static MachineRow Resolve(ExistingFillIdentity identity,
            IReadOnlyList<MachineRow> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var window = new CircuitAmbiguityWindow(
                identity?.MachineId ?? "", identity?.DeviceName ?? "", candidates);
            ModalWindowHost.Show(window);
            return window.Confirmed ? window.SelectedRow : null;
        }

        /// <summary>
        /// 单框 U1U 的「匹配 + 必要时指认」一步入口（D-056）：键/唯一同名直接
        /// 命中；同名歧义弹窗指认；取消返回 null（error 已给中文提示）。
        /// </summary>
        internal static MachineRow MatchOrPrompt(ExistingFillIdentity identity,
            IReadOnlyList<MachineRow> candidates, out string error)
        {
            MachineRow match = ExistingFillIdentityResolver.MatchMachine(identity,
                candidates, out IReadOnlyList<MachineRow> ambiguous, out error);
            if (match != null || ambiguous.Count == 0) return match;
            match = Resolve(identity, ambiguous);
            if (match == null)
            {
                error = "已取消同名回路选择，本次不更新该图框。";
                return null;
            }
            error = "";
            return match;
        }
    }
}

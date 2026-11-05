using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段2「求和」：按图上实际标注统计电缆/桥架/线管，并在 U1U 模式下
    /// 报告三态测量状态（已测量/确认清空/未完整选择保留旧值）。
    /// 从 FillFeature 拆出：预览、规划与写入必须复用同一份求和结果，
    /// 状态文案口径也需要单点维护。
    /// </summary>
    internal static class FillStatisticsStage
    {
        /// <summary>按选集统计文字求和；updateMode 时同步打印测量三态。
        /// excludeTextIds：不参与求和的文字（2026-11-05：被 D-043 吸收的设备
        /// 内预留标记——同一笔毫米数不能既算实测长度又算预留）。</summary>
        internal static SummationOutput Execute(CadContext ctx, FillSelection selection,
            FillRuntimeOptions options, bool updateMode,
            IReadOnlyCollection<ObjectId> excludeTextIds = null)
        {
            SummationOutput summation = FillStatisticsModule.Execute(ctx,
                selection.TextIds, options.MmPerGrid, selection.StatisticsScopeComplete,
                excludeTextIds);
            if (updateMode) WriteMeasurementState(ctx, summation.Statistics);
            return summation;
        }

        private static void WriteMeasurementState(CadContext ctx, CableStatResult statistics)
        {
            string StateText(MeasurementState state)
            {
                switch (state)
                {
                    case MeasurementState.Measured: return "已测量";
                    case MeasurementState.ConfirmedEmpty: return "确认清空";
                    default: return "未完整选择，保留旧值";
                }
            }
            ctx.Write("\n[U1F/U1U] 电缆 " + StateText(statistics.CableState)
                + "；桥架 " + StateText(statistics.BridgeState)
                + "；线管 " + StateText(statistics.ConduitState) + "。");
        }
    }
}

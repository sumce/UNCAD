using System;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;

namespace UNCAD.Features.Fill
{
    /// <summary>软管（Ruanguan）长度三态：有效 / 确认不存在 / 未知。</summary>
    internal enum RuanguanLengthState
    {
        Valid,
        ConfirmedAbsent,
        InvalidOrUnknown
    }

    /// <summary>
    /// 阶段4/5 之间的「软管行同步」：按 Ruanguan 块长度把软管行加入或移出清单。
    /// 从 FillFeature 拆出：单框 U1F/U1U（审阅前后各一次）与批量 U1U 共用，
    /// 且"未知长度必须中止而不是误删"是现场安全规则，需要单点维护。
    /// </summary>
    internal static class FillRuanguanStage
    {
        internal static bool Apply(CadContext ctx, FillSelection selection,
            FillReviewData review)
            => Apply(ctx, selection, review, null,
                FillPlanningOptions.Default, false);

        internal static bool Apply(CadContext ctx, FillSelection selection,
            FillReviewData review, BoqCatalogIndex catalog, FillPlanningOptions options,
            bool updateMode = false, Transaction transaction = null)
        {
            if (review == null) return true;
            if (!review.AutoFill.FlexibleConduit) return true;
            options = options ?? FillPlanningOptions.Default;
            ObjectId[] blockIds = selection?.RuanguanBlockIds ?? Array.Empty<ObjectId>();
            string meters = blockIds.Length == 0 ? ""
                : transaction != null
                    ? FillSelectionCollector.ReadRuanguanLengthMeters(transaction, blockIds)
                    : FillSelectionCollector.ReadRuanguanLengthMeters(ctx, blockIds);
            RuanguanLengthState state = ClassifyRuanguanLength(blockIds.Length,
                selection?.StatisticsScopeComplete == true, updateMode, meters);
            if (state == RuanguanLengthState.InvalidOrUnknown)
            {
                string message = blockIds.Length > 0
                    ? "已找到 Ruanguan 块，但没有可识别的软管长度"
                    : "当前选择范围不是完整图框，无法确认 Ruanguan 块是否已删除";
                ctx.Write("\n[U1F/U1U] " + message
                    + "；为避免误删现有软管清单，本次操作已停止。");
                return false;
            }
            if (state == RuanguanLengthState.ConfirmedAbsent)
            {
                FillReviewItem existing = review.FlexibleConduitItem();
                if (existing != null) review.RemoveItem(existing);
                return true;
            }
            FillReviewItem flexible = review.FlexibleConduitItem();
            if (flexible == null && !string.IsNullOrWhiteSpace(review.Machine.Dia))
                flexible = review.SetFlexibleConduitDiameter(review.Machine.Dia,
                    catalog, options);
            if (flexible == null)
            {
                ctx.Write("\n[U1F/U1U] 已读取 Ruanguan 长度，但电缆没有可映射的软管直径，未加入软管清单。");
                return true;
            }
            if (!flexible.CatalogMatched)
            {
                flexible.Quantity = meters;
                ctx.Write("\n[U1F/U1U] 软管直径未匹配固定清单，已保留在清单中供选择替代规格。");
                return true;
            }
            flexible.Quantity = meters;
            ctx.Write("\n[U1F/U1U] Ruanguan 软管长度: " + meters + "M。");
            return true;
        }

        internal static RuanguanLengthState ClassifyRuanguanLength(int blockCount,
            bool completeFrameScope, bool updateMode, string meters)
        {
            if (!string.IsNullOrWhiteSpace(meters)) return RuanguanLengthState.Valid;
            if (blockCount > 0 || (updateMode && !completeFrameScope))
                return RuanguanLengthState.InvalidOrUnknown;
            return RuanguanLengthState.ConfirmedAbsent;
        }
    }
}

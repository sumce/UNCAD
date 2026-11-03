using System;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// U1F/U1U 电缆预留与长度覆盖的纯逻辑：从统计结果和 frameinfo_json 计算预留、
    /// 在清单确认窗口与统计结果之间同步预留/总长、应用用户修改的电缆米数。
    /// 只操作 Core 模型（无 AutoCAD、无 IO），从 FillFeature 拆出以便
    /// U1F 单框、U1U 批量（BatchFillUpdateCoordinator）和测试共用同一套口径。
    /// </summary>
    public static class CableReserveReview
    {
        /// <summary>
        /// 从图框现有 CABLE_INFO 文本里恢复设备内预留（2026-11-02 现场需求）：
        /// 形如「…+N（设备内）+…」，N 为任意数值（整数/小数）。U1U 在
        /// frameinfo_json 没有记录时默认采用文本里的这个值，不再询问用户。
        /// 只认「数值（设备内）」结构，不会误读「（预留）」等其他段。
        /// </summary>
        public static bool TryParseDeviceReserveMillimeters(string cableInfoText,
            out double millimeters)
        {
            millimeters = 0d;
            string text = cableInfoText ?? "";
            int at = text.IndexOf("设备内", StringComparison.Ordinal);
            while (at > 0)
            {
                // 只认精确的「数值（设备内）」结构：左括号紧贴在前、右括号紧贴
                // 在后（各允许空白）。“设备内预留说明”这类文字不会误配。
                int close = at + 3;
                while (close < text.Length && char.IsWhiteSpace(text[close])) close++;
                bool closed = close < text.Length
                    && (text[close] == '）' || text[close] == ')');
                int open = at - 1;
                while (open >= 0 && char.IsWhiteSpace(text[open])) open--;
                if (closed && open >= 0 && (text[open] == '（' || text[open] == '('))
                {
                    int end = open - 1;
                    while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
                    int start = end;
                    while (start >= 0 && (char.IsDigit(text[start])
                        || text[start] == '.' || text[start] == ','))
                    {
                        start--;
                    }
                    if (start < end)
                    {
                        string number = text.Substring(start + 1, end - start)
                            .Replace(",", "");
                        if (double.TryParse(number,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out millimeters)
                            && millimeters >= 0d && millimeters <= 100000d)
                        {
                            return true;
                        }
                    }
                }
                at = text.IndexOf("设备内", at + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>U1F/U1U 电缆预留：盘内/箱内按上游类型（I-Line 电盘 / 插座盘 / 母线插接箱，
        /// 默认 3400/1000/500，U1SET 可改），设备内预留从 frameinfo_json 读取
        /// （首次按 0 进清单确认窗口填写），业主统一预留默认 3000（U1SET 可改）。
        /// </summary>
        public static CableReserveInfo ResolveCableReserve(FrameInfoJsonRecord previous,
            MachineRow machine, CableStatResult statistics,
            double ownerReserveMillimeters = CableReservePolicy.DefaultOwnerReserveMillimeters,
            CableReserveSettings reserveSettings = null)
        {
            // 没有实测电缆长度时不要凭预留凭空生成电缆清单；等有测量值的那次再处理。
            if (statistics == null || statistics.CableSum <= 0d)
                return null;
            double device = 0d;
            if (previous != null && !string.IsNullOrWhiteSpace(previous.DeviceReserveMm))
            {
                double.TryParse(previous.DeviceReserveMm,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out device);
            }
            return CableReservePolicy.Apply(statistics, machine?.Next, device,
                ownerReserveMillimeters, reserveSettings);
        }

        /// <summary>清单确认窗口：把预留明细交给 GUI 显示/编辑，并让电缆长度先按明细同步。</summary>
        public static void InitializeReserveReview(FillReviewData review,
            CableReserveInfo reserve)
        {
            if (review == null || reserve == null) return;
            review.CableReserveEditable = true;
            review.MeasuredCableMeters = TextFormatter.FormatNum(reserve.MeasuredMeters);
            review.PanelReserveMm = TextFormatter.FormatNum(reserve.PanelReserveMillimeters);
            review.DeviceReserveMm = TextFormatter.FormatNum(reserve.DeviceReserveMillimeters);
            review.OwnerReserveMm = TextFormatter.FormatNum(reserve.OwnerReserveMillimeters);
            review.SyncCableReserveMeters();
        }

        /// <summary>
        /// 用户在 GUI 里改完设备内预留后，把最终值同步回统计结果与 JSON 预留信息。
        /// 电缆长度若被用户手工改过，后续 ApplyCableLengthOverride / SyncCableReserveTotal 会再校准。
        /// </summary>
        public static void ApplyReviewReserve(CableStatResult statistics,
            CableReserveInfo reserve, FillReviewData review)
        {
            if (statistics == null || reserve == null || review == null) return;
            double device = reserve.DeviceReserveMillimeters;
            string text = (review.DeviceReserveMm ?? "").Trim();
            if (text.Length > 0
                && double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                && parsed >= 0d && parsed <= 100000d)
                device = parsed;
            ApplyDeviceReserve(statistics, reserve, device);
            review.DeviceReserveMm = TextFormatter.FormatNum(device);
        }

        public static void ApplyDeviceReserve(CableStatResult statistics,
            CableReserveInfo reserve, double deviceReserveMillimeters)
        {
            if (statistics == null || reserve == null) return;
            double measured = statistics.MeasuredCableSum;
            reserve.DeviceReserveMillimeters = deviceReserveMillimeters;
            reserve.TotalMeters = measured + (reserve.PanelReserveMillimeters
                + deviceReserveMillimeters + reserve.OwnerReserveMillimeters) / 1000d;
            statistics.DeviceReserveMillimeters = deviceReserveMillimeters;
            statistics.OwnerReserveMillimeters = reserve.OwnerReserveMillimeters;
            statistics.CableSum = reserve.TotalMeters;
        }

        /// <summary>
        /// 应用用户在审阅窗中修改的电缆米数。输入为空或无法解析时**保留图上
        /// 实测值**并返回 false——此前无效输入会被静默清零并把图框电缆字段
        /// 抹空，只有日志痕迹。输入显式为 0 才视为"确认清空"。
        /// </summary>
        public static bool ApplyCableLengthOverride(CableStatResult statistics, string value)
        {
            string text = (value ?? "").Trim();
            double meters;
            if (text.Length == 0
                || !(double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out meters)
                    || double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.CurrentCulture, out meters))
                || meters < 0d)
                return false;
            // 预留联动不是长度覆盖：在审阅窗改设备内预留时，窗体会把
            // 「实测+四项预留」的新总长实时回写进电缆米数框，数值与
            // ApplyDeviceReserve 刚写入 CableSum 的总长一致。这种输入若照旧
            // 走覆盖分支，CableFormatted 会被清空，CABLE_INFO 里 1000+700 的
            // 实测明细塌缩成一个预先加总的数字。总长仍等于预留联动结果的
            // 输入直接放行，明细与统计原样保留。
            if (statistics.CableReserveApplied
                && string.Equals(text, TextFormatter.FormatNum(statistics.CableSum),
                    StringComparison.Ordinal))
                return true;
            statistics.CableFormatted.Clear();
            statistics.CableSum = meters;
            if (meters > 0d)
            {
                if (statistics.CableReserveApplied)
                {
                    // 真正手工覆盖总长时，实测段必须同步扣掉预留，否则
                    // CABLE_INFO 左侧仍写旧实测，与右侧总长和 frameinfo_json
                    // 的 cable.measuredM 互相矛盾。
                    statistics.MeasuredCableSum = Math.Max(0d, meters
                        - (statistics.PanelReserveMillimeters
                            + statistics.DeviceReserveMillimeters
                            + statistics.OwnerReserveMillimeters) / 1000d);
                    statistics.CableFormatted.Add(
                        TextFormatter.FormatNum(statistics.MeasuredCableSum));
                }
                else
                {
                    statistics.CableFormatted.Add(TextFormatter.FormatNum(statistics.CableSum));
                }
                statistics.CableState = MeasurementState.Measured;
            }
            else
            {
                statistics.CableState = MeasurementState.ConfirmedEmpty;
            }
            return true;
        }

        public static void SyncCableReserveTotal(CableReserveInfo reserve, string cableMeters)
        {
            if (reserve == null) return;
            string text = (cableMeters ?? "").Trim();
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double total)
                || total < 0d) return;
            reserve.TotalMeters = total;
            reserve.MeasuredMeters = Math.Max(0d, total
                - (reserve.PanelReserveMillimeters + reserve.DeviceReserveMillimeters
                    + reserve.OwnerReserveMillimeters) / 1000d);
        }
    }
}

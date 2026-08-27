using System.Text.RegularExpressions;

namespace UNCAD.Core.Fill
{
    /// <summary>清单表填充模板（与图纸手工示例逐字一致）。</summary>
    public static class FillTemplates
    {
        // \P = 表格单元格内换行（MTEXT 换行码）
        public const string CableDesc =
            "1.名称:{0}mm²单芯电缆\\P2.配线形式:穿管或桥架敷设\\P3.说明:包含电缆接头；支吊架；测试等一切主辅材料";

        public const string ConduitDesc =
            "1.名称:{0}mm(1-1/2\")包塑金属软管(波纹管)附镀锌接头\\P2.材质:镀锌金属软管和PVC包覆";

        private static readonly Regex BreakerRegex = new Regex(@"(\d+P\d+A)", RegexOptions.Compiled);

        /// <summary>详情列如 "N208 3P4W 3P50A" → "3P50A配电"；无匹配返回 fallback。</summary>
        public static string BreakerName(string detail, string fallback)
        {
            var m = BreakerRegex.Match(detail ?? "");
            return m.Success ? m.Groups[1].Value + "配电" : (fallback ?? "");
        }
    }
}

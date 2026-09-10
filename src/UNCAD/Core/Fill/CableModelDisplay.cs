namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 图框 CABLE_INFO 中显示用的电缆型号命名。
    ///
    /// 工作簿/设备块只提供芯数规格（如 3*35+1*16、3*2.5），不带工程前缀；
    /// 图框需要显示带前缀的完整型号：含 "+" 的多芯组合为 ZB-YJV-，
    /// 其余为 ZB-YJVR-。设备接地专用接地线（1*16）保持原样不补前缀。
    ///
    /// 仅用于图框属性显示。BOQ 清单编码与表格项目特征不使用本规则；
    /// 回读图框时 BoqCatalogIndex.NormalizeCable 会剥掉前缀再匹配，
    /// 因此补前缀不会影响固定清单匹配。
    /// </summary>
    public static class CableModelDisplay
    {
        public const string PowerPrefix = "ZB-YJV-";
        public const string FlexiblePrefix = "ZB-YJVR-";

        /// <summary>
        /// 返回带工程前缀的显示型号。对已带前缀的输入幂等：先剥掉已有前缀再按规则
        /// 补上，因此重复执行 U1F/U1U 不会累积成 ZB-YJV-ZB-YJV-…。
        /// </summary>
        public static string WithTypePrefix(string model)
        {
            string text = (model ?? "").Trim();
            if (text.Length == 0) return "";
            // 接地线不补前缀：原样返回，不做任何改写。
            if (TableFillPlanner.IsGroundingCableModel(text)) return text;
            string core = StripTypePrefix(text);
            if (core.Length == 0) return text;
            return (core.Contains("+") ? PowerPrefix : FlexiblePrefix) + core;
        }

        /// <summary>
        /// 剥掉最多两段 "-" 分隔的类型前缀（ZB-YJV-3*35+1*16 → 3*35+1*16）。
        /// 前缀段数与 BoqCatalogIndex.NormalizeCable 的约定一致，但这里保留其余
        /// 文本原样，不做规格规范化，以免改写图框上的显示写法。
        /// </summary>
        public static string StripTypePrefix(string model)
        {
            string value = (model ?? "").Trim();
            for (int segment = 0; segment < 2; segment++)
            {
                int separator = value.IndexOf('-');
                if (separator < 0) break;
                value = value.Substring(separator + 1).Trim();
            }
            return value;
        }
    }
}

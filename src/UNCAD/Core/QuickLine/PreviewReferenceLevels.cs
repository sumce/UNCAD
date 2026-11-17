using System;
using System.Collections.Generic;
using System.Globalization;

namespace UNCAD.Core.QuickLine
{
    /// <summary>一条 3D 预览参照层（水平参照线 + 标签，如「2F地面 7800」）。</summary>
    public sealed class PreviewReferenceLevel
    {
        public PreviewReferenceLevel(string label, double zMillimetres)
        {
            Label = label ?? string.Empty;
            ZMillimetres = zMillimetres;
        }

        /// <summary>标签原文（如「2F工字钢顶面」）。</summary>
        public string Label { get; }
        /// <summary>相对场景地面的高度（mm；场景地面=最低线段端点 Z=0）。</summary>
        public double ZMillimetres { get; }
    }

    /// <summary>
    /// 3.3.6（D-054 Amendment）：U1U 3D 预览的**参照层**清单——用户提供的建筑
    /// 标高链与设备高度（「1F地面——1F工字钢顶面：2850mm … 第三层母线高度：
    /// 4900mm」），在场景里画成水平参照线+标签，供电缆路由的爬升高度对照。
    /// 楼层链按累计标高换算（2F工字钢顶面=7800+3200=11000），设备高度按原值；
    /// 全部相对场景地面。清单是 U1SET 配置（UNC_PREVIEW_REF_LEVELS）——
    /// 不同项目标高不同，页面不写死；格式「标签=毫米」，分号/换行分隔，
    /// 非法条目跳过不炸（解析纯函数，单测锁死）。
    /// </summary>
    public static class PreviewReferenceLevels
    {
        /// <summary>默认清单（用户 3.3.6 提供的数值；楼层链已累计）。</summary>
        public const string DefaultList =
            "1F工字钢顶面=2850"
            + ";2F地面=7800"
            + ";2F工字钢顶面=11000"
            + ";2F葡萄架顶面=13300"
            + ";3F地面=15000"
            + ";3F高架地板面=15600"
            + ";I-L盘顶面=2200"
            + ";插座盘顶面=1000"
            + ";第一层母线=3660"
            + ";第二层母线=4220"
            + ";第三层母线=4900"
            + ";插座盘安装=1000";

        /// <summary>
        /// 解析「标签=毫米」清单（分号/换行分隔）。条目缺 =、标签空、数值
        /// 非有限正数 → 跳过该条；整串空 → 空清单（页面不画参照层）。
        /// </summary>
        public static IReadOnlyList<PreviewReferenceLevel> Parse(string text)
        {
            var result = new List<PreviewReferenceLevel>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            string[] entries = text.Split(new[] { ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                string item = entry.Trim();
                if (item.Length == 0) continue;
                int split = item.LastIndexOf('=');
                if (split <= 0 || split == item.Length - 1) continue;
                string label = item.Substring(0, split).Trim();
                string value = item.Substring(split + 1).Trim();
                if (label.Length == 0
                    || !double.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double millimetres)
                    || double.IsNaN(millimetres) || double.IsInfinity(millimetres)
                    || millimetres <= 0.0 || millimetres > 10_000_000.0)
                    continue;
                result.Add(new PreviewReferenceLevel(label, millimetres));
            }
            return result;
        }
    }
}

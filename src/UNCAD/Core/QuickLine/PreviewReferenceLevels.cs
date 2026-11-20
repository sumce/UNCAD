using System;
using System.Collections.Generic;
using System.Globalization;

namespace UNCAD.Core.QuickLine
{
    /// <summary>一条 3D 预览参照物（3.3.7 起带类型与锚定）。</summary>
    public sealed class PreviewReferenceLevel
    {
        public PreviewReferenceLevel(string label, double zMillimetres,
            string kind = null, string anchorLabel = null)
        {
            Label = label ?? string.Empty;
            ZMillimetres = zMillimetres;
            Kind = NormalizeKind(kind);
            AnchorLabel = (anchorLabel ?? "").Trim();
        }

        /// <summary>标签原文（如「2F工字钢顶面」）。</summary>
        public string Label { get; }
        /// <summary>
        /// 高度（mm）。无锚定=清单绝对标高；有锚定=相对锚定条目（如
        /// 母线三层锚定 2F地面：7800+3660）——设备/母线高度本来就是本层
        /// 地面起算的（用户口径「第一/2/3层母线放到二层」）。
        /// </summary>
        public double ZMillimetres { get; }
        /// <summary>
        /// 参照物类型（规范 token，页面按它画几何）：
        /// floor=普通楼板 epoxy=环氧地坪 waffle=华夫孔洞板（圆孔） steel=工字钢梁阵
        /// grating=格栅/葡萄架 raised=高架地板 device=设备盒 bus=母线（半透明
        /// 玻璃层） frame=水平线框（缺省，3.3.6 行为）。
        /// </summary>
        public string Kind { get; }
        /// <summary>
        /// 锚定条目标签（第四段，可选）：ZMillimetres 相对该条目（按标签包含
        /// 匹配，自引用排除）。空=绝对标高。
        /// </summary>
        public string AnchorLabel { get; }

        /// <summary>类型归一化：中英关键词→规范 token；认不出一律 frame（线框兜底）。</summary>
        internal static string NormalizeKind(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return "frame";
            switch (text.ToLowerInvariant())
            {
                case "floor": return "floor";
                case "epoxy": return "epoxy";
                case "waffle": return "waffle";
                case "steel": return "steel";
                case "grating": return "grating";
                case "raised": return "raised";
                case "device": return "device";
                case "bus": return "bus";
                case "frame": return "frame";
            }
            if (Contains(text, "母线")) return "bus";
            if (Contains(text, "工字钢") || Contains(text, "钢梁")
                || Contains(text, "型钢") || Contains(text, "钢架")) return "steel";
            if (Contains(text, "葡萄架") || Contains(text, "格栅")
                || Contains(text, "栅格") || Contains(text, "网格")) return "grating";
            if (Contains(text, "高架")) return "raised";
            if (Contains(text, "华夫")) return "waffle";
            if (Contains(text, "环氧") || Contains(text, "树脂")) return "epoxy";
            if (Contains(text, "地面") || Contains(text, "楼板")
                || Contains(text, "地板") || Contains(text, "板面")) return "floor";
            if (Contains(text, "设备") || Contains(text, "盘")
                || Contains(text, "箱") || Contains(text, "柜")) return "device";
            return "frame";
        }

        private static bool Contains(string text, string token)
            => text.IndexOf(token, StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// 3.3.6（D-054 Amendment）：U1U 3D 预览的**参照层**清单——用户提供的建筑
    /// 标高链与母线高度（「1F地面——1F工字钢顶面：2850mm … 第三层母线高度：
    /// 4900mm」）。楼层链按累计标高换算（2F工字钢顶面=7800+3200=11000）；
    /// 全部相对场景地面。清单是 U1SET 配置（UNC_PREVIEW_REF_LEVELS）
    /// ——不同项目标高不同，页面不写死；非法条目跳过不炸（解析纯函数）。
    /// 3.3.7（D-054 Amendment）：参照物从「水平线框」升级为**简化建筑模型**——
    /// 条目可选第三段类型「标签=毫米=类型」（地面/工字钢/葡萄架/高架地板/
    /// 设备/母线；中英关键词归一化，认不出/缺省=线框 frame 兼容 3.3.6 旧
    /// 清单），页面按类型画实体几何（楼板/型钢梁阵/格栅/薄板/设备盒/母线塔）。
    /// 平面范围=场景包络；设备/母线无平面数据，沿场景边示意布置（预览是
    /// 展示件，高度准确、位置示意）。
    /// </summary>
    public static class PreviewReferenceLevels
    {
        /// <summary>
        /// 默认清单（用户提供的数值；楼层链已累计）。3.3.7 三轮用户口径：
        /// 本单机台在 **2F**（「上游是有楼层的！如果是2F 那上游就在2楼的」）
        /// ——`本层=2F地面` 指令让页面把整栋楼按 2F 重定位（2F地面=场景 0，
        /// 3F=7200、1F=-7800，均相对本层）；母线高度本来就是本层地面
        /// 起算（「第一/2/3层母线放到二层」）——用第四段锚定到 2F地面，
        /// 锚定解析后绝对标高=7800+3660/4220/4900。钢梁/葡萄架默认隐藏
        /// （页面「结构」开关）；3F=华夫圆孔板、2F=环氧地坪（二轮口径）。
        /// </summary>
        public const string DefaultList =
            "本层=2F地面"
            + ";1F工字钢顶面=2850=工字钢"
            + ";2F地面=7800=环氧地坪"
            + ";2F工字钢顶面=11000=工字钢"
            + ";2F葡萄架顶面=13300=葡萄架"
            + ";3F地面=15000=华夫板"
            + ";3F高架地板面=15600=高架地板"
            + ";第一层母线=3660=母线=2F地面"
            + ";第二层母线=4220=母线=2F地面"
            + ";第三层母线=4900=母线=2F地面";

        /// <summary>
        /// 解析「标签=毫米[=类型[=锚定]]」清单（分号/换行分隔）。条目缺 =、
        /// 标签空、数值非有限正数 → 跳过；类型空/认不出 → frame（线框兼容）；
        /// 段数不是 2/3/4 → 跳过；「本层=标签」指令行不进清单（单独
        /// TryParseBaseFloor 读）；整串空 → 空清单（页面不画参照物）。
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
                string[] parts = item.Split('=');
                if (parts.Length < 2 || parts.Length > 4) continue;
                string label = parts[0].Trim();
                string value = parts[1].Trim();
                if (label.Length == 0) continue;
                if (string.Equals(label, BaseFloorDirective,
                        StringComparison.Ordinal))
                    continue;
                if (!double.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double millimetres)
                    || double.IsNaN(millimetres) || double.IsInfinity(millimetres)
                    || millimetres <= 0.0 || millimetres > 10_000_000.0)
                    continue;
                string kind = parts.Length >= 3 ? parts[2] : null;
                string anchor = parts.Length == 4 ? parts[3] : null;
                result.Add(new PreviewReferenceLevel(label, millimetres,
                    kind, anchor));
            }
            return result;
        }

        /// <summary>「本层」指令行的标签原文（值=基准楼层条目标签）。</summary>
        public const string BaseFloorDirective = "本层";

        /// <summary>
        /// 读「本层=标签」指令（机台所在楼层，如「本层=2F地面」）。没有该行
        /// 返回 false——页面按 1F 绝对标高渲染（3.3.6 口径），有则整栋楼按
        /// 该楼层重定位（该层地面=场景 0，上游就在本层——用户口径）。
        /// </summary>
        public static bool TryParseBaseFloor(string text, out string baseFloorLabel)
        {
            baseFloorLabel = "";
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] entries = text.Split(new[] { ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                string item = entry.Trim();
                int split = item.IndexOf('=');
                if (split <= 0) continue;
                string label = item.Substring(0, split).Trim();
                if (!string.Equals(label, BaseFloorDirective,
                        StringComparison.Ordinal)) continue;
                string value = item.Substring(split + 1).Trim();
                if (value.Length == 0) return false;
                baseFloorLabel = value;
                return true;
            }
            return false;
        }
    }
}

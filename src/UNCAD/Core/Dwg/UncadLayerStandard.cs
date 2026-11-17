using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// 3.3.0（D-054）：法定图层规范（用户拍板，一字不差全大写、无空格）——
    /// U_CABLE=电缆路由（U1L 线段+毫米标，色号 7）、U_TRAY=桥架（U1Q 输出，
    /// 色号 1 红）、U_CONDUIT=线管（U1C 输出，色号 4 青）。
    /// 3.3.3（D-054 Amendment）：U_HOSE=软管（U1B 输出，色号 3 绿）入法定家族。
    /// 纯 Core：名字、色号与种类判定的唯一来源，Cad 层图层矫正与写入路径都消费它。
    /// </summary>
    public static class UncadLayerStandard
    {
        public const string CableLayerName = "U_CABLE";
        public const string TrayLayerName = "U_TRAY";
        public const string ConduitLayerName = "U_CONDUIT";
        public const string HoseLayerName = "U_HOSE";
        public const short CableColorIndex = 7;
        public const short TrayColorIndex = 1;
        public const short ConduitColorIndex = 4;
        public const short HoseColorIndex = 3;

        /// <summary>U1Q/U1C/U1B 输出元数据里的 KIND 值（D-018/D-050 起恒写入；
        /// U1B 自 3.3.3 起——见 ParallelAnnotationMetadata 消费方）。</summary>
        public const string TrayAnnotationKind = "U1Q";
        public const string ConduitAnnotationKind = "U1C";
        public const string HoseAnnotationKind = "U1B";

        /// <summary>
        /// KIND → 法定图层；「U1Q」→U_TRAY、「U1C」→U_CONDUIT、「U1B」→U_HOSE。
        /// 其它值（未来种类/未知）返回 null = 无法判定——不移动、点名报告，不猜测。
        /// </summary>
        public static string TryResolveAnnotationLayer(string kind)
        {
            if (string.Equals(kind, TrayAnnotationKind,
                    StringComparison.OrdinalIgnoreCase))
                return TrayLayerName;
            if (string.Equals(kind, ConduitAnnotationKind,
                    StringComparison.OrdinalIgnoreCase))
                return ConduitLayerName;
            if (string.Equals(kind, HoseAnnotationKind,
                    StringComparison.OrdinalIgnoreCase))
                return HoseLayerName;
            return null;
        }

        /// <summary>法定图层 → 色号（建层用）；非法定名字抛 ArgumentException。</summary>
        public static short ColorIndexOf(string layerName)
        {
            if (string.Equals(layerName, CableLayerName,
                    StringComparison.OrdinalIgnoreCase)) return CableColorIndex;
            if (string.Equals(layerName, TrayLayerName,
                    StringComparison.OrdinalIgnoreCase)) return TrayColorIndex;
            if (string.Equals(layerName, ConduitLayerName,
                    StringComparison.OrdinalIgnoreCase)) return ConduitColorIndex;
            if (string.Equals(layerName, HoseLayerName,
                    StringComparison.OrdinalIgnoreCase)) return HoseColorIndex;
            throw new ArgumentException(
                "Not a governed UNCAD layer: " + layerName, nameof(layerName));
        }

        /// <summary>全部法定图层（显示顺序：电缆/桥架/线管/软管）。</summary>
        public static IReadOnlyList<string> GovernedLayerNames
            => new[] { CableLayerName, TrayLayerName, ConduitLayerName, HoseLayerName };
    }

    /// <summary>3.3.0（D-054）：一次图层矫正的计数（可独立单测的纯数据）。
    /// 3.3.3：软管列（U1B 输出 → U_HOSE）。</summary>
    public sealed class UncadLayerCorrectionCounters
    {
        public int CableEntities { get; set; }
        public int TrayEntities { get; set; }
        public int ConduitEntities { get; set; }
        public int HoseEntities { get; set; }
        /// <summary>带元数据但 KIND 不是 U1Q/U1C/U1B——不移动、点名「无法判定」。</summary>
        public int UndeterminableEntities { get; set; }
        public List<string> UndeterminableHandles { get; } = new List<string>();

        public int TotalMoves
            => CableEntities + TrayEntities + ConduitEntities + HoseEntities;

        public bool IsEmpty => TotalMoves == 0 && UndeterminableEntities == 0;

        /// <summary>确认窗/预检报告文案：「图层矫正：N 个实体移正（电缆 x/桥架 y/线管 z/软管 h/无法判定 w）」。</summary>
        public string Describe()
        {
            var text = new StringBuilder("图层矫正：")
                .Append(TotalMoves).Append(" 个实体移正（电缆 ")
                .Append(CableEntities).Append("/桥架 ").Append(TrayEntities)
                .Append("/线管 ").Append(ConduitEntities)
                .Append("/软管 ").Append(HoseEntities)
                .Append("/无法判定 ").Append(UndeterminableEntities).Append("）");
            if (UndeterminableHandles.Count > 0)
                text.Append("；无法判定：").Append(string.Join("、",
                    UndeterminableHandles.Take(6)))
                    .Append(UndeterminableHandles.Count > 6
                        ? " 等" : string.Empty);
            return text.ToString();
        }
    }
}

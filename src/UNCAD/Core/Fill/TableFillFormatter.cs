using UNCAD.Core.Stat;
using UNCAD.Core.Text;

namespace UNCAD.Core.Fill
{
    public static class TableFillFormatter
    {
        public const double DefaultFlexibleConduitMm = 2000.0;
        public const double DefaultTextHeight = 500.0;
        public const double GeneratedRowHeight = 5847.8848;
        /// <summary>清单数量列只写电缆总米数，不带单位。</summary>
        public static string CableQuantity(CableStatResult stat)
        {
            return stat != null && stat.CableSum > 0
                ? TextFormatter.FormatNum(stat.CableSum)
                : "";
        }

        /// <summary>包塑金属软管默认按 2000mm 计量，清单数量单位为米。</summary>
        public static string FlexibleConduitQuantity()
        {
            return TextFormatter.FormatNum(DefaultFlexibleConduitMm / 1000.0);
        }
    }
}

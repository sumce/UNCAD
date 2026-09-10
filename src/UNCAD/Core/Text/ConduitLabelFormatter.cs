namespace UNCAD.Core.Text
{
    /// <summary>生成固定两米占位长度的线管标注，不接受图上实测距离。</summary>
    public static class ConduitLabelFormatter
    {
        public const double DefaultLengthMm = 2000.0;

        /// <summary>
        /// 标注的长度文字。线管文字用于后续人工修改，业务约定始终写 2000mm 占位，
        /// 禁止带入图上实测距离；两行标注的第二行也用它。
        /// </summary>
        public static string LengthText => TextFormatter.FormatNum(DefaultLengthMm) + "mm";

        public static string Build(string diameter)
            => "⌀" + ConduitDiameter.NormalizeOrDefault(diameter, "20") + "线管 "
                + LengthText;
    }
}

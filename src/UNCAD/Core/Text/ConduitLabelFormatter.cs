namespace UNCAD.Core.Text
{
    /// <summary>生成固定两米占位长度的线管标注，不接受图上实测距离。</summary>
    public static class ConduitLabelFormatter
    {
        public const double DefaultLengthMm = 2000.0;

        public static string Build(string diameter)
        {
            // 线管文字用于后续人工修改，业务约定始终写 2000mm 占位，禁止带入图上实测距离。
            return "⌀" + ConduitDiameter.NormalizeOrDefault(diameter, "20") + "线管 "
                + TextFormatter.FormatNum(DefaultLengthMm) + "mm";
        }
    }
}

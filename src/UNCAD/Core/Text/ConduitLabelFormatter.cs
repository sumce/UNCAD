namespace UNCAD.Core.Text
{
    public static class ConduitLabelFormatter
    {
        public const double DefaultLengthMm = 2000.0;

        public static string Build(string diameter)
        {
            return "⌀" + (diameter ?? "20") + "线管 "
                + TextFormatter.FormatNum(DefaultLengthMm) + "mm";
        }
    }
}

namespace UNCAD.Core.Text
{
    public static class ConduitLabelFormatter
    {
        public const double DefaultLengthMm = 2000.0;

        public static string Build(string diameter)
            => Build(diameter, DefaultLengthMm);

        public static string Build(string diameter, double lengthMm)
        {
            double length = lengthMm > 0 && !double.IsNaN(lengthMm)
                && !double.IsInfinity(lengthMm) ? lengthMm : DefaultLengthMm;
            return "⌀" + ConduitDiameter.NormalizeOrDefault(diameter, "20") + "线管 "
                + TextFormatter.FormatNum(length) + "mm";
        }
    }
}

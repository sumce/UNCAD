using System;
using System.Globalization;

namespace UNCAD.Core.Text
{
    /// <summary>Builds the visible Ruanguan label from a mapped hose diameter and length.</summary>
    public static class RuanguanLabelFormatter
    {
        public static bool TryBuild(string diameter, string source,
            out string label, out double millimetres)
        {
            label = "";
            millimetres = 0;
            string normalizedDiameter = ConduitDiameter.NormalizeOrEmpty(diameter);
            if (normalizedDiameter.Length == 0
                || !RuanguanLengthParser.TryParseMillimetres(source, out millimetres)
                || millimetres <= 0) return false;

            label = Build(normalizedDiameter, millimetres);
            return true;
        }

        public static string Build(string diameter, double millimetres)
        {
            string normalizedDiameter = ConduitDiameter.NormalizeOrEmpty(diameter);
            if (normalizedDiameter.Length == 0)
                throw new ArgumentException("A hose diameter is required.", nameof(diameter));
            if (double.IsNaN(millimetres) || double.IsInfinity(millimetres)
                || millimetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(millimetres));
            return normalizedDiameter + "mm软管:" + millimetres.ToString(
                "0.##", CultureInfo.InvariantCulture) + "mm";
        }
    }
}

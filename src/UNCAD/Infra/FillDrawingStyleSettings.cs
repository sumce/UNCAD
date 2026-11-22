using System;
using System.Globalization;

namespace UNCAD.Infra
{
    /// <summary>
    /// Immutable U1L/U1Q/U1C drawing-style settings captured once for a fill command.
    /// </summary>
    public sealed class FillDrawingStyleSettings
    {
        private FillDrawingStyleSettings()
        {
        }

        public double CableTextHeight { get; private set; }
        public double CableTextOffset { get; private set; }
        public string CableTextPosition { get; private set; }
        public bool CorrectCableLabelPlacement { get; private set; }
        public double AdjacentLabelAngleToleranceRadians
            => CorrectCableLabelPlacement ? Math.PI / 12.0 : 0.18;
        public double StaleLabelAssociationMaximumDistance
            => Math.Max(CableTextOffset, CableTextHeight * 2.0)
                + Math.Max(1.0, CableTextHeight * 0.1);
        public double TrayTextHeight { get; private set; }
        public double TrayLineOffset { get; private set; }
        public double TrayTextOffset { get; private set; }
        public bool TrayAbove { get; private set; }
        public double ConduitTextHeight { get; private set; }
        public double ConduitLineOffset { get; private set; }
        public double ConduitTextOffset { get; private set; }
        public bool ConduitAbove { get; private set; }

        public static FillDrawingStyleSettings Capture()
        {
            string cablePosition = Settings.Get(ConfigKeys.UnlPos, "1");
            if (cablePosition != "0" && cablePosition != "2") cablePosition = "1";

            return new FillDrawingStyleSettings
            {
                CableTextHeight = Positive(ConfigKeys.UnlHeight, 180.0),
                CableTextOffset = NonNegative(ConfigKeys.UnlOffset, 0.0),
                CableTextPosition = cablePosition,
                CorrectCableLabelPlacement = Settings.GetBool(
                    ConfigKeys.FillCorrectCableLabelPlacement, false),
                TrayTextHeight = Positive(ConfigKeys.UnqHeight, 180.0),
                TrayLineOffset = NonNegative(ConfigKeys.UnqLineOff, 15.0),
                TrayTextOffset = NonNegative(ConfigKeys.UnqTextOff, 0.0),
                TrayAbove = Settings.Get(ConfigKeys.UnqSide, "1") != "0",
                ConduitTextHeight = Positive(ConfigKeys.ConduitHeight, 180.0),
                ConduitLineOffset = NonNegative(ConfigKeys.ConduitLineOff, 15.0),
                ConduitTextOffset = NonNegative(ConfigKeys.ConduitTextOff, 0.0),
                ConduitAbove = Settings.Get(ConfigKeys.ConduitSide, "1") != "0"
            };
        }

        private static double Positive(string key, double fallback)
            => Read(key, fallback, value => value > 0.0);

        private static double NonNegative(string key, double fallback)
            => Settings.GetNonNegativeDouble(key, fallback);

        private static double Read(string key, double fallback,
            Func<double, bool> predicate)
        {
            string raw = Settings.Get(key, null);
            return double.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value)
                && IsFinite(value) && predicate(value) ? value : fallback;
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

using System.Globalization;

namespace UNCAD.Infra
{
    public static class FillColorSettings
    {
        public const short DefaultDeviceColorIndex = 3;
        public const short DefaultUpstreamColorIndex = 6;

        public static short DeviceColorIndex()
            => Read(ConfigKeys.FillDeviceColorIndex, DefaultDeviceColorIndex);

        public static short UpstreamColorIndex()
            => Read(ConfigKeys.FillUpstreamColorIndex, DefaultUpstreamColorIndex);

        public static short Normalize(int value, short fallback)
            => value >= 1 && value <= 255 ? (short)value : fallback;

        private static short Read(string key, short fallback)
        {
            string raw = Settings.Get(key,
                fallback.ToString(CultureInfo.InvariantCulture));
            return short.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out short value)
                ? Normalize(value, fallback)
                : fallback;
        }
    }
}

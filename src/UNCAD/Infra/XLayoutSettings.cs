using UNCAD.Core.Dwg;

namespace UNCAD.Infra
{
    /// <summary>
    /// D-057：XLAYOUT 运行参数的一次性快照（U1SET「图框与排版」）。命令开始时读
    /// 一次，整个排版过程用同一份，不逐项重读注册表。
    /// </summary>
    public static class XLayoutSettings
    {
        public static XLayoutRuntimeOptions Current() => XLayoutRuntimeOptions.Create(
            Settings.GetDouble(ConfigKeys.XLayoutSpacing, XLayoutRuntimeOptions.DefaultSpacing),
            Settings.GetDouble(ConfigKeys.XLayoutLabelTextHeight,
                XLayoutRuntimeOptions.DefaultLabelTextHeight),
            Settings.GetNonNegativeDouble(ConfigKeys.XLayoutLabelDistance,
                XLayoutRuntimeOptions.DefaultLabelLeftDistance),
            Settings.GetBool(ConfigKeys.XLayoutWarningMarker,
                XLayoutRuntimeOptions.DefaultWarningMarker));
    }
}

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// 出图颜色安全（3.8.0.10，D-054 Amendment）：真彩白（RGB 255,255,255）与
    /// 255 号白在白纸上打印不可见；7 号色是「黑/白自适应」，白纸出图会变黑，
    /// 不算问题。U1PDF 是保留原色出图（不套打印样式表），真彩白会整段消失。
    /// </summary>
    public static class PlotColorSafety
    {
        public const int WhiteRgb = 0xFFFFFF;
        public const short WhiteColorIndex = 255;

        /// <summary>255 号 ACI 白：白纸上不可见。</summary>
        public static bool IsWhiteAci(short colorIndex)
            => colorIndex == WhiteColorIndex;

        /// <summary>真彩白：白纸上不可见。</summary>
        public static bool IsTrueWhite(int rgb)
            => rgb == WhiteRgb;
    }
}

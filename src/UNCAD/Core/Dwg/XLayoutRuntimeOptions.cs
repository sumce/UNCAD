using System;

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// XLAYOUT 的运行参数（D-057，U1SET「图框与排版」可改）。3.5.0 及之前这四项
    /// 是代码里的常量：图框间距 10000、机台标签字高 25000、标签离图框 300000、
    /// 重复图框一律画黄色提示框。默认值与旧常量完全一致。
    /// </summary>
    public sealed class XLayoutRuntimeOptions
    {
        public const double DefaultSpacing = XLayoutLayout.DefaultSpacing;
        public const double MaximumSpacing = 10000000d;
        public const double DefaultLabelTextHeight = 25000d;
        public const double MaximumLabelTextHeight = 1000000d;
        public const double DefaultLabelLeftDistance = 300000d;
        public const double MaximumLabelLeftDistance = 100000000d;
        public const bool DefaultWarningMarker = true;

        private XLayoutRuntimeOptions(double spacing, double labelTextHeight,
            double labelLeftDistance, bool warningMarker)
        {
            Spacing = spacing;
            LabelTextHeight = labelTextHeight;
            LabelLeftDistance = labelLeftDistance;
            WarningMarker = warningMarker;
        }

        /// <summary>图框之间的间距（图纸单位）。</summary>
        public double Spacing { get; }
        /// <summary>行首机台ID/上游编号标签的字高（图纸单位）。</summary>
        public double LabelTextHeight { get; }
        /// <summary>标签左端离本行第一张图框左边缘的距离（图纸单位）。</summary>
        public double LabelLeftDistance { get; }
        /// <summary>是否给重复/待确认的图框画黄色提示框。</summary>
        public bool WarningMarker { get; }

        public static XLayoutRuntimeOptions Default { get; } = Create(DefaultSpacing,
            DefaultLabelTextHeight, DefaultLabelLeftDistance, DefaultWarningMarker);

        /// <summary>越界或无法解析的值回落到默认值（不让一个手误的设置把图框叠在一起）。</summary>
        public static XLayoutRuntimeOptions Create(double spacing, double labelTextHeight,
            double labelLeftDistance, bool warningMarker = DefaultWarningMarker)
            => new XLayoutRuntimeOptions(
                Within(spacing, MaximumSpacing, DefaultSpacing),
                Within(labelTextHeight, MaximumLabelTextHeight, DefaultLabelTextHeight),
                NonNegativeWithin(labelLeftDistance, MaximumLabelLeftDistance,
                    DefaultLabelLeftDistance),
                warningMarker);

        private static double Within(double value, double maximum, double fallback)
            => double.IsNaN(value) || double.IsInfinity(value) || value <= 0d || value > maximum
                ? fallback
                : value;

        private static double NonNegativeWithin(double value, double maximum, double fallback)
            => double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > maximum
                ? fallback
                : value;
    }
}

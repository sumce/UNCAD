using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace UNCAD.Features.PdfExport
{
    /// <summary>出图时相对纸张的旋转：不旋转 / 旋转 90°（让图框长边对齐纸张长边）。</summary>
    internal enum PdfPlotRotation
    {
        None = 0,
        Quarter = 90
    }

    /// <summary>
    /// 一个出图窗口（轴对齐，模型空间或图纸空间的 WCS 坐标）。
    /// 坐标由 <c>FrameRegionCollector</c> 的图框边界给出，已在收集阶段做过旋转检查。
    /// </summary>
    internal sealed class PdfPlotWindow
    {
        public PdfPlotWindow(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public bool IsLandscape => Width >= Height;

        /// <summary>诊断用一行文案：窗口=(min)-(max) 宽=… 高=…</summary>
        public string Describe()
            => "窗口=(" + Num(MinX) + "," + Num(MinY) + ")-(" + Num(MaxX) + "," + Num(MaxY)
                + ") 宽=" + Num(Width) + " 高=" + Num(Height);

        /// <summary>由图框边界生成窗口（边界为空时返回 null）。</summary>
        internal static PdfPlotWindow FromBoundary(UNCAD.Core.Geometry.FrameRectangle boundary)
            => boundary == null
                ? null
                : new PdfPlotWindow(boundary.MinX, boundary.MinY, boundary.MaxX, boundary.MaxY);

        internal static string Num(double value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>选定的介质 + 是否需要旋转 90°（让图框长边对齐纸张长边）。</summary>
    internal sealed class PdfPlotMediaChoice
    {
        internal PdfPlotMediaChoice(string mediaName, double paperWidth, double paperHeight,
            PdfPlotRotation rotation)
        {
            MediaName = mediaName ?? "";
            PaperWidth = paperWidth;
            PaperHeight = paperHeight;
            Rotation = rotation;
        }

        internal string MediaName { get; }
        internal double PaperWidth { get; }
        internal double PaperHeight { get; }
        internal PdfPlotRotation Rotation { get; }
        internal bool HasPaperSize => PaperWidth > 0d && PaperHeight > 0d;

        internal string Describe()
            => MediaName + "  纸张=" + PdfPlotWindow.Num(PaperWidth) + "×"
                + PdfPlotWindow.Num(PaperHeight) + "  旋转="
                + PdfPlotPolicy.DescribeRotation(Rotation);
    }

    /// <summary>
    /// U1PDF 出图的**纯策略**（不依赖 AutoCAD，可单测）：
    /// 规范介质选择、纸张方向、窗口数值校验。
    ///
    /// 这些判断刻意放在纯代码里，因为纯逻辑可以直接被单测覆盖；真正会抛
    /// <c>eInvalidInput</c> 的是 AutoCAD 的 validator / 打印引擎，它们在
    /// <see cref="AutoCadPlotSession"/> 中被逐步包装（见 <see cref="PdfPlotPipeline"/>）。
    /// </summary>
    internal static class PdfPlotPolicy
    {
        /// <summary>纸张优选顺序：A3（图框常用）→ A4 → A2 → A1 → A0，都没有则取列表首项。</summary>
        private static readonly string[] PreferredPaperTokens = { "A3", "A4", "A2", "A1", "A0" };

        /// <summary>
        /// 名字里带这些字样的介质优先度更低：
        /// <c>full_bleed</c>/<c>expand</c> 没有正常页边距，会让图框边线紧贴纸边；
        /// <c>UserDefined</c>/<c>Pixels</c> 不是定尺图纸。
        /// 只有在没有更好选择时才会用到它们。
        /// </summary>
        private static readonly string[] DiscouragedMediaTokens =
            { "UserDefined", "Pixels", "full_bleed", "expand" };

        private static readonly Regex MediaSizePattern = new Regex(
            @"\(\s*([0-9]+(?:\.[0-9]+)?)\s*_?x_?\s*([0-9]+(?:\.[0-9]+)?)\s*_?[A-Za-z]+\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 从规范介质名里解析纸张尺寸（单位随名字，只用于判断横竖方向）。
        /// 例：<c>ISO_A3_(420.00_x_297.00_MM)</c> → (420, 297)。
        /// 解析不出来返回 false（调用方按"不旋转"处理，不影响出图成败）。
        /// </summary>
        internal static bool TryParseMediaSize(string mediaName, out double width, out double height)
        {
            width = 0d;
            height = 0d;
            if (string.IsNullOrWhiteSpace(mediaName)) return false;
            Match match = MediaSizePattern.Match(mediaName);
            if (!match.Success) return false;
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out width)) return false;
            if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out height)) return false;
            return width > 0d && height > 0d;
        }

        /// <summary>
        /// 选规范介质：优选标准 A3（其次 A4/A2/A1/A0），并优先挑**不带**
        /// full_bleed/expand 前缀、且**横竖方向与图框一致**的那一份
        /// （这样 PDF 页面方向就和图框一致，网页预览不用转屏）。
        /// 列表为空返回 null（调用方必须停止出图 —— 没有介质时打印引擎必然报错）。
        /// </summary>
        internal static PdfPlotMediaChoice SelectMedia(IReadOnlyList<string> mediaNames,
            PdfPlotWindow window)
        {
            List<string> names = (mediaNames ?? Array.Empty<string>())
                .Select(name => (name ?? "").Trim())
                .Where(name => name.Length > 0)
                .ToList();
            if (names.Count == 0) return null;

            List<string> clean = names.Where(name => !IsDiscouraged(name)).ToList();
            foreach (string token in PreferredPaperTokens)
            {
                // 首选：既匹配纸张、又是"干净"的介质名。
                string match = PickByOrientation(
                    clean.Where(name => Contains(name, token)).ToList(), window);
                if (match == null)
                {
                    // 次选：只有 full_bleed/expand 版本时也接受（好过没有）。
                    match = PickByOrientation(
                        names.Where(name => Contains(name, token)).ToList(), window);
                }
                if (match != null) return Build(match, window);
            }
            string fallback = clean.Count > 0 ? clean[0] : names[0];
            return Build(fallback, window);
        }

        private static bool Contains(string name, string token)
            => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>在候选里挑与图框横竖一致的那份；没有一致的取第一份。</summary>
        private static string PickByOrientation(List<string> candidates, PdfPlotWindow window)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (window != null)
                foreach (string candidate in candidates)
                    if (TryParseMediaSize(candidate, out double width, out double height)
                        && (width >= height) == window.IsLandscape)
                        return candidate;
            return candidates[0];
        }

        private static PdfPlotMediaChoice Build(string mediaName, PdfPlotWindow window)
        {
            double width = 0d;
            double height = 0d;
            TryParseMediaSize(mediaName, out width, out height);
            return new PdfPlotMediaChoice(mediaName, width, height,
                SelectRotation(window, width, height));
        }

        private static bool IsDiscouraged(string name)
            => DiscouragedMediaTokens.Any(token =>
                name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>
        /// 纸张方向：图框与纸张长短边方向不一致时旋转 90°，让图框尽量铺满纸张。
        /// 尺寸未知时返回 <see cref="PdfPlotRotation.None"/>（Fit + 居中同样能出图，只是留白多）。
        /// </summary>
        internal static PdfPlotRotation SelectRotation(PdfPlotWindow window,
            double paperWidth, double paperHeight)
        {
            if (window == null || paperWidth <= 0d || paperHeight <= 0d)
                return PdfPlotRotation.None;
            bool paperLandscape = paperWidth >= paperHeight;
            return window.IsLandscape == paperLandscape
                ? PdfPlotRotation.None
                : PdfPlotRotation.Quarter;
        }

        /// <summary>把旋转还原成"相对窗口"的文案，便于日志判断。</summary>
        internal static string DescribeRotation(PdfPlotRotation rotation)
            => rotation == PdfPlotRotation.Quarter ? "90°" : "0°";

        /// <summary>
        /// 窗口数值校验：必须有限、宽高为正且不是退化矩形。
        /// 这些情况下 <c>PlotSettingsValidator.SetPlotWindowArea</c> / 打印引擎必然抛
        /// <c>eInvalidInput</c>，必须在调用 AutoCAD 之前就用中文说清楚。
        /// </summary>
        internal static bool TryValidateWindow(PdfPlotWindow window, out string error)
        {
            error = "";
            if (window == null)
            {
                error = "找不到图框边界，无法确定出图窗口。";
                return false;
            }
            foreach (double value in new[] { window.MinX, window.MinY, window.MaxX, window.MaxY })
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    error = "图框窗口坐标不是有效数值（NaN/Inf）：" + window.Describe();
                    return false;
                }
                if (Math.Abs(value) > 1e12d)
                {
                    error = "图框窗口坐标超出合理范围（|坐标| > 1e12）：" + window.Describe();
                    return false;
                }
            }
            if (window.Width <= 1e-6d || window.Height <= 1e-6d)
            {
                error = "图框窗口宽或高接近 0，无法出图：" + window.Describe();
                return false;
            }
            return true;
        }
    }
}

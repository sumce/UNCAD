using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using UNCAD.Infra;

namespace UNCAD.UI
{
    internal static class RibbonIconFactory
    {
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource Create(string command, int pixelSize)
        {
            string icon = IconName(command);
            string cacheKey = icon + ":" + pixelSize;
            if (Cache.TryGetValue(cacheKey, out ImageSource cached)) return cached;
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resource = "UNCAD.Assets.Icons." + icon + ".svg";
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null) throw new InvalidDataException("找不到图标资源 " + resource);
                    var document = new XmlDocument();
                    document.Load(stream);
                    var drawings = new DrawingGroup
                    {
                        Transform = new ScaleTransform(pixelSize / 24.0, pixelSize / 24.0),
                        ClipGeometry = new RectangleGeometry(new Rect(0, 0, 24, 24))
                    };
                    var brush = new SolidColorBrush(IconColor(icon));
                    var pen = new Pen(brush, 1.8)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    foreach (XmlNode node in document.GetElementsByTagName("path"))
                    {
                        string data = node.Attributes?["d"]?.Value;
                        if (string.IsNullOrWhiteSpace(data)) continue;
                        drawings.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(data)));
                    }
                    drawings.Freeze();
                    var image = new DrawingImage(drawings);
                    image.Freeze();
                    Cache[cacheKey] = image;
                    return image;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Ribbon SVG icon failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 命令 → 图标语义映射。同一视觉族（清单/标注/输出/系统）共用图标形状，
        /// 靠 IconColor 区分具体命令；新增命令必须在这里给图标，否则落回 tool。
        /// </summary>
        private static string IconName(string command)
        {
            if (string.Equals(command, CommandIds.Fill, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.FillUpdate, StringComparison.OrdinalIgnoreCase))
                return "fill";
            if (string.Equals(command, CommandIds.Submit, StringComparison.OrdinalIgnoreCase))
                return "submit";
            if (string.Equals(command, CommandIds.DwgExport, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.DwgExportShort, StringComparison.OrdinalIgnoreCase))
                return "dwg";
            if (string.Equals(command, CommandIds.PdfExport, StringComparison.OrdinalIgnoreCase))
                return "pdf";
            if (string.Equals(command, CommandIds.XLayout, StringComparison.OrdinalIgnoreCase))
                return "layout";
            if (string.Equals(command, CommandIds.Merge, StringComparison.OrdinalIgnoreCase))
                return "merge";
            if (string.Equals(command, CommandIds.Settings, StringComparison.OrdinalIgnoreCase))
                return "settings";
            if (string.Equals(command, CommandIds.DataRefresh, StringComparison.OrdinalIgnoreCase))
                return "data";
            if (string.Equals(command, CommandIds.ClientLogin, StringComparison.OrdinalIgnoreCase))
                return "login";
            if (string.Equals(command, CommandIds.About, StringComparison.OrdinalIgnoreCase))
                return "about";
            if (string.Equals(command, CommandIds.Help, StringComparison.OrdinalIgnoreCase))
                return "help";
            if (string.Equals(command, CommandIds.Conduit, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Conduit20, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Conduit25, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Conduit38, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Conduit51, StringComparison.OrdinalIgnoreCase))
                return "conduit";
            if (string.Equals(command, CommandIds.Tray100, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Tray200, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Tray400, StringComparison.OrdinalIgnoreCase)) return "tray";
            if (string.Equals(command, CommandIds.Line3dPreview, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Line3dBatchPreview, StringComparison.OrdinalIgnoreCase))
                return "line3d";
            if (string.Equals(command, CommandIds.Line, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.LineQuick, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.DimensionText, StringComparison.OrdinalIgnoreCase))
                return "line";
            if (string.Equals(command, CommandIds.Arch, StringComparison.OrdinalIgnoreCase)) return "arch";
            if (string.Equals(command, CommandIds.LegacyStatistics, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Statistics, StringComparison.OrdinalIgnoreCase))
                return "stat";
            return "tool";
        }

        // 图标色取自 UiTheme 色板。WPF Color 与 WinForms Color 类型不通用，
        // 因此这里做一次显式转换，而不是各自维护一份十六进制值。
        // 3.1.0.2：全部图标统一为单一浅灰（UiTheme.RibbonIcon，AutoCAD 原生
        // 功能区风格）。旧的"一命令一色"商务色方案（绿提交/红 PDF/橙拱桥/
        // 各档钢蓝）让功能区看起来像彩虹盘，与 AutoCAD 深色主题下原生面板
        // 的统一浅灰线性图标反差强烈（2026-11-15 用户对照截图直言"我们的
        // 明显难看"）。命令区分靠图标形状与文字标签，与原生一致；SVG 线性
        // 图标族本身已是统一的 1.8px 圆角笔画。
        internal static Color IconColor(string icon)
        {
            _ = icon;
            return ToWpf(UiTheme.RibbonIcon);
        }

        private static Color ToWpf(System.Drawing.Color token)
            => Color.FromRgb(token.R, token.G, token.B);
    }
}

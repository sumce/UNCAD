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
        // 因此这里做一次显式转换，而不是各自维护一份十六进制值——原先两处
        // 独立定义会随主题调整漂移。
        // 商务体系：同一面板内用“深墨蓝 / 钢蓝 / 深钢蓝”三档区分动作层级，
        // 只有 PDF（交付物）与拱桥（开洞风险）使用语义色，避免图标变成彩虹。
        private static Color IconColor(string icon)
        {
            if (icon == "fill") return ToWpf(UiTheme.Ink);
            if (icon == "submit" || icon == "excel") return ToWpf(UiTheme.SuccessFg);
            if (icon == "pdf") return ToWpf(UiTheme.DangerFg);
            if (icon == "arch") return ToWpf(UiTheme.WarningFg);
            if (icon == "settings" || icon == "about" || icon == "stat")
                return ToWpf(UiTheme.TextSecondary);
            if (icon == "data") return ToWpf(UiTheme.AccentDark);
            if (icon == "login") return ToWpf(UiTheme.Ink);
            if (icon == "help") return ToWpf(UiTheme.Accent);
            if (icon == "tray" || icon == "dwg") return ToWpf(UiTheme.AccentDark);
            if (icon == "conduit" || icon == "line" || icon == "layout" || icon == "merge"
                || icon == "line3d") return ToWpf(UiTheme.Accent);
            return ToWpf(UiTheme.TextDisabled);
        }

        private static Color ToWpf(System.Drawing.Color token)
            => Color.FromRgb(token.R, token.G, token.B);
    }
}

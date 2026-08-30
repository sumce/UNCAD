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

        private static string IconName(string command)
        {
            if (string.Equals(command, CommandIds.Fill, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.FillUpdate, StringComparison.OrdinalIgnoreCase))
                return "fill";
            if (string.Equals(command, CommandIds.Submit, StringComparison.OrdinalIgnoreCase)) return "submit";
            if (string.Equals(command, CommandIds.Settings, StringComparison.OrdinalIgnoreCase)) return "settings";
            if (string.Equals(command, CommandIds.About, StringComparison.OrdinalIgnoreCase)) return "about";
            if (string.Equals(command, CommandIds.Conduit, StringComparison.OrdinalIgnoreCase)) return "conduit";
            if (string.Equals(command, CommandIds.Tray100, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Tray200, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, CommandIds.Tray400, StringComparison.OrdinalIgnoreCase)) return "tray";
            if (string.Equals(command, CommandIds.Line, StringComparison.OrdinalIgnoreCase)) return "line";
            if (string.Equals(command, CommandIds.Arch, StringComparison.OrdinalIgnoreCase)) return "arch";
            if (string.Equals(command, CommandIds.LegacyStatistics, StringComparison.OrdinalIgnoreCase)) return "stat";
            return "tool";
        }

        private static Color IconColor(string icon)
        {
            if (icon == "fill") return Color.FromRgb(47, 128, 183);
            if (icon == "submit") return Color.FromRgb(37, 139, 98);
            if (icon == "settings") return Color.FromRgb(210, 139, 39);
            if (icon == "about") return Color.FromRgb(78, 114, 165);
            if (icon == "conduit") return Color.FromRgb(34, 148, 172);
            if (icon == "tray") return Color.FromRgb(192, 85, 70);
            if (icon == "line") return Color.FromRgb(197, 150, 45);
            if (icon == "arch") return Color.FromRgb(68, 139, 104);
            if (icon == "stat") return Color.FromRgb(92, 120, 176);
            if (icon == "excel") return Color.FromRgb(44, 128, 87);
            return Color.FromRgb(100, 112, 125);
        }
    }
}

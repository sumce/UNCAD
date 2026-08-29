using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Geometry;

namespace UNCAD.Core.Dwg
{
    /// <summary>One frame and its identity used by the DWG export layout.</summary>
    public class DwgFrameLayoutItem
    {
        public DwgFrameLayoutItem(string machineId, string deviceName, FrameRectangle boundary)
        {
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            Boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        }

        public string MachineId { get; }
        public string DeviceName { get; }
        public FrameRectangle Boundary { get; }
    }

    /// <summary>Placement of one source frame in the exported horizontal row.</summary>
    public sealed class DwgFramePlacement
    {
        internal DwgFramePlacement(DwgFrameLayoutItem item, double translationX, double translationY)
        {
            Item = item;
            TranslationX = translationX;
            TranslationY = translationY;
        }

        public DwgFrameLayoutItem Item { get; }
        public double TranslationX { get; }
        public double TranslationY { get; }
    }

    /// <summary>Arranges selected frame contents in one row with a fixed 10000-unit gap.</summary>
    public static class DwgExportLayout
    {
        public const double DefaultSpacing = 10000d;

        public static IReadOnlyList<DwgFramePlacement> Arrange(
            IEnumerable<DwgFrameLayoutItem> items, double spacing = DefaultSpacing)
        {
            if (spacing < 0) throw new ArgumentOutOfRangeException(nameof(spacing));
            var ordered = (items ?? Enumerable.Empty<DwgFrameLayoutItem>())
                .Where(item => item != null)
                .OrderBy(item => item.Boundary.MinX)
                .ThenByDescending(item => item.Boundary.MaxY)
                .ThenBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new List<DwgFramePlacement>(ordered.Count);
            double nextX = 0d;
            foreach (DwgFrameLayoutItem item in ordered)
            {
                // Normalize each frame to y=0 and place the next one after the configured gap.
                result.Add(new DwgFramePlacement(item,
                    nextX - item.Boundary.MinX, -item.Boundary.MinY));
                nextX += item.Boundary.Width + spacing;
            }
            return result;
        }
    }
}

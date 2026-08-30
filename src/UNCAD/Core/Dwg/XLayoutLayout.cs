using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Geometry;

namespace UNCAD.Core.Dwg
{
    /// <summary>One source frame used by XLAYOUT.</summary>
    public sealed class XLayoutFrameItem : DwgFrameLayoutItem
    {
        public XLayoutFrameItem(string machineId, string deviceName, FrameRectangle boundary,
            string handle = "") : base(machineId, deviceName, boundary)
        {
            Handle = handle ?? "";
        }

        public string Handle { get; }
    }

    /// <summary>One frame translation produced by XLAYOUT.</summary>
    public sealed class XLayoutPlacement
    {
        internal XLayoutPlacement(XLayoutFrameItem item, double translationX,
            double translationY, int rowIndex)
        {
            Item = item;
            TranslationX = translationX;
            TranslationY = translationY;
            RowIndex = rowIndex;
        }

        public XLayoutFrameItem Item { get; }
        public double TranslationX { get; }
        public double TranslationY { get; }
        public int RowIndex { get; }
    }

    /// <summary>Arranges frames into one row per machine with a fixed gap.</summary>
    public static class XLayoutLayout
    {
        public const double DefaultSpacing = 10000d;

        public static IReadOnlyList<XLayoutPlacement> Arrange(
            IEnumerable<XLayoutFrameItem> items, double spacing = DefaultSpacing)
        {
            if (spacing < 0) throw new ArgumentOutOfRangeException(nameof(spacing));
            var machineRows = (items ?? Enumerable.Empty<XLayoutFrameItem>())
                .Where(item => item != null)
                .GroupBy(item => item.MachineId ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new List<XLayoutPlacement>();
            double nextRowTop = 0d;
            for (int rowIndex = 0; rowIndex < machineRows.Count; rowIndex++)
            {
                IGrouping<string, XLayoutFrameItem> row = machineRows[rowIndex];
                var ordered = row.OrderBy(item => item.Boundary.MinX)
                    .ThenBy(item => item.Boundary.MinY)
                    .ThenBy(item => item.Handle, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                double nextX = 0d;
                double rowHeight = ordered.Count == 0 ? 0d : ordered.Max(item => item.Boundary.Height);
                foreach (XLayoutFrameItem item in ordered)
                {
                    // Align the top edge of every frame in a machine row. The next row starts
                    // exactly spacing units below the lowest frame in the current row.
                    result.Add(new XLayoutPlacement(item,
                        nextX - item.Boundary.MinX,
                        nextRowTop - item.Boundary.MaxY,
                        rowIndex));
                    nextX += item.Boundary.Width + spacing;
                }
                nextRowTop -= rowHeight + spacing;
            }
            return result;
        }
    }
}

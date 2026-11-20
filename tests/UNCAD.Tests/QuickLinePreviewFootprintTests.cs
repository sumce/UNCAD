using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class QuickLinePreviewFootprintTests
    {
        [Fact]
        public void AxisLabels_CombineNumberAndMultipleLettersAtEveryOccupiedColumn()
        {
            var model = new QuickLineOrbitPreviewModel(new[] { Segment(0, 0, 0, 9600) },
                "", null, "48/AA", "46/AA",
                new QuickLineSpatialPoint(0, 0, 0), new QuickLineSpatialPoint(0, 9600, 0),
                directionResolved: true);
            var envelope = QuickLineOrbitPreviewEnvelope.Build(model);
            var footprint = QuickLinePreviewFootprint.Plan(envelope);
            var labels = footprint.AxisLabels(envelope).ToArray();

            Assert.Equal(footprint.Columns.Count, labels.Length);
            Assert.Contains(labels, label => label.Text == "48/AA" && label.X == 0 && label.Y == 0);
            Assert.Contains(labels, label => label.Text == "47/AA" && label.X == 0 && label.Y == 4800);
            Assert.Contains(labels, label => label.Text == "48/AB" && label.X == 4800 && label.Y == 0);
            Assert.Contains(labels, label => label.Text == "48/Z" && label.X == -4800 && label.Y == 0);
            Assert.All(labels, label =>
                Assert.Contains(footprint.Columns, column => column.X == label.X && column.Y == label.Y));
        }

        [Fact]
        public void AxisLabels_UnresolvedDirectionDoesNotInventColumnNames()
        {
            var envelope = QuickLineOrbitPreviewEnvelope.Build(
                new QuickLineOrbitPreviewModel(new[] { Segment(0, 0, 9600, 0) }, "", null));
            Assert.Empty(QuickLinePreviewFootprint.Plan(envelope).AxisLabels(envelope));
        }

        [Fact]
        public void LRoute_CoversBothArmsWithOneExtraBayAndLeavesInteriorEmpty()
        {
            QuickLinePreviewFootprint footprint = Plan(
                Segment(0, 0, 96000, 0), Segment(96000, 0, 96000, 96000));

            Assert.Equal(176, footprint.Bays.Count);
            Assert.Equal(2, footprint.Regions.Count);
            Assert.Equal(-9600, footprint.Bays.Min(bay => bay.MinX));
            Assert.Equal(-9600, footprint.Bays.Min(bay => bay.MinY));
            Assert.Equal(105600, footprint.Bays.Max(bay => bay.MaxX));
            Assert.Equal(105600, footprint.Bays.Max(bay => bay.MaxY));
            Assert.DoesNotContain(footprint.Bays, bay => Covers(bay, 48000, 48000));
            Assert.DoesNotContain(footprint.Columns,
                column => column.X == 48000 && column.Y == 48000);
            AssertMergedCoverage(footprint);
        }

        [Fact]
        public void NegativeCoordinates_ReversedAndDuplicateSegmentsProduceSameFootprint()
        {
            var segments = new[]
            {
                Segment(-20000, -17000, 76000, -17000),
                Segment(76000, -17000, 76000, 79000)
            };
            var reversed = segments.Reverse().Select(segment =>
                Segment(segment.End.X, segment.End.Y,
                    segment.Start.X, segment.Start.Y)).ToArray();
            QuickLinePreviewFootprint expected = Plan(segments);
            QuickLinePreviewFootprint actual = Plan(reversed.Concat(segments).ToArray());

            Assert.Equal(expected.Bays.Select(bay => (bay.MinX, bay.MinY)),
                actual.Bays.Select(bay => (bay.MinX, bay.MinY)));
            AssertMergedCoverage(actual);
        }

        [Fact]
        public void VerticalDisconnectedRoutes_DoNotFillSpaceBetweenThem()
        {
            QuickLinePreviewFootprint footprint = Plan(
                Segment(0, 0, 0, 0), Segment(480000, 480000, 480000, 480000));

            Assert.Equal(32, footprint.Bays.Count);
            Assert.Equal(2, footprint.Regions.Count);
            Assert.DoesNotContain(footprint.Regions, bay => Covers(bay, 240000, 240000));
            AssertMergedCoverage(footprint);
        }

        [Fact]
        public void Diagonal_CoversEveryCrossingWithoutFillingBoundingRectangle()
        {
            QuickLinePreviewFootprint footprint = Plan(Segment(0, 0, 300000, 300000));
            Assert.InRange(footprint.Bays.Count, 300, 600);
            for (int step = 0; step <= 300; step++)
            {
                double at = step * 1000;
                Assert.Contains(footprint.Bays, bay => Covers(bay, at, at));
            }
            Assert.DoesNotContain(footprint.Bays, bay => Covers(bay, 150000, 10000));
            AssertMergedCoverage(footprint);
        }

        [Fact]
        public void WideChannel_IncludesItsWidthBeforeAddingMargin()
        {
            var route = new QuickLineOrbitPreviewSegment("wide",
                new QuickLineSpatialPoint(0, 0, 0),
                new QuickLineSpatialPoint(48000, 0, 0),
                QuickLineSpatialAxis.X, 48000, true, "",
                new QuickLineOrbitPreviewChannel(24000, false, false));
            QuickLinePreviewFootprint footprint = Plan(route);

            Assert.Equal(-19200, footprint.Bays.Min(bay => bay.MinY));
            Assert.Equal(19200, footprint.Bays.Max(bay => bay.MaxY));
            AssertMergedCoverage(footprint);
        }

        [Fact]
        public void CoordinatesBeyondGridIndexRange_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => Plan(Segment(0, 0, 1e20, 0)));
        }

        [Fact]
        public void ExcessiveOccupiedArea_IsRejectedInsteadOfSamplingHoles()
        {
            Assert.Throws<InvalidOperationException>(() =>
                Plan(Segment(0, 0, 1000000000, 0)));
        }

        private static QuickLinePreviewFootprint Plan(
            params QuickLineOrbitPreviewSegment[] segments)
            => QuickLinePreviewFootprint.Plan(QuickLineOrbitPreviewEnvelope.Build(
                new QuickLineOrbitPreviewModel(segments, "", new List<string>())));

        private static QuickLineOrbitPreviewSegment Segment(
            double ax, double ay, double bx, double by)
            => new QuickLineOrbitPreviewSegment("route",
                new QuickLineSpatialPoint(ax, ay, 0),
                new QuickLineSpatialPoint(bx, by, ax == bx && ay == by ? 2500 : 0),
                QuickLineSpatialAxis.X, 4800, true);

        private static bool Covers(QuickLinePreviewFootprint.Area area, double x, double y)
            => x >= area.MinX && x <= area.MaxX && y >= area.MinY && y <= area.MaxY;

        private static void AssertMergedCoverage(QuickLinePreviewFootprint footprint)
        {
            var mergedCells = new List<(double X, double Y)>();
            foreach (QuickLinePreviewFootprint.Area region in footprint.Regions)
            {
                for (double x = region.MinX; x < region.MaxX; x += 4800)
                for (double y = region.MinY; y < region.MaxY; y += 4800)
                    mergedCells.Add((x, y));
            }
            Assert.Equal(mergedCells.Count, mergedCells.Distinct().Count());
            Assert.Equal(footprint.Bays.Select(bay => (bay.MinX, bay.MinY))
                    .OrderBy(cell => cell.MinY).ThenBy(cell => cell.MinX),
                mergedCells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X));
        }
    }
}

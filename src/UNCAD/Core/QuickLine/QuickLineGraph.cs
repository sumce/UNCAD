using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UNCAD.Core.QuickLine
{
    /// <summary>
    /// One endpoint-to-endpoint connection in a U1LX graph.  The source side
    /// is represented by <see cref="Endpoint"/> and the neighbor side by
    /// <see cref="ConnectedEndpoint"/>.
    /// </summary>
    public sealed class QuickLineConnection
    {
        internal QuickLineConnection(string segmentId, QuickLineEndpoint endpoint,
            string connectedSegmentId, QuickLineEndpoint connectedEndpoint,
            QuickLinePoint point)
        {
            SegmentId = segmentId;
            Endpoint = endpoint;
            ConnectedSegmentId = connectedSegmentId;
            ConnectedEndpoint = connectedEndpoint;
            Point = point;
        }

        public string SegmentId { get; }
        public QuickLineEndpoint Endpoint { get; }
        public string ConnectedSegmentId { get; }
        public QuickLineEndpoint ConnectedEndpoint { get; }
        public QuickLinePoint Point { get; }

        // Short aliases make the object convenient for Cad/Feature callers.
        public string NeighborId => ConnectedSegmentId;
        public QuickLineEndpoint NeighborEndpoint => ConnectedEndpoint;
    }

    /// <summary>
    /// Endpoint tolerance graph for independent U1L Line entities.
    /// Connections are intentionally endpoint-only: crossing line interiors
    /// are not treated as cable continuation.
    /// </summary>
    public sealed class QuickLineGraph
    {
        private static readonly IReadOnlyList<QuickLineConnection> EmptyConnections
            = new ReadOnlyCollection<QuickLineConnection>(new List<QuickLineConnection>());

        private readonly Dictionary<string, QuickLineSegment> _segments;
        private readonly Dictionary<string, List<QuickLineConnection>> _connections;
        private readonly ReadOnlyCollection<QuickLineSegment> _segmentList;

        private QuickLineGraph(List<QuickLineSegment> segments, double endpointTolerance)
        {
            EndpointTolerance = endpointTolerance;
            _segments = new Dictionary<string, QuickLineSegment>(StringComparer.OrdinalIgnoreCase);
            _connections = new Dictionary<string, List<QuickLineConnection>>(StringComparer.OrdinalIgnoreCase);

            foreach (QuickLineSegment segment in segments)
            {
                if (segment == null) throw new ArgumentException("The segment collection contains null.", "segments");
                if (_segments.ContainsKey(segment.Id))
                    throw new ArgumentException("Duplicate line segment id: " + segment.Id, "segments");
                _segments.Add(segment.Id, segment);
                _connections.Add(segment.Id, new List<QuickLineConnection>());
            }

            _segmentList = new ReadOnlyCollection<QuickLineSegment>(segments);
            BuildConnections(segments);
        }

        public IReadOnlyList<QuickLineSegment> Segments => _segmentList;
        public double EndpointTolerance { get; }

        public static QuickLineGraph Build(
            IEnumerable<QuickLineSegment> segments,
            double endpointTolerance = 0.001)
        {
            if (segments == null) throw new ArgumentNullException("segments");
            ValidateTolerance(endpointTolerance);
            var list = new List<QuickLineSegment>(segments);
            return new QuickLineGraph(list, endpointTolerance);
        }

        public bool TryGetSegment(string id, out QuickLineSegment segment)
        {
            if (id == null)
            {
                segment = null;
                return false;
            }
            return _segments.TryGetValue(id, out segment);
        }

        public QuickLineSegment GetSegment(string id)
        {
            if (id == null) throw new ArgumentNullException("id");
            QuickLineSegment segment;
            if (!_segments.TryGetValue(id, out segment))
                throw new KeyNotFoundException("Unknown line segment id: " + id);
            return segment;
        }

        public IReadOnlyList<QuickLineConnection> GetConnections(
            string segmentId, QuickLineEndpoint endpoint)
        {
            if (segmentId == null || endpoint == QuickLineEndpoint.None)
                return EmptyConnections;

            List<QuickLineConnection> connections;
            if (!_connections.TryGetValue(segmentId, out connections))
                return EmptyConnections;

            var filtered = new List<QuickLineConnection>();
            foreach (QuickLineConnection connection in connections)
                if (connection.Endpoint == endpoint) filtered.Add(connection);
            return new ReadOnlyCollection<QuickLineConnection>(filtered);
        }

        public IReadOnlyList<QuickLineConnection> GetConnections(string segmentId)
        {
            if (segmentId == null) return EmptyConnections;
            List<QuickLineConnection> connections;
            if (!_connections.TryGetValue(segmentId, out connections))
                return EmptyConnections;
            return new ReadOnlyCollection<QuickLineConnection>(connections);
        }

        /// <summary>
        /// Counts distinct segments reachable from one side of a segment. The
        /// source segment and any ids in excludedIds are not counted. By
        /// default excluded ids are barriers; when traverseExcluded is true
        /// they remain pass-through nodes, which lets U1LX find unfilled
        /// segments beyond an already completed annotation.
        /// </summary>
        public int CountReachableFrom(
            string segmentId,
            QuickLineEndpoint endpoint,
            ISet<string> excludedIds = null,
            bool traverseExcluded = false)
        {
            if (segmentId == null || endpoint == QuickLineEndpoint.None)
                return 0;
            if (!_segments.ContainsKey(segmentId)) return 0;

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludedIds != null)
                foreach (string id in excludedIds)
                    if (id != null) excluded.Add(id);

            // A barrier is marked visited up front; a pass-through completed
            // segment is discovered normally and simply omitted from count.
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                segmentId
            };
            if (!traverseExcluded)
                foreach (string id in excluded)
                    visited.Add(id);

            var queue = new Queue<string>();
            foreach (QuickLineConnection connection in GetConnections(segmentId, endpoint))
            {
                if (visited.Add(connection.ConnectedSegmentId))
                    queue.Enqueue(connection.ConnectedSegmentId);
            }

            int count = 0;
            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                if (!excluded.Contains(currentId)) count++;
                foreach (QuickLineConnection connection in GetConnections(currentId))
                {
                    if (visited.Add(connection.ConnectedSegmentId))
                        queue.Enqueue(connection.ConnectedSegmentId);
                }
            }
            return count;
        }

        private void BuildConnections(IReadOnlyList<QuickLineSegment> segments)
        {
            double toleranceSquared = EndpointTolerance * EndpointTolerance;
            if (EndpointTolerance == 0)
            {
                BuildExactConnections(segments);
                return;
            }

            var buckets = new Dictionary<GridCell, List<IndexedEndpoint>>();
            foreach (QuickLineSegment segment in segments)
            {
                IndexEndpoint(segment, QuickLineEndpoint.Start, buckets, toleranceSquared);
                IndexEndpoint(segment, QuickLineEndpoint.End, buckets, toleranceSquared);
            }
        }

        private void BuildExactConnections(IReadOnlyList<QuickLineSegment> segments)
        {
            var points = new Dictionary<QuickLinePoint, List<IndexedEndpoint>>();
            foreach (QuickLineSegment segment in segments)
            {
                IndexExactEndpoint(segment, QuickLineEndpoint.Start, points);
                IndexExactEndpoint(segment, QuickLineEndpoint.End, points);
            }
        }

        private void IndexEndpoint(QuickLineSegment segment, QuickLineEndpoint endpoint,
            IDictionary<GridCell, List<IndexedEndpoint>> buckets, double toleranceSquared)
        {
            QuickLinePoint point = segment.PointAt(endpoint);
            GridCell cell = GridCell.From(point, EndpointTolerance);
            for (long x = cell.X - 1; x <= cell.X + 1; x++)
            for (long y = cell.Y - 1; y <= cell.Y + 1; y++)
            {
                if (!buckets.TryGetValue(new GridCell(x, y),
                        out List<IndexedEndpoint> candidates)) continue;
                foreach (IndexedEndpoint candidate in candidates)
                {
                    if (string.Equals(candidate.Segment.Id, segment.Id,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    AddIfConnected(candidate.Segment, candidate.Endpoint, segment,
                        endpoint, toleranceSquared);
                }
            }

            if (!buckets.TryGetValue(cell, out List<IndexedEndpoint> items))
            {
                items = new List<IndexedEndpoint>();
                buckets.Add(cell, items);
            }
            items.Add(new IndexedEndpoint(segment, endpoint));
        }

        private void IndexExactEndpoint(QuickLineSegment segment, QuickLineEndpoint endpoint,
            IDictionary<QuickLinePoint, List<IndexedEndpoint>> points)
        {
            QuickLinePoint point = segment.PointAt(endpoint);
            if (!points.TryGetValue(point, out List<IndexedEndpoint> items))
            {
                items = new List<IndexedEndpoint>();
                points.Add(point, items);
            }
            else
            {
                foreach (IndexedEndpoint candidate in items)
                {
                    if (string.Equals(candidate.Segment.Id, segment.Id,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    AddIfConnected(candidate.Segment, candidate.Endpoint, segment,
                        endpoint, 0);
                }
            }
            items.Add(new IndexedEndpoint(segment, endpoint));
        }

        private void AddIfConnected(QuickLineSegment left, QuickLineEndpoint leftEndpoint,
            QuickLineSegment right, QuickLineEndpoint rightEndpoint, double toleranceSquared)
        {
            QuickLinePoint leftPoint = left.PointAt(leftEndpoint);
            QuickLinePoint rightPoint = right.PointAt(rightEndpoint);
            if (leftPoint.DistanceSquaredTo(rightPoint) > toleranceSquared) return;

            // Use the average endpoint for view-centering/highlighting. It is
            // less biased when AutoCAD geometry has a tiny numeric mismatch.
            var join = new QuickLinePoint(
                (leftPoint.X + rightPoint.X) / 2.0,
                (leftPoint.Y + rightPoint.Y) / 2.0);
            AddConnection(left.Id, leftEndpoint, right.Id, rightEndpoint, join);
            AddConnection(right.Id, rightEndpoint, left.Id, leftEndpoint, join);
        }

        private void AddConnection(string segmentId, QuickLineEndpoint endpoint,
            string connectedSegmentId, QuickLineEndpoint connectedEndpoint,
            QuickLinePoint point)
        {
            List<QuickLineConnection> list = _connections[segmentId];
            foreach (QuickLineConnection existing in list)
            {
                if (existing.Endpoint == endpoint
                    && string.Equals(existing.ConnectedSegmentId, connectedSegmentId,
                        StringComparison.OrdinalIgnoreCase)
                    && existing.ConnectedEndpoint == connectedEndpoint)
                    return;
            }
            list.Add(new QuickLineConnection(segmentId, endpoint,
                connectedSegmentId, connectedEndpoint, point));
        }

        private static void ValidateTolerance(double tolerance)
        {
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                throw new ArgumentOutOfRangeException("endpointTolerance");
        }

        private struct IndexedEndpoint
        {
            public IndexedEndpoint(QuickLineSegment segment, QuickLineEndpoint endpoint)
            {
                Segment = segment;
                Endpoint = endpoint;
            }

            public QuickLineSegment Segment { get; }
            public QuickLineEndpoint Endpoint { get; }
        }

        private struct GridCell : IEquatable<GridCell>
        {
            public GridCell(long x, long y)
            {
                X = x;
                Y = y;
            }

            public long X { get; }
            public long Y { get; }

            public static GridCell From(QuickLinePoint point, double size)
                => new GridCell((long)Math.Floor(point.X / size),
                    (long)Math.Floor(point.Y / size));

            public bool Equals(GridCell other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is GridCell other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (X.GetHashCode() * 397) ^ Y.GetHashCode(); }
            }
        }
    }
}

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UNCAD.Core.QuickLine
{
    public enum QuickLineSpatialAxis
    {
        X = 1,
        Y = 2,
        Z = 3
    }

    /// <summary>Projection family used to interpret the source 2D sketch.</summary>
    public enum QuickLineProjectionMode
    {
        Auto = 0,
        Isometric = 1,
        Orthographic = 2
    }

    public sealed class QuickLineIsometricNode
    {
        internal QuickLineIsometricNode(string id, QuickLineSpatialPoint position)
        {
            Id = id;
            Position = position;
        }

        public string Id { get; }
        public QuickLineSpatialPoint Position { get; }
    }

    public sealed class QuickLineIsometricSegment
    {
        internal QuickLineIsometricSegment(string id, string startNodeId,
            string endNodeId, QuickLineSpatialAxis axis, int directionSign,
            double planAngleDegrees, double distanceMillimetres)
        {
            Id = id;
            StartNodeId = startNodeId;
            EndNodeId = endNodeId;
            Axis = axis;
            DirectionSign = directionSign;
            PlanAngleDegrees = planAngleDegrees;
            DistanceMillimetres = distanceMillimetres;
        }

        public string Id { get; }
        public string StartNodeId { get; }
        public string EndNodeId { get; }
        public QuickLineSpatialAxis Axis { get; }
        public int DirectionSign { get; }
        public double PlanAngleDegrees { get; }
        public double DistanceMillimetres { get; }
    }

    public sealed class QuickLineIsometricScene
    {
        internal QuickLineIsometricScene(string rootNodeId,
            QuickLineProjectionMode projectionMode,
            IList<QuickLineIsometricNode> nodes,
            IList<QuickLineIsometricSegment> segments,
            IList<string> diagnostics)
        {
            RootNodeId = rootNodeId;
            ProjectionMode = projectionMode;
            Nodes = new ReadOnlyCollection<QuickLineIsometricNode>(nodes);
            Segments = new ReadOnlyCollection<QuickLineIsometricSegment>(segments);
            Diagnostics = new ReadOnlyCollection<string>(diagnostics);
        }

        public string RootNodeId { get; }
        public QuickLineProjectionMode ProjectionMode { get; }
        public IReadOnlyList<QuickLineIsometricNode> Nodes { get; }
        public IReadOnlyList<QuickLineIsometricSegment> Segments { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }
}

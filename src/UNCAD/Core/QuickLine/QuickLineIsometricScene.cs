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
            double planAngleDegrees, double distanceMillimetres,
            double displayDistanceMillimetres, bool completed)
        {
            Id = id;
            StartNodeId = startNodeId;
            EndNodeId = endNodeId;
            Axis = axis;
            DirectionSign = directionSign;
            PlanAngleDegrees = planAngleDegrees;
            DistanceMillimetres = distanceMillimetres;
            DisplayDistanceMillimetres = displayDistanceMillimetres;
            Completed = completed;
        }

        public string Id { get; }
        public string StartNodeId { get; }
        public string EndNodeId { get; }
        public QuickLineSpatialAxis Axis { get; }
        public int DirectionSign { get; }
        public double PlanAngleDegrees { get; }
        /// <summary>The actual millimetre value retained for labels and CAD writes.</summary>
        public double DistanceMillimetres { get; }
        /// <summary>
        /// Initial editor geometry length. Unconfirmed U1L placeholders use
        /// their source CAD line length until the user enters a real value.
        /// </summary>
        public double DisplayDistanceMillimetres { get; }
        public bool Completed { get; }
    }

    /// <summary>
    /// One orthogonal segment created in the U1LX drawing editor. The axis
    /// and sign describe its true 3D direction; the distance is the actual
    /// millimetre value that must be retained in the CAD annotation.
    /// </summary>
    public sealed class QuickLineCreatedSegment
    {
        public QuickLineCreatedSegment(string id, string startNodeId,
            string endNodeId, QuickLineSpatialAxis axis, int directionSign,
            double distanceMillimetres)
        {
            Id = id;
            StartNodeId = startNodeId;
            EndNodeId = endNodeId;
            Axis = axis;
            DirectionSign = directionSign;
            DistanceMillimetres = distanceMillimetres;
        }

        public string Id { get; }
        public string StartNodeId { get; }
        public string EndNodeId { get; }
        public QuickLineSpatialAxis Axis { get; }
        public int DirectionSign { get; }
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

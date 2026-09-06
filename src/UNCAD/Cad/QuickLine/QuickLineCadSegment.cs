using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace UNCAD.Cad.QuickLine
{
    /// <summary>
    /// A line discovered by the U1LX CAD scanner and its nearest millimetre
    /// annotation.  The object contains value data copied from AutoCAD so it
    /// can be used after the scan transaction has been disposed.
    /// </summary>
    public sealed class QuickLineCadSegment
    {
        public QuickLineCadSegment(ObjectId lineId, Point3d startPoint, Point3d endPoint,
            ObjectId labelId, string labelText, double? labelMillimetres,
            bool hasCompletionMarker = false, string labelKind = "")
        {
            LineId = lineId;
            StartPoint = startPoint;
            EndPoint = endPoint;
            LabelId = labelId;
            LabelText = labelText ?? string.Empty;
            LabelMillimetres = labelMillimetres;
            HasCompletionMarker = hasCompletionMarker;
            LabelKind = labelKind ?? string.Empty;
        }

        public ObjectId LineId { get; }
        public ObjectId Id => LineId;
        public Point3d StartPoint { get; }
        public Point3d Start => StartPoint;
        public Point3d EndPoint { get; }
        public Point3d End => EndPoint;
        public ObjectId LabelId { get; }
        public string LabelText { get; }
        public double? LabelMillimetres { get; }
        public bool HasMillimetreLabel => !LabelId.IsNull && LabelMillimetres.HasValue;
        /// <summary>
        /// True when U1LX has explicitly confirmed this label. Values ending
        /// in 00mm can still be treated as editable placeholders.
        /// </summary>
        public bool HasCompletionMarker { get; }
        /// <summary>CAD source kind used for diagnostics (DBText/MText/Dimension/Attribute).</summary>
        public string LabelKind { get; }
        public Point3d MidPoint => StartPoint + (EndPoint - StartPoint) / 2.0;
        public Point3d Midpoint => MidPoint;
        public double Length => StartPoint.DistanceTo(EndPoint);

        /// <summary>Returns the endpoint nearest the supplied pick point.</summary>
        public bool IsNearStart(Point3d point, double tolerance)
            => point.DistanceTo(StartPoint) <= Math.Max(0.0, tolerance);

        /// <summary>Returns the endpoint nearest the supplied pick point.</summary>
        public bool IsNearEnd(Point3d point, double tolerance)
            => point.DistanceTo(EndPoint) <= Math.Max(0.0, tolerance);
    }
}

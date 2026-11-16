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
            bool hasCompletionMarker = false, string labelKind = "",
            bool isPolyline = false, bool isBridgePolyline = false)
        {
            LineId = lineId;
            StartPoint = startPoint;
            EndPoint = endPoint;
            LabelId = labelId;
            LabelText = labelText ?? string.Empty;
            LabelMillimetres = labelMillimetres;
            HasCompletionMarker = hasCompletionMarker;
            LabelKind = labelKind ?? string.Empty;
            IsPolyline = isPolyline;
            IsBridgePolyline = isBridgePolyline;
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
        /// 3.3.0（D-054）：实体是开放 LWPOLYLINE 而不是 Line（U1R/Xauto 开洞后
        /// 的线是带 bulge 的开放 Polyline；起点=首顶点、终点=末顶点）。
        /// </summary>
        public bool IsPolyline { get; }
        /// <summary>带 bulge 的开洞 Polyline（U1R/Xauto 开洞产物）。</summary>
        public bool IsBridgePolyline { get; }
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

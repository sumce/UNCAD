using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_DUMP_HANDLES（诊断，只读）：按句柄导出实体的真实几何（类型/图层/端点/
    /// 文字定位点、字高、旋转、对齐、外框、U1L 关联元数据），再导出整图扫描结果
    /// （线段与各自归属的毫米标、没有归属的毫米标）。给「现场这个毫米标为什么
    /// 归不到线上」这类问题取证用——不猜，直接看坐标。
    /// 环境变量：UNCAD_DUMP_HANDLES=句柄列表（逗号/空格分隔，可带 0x）；
    /// UNCAD_DUMP_OUT=导出文本路径。始终报 PASS（导不出内容才是问题）。
    /// </summary>
    public sealed class DumpHandlesSelfTestCommand
    {
        [CommandMethod("UNCAD_DUMP_HANDLES")]
        public void Run()
        {
            string result = "PASS";
            try { RunCore(); }
            catch (System.Exception ex) { result = ex.ToString(); }
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, result);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + result + "\n");
        }

        private static void RunCore()
        {
            string handles = Environment.GetEnvironmentVariable("UNCAD_DUMP_HANDLES")
                ?? string.Empty;
            string output = Environment.GetEnvironmentVariable("UNCAD_DUMP_OUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException(
                "FAILED: UNCAD_DUMP_OUT is not set");
            var text = new StringBuilder();
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database db = document.Database;
            var ctx = new CadContext(document);
            text.AppendLine("Drawing: " + document.Name);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (string raw in handles.Split(new[]
                        { ',', ';', ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                {
                    string hex = raw.Trim();
                    if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        hex = hex.Substring(2);
                    long value;
                    if (!long.TryParse(hex, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out value))
                    {
                        text.AppendLine(hex + ": not a handle");
                        continue;
                    }
                    ObjectId id = ObjectId.Null;
                    try { id = db.GetObjectId(false, new Handle(value), 0); }
                    catch { }
                    if (id.IsNull)
                    {
                        text.AppendLine(hex.ToUpperInvariant() + ": not found in this drawing");
                        continue;
                    }
                    DumpEntity(text, tr, id);
                }
                tr.Commit();
            }

            // 与填充/U1U 同一口径（线性标注当线段、带拐角多段线按段拆开）。
            QuickLineScanResult scan = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    IncludeDimensionSegments = true,
                    SplitBentPolylines = true
                });
            text.AppendLine();
            text.AppendLine("== scan: segments " + scan.Segments.Count
                + ", unbound labels " + scan.UnboundLabels.Count);
            foreach (QuickLineCadSegment segment in scan.Segments.Take(80))
            {
                text.AppendLine("seg " + segment.LineId.Handle + " "
                    + Point(segment.StartPoint) + " -> " + Point(segment.EndPoint)
                    + " len=" + segment.Length.ToString("0.##",
                        CultureInfo.InvariantCulture)
                    + " label=[" + segment.LabelText + "]"
                    + (segment.LabelId.IsNull ? ""
                        : " labelHandle=" + segment.LabelId.Handle)
                    + (segment.IsPolyline ? " polyline" : "")
                    + (segment.IsDimension ? " dimension" : ""));
            }
            foreach (QuickLineUnboundLabel label in scan.UnboundLabels.Take(80))
            {
                text.AppendLine("unbound " + label.Handle + " [" + label.Text + "]"
                    + " anchor=" + Point(label.Anchor)
                    + " height=" + label.Height.ToString("0.##", CultureInfo.InvariantCulture)
                    + " rotation=" + label.Rotation.ToString("0.####", CultureInfo.InvariantCulture)
                    + " kind=" + label.SourceKind
                    + (label.AssociationDangling ? " association=dangling" : string.Empty)
                    + " | " + Nearest(scan.Segments, label));
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (QuickLineUnboundLabel label in scan.UnboundLabels.Take(12))
                {
                    Entity entity = tr.GetObject(label.EntityId, OpenMode.ForRead)
                        as Entity;
                    if (entity != null) DumpEntity(text, tr, label.EntityId);
                }
                tr.Commit();
            }
            File.WriteAllText(output, text.ToString(), Encoding.UTF8);
        }

        /// <summary>离这个毫米标最近的两根线段：句柄、有无标注、距文字定位点多远。</summary>
        private static string Nearest(IReadOnlyList<QuickLineCadSegment> segments,
            QuickLineUnboundLabel label)
        {
            var ordered = segments
                .Select(segment => new
                {
                    Segment = segment,
                    Distance = DistanceToSegment(label.Anchor, segment)
                })
                .OrderBy(item => item.Distance)
                .Take(2)
                .ToList();
            if (ordered.Count == 0) return "no segments";
            return "nearest " + string.Join("; ", ordered.Select(item =>
                item.Segment.LineId.Handle
                + (item.Segment.HasMillimetreLabel
                    ? " labelled[" + item.Segment.LabelText + "]" : " unlabelled")
                + (item.Segment.IsPolyline ? " polyline" : "")
                + " d=" + item.Distance.ToString("0.#", CultureInfo.InvariantCulture)));
        }

        private static double DistanceToSegment(
            Autodesk.AutoCAD.Geometry.Point3d point, QuickLineCadSegment segment)
        {
            var start = segment.StartPoint;
            var end = segment.EndPoint;
            var direction = end - start;
            double lengthSquared = direction.DotProduct(direction);
            if (lengthSquared <= 1e-18) return point.DistanceTo(start);
            double parameter = (point - start).DotProduct(direction) / lengthSquared;
            if (parameter < 0.0) parameter = 0.0;
            if (parameter > 1.0) parameter = 1.0;
            return point.DistanceTo(start + direction * parameter);
        }

        private static void DumpEntity(StringBuilder text, Transaction tr,
            ObjectId id)
        {
            Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
            if (entity == null)
            {
                text.AppendLine(id.Handle + ": not an entity");
                return;
            }
            string owner = string.Empty;
            var ownerRecord = tr.GetObject(entity.OwnerId, OpenMode.ForRead)
                as BlockTableRecord;
            if (ownerRecord != null && !ownerRecord.IsLayout) owner = ownerRecord.Name;
            text.AppendLine("-- " + id.Handle + " " + entity.GetType().Name
                + " layer=" + entity.Layer
                + (entity.LayerId.IsNull ? "" : "")
                + " color=" + entity.ColorIndex
                + (owner.Length > 0 ? " inBlock=" + owner : string.Empty));
            try
            {
                Extents3d extents = entity.GeometricExtents;
                text.AppendLine("   extents " + Point(extents.MinPoint) + " .. "
                    + Point(extents.MaxPoint));
            }
            catch
            {
                text.AppendLine("   extents unreadable");
            }
            var line = entity as Line;
            if (line != null)
                text.AppendLine("   line " + Point(line.StartPoint) + " -> "
                    + Point(line.EndPoint));
            var dbText = entity as DBText;
            if (dbText != null)
                text.AppendLine("   dbtext [" + dbText.TextString + "] justify="
                    + dbText.Justify + " position=" + Point(dbText.Position)
                    + " alignment=" + Point(dbText.AlignmentPoint)
                    + " height=" + dbText.Height.ToString("0.##", CultureInfo.InvariantCulture)
                    + " width=" + dbText.WidthFactor.ToString("0.##", CultureInfo.InvariantCulture)
                    + " rotation=" + dbText.Rotation.ToString("0.####", CultureInfo.InvariantCulture)
                    + " normal=" + Point(dbText.Normal));
            var mText = entity as MText;
            if (mText != null)
                text.AppendLine("   mtext [" + mText.Contents + "] location="
                    + Point(mText.Location)
                    + " height=" + mText.TextHeight.ToString("0.##", CultureInfo.InvariantCulture)
                    + " rotation=" + mText.Rotation.ToString("0.####", CultureInfo.InvariantCulture)
                    + " attachment=" + mText.Attachment);
            var attribute = entity as AttributeReference;
            if (attribute != null)
                text.AppendLine("   attribute [" + attribute.TextString + "] tag="
                    + attribute.Tag + " position=" + Point(attribute.Position)
                    + " height=" + attribute.Height.ToString("0.##", CultureInfo.InvariantCulture)
                    + " block=" + BlockName(tr, attribute.OwnerId));
            var dimension = entity as Autodesk.AutoCAD.DatabaseServices.Dimension;
            if (dimension != null)
                text.AppendLine("   dimension [" + dimension.DimensionText + "] textPos="
                    + Point(dimension.TextPosition) + " measurement="
                    + dimension.Measurement.ToString("0.##", CultureInfo.InvariantCulture));
            var curve = entity as Curve;
            if (curve != null && line == null)
            {
                try
                {
                    text.AppendLine("   curve " + Point(curve.StartPoint) + " -> "
                        + Point(curve.EndPoint) + " closed=" + curve.Closed);
                }
                catch { }
            }
            string associated = QuickLineMetadataStore.ReadAssociatedLineHandle(entity);
            if (!string.IsNullOrEmpty(associated))
                text.AppendLine("   U1L associatedLine=" + associated);
        }

        private static string BlockName(Transaction tr, ObjectId ownerId)
        {
            var definition = tr.GetObject(ownerId, OpenMode.ForRead) as BlockReference;
            if (definition == null) return "?";
            var record = tr.GetObject(definition.BlockTableRecord, OpenMode.ForRead)
                as BlockTableRecord;
            return record == null ? "?" : record.Name;
        }

        private static string Point(Autodesk.AutoCAD.Geometry.Point3d point)
            => "(" + point.X.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                + point.Y.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                + point.Z.ToString("0.##", CultureInfo.InvariantCulture) + ")";

        private static string Point(Autodesk.AutoCAD.Geometry.Vector3d vector)
            => "(" + vector.X.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                + vector.Y.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                + vector.Z.ToString("0.##", CultureInfo.InvariantCulture) + ")";
    }
}

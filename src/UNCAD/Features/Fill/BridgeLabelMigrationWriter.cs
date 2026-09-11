using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Features.Fill
{
    /// <summary>Upgrades selected legacy U1Q labels inside the caller's transaction.</summary>
    internal static class BridgeLabelMigrationWriter
    {
        public static int Migrate(Transaction transaction,
            IEnumerable<ObjectId> textIds, double mmPerGrid)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (textIds == null) return 0;

            int changed = 0;
            foreach (ObjectId id in textIds.Distinct())
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                string value = entity is DBText text ? text.TextString
                    : entity is MText mtext ? mtext.Contents : "";
                if (!AnnotationLabelPair.TryUpgradeBridgeLabel(value, mmPerGrid,
                        out string upgraded))
                {
                    if (entity is MText migrated
                        && RepairMigratedRotation(transaction, migrated, mmPerGrid))
                        changed++;
                    continue;
                }

                if (entity is MText existing)
                {
                    existing.UpgradeOpen();
                    existing.Contents = upgraded;
                    existing.Width = 0.0;
                    existing.LineSpacingStyle = LineSpacingStyle.Exactly;
                    existing.LineSpacingFactor = EntityFactory.LabelLineSpacingFactor;
                    changed++;
                }
                else if (entity is DBText legacy
                    && ReplaceDbText(transaction, legacy, upgraded))
                {
                    changed++;
                }
            }
            return changed;
        }

        private static bool RepairMigratedRotation(Transaction transaction, MText text,
            double mmPerGrid)
        {
            string raw = (text.Contents ?? "").Trim();
            if (TextParser.SplitMTextLines(raw).Count < 2) return false;
            string collapsed = AnnotationLabelPair.CollapseText(raw);
            if (string.Equals(collapsed, raw, StringComparison.Ordinal)
                || !TextParser.TryExtractBridgeLabel(collapsed, mmPerGrid,
                    out _, out _)
                || !ParallelAnnotationMetadata.TryReadCurveRotation(transaction, text,
                    "U1Q", text.Location, out double rotation)
                || AngleDifference(text.Rotation, rotation) <= 1e-6) return false;
            text.UpgradeOpen();
            text.Rotation = rotation;
            return true;
        }

        private static double AngleDifference(double first, double second)
        {
            double difference = Math.Abs(first - second) % (Math.PI * 2.0);
            return difference > Math.PI ? Math.PI * 2.0 - difference : difference;
        }

        private static bool ReplaceDbText(Transaction transaction, DBText source,
            string contents)
        {
            var owner = transaction.GetObject(source.OwnerId, OpenMode.ForWrite, false)
                as BlockTableRecord;
            if (owner == null) return false;

            var replacement = new MText();
            replacement.SetDatabaseDefaults(source.Database);
            replacement.SetPropertiesFrom(source);
            replacement.Contents = contents;
            replacement.TextHeight = source.Height;
            replacement.TextStyleId = source.TextStyleId;
            replacement.Normal = source.Normal;
            replacement.Attachment = ToMTextAttachment(source.Justify);
            replacement.Location = source.Justify == AttachmentPoint.BaseLeft
                ? source.Position : source.AlignmentPoint;
            replacement.Width = 0.0;
            replacement.LineSpacingStyle = LineSpacingStyle.Exactly;
            replacement.LineSpacingFactor = EntityFactory.LabelLineSpacingFactor;
            // MText 的 Normal/Attachment setter 会重建文字坐标系，旋转角必须最后写。
            replacement.Rotation = source.Rotation;

            owner.AppendEntity(replacement);
            transaction.AddNewlyCreatedDBObject(replacement, true);
            if (ParallelAnnotationMetadata.TryRead(source,
                    out string kind, out string sourceHandle))
                ParallelAnnotationMetadata.Set(replacement, kind, sourceHandle);
            source.UpgradeOpen();
            source.Erase();
            return true;
        }

        private static AttachmentPoint ToMTextAttachment(AttachmentPoint value)
        {
            switch (value)
            {
                case AttachmentPoint.TopLeft:
                case AttachmentPoint.TopCenter:
                case AttachmentPoint.TopRight:
                case AttachmentPoint.MiddleLeft:
                case AttachmentPoint.MiddleCenter:
                case AttachmentPoint.MiddleRight:
                case AttachmentPoint.BottomLeft:
                case AttachmentPoint.BottomCenter:
                case AttachmentPoint.BottomRight:
                    return value;
                default:
                    return AttachmentPoint.BottomLeft;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Text;

namespace UNCAD.Cad
{
    /// <summary>Result of converting selected aligned-dimension labels inside a caller-owned transaction.</summary>
    public sealed class DimensionTextConversionResult
    {
        internal DimensionTextConversionResult(IReadOnlyList<ObjectId> textIds,
            int alreadyConvertedCount, int skippedCount)
        {
            TextIds = textIds == null ? Array.Empty<ObjectId>() : textIds.ToArray();
            AlreadyConvertedCount = alreadyConvertedCount;
            SkippedCount = skippedCount;
        }

        public IReadOnlyList<ObjectId> TextIds { get; }
        public int ConvertedCount => TextIds.Count;
        public int AlreadyConvertedCount { get; }
        public int SkippedCount { get; }
    }

    /// <summary>Creates DBText at aligned-dimension text positions and suppresses only the original labels.</summary>
    public static class AlignedDimensionTextConverter
    {
        public static DimensionTextConversionResult Convert(CadContext ctx,
            Transaction transaction, IEnumerable<ObjectId> sourceIds, double textHeight)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (sourceIds == null) throw new ArgumentNullException(nameof(sourceIds));
            if (double.IsNaN(textHeight) || double.IsInfinity(textHeight)
                || textHeight <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(textHeight));

            ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, transaction);
            var space = transaction.GetObject(ctx.CurrentSpaceId, OpenMode.ForWrite, false)
                as BlockTableRecord;
            if (space == null)
                throw new InvalidOperationException("无法打开当前绘图空间。");

            var created = new List<ObjectId>();
            int alreadyConverted = 0;
            int skipped = 0;
            foreach (ObjectId id in sourceIds.Distinct())
            {
                if (id.IsNull || !id.IsValid)
                {
                    skipped++;
                    continue;
                }

                var dimension = transaction.GetObject(id, OpenMode.ForRead, true)
                    as AlignedDimension;
                if (dimension == null || dimension.IsErased
                    || dimension.OwnerId != ctx.CurrentSpaceId)
                {
                    skipped++;
                    continue;
                }

                string dimensionText = dimension.DimensionText;
                if (DimensionTextFormatter.IsSuppressed(dimensionText))
                {
                    alreadyConverted++;
                    continue;
                }
                if (!DimensionTextFormatter.TryBuildSingleLine(dimensionText,
                        dimension.Dimpost, dimension.Measurement, out string textValue))
                {
                    skipped++;
                    continue;
                }

                Point3d position = dimension.TextPosition;
                double rotation = dimension.TextRotation;
                if (!IsFinite(position) || double.IsNaN(rotation)
                    || double.IsInfinity(rotation))
                {
                    skipped++;
                    continue;
                }

                var text = EntityFactory.DBText(ctx, textValue, position, textHeight,
                    rotation, AttachmentPoint.MiddleCenter, textStyleId: styleId);
                text.LayerId = dimension.LayerId;
                text.Color = dimension.Color;
                Vector3d normal = dimension.Normal;
                if (IsFinite(normal) && normal.Length > 1e-9) text.Normal = normal;
                space.AppendEntity(text);
                transaction.AddNewlyCreatedDBObject(text, true);
                text.AdjustAlignment(ctx.Db);

                // Keep the user's dimension geometry. Only its original text is
                // suppressed so the new editable DBText is the sole visible label.
                dimension.UpgradeOpen();
                dimension.DimensionText = DimensionTextFormatter.SuppressedDimensionText;
                dimension.RecomputeDimensionBlock(true);
                created.Add(text.Id);
            }
            return new DimensionTextConversionResult(created, alreadyConverted, skipped);
        }

        private static bool IsFinite(Point3d point)
            => IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

        private static bool IsFinite(Vector3d vector)
            => IsFinite(vector.X) && IsFinite(vector.Y) && IsFinite(vector.Z);

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

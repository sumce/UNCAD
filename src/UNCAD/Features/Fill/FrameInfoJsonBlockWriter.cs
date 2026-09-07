using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// Reads and writes the optional frameinfo_json block. It is deliberately an ordinary
    /// attribute-backed block so existing drawings can add it without a plugin migration.
    /// </summary>
    internal static class FrameInfoJsonBlockWriter
    {
        private const string DefinitionName = "frameinfo_json";

        public static FrameInfoJsonRecord Read(CadContext ctx, ObjectId[] blockIds)
        {
            if (ctx == null || blockIds == null || blockIds.Length == 0) return null;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FrameInfoJsonRecord record = Read(transaction, blockIds);
                transaction.Commit();
                return record;
            }
        }

        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds)
        {
            FrameInfoJsonRecord result = null;
            foreach (ObjectId id in blockIds ?? Array.Empty<ObjectId>())
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (!IsJsonHostBlock(transaction, block)) continue;
                List<AttributeReference> attributes = Attributes(transaction, block).ToList();
                foreach (AttributeReference attribute in attributes)
                {
                    if (!IsJsonTag(attribute.Tag) && attributes.Count != 1) continue;
                    FrameInfoJsonRecord record = ParsePayload(AttributeText(attribute), id);
                    if (record != null) result = Merge(result, record, id);
                }
                if (block.IsDynamicBlock)
                {
                    DynamicBlockReferenceProperty[] dynamicProperties = block
                        .DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                        .ToArray();
                    foreach (DynamicBlockReferenceProperty property
                        in dynamicProperties.Where(property => IsJsonTag(property.PropertyName)
                            || (dynamicProperties.Length == 1 && property.Value is string)))
                    {
                        FrameInfoJsonRecord record = ParsePayload(
                            Convert.ToString(property.Value), id);
                        if (record != null) result = Merge(result, record, id);
                    }
                }
            }
            return result;
        }

        internal static FillWriteResult Fill(CadContext ctx, Transaction transaction,
            ObjectId[] blockIds, MachineRow machine, FillReviewData review,
            string commandName)
        {
            if (ctx == null || transaction == null || blockIds == null
                || blockIds.Length == 0 || machine == null) return FillWriteResult.Empty;

            int blocks = 0;
            int values = 0;
            var visited = new HashSet<ObjectId>();
            var targets = new List<ObjectId>();
            foreach (ObjectId id in blockIds)
            {
                if (!visited.Add(id)) continue;
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (!IsJsonHostBlock(transaction, block)) continue;

                // Older drawings may contain a frameinfo_json insert created from a
                // definition that had no attribute reference. Adding the definition alone
                // does not retrofit existing inserts, so create the missing JSON reference
                // before reading/writing the payload.
                if (IsFrameInfoJsonBlock(transaction, block))
                    EnsureJsonAttributeReference(ctx.Db, transaction, block);

                targets.Add(id);
            }

            // A frame can temporarily contain both an embedded and a standalone metadata
            // insert. Read them as one source of truth before writing so a stale second copy
            // cannot silently overwrite a user's confirmed replacement.
            FrameInfoJsonRecord previousRecord = null;
            foreach (ObjectId id in targets)
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                FrameInfoJsonRecord current = ReadBlock(transaction, block);
                if (current != null)
                    previousRecord = Merge(previousRecord, current, id);
            }

            FrameInfoJsonRecord next = FrameInfoJsonRecordUpdater.Update(previousRecord,
                machine, review, commandName, DateTime.UtcNow);
            string payload = FrameInfoJsonCodec.Serialize(next);
            foreach (ObjectId id in targets)
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;

                bool touched = false;
                List<AttributeReference> attributes = Attributes(transaction, block).ToList();
                List<AttributeReference> jsonAttributes = attributes
                    .Where(attribute => IsJsonTag(attribute.Tag)).ToList();
                if (jsonAttributes.Count == 0 && attributes.Count == 1)
                    jsonAttributes = attributes;
                foreach (AttributeReference attribute in jsonAttributes)
                {
                    if (string.Equals(attribute.TextString, payload,
                        StringComparison.Ordinal)) continue;
                    attribute.UpgradeOpen();
                    attribute.TextString = payload;
                    if (attribute.IsMTextAttribute)
                    {
                        using (MText mtext = attribute.MTextAttribute)
                        {
                            if (mtext != null)
                            {
                                mtext.Contents = payload;
                                attribute.MTextAttribute = mtext;
                            }
                        }
                        attribute.UpdateMTextAttribute();
                    }
                    attribute.AdjustAlignment(ctx.Db);
                    values++;
                    touched = true;
                }

                if (!touched && jsonAttributes.Count == 0 && block.IsDynamicBlock)
                {
                    DynamicBlockReferenceProperty[] dynamicProperties = block
                        .DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                        .ToArray();
                    foreach (DynamicBlockReferenceProperty property
                        in dynamicProperties.Where(property => IsJsonTag(property.PropertyName)
                            || (dynamicProperties.Length == 1 && property.Value is string)))
                    {
                        if (property.ReadOnly
                            || !(property.Value is string)) continue;
                        if (string.Equals(Convert.ToString(property.Value), payload,
                            StringComparison.Ordinal))
                        {
                            touched = true;
                            continue;
                        }
                        block.UpgradeOpen();
                        property.Value = payload;
                        values++;
                        touched = true;
                        break;
                    }
                }
                if (touched) blocks++;
                else if (jsonAttributes.Count == 0)
                    ctx.Write("\n[U1F/U1U] frameinfo_json 块没有可写入的 JSON 属性，已跳过。");
            }
            return new FillWriteResult(blocks, values);
        }

        /// <summary>
        /// One sweep over the current space listing every standalone frameinfo_json insert and
        /// its position. Scanning once per batch and sharing the result replaces the previous
        /// per-frame O(entities) scan inside the write transaction.
        /// </summary>
        internal sealed class MetadataBlockIndex
        {
            private readonly List<KeyValuePair<ObjectId, Point3d>> _blocks;

            private MetadataBlockIndex(List<KeyValuePair<ObjectId, Point3d>> blocks)
            {
                _blocks = blocks;
            }

            internal static MetadataBlockIndex Scan(CadContext ctx,
                Transaction transaction)
            {
                var blocks = new List<KeyValuePair<ObjectId, Point3d>>();
                BlockTableRecord space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                    OpenMode.ForRead, false) as BlockTableRecord;
                if (space == null) return new MetadataBlockIndex(blocks);
                foreach (ObjectId id in space)
                {
                    BlockReference candidate = transaction.GetObject(id,
                        OpenMode.ForRead, true) as BlockReference;
                    if (!IsFrameInfoJsonBlock(transaction, candidate)) continue;
                    blocks.Add(new KeyValuePair<ObjectId, Point3d>(id,
                        candidate.Position));
                }
                return new MetadataBlockIndex(blocks);
            }

            internal ObjectId FindNear(Extents3d extents)
            {
                ObjectId found = ObjectId.Null;
                foreach (KeyValuePair<ObjectId, Point3d> block in _blocks)
                {
                    Point3d p = block.Value;
                    if (p.X >= extents.MinPoint.X && p.X <= extents.MaxPoint.X
                        && p.Y >= extents.MinPoint.Y && p.Y <= extents.MaxPoint.Y)
                    {
                        if (!found.IsNull)
                            throw new InvalidDataException(
                                "同一图框范围内存在多个 frameinfo_json 块（"
                                + found.Handle + "、" + block.Key.Handle
                                + "），请只保留一个后重试。");
                        found = block.Key;
                    }
                }
                return found;
            }
        }

        internal static FillWriteResult FillOrMigrate(CadContext ctx, Transaction transaction,
            ObjectId[] frameBlockIds, ObjectId[] blockIds, MachineRow machine,
            FillReviewData review, string commandName)
            => FillOrMigrate(ctx, transaction, frameBlockIds, blockIds, machine,
                review, commandName, null);

        internal static FillWriteResult FillOrMigrate(CadContext ctx, Transaction transaction,
            ObjectId[] frameBlockIds, ObjectId[] blockIds, MachineRow machine,
            FillReviewData review, string commandName,
            MetadataBlockIndex sharedIndex)
        {
            var targets = new List<ObjectId>(blockIds ?? Array.Empty<ObjectId>());
            var seen = new HashSet<ObjectId>(targets);
            foreach (ObjectId frameId in frameBlockIds ?? Array.Empty<ObjectId>())
            {
                if (frameId.IsNull || !seen.Add(frameId)) continue;
                BlockReference frame = transaction.GetObject(frameId, OpenMode.ForRead, true)
                    as BlockReference;
                if (frame == null) continue;

                // Prefer an existing standalone metadata insert so legacy drawings that
                // already carry one do not gain a second JSON source of truth.
                ObjectId existing = FindExistingNearFrame(ctx, transaction, frame,
                    sharedIndex ?? MetadataBlockIndex.Scan(ctx, transaction));
                if (!existing.IsNull)
                {
                    targets.Add(existing);
                    continue;
                }

                // Newer frame definitions store JSON as an invisible attribute directly on
                // frame_20260812. Reuse it when present; otherwise retrofit the legacy frame
                // definition and selected insert before falling back to a standalone block.
                if (HasJsonAttribute(transaction, frame)
                    || EnsureFrameJsonAttribute(ctx.Db, transaction, frame))
                {
                    targets.Add(frameId);
                    continue;
                }
                ObjectId created = CreateMetadataBlock(ctx, transaction, frame);
                if (!created.IsNull) targets.Add(created);
            }
            return Fill(ctx, transaction, targets.ToArray(), machine, review, commandName);
        }

        internal static bool IsFrameInfoJsonBlock(Transaction transaction,
            BlockReference block)
        {
            return Definitions(transaction, block).Any(definition => string.Equals(
                BlockNameNormalizer.RemoveMangledSuffix(definition.Name), DefinitionName,
                StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsJsonHostBlock(Transaction transaction,
            BlockReference block)
            => IsFrameInfoJsonBlock(transaction, block) || HasJsonAttribute(transaction, block);

        private static bool HasJsonAttribute(Transaction transaction, BlockReference block)
            => block != null && Attributes(transaction, block)
                .Any(attribute => IsJsonTag(attribute.Tag));

        private static FrameInfoJsonRecord ReadBlock(Transaction transaction,
            BlockReference block)
        {
            FrameInfoJsonRecord result = null;
            List<AttributeReference> attributes = Attributes(transaction, block).ToList();
            foreach (AttributeReference attribute in attributes)
            {
                if (!IsJsonTag(attribute.Tag)
                    && block.AttributeCollection.Count != 1) continue;
                FrameInfoJsonRecord record = ParsePayload(AttributeText(attribute),
                    block.ObjectId);
                if (record != null) result = Merge(result, record, block.ObjectId);
            }
            if (block.IsDynamicBlock)
            {
                DynamicBlockReferenceProperty[] properties = block
                    .DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                    .ToArray();
                foreach (DynamicBlockReferenceProperty property in properties.Where(property =>
                    IsJsonTag(property.PropertyName)
                    || (properties.Length == 1 && property.Value is string)))
                {
                    FrameInfoJsonRecord record = ParsePayload(
                        Convert.ToString(property.Value), block.ObjectId);
                    if (record != null) result = Merge(result, record, block.ObjectId);
                }
            }
            return result;
        }

        private static FrameInfoJsonRecord ParsePayload(string payload, ObjectId id)
        {
            string text = (payload ?? "").Trim();
            if (text.Length == 0) return null;
            FrameInfoJsonRecord record = FrameInfoJsonCodec.Parse(text);
            if (record != null) return record;
            if (text.StartsWith("{", StringComparison.Ordinal))
                throw new InvalidDataException("frameinfo_json 块 " + id.Handle
                    + " 包含无效 JSON，已停止读取以避免覆盖历史替代型号。");
            return null;
        }

        private static FrameInfoJsonRecord Merge(FrameInfoJsonRecord current,
            FrameInfoJsonRecord next, ObjectId id)
        {
            if (current == null) return next;
            if (!string.Equals(FrameInfoJsonCodec.Serialize(current),
                FrameInfoJsonCodec.Serialize(next), StringComparison.Ordinal))
                throw new InvalidDataException("图框中存在多个不一致的 frameinfo_json 记录（块 "
                    + id.Handle + "）。请只保留一个有效记录后重试。");
            return current;
        }

        private static bool EnsureFrameJsonAttribute(Database database,
            Transaction transaction, BlockReference frame)
        {
            if (frame == null || HasJsonAttribute(transaction, frame)) return true;

            List<BlockTableRecord> definitions = Definitions(transaction, frame).ToList();
            if (definitions.Count == 0) return false;
            // Add new attributes to the named definition when possible so all dynamic states
            // inherit the metadata; read an existing attribute from either definition.
            BlockTableRecord definition = definitions.Last();
            AttributeDefinition jsonDefinition = null;
            foreach (BlockTableRecord candidateDefinition in definitions)
            {
                foreach (ObjectId entityId in candidateDefinition)
                {
                    AttributeDefinition candidate = transaction.GetObject(entityId,
                        OpenMode.ForRead, true) as AttributeDefinition;
                    if (candidate != null && !candidate.Constant && IsJsonTag(candidate.Tag))
                    {
                        jsonDefinition = candidate;
                        break;
                    }
                }
                if (jsonDefinition != null) break;
            }
            try
            {
                if (jsonDefinition == null)
                {
                    definition.UpgradeOpen();
                    jsonDefinition = new AttributeDefinition
                    {
                        Tag = "FRAMEINFO_JSON",
                        Prompt = "frameinfo_json",
                        TextString = "",
                        Position = Point3d.Origin,
                        Invisible = true,
                        Constant = false,
                        Preset = true
                    };
                    jsonDefinition.SetDatabaseDefaults(database);
                    definition.AppendEntity(jsonDefinition);
                    transaction.AddNewlyCreatedDBObject(jsonDefinition, true);
                }

                frame.UpgradeOpen();
                var reference = new AttributeReference();
                reference.SetDatabaseDefaults(database);
                reference.SetAttributeFromBlock(jsonDefinition, frame.BlockTransform);
                reference.TextString = "";
                frame.AttributeCollection.AppendAttribute(reference);
                transaction.AddNewlyCreatedDBObject(reference, true);
                return true;
            }
            catch
            {
                // Some legacy frames are read-only/xref-backed. FillOrMigrate will use a
                // standalone metadata block in that case rather than changing the frame.
                return false;
            }
        }

        private static ObjectId FindExistingNearFrame(CadContext ctx,
            Transaction transaction, BlockReference frame, MetadataBlockIndex index)
        {
            try
            {
                Extents3d extents = frame.GeometricExtents;
                return index.FindNear(extents);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch
            {
                // A malformed legacy frame should still be migratable; insertion falls back
                // to its block position when extents cannot be read.
            }
            return ObjectId.Null;
        }

        private static ObjectId CreateMetadataBlock(CadContext ctx,
            Transaction transaction, BlockReference frame)
        {
            Database database = ctx.Db;
            BlockTable table = transaction.GetObject(database.BlockTableId,
                OpenMode.ForRead) as BlockTable;
            if (table == null) return ObjectId.Null;

            ObjectId definitionId;
            if (table.Has(DefinitionName))
            {
                definitionId = table[DefinitionName];
                BlockTableRecord existing = transaction.GetObject(definitionId,
                    OpenMode.ForRead, true) as BlockTableRecord;
                EnsureJsonAttributeDefinition(database, transaction, existing);
            }
            else
            {
                table.UpgradeOpen();
                var definition = new BlockTableRecord { Name = DefinitionName };
                definitionId = table.Add(definition);
                transaction.AddNewlyCreatedDBObject(definition, true);
                AddJsonAttributeDefinition(database, transaction, definition);
            }

            Point3d position = frame.Position;
            try
            {
                Extents3d extents = frame.GeometricExtents;
                position = new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2d,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2d,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2d);
            }
            catch { }

            BlockTableRecord space = transaction.GetObject(database.CurrentSpaceId,
                OpenMode.ForWrite) as BlockTableRecord;
            if (space == null) return ObjectId.Null;
            var reference = new BlockReference(position, definitionId);
            reference.SetDatabaseDefaults(database);
            reference.Layer = "0";
            space.AppendEntity(reference);
            transaction.AddNewlyCreatedDBObject(reference, true);

            BlockTableRecord definitionRecord = transaction.GetObject(definitionId,
                OpenMode.ForRead, true) as BlockTableRecord;
            if (definitionRecord == null) return reference.ObjectId;
            foreach (ObjectId entityId in definitionRecord)
            {
                AttributeDefinition definition = transaction.GetObject(entityId,
                    OpenMode.ForRead, true) as AttributeDefinition;
                if (definition == null || definition.Constant) continue;
                var attribute = new AttributeReference();
                attribute.SetDatabaseDefaults(database);
                attribute.SetAttributeFromBlock(definition, reference.BlockTransform);
                attribute.TextString = "";
                reference.AttributeCollection.AppendAttribute(attribute);
                transaction.AddNewlyCreatedDBObject(attribute, true);
            }
            return reference.ObjectId;
        }

        private static void EnsureJsonAttributeDefinition(Database database,
            Transaction transaction, BlockTableRecord definition)
        {
            if (definition == null) return;
            foreach (ObjectId entityId in definition)
            {
                AttributeDefinition attribute = transaction.GetObject(entityId,
                    OpenMode.ForRead, true) as AttributeDefinition;
                if (attribute != null && !attribute.Constant && IsJsonTag(attribute.Tag)) return;
            }
            AddJsonAttributeDefinition(database, transaction, definition);
        }

        private static void AddJsonAttributeDefinition(Database database,
            Transaction transaction, BlockTableRecord definition)
        {
            definition.UpgradeOpen();
            var attribute = new AttributeDefinition
            {
                Tag = "JSON",
                Prompt = "frameinfo_json",
                TextString = "",
                Position = Point3d.Origin,
                Invisible = true,
                Constant = false,
                Preset = true
            };
            attribute.SetDatabaseDefaults(database);
            definition.AppendEntity(attribute);
            transaction.AddNewlyCreatedDBObject(attribute, true);
        }

        private static bool EnsureJsonAttributeReference(Database database,
            Transaction transaction, BlockReference block)
        {
            if (block == null || !IsFrameInfoJsonBlock(transaction, block)) return false;
            if (Attributes(transaction, block).Any(attribute => IsJsonTag(attribute.Tag)))
                return true;

            AttributeDefinition jsonDefinition = null;
            foreach (BlockTableRecord definition in Definitions(transaction, block))
            {
                foreach (ObjectId entityId in definition)
                {
                    AttributeDefinition candidate = transaction.GetObject(entityId,
                        OpenMode.ForRead, true) as AttributeDefinition;
                    if (candidate != null && !candidate.Constant && IsJsonTag(candidate.Tag))
                    {
                        jsonDefinition = candidate;
                        break;
                    }
                }
                if (jsonDefinition != null) break;
            }
            if (jsonDefinition == null) return false;

            block.UpgradeOpen();
            var reference = new AttributeReference();
            reference.SetDatabaseDefaults(database);
            reference.SetAttributeFromBlock(jsonDefinition, block.BlockTransform);
            reference.TextString = "";
            block.AttributeCollection.AppendAttribute(reference);
            transaction.AddNewlyCreatedDBObject(reference, true);
            return true;
        }

        private static IEnumerable<BlockTableRecord> Definitions(Transaction transaction,
            BlockReference block)
        {
            if (transaction == null || block == null) yield break;
            foreach (ObjectId definitionId in FrameRegionCollector.DefinitionIds(block))
            {
                BlockTableRecord definition;
                try
                {
                    definition = transaction.GetObject(definitionId,
                        OpenMode.ForRead, true) as BlockTableRecord;
                }
                catch
                {
                    continue;
                }
                if (definition != null) yield return definition;
            }
        }

        private static string AttributeText(AttributeReference attribute)
        {
            if (attribute == null) return "";
            if (!attribute.IsMTextAttribute) return attribute.TextString ?? "";
            using (MText mtext = attribute.MTextAttribute)
                return mtext?.Contents ?? attribute.TextString ?? "";
        }

        private static bool IsJsonTag(string tag)
            => (tag ?? "").IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0;

        private static IEnumerable<AttributeReference> Attributes(Transaction transaction,
            BlockReference block)
        {
            if (block?.AttributeCollection == null) yield break;
            foreach (ObjectId id in block.AttributeCollection)
            {
                AttributeReference attribute = transaction.GetObject(id,
                    OpenMode.ForRead, true) as AttributeReference;
                if (attribute != null) yield return attribute;
            }
        }
    }
}

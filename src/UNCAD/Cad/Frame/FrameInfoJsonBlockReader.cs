using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Fill;

namespace UNCAD.Cad.Frame
{
    /// <summary>
    /// 只读读取图框内的 frameinfo_json（D-040 主存储为图框块 FRAMEINFO 属性，
    /// 独立 frameinfo_json 块和旧内嵌 FRAMEINFO_JSON 为兼容读源），并执行
    /// 句柄归属校验与 D-052-C 搬场重绑判定。下沉 Cad/Frame 供读写两侧共享。
    /// </summary>
    internal static class FrameInfoJsonBlockReader
    {
        internal const string DefinitionName = "frameinfo_json";
        internal const string FrameInfoTag = "FRAMEINFO";

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

        internal static ObjectId[] ReadSources(FillSelection selection)
        {
            if (selection == null) return Array.Empty<ObjectId>();
            return (selection.FrameInfoJsonBlockIds ?? Array.Empty<ObjectId>())
                .Concat(selection.FrameBlockIds ?? Array.Empty<ObjectId>())
                .Where(id => !id.IsNull)
                .Distinct()
                .ToArray();
        }

        internal static string OwnFrameHandle(Transaction transaction,
            ObjectId[] frameBlockIds)
        {
            ObjectId only = ObjectId.Null;
            int count = 0;
            foreach (ObjectId id in frameBlockIds ?? Array.Empty<ObjectId>())
            {
                if (id.IsNull) continue;
                count++;
                if (count == 1) only = id;
            }
            if (count != 1) return "";
            BlockReference frame = transaction.GetObject(only,
                OpenMode.ForRead, true) as BlockReference;
            return frame?.Handle.ToString() ?? "";
        }

        internal static FrameInfoJsonRecord ReadOwn(CadContext ctx, FillSelection selection)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FillSelectionCollector.TryReadPhysicalIdentity(transaction, selection,
                    out ExistingFillIdentity physicalIdentity, out _, true);
                FrameInfoJsonRecord record = Read(transaction,
                    ReadSources(selection),
                    OwnFrameHandle(transaction, selection?.FrameBlockIds),
                    physicalIdentity, out _, out _);
                transaction.Commit();
                return record;
            }
        }

        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds)
            => Read(transaction, blockIds, null, out _);

        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds, string expectedFrameHandle)
            => Read(transaction, blockIds, expectedFrameHandle, out _);

        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds, string expectedFrameHandle, out string foreignJsonNote)
            => Read(transaction, blockIds, expectedFrameHandle, null,
                out foreignJsonNote, out _);

        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds, string expectedFrameHandle,
            ExistingFillIdentity expectedIdentity,
            out string foreignJsonNote, out string movedJsonNote)
        {
            foreignJsonNote = "";
            movedJsonNote = "";
            FrameInfoJsonRecord result = null;
            foreach (ObjectId id in blockIds ?? Array.Empty<ObjectId>())
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;
                if (!IsJsonHostBlock(transaction, block)) continue;
                List<AttributeReference> attributes = Attributes(transaction, block).ToList();
                foreach (AttributeReference attribute in attributes)
                {
                    if (!IsJsonTag(attribute.Tag) && attributes.Count != 1) continue;
                    FrameInfoJsonRecord record = ParsePayload(AttributeText(attribute), id);
                    if (record == null) continue;
                    if (!AcceptRecord(transaction, record, expectedFrameHandle,
                        expectedIdentity, id, ref foreignJsonNote, ref movedJsonNote))
                        continue;
                    result = Merge(result, record);
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
                        if (record == null) continue;
                        if (!AcceptRecord(transaction, record, expectedFrameHandle,
                            expectedIdentity, id, ref foreignJsonNote, ref movedJsonNote))
                            continue;
                        result = Merge(result, record);
                    }
                }
            }
            return result;
        }

        private static bool AcceptRecord(Transaction transaction,
            FrameInfoJsonRecord record, string expectedFrameHandle,
            ExistingFillIdentity expectedIdentity, ObjectId id,
            ref string foreignJsonNote, ref string movedJsonNote)
        {
            if (!record.IsForeignTo(expectedFrameHandle)) return true;
            if (FrameMetadataRebindPolicy.IsMovedFrame(record,
                    expectedIdentity?.MachineId ?? "", expectedIdentity?.DeviceName ?? "",
                    expectedIdentity?.UpstreamId ?? "",
                    handle => HandleAliveInDatabase(transaction, id, handle, record)))
            {
                movedJsonNote = "图框 " + expectedFrameHandle
                    + " 携带的 frameinfo_json 记录属于同一机台（"
                    + record.MachineId + " / " + record.DeviceName
                    + "）且原句柄已不在本图——判定为整机台搬场（跨文件复制/移动），"
                    + "已收下其设备内预留/替代型号/更新历史；本次写入时将盖成本框句柄。";
                return true;
            }
            foreignJsonNote = "图框 " + expectedFrameHandle
                + " 范围内发现归属不符的 frameinfo_json 块（块 "
                + id.Handle + "，机台 " + record.MachineId
                + "），已忽略——多为图框移动后遗留，建议删除该块。";
            return false;
        }

        private static bool HandleAliveInDatabase(Transaction transaction,
            ObjectId sourceId, string handleText, FrameInfoJsonRecord record)
        {
            if (!long.TryParse((handleText ?? "").Trim(), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value) || value <= 0)
                return true;
            try
            {
                if (!sourceId.Database.TryGetObjectId(new Handle(value),
                        out ObjectId found))
                    return false;
                if (found == ObjectId.Null || found.IsErased) return false;
                if (!(transaction.GetObject(found, OpenMode.ForRead, true)
                        is BlockReference candidate))
                    return false;
                if (!LooksLikeFrame(transaction, candidate)) return false;
                return !ClearlyDifferentFrameIdentity(transaction, candidate, record);
            }
            catch
            {
                return true;
            }
        }

        private static bool ClearlyDifferentFrameIdentity(Transaction transaction,
            BlockReference candidate, FrameInfoJsonRecord record)
        {
            if (candidate == null || record == null) return false;
            FrameInfoJsonRecord candidateJson = ReadBlock(transaction, candidate);
            if (candidateJson != null && FrameMetadataRebindPolicy.IsDifferentFrameIdentity(
                    record, candidateJson.MachineId, candidateJson.DeviceName, candidateJson.Fr))
                return true;
            if (FillSelectionCollector.TryReadPhysicalIdentity(transaction,
                    new FillSelection { FrameBlockIds = new[] { candidate.ObjectId } },
                    out ExistingFillIdentity physical, out _, false)
                && FrameMetadataRebindPolicy.IsDifferentFrameIdentity(record,
                    physical.MachineId, physical.DeviceName, physical.UpstreamId))
                return true;
            return false;
        }

        private static bool LooksLikeFrame(Transaction transaction, BlockReference block)
        {
            if (block.AttributeCollection == null) return false;
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                if (!(transaction.GetObject(attributeId, OpenMode.ForRead, true)
                        is AttributeReference attribute)) continue;
                string tag = attribute.Tag ?? "";
                if (IsJsonTagName(tag)
                    || string.Equals(tag, FrameBlockFiller.TagCable,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, FrameBlockFiller.TagConduit,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, FrameBlockFiller.TagDevice,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, FrameBlockFiller.TagPower,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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

        internal static bool HasJsonAttribute(Transaction transaction, BlockReference block)
            => block != null && Attributes(transaction, block)
                .Any(attribute => IsJsonTag(attribute.Tag));

        internal static bool HasLegacyJsonAttribute(Transaction transaction, BlockReference block)
            => block != null && Attributes(transaction, block)
                .Any(attribute => (attribute.Tag ?? "")
                    .IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0);

        internal static FrameInfoJsonRecord ReadBlock(Transaction transaction,
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
                if (record != null) result = Merge(result, record);
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
                    if (record != null) result = Merge(result, record);
                }
            }
            return result;
        }

        internal static FrameInfoJsonRecord ParsePayload(string payload, ObjectId id)
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

        internal static FrameInfoJsonRecord Merge(FrameInfoJsonRecord current,
            FrameInfoJsonRecord next)
        {
            if (current == null) return next;
            if (next == null) return current;
            if (string.Equals(FrameInfoJsonCodec.Serialize(current),
                FrameInfoJsonCodec.Serialize(next), StringComparison.Ordinal))
                return current;

            bool currentParsed = DateTimeOffset.TryParse(current.LastModifiedUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out DateTimeOffset currentTime);
            bool nextParsed = DateTimeOffset.TryParse(next.LastModifiedUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out DateTimeOffset nextTime);
            if (nextParsed && (!currentParsed || nextTime > currentTime)) return next;
            return current;
        }

        internal static IEnumerable<BlockTableRecord> Definitions(Transaction transaction,
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

        internal static string AttributeText(AttributeReference attribute)
        {
            if (attribute == null) return "";
            if (!attribute.IsMTextAttribute) return attribute.TextString ?? "";
            using (MText mtext = attribute.MTextAttribute)
                return mtext?.Contents ?? attribute.TextString ?? "";
        }

        internal static bool IsJsonTagName(string tag)
            => IsJsonTag(tag);

        internal static bool IsJsonTag(string tag)
        {
            string value = tag ?? "";
            return value.IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(value, FrameInfoTag, StringComparison.OrdinalIgnoreCase);
        }

        internal static IEnumerable<AttributeReference> Attributes(Transaction transaction,
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

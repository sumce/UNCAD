using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Infra;

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

        /// <summary>
        /// 读取 frameinfo_json 时要一起看的块：独立 JSON 块 + 图框块本身（旧图框内嵌
        /// FRAMEINFO_JSON）。XFrameMigrationService 在写入事务里会把这些旧属性从图框块上
        /// 移除，所以调用方**必须在迁移前**用本方法读出 previous 记录，否则旧图框的
        /// 身份/替代型号/预留/历史会在首次 U1F/U1U 时丢失。
        /// </summary>
        internal static ObjectId[] ReadSources(FillSelection selection)
        {
            if (selection == null) return Array.Empty<ObjectId>();
            return (selection.FrameInfoJsonBlockIds ?? Array.Empty<ObjectId>())
                .Concat(selection.FrameBlockIds ?? Array.Empty<ObjectId>())
                .Where(id => !id.IsNull)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// 单框语义的“本图框句柄”（与写入盖戳 frame.Handle.ToString() 同源）：
        /// 恰好一个非空图框块时返回其句柄；0 个或多个返回空串——IsForeignTo
        /// 对空期望句柄放行（老 JSON 兼容路径），因此多框场景不会误拒。
        /// 2026-11-04 用户报告：复制图框会把源框的 JSON 一起带过来，所有
        /// 决策读取（身份、设备内预留、替代型号、母线箱选择、更新历史）
        /// 必须带句柄校验，否则复制框会静默继承源框的用户选择。
        /// </summary>
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

        /// <summary>
        /// ctx 版“只读本图框自己的记录”（自动开短读事务 + 句柄校验）：
        /// 复制图框带来的源框 JSON 被拒绝后返回 null，调用方走各自的
        /// 物理兜底（现有表格/标签），绝不用别的图框的用户选择。
        /// </summary>
        internal static FrameInfoJsonRecord ReadOwn(CadContext ctx, FillSelection selection)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FrameInfoJsonRecord record = Read(transaction,
                    ReadSources(selection),
                    OwnFrameHandle(transaction, selection?.FrameBlockIds));
                transaction.Commit();
                return record;
            }
        }

        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds)
            => Read(transaction, blockIds, null, out _);

        /// <summary>带句柄校验、不需要外来说明的读取（决策读取点用）。</summary>
        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds, string expectedFrameHandle)
            => Read(transaction, blockIds, expectedFrameHandle, out _);

        /// <summary>
        /// 带图框句柄校验的读取：JSON 记录里带 frameHandle 且与当前图框不符时
        /// 视为外块（图框移动后遗留/几何归属认错），跳过并在 foreignJsonNote 里
        /// 说明，调用方可提示用户清理。老 JSON 无句柄 → 不校验（兼容旧图）。
        /// </summary>
        internal static FrameInfoJsonRecord Read(Transaction transaction,
            ObjectId[] blockIds, string expectedFrameHandle, out string foreignJsonNote)
            => Read(transaction, blockIds, expectedFrameHandle, null,
                out foreignJsonNote, out _);

        /// <summary>
        /// D-052（2026-11-10）身份判别版读取：句柄失配时先走搬场重绑判定——
        /// 记录身份与本框当前身份一致且原句柄已不在本图（跨文件复制/移动整图框）
        /// → 判定整机台搬场，收下记录并在 movedJsonNote 说明（不算外来，不触发
        /// 复制框清理）；否则维持外来拒绝（foreignJsonNote）。老 JSON 无句柄 →
        /// 不校验（兼容旧图）。
        /// </summary>
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
                    result = Merge(result, record, id);
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
                        result = Merge(result, record, id);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 句柄校验 + 搬场重绑的统一闸门：句柄一致（或老记录无句柄）→ 收下；
        /// 句柄失配 → 先判搬场（身份一致且原句柄已不在本图 → 收下并记
        /// movedJsonNote），否则外来拒绝（foreignJsonNote，语义与 3.0.0.6 相同）。
        /// </summary>
        private static bool AcceptRecord(Transaction transaction,
            FrameInfoJsonRecord record, string expectedFrameHandle,
            ExistingFillIdentity expectedIdentity, ObjectId id,
            ref string foreignJsonNote, ref string movedJsonNote)
        {
            if (!record.IsForeignTo(expectedFrameHandle)) return true;
            if (FrameMetadataRebindPolicy.IsMovedFrame(record,
                    expectedIdentity?.MachineId ?? "", expectedIdentity?.DeviceName ?? "",
                    handle => HandleAliveInDatabase(transaction, id, handle)))
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

        /// <summary>句柄字符串是否仍指向本图一个未删除的**图框块**（解析失败按存在处理）。
        /// 3.0.0.14 收紧：句柄号回答的是“本图还有没有源框”，不能拿“任意同号实体”
        /// 当源框还在——跨文件复制后，记录里的原句柄极易与目标图的无关实体（图层/
        /// 线型等表记录、直线、文字）撞号，撞号会被误判成同文件复制，搬场记录被拒、
        /// 图框被当复制框清理。只有句柄仍指向携带图框属性（FRAMEINFO/CABLE_INFO/
        /// CONDUIT_INFO/MACHINEID-*）的块参照才算源框还在。</summary>
        private static bool HandleAliveInDatabase(Transaction transaction,
            ObjectId sourceId, string handleText)
        {
            if (!long.TryParse((handleText ?? "").Trim(), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value) || value <= 0)
                return true; // 解析不出来宁可保守：不判搬场，交回外来语义。
            try
            {
                if (!sourceId.Database.TryGetObjectId(new Handle(value),
                        out ObjectId found))
                    return true;
                if (found == ObjectId.Null || found.IsErased) return false;
                return transaction.GetObject(found, OpenMode.ForRead, true)
                    is BlockReference candidate && LooksLikeFrame(transaction, candidate);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>块参照是否仍带着图框身份属性（句柄撞号防御用）。
        /// 同文件复制出的复制框与源框都带这些属性；无关块参照（设备块/图签等）
        /// 不带——撞上它们不代表源框还在。</summary>
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
            MetadataBlockIndex sharedIndex, CableReserveInfo reserve = null,
            FrameInfoJsonRecord previousRecord = null,
            bool inheritLegacyStandaloneRecord = true)
        {
            // D-040（2026-11-02，用户模板）：JSON 主存储 = 图框块内的 FRAMEINFO
            // 文字属性（FRAME_20261102 模板自带）。元数据跟着图框实体走，复制/
            // 移动零丢失，几何“就近归属”的整类问题就此消失。独立 frameinfo_json
            // 块与旧内嵌 FRAMEINFO_JSON 属性降级为遗留读源：读取照常，写入后
            // 清空属于本框的遗留副本。
            var legacyJsonBlocks = new List<ObjectId>();
            var seen = new HashSet<ObjectId>();
            foreach (ObjectId id in blockIds ?? Array.Empty<ObjectId>())
            {
                if (id.IsNull || !seen.Add(id)) continue;
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block != null && IsFrameInfoJsonBlock(transaction, block))
                    legacyJsonBlocks.Add(id);
            }

            var legacyFrames = new List<ObjectId>();
            var frames = new List<ObjectId>();
            FrameInfoJsonRecord previous = previousRecord;
            string owningFrameHandle = "";
            foreach (ObjectId frameId in frameBlockIds ?? Array.Empty<ObjectId>())
            {
                if (frameId.IsNull) continue;
                BlockReference frame = transaction.GetObject(frameId, OpenMode.ForRead, true)
                    as BlockReference;
                if (frame == null) continue;
                frames.Add(frameId);
                if (owningFrameHandle.Length == 0) owningFrameHandle = frame.Handle.ToString();

                if (HasJsonAttribute(transaction, frame))
                {
                    // 块内 JSON 属性（新 FRAMEINFO 或旧 FRAMEINFO_JSON）是首选档案源。
                    FrameInfoJsonRecord embedded = ReadBlock(transaction, frame);
                    // 继承担卫：不得是他机遗留；3.2.0.6：U1F(inherit=false)不合并内嵌旧记录
                    //(无时间戳会话记录在 Merge 恒败给旧记录，旧预留从写侧暗道复活)。
                    if (embedded != null && embedded.BelongsToMachine(machine.MachineId) && inheritLegacyStandaloneRecord) previous = Merge(previous, embedded, frameId);
                    // 只有旧式 FRAMEINFO_JSON 属性需要迁移后清空；FRAMEINFO 是主存储。
                    if (HasLegacyJsonAttribute(transaction, frame))
                        legacyFrames.Add(frameId);
                }

                // 就近的遗留独立块（几何找块只用于识别历史遗留）：属于本框的
                // 稍后清空；绑定在别的图框上的不动——那是别人的数据。
                ObjectId existing = FindExistingNearFrame(ctx, transaction, frame,
                    sharedIndex ?? MetadataBlockIndex.Scan(ctx, transaction));
                if (!existing.IsNull && seen.Add(existing))
                    legacyJsonBlocks.Add(existing);
            }

            // 3.2.0.5（D-052 附录7 贯彻）：U1F=重新定义时不继承遗留独立块记录
            // （"清理图框旧信息(所有的旧信息)"——否则老图纸的独立 frameinfo_json
            // 块会把旧替代型号/预留/历史从这条写侧暗道复活回来）。U1U 保持继承
            // （D-040 迁移语义不变）。
            if (previous == null && inheritLegacyStandaloneRecord)
            {
                foreach (ObjectId id in legacyJsonBlocks)
                {
                    BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                    FrameInfoJsonRecord record = block == null ? null : ReadBlock(transaction, block);
                    // 继承担卫：遗留独立块上的既有 JSON 不得是他机遗留（见 Fill 内注释）。
                    if (record != null && record.BelongsToMachine(machine.MachineId))
                        previous = Merge(previous, record, id);
                }
            }

            // 入参 previousRecord 可能来自调用方的未过滤读取：他机遗留记录
            // 不得继承（身份仲裁选对了机台，型号/预留/历史也不能被前任污染）。
            if (previous != null && !previous.BelongsToMachine(machine.MachineId))
            {
                Log.Warn("丢弃他机遗留 frameinfo_json（记录机台 "
                    + previous.MachineId + "，本次填表机台 "
                    + machine.MachineId + "），不继承其型号/预留/历史。");
                previous = null;
            }

            FrameInfoJsonRecord next = FrameInfoJsonRecordUpdater.Update(previous,
                machine, review, commandName, DateTime.UtcNow, null, reserve);
            // 盖上所属图框句柄（meta.frameHandle）：遗留独立块的读取路径仍按句柄
            // 拒绝几何错误归属的外块（幽灵机台修复，2026-11-02）。
            string stampedHandle = (owningFrameHandle ?? "").Trim();
            if (stampedHandle.Length > 0) next.FrameHandle = stampedHandle;
            string payload = FrameInfoJsonCodec.Serialize(next);

            int blocks = 0, values = 0;
            foreach (ObjectId frameId in frames)
            {
                BlockReference frame = transaction.GetObject(frameId, OpenMode.ForRead, true)
                    as BlockReference;
                if (frame == null) continue;
                if (WriteFrameJsonAttribute(ctx, transaction, frame, payload) > 0)
                {
                    blocks++;
                    values++;
                }
                else
                {
                    ctx.Write("\n[U1F/U1U] 图框 " + frame.Handle
                        + " 缺少 FRAMEINFO 属性且无法补建，JSON 未写入该图框。");
                }
            }

            // 清空属于本框的遗留副本，防止同一图框两份 JSON 互相覆盖。
            values += ClearEmbeddedJson(transaction, legacyFrames.ToArray());
            values += ClearStandaloneJson(transaction, legacyJsonBlocks, stampedHandle);

            return new FillWriteResult(blocks, values);
        }

        /// <summary>图框块内 JSON 的首选属性名（2026-11-02 用户模板 FRAME_20261102）。</summary>
        internal const string FrameInfoTag = "FRAMEINFO";

        /// <summary>写 JSON 到图框块的 FRAMEINFO 属性；缺失时从块定义补建属性引用。
        /// 返回 1=写入/已一致，0=失败。</summary>
        private static int WriteFrameJsonAttribute(CadContext ctx, Transaction transaction,
            BlockReference frame, string payload)
        {
            AttributeReference chosen = null;
            AttributeReference fallback = null;
            foreach (AttributeReference attribute in Attributes(transaction, frame))
            {
                if (!IsJsonTag(attribute.Tag)) continue;
                if (string.Equals(attribute.Tag, FrameInfoTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    chosen = attribute;
                    break;
                }
                fallback = fallback ?? attribute;
            }
            if (chosen == null) chosen = fallback;
            if (chosen == null) return AppendFrameInfoAttribute(ctx, transaction, frame, payload);
            if (string.Equals(chosen.TextString, payload, StringComparison.Ordinal)) return 1;
            chosen.UpgradeOpen();
            chosen.TextString = payload;
            if (chosen.IsMTextAttribute)
            {
                using (MText mtext = chosen.MTextAttribute)
                {
                    if (mtext != null)
                    {
                        mtext.Contents = payload;
                        chosen.MTextAttribute = mtext;
                    }
                }
                chosen.UpdateMTextAttribute();
            }
            chosen.AdjustAlignment(ctx.Db);
            return 1;
        }

        /// <summary>图框实例缺 FRAMEINFO 属性引用时，从块定义克隆补建并写入。</summary>
        private static int AppendFrameInfoAttribute(CadContext ctx, Transaction transaction,
            BlockReference frame, string payload)
        {
            foreach (ObjectId definitionId in Cad.FrameRegionCollector.DefinitionIds(frame))
            {
                BlockTableRecord definition;
                try
                {
                    definition = transaction.GetObject(definitionId, OpenMode.ForRead, true)
                        as BlockTableRecord;
                }
                catch { continue; }
                if (definition == null) continue;
                foreach (ObjectId entityId in definition)
                {
                    AttributeDefinition attributeDefinition =
                        transaction.GetObject(entityId, OpenMode.ForRead, true)
                            as AttributeDefinition;
                    if (attributeDefinition == null
                        || !string.Equals(attributeDefinition.Tag, FrameInfoTag,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    var reference = new AttributeReference();
                    reference.SetAttributeFromBlock(attributeDefinition, frame.BlockTransform);
                    reference.TextString = payload;
                    frame.UpgradeOpen();
                    frame.AttributeCollection.AppendAttribute(reference);
                    transaction.AddNewlyCreatedDBObject(reference, true);
                    return 1;
                }
            }
            return 0;
        }

        /// <summary>块上是否带旧式（含 JSON 字样）内嵌 JSON 属性——迁移后要清空的
        /// 只有这一种；FRAMEINFO 是新主存储，永不清空。</summary>
        private static bool HasLegacyJsonAttribute(Transaction transaction, BlockReference block)
            => block != null && Attributes(transaction, block)
                .Any(attribute => (attribute.Tag ?? "")
                    .IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>清空属于本框（无绑定或句柄相符）的遗留独立块 JSON；
        /// 绑定在其他图框上的块不动——那不是本框的数据。</summary>
        private static int ClearStandaloneJson(Transaction transaction,
            List<ObjectId> blockIds, string owningFrameHandle)
        {
            int cleared = 0;
            foreach (ObjectId id in blockIds ?? new List<ObjectId>())
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;
                FrameInfoJsonRecord record = ReadBlock(transaction, block);
                if (record != null && record.IsForeignTo(owningFrameHandle)) continue;
                foreach (AttributeReference attribute in Attributes(transaction, block))
                {
                    if (!IsJsonTag(attribute.Tag)) continue;
                    if (attribute.TextString.Length == 0) continue;
                    attribute.UpgradeOpen();
                    attribute.TextString = "";
                    cleared++;
                }
            }
            return cleared;
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
            if (next == null) return current;
            if (string.Equals(FrameInfoJsonCodec.Serialize(current),
                FrameInfoJsonCodec.Serialize(next), StringComparison.Ordinal))
                return current;

            // 旧图框可能同时有内嵌 FRAMEINFO_JSON 和独立 frameinfo_json 块，且两份内容
            // 因历史写入时间不同并不一致。这里以 lastModifiedUtc 较新的一份为准，避免
            // 以前的“多个记录不一致就整体失败”把整个 U1U 卡死。
            bool currentParsed = DateTimeOffset.TryParse(current.LastModifiedUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out DateTimeOffset currentTime);
            bool nextParsed = DateTimeOffset.TryParse(next.LastModifiedUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out DateTimeOffset nextTime);
            if (nextParsed && (!currentParsed || nextTime > currentTime)) return next;
            return current;
        }

        /// <summary>清空旧图框内嵌的 JSON 属性/动态属性，避免同一图框保留两份 JSON。</summary>
        private static int ClearEmbeddedJson(Transaction transaction, ObjectId[] frameIds)
        {
            int cleared = 0;
            foreach (ObjectId id in frameIds ?? Array.Empty<ObjectId>())
            {
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;
                foreach (AttributeReference attribute in Attributes(transaction, block))
                {
                    // 只清旧式（含 JSON 字样）内嵌属性；FRAMEINFO 是 D-040 主存储。
                    if ((attribute.Tag ?? "").IndexOf("JSON",
                            StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (string.IsNullOrEmpty(attribute.TextString)
                        && !attribute.IsMTextAttribute) continue;
                    attribute.UpgradeOpen();
                    attribute.TextString = "";
                    if (attribute.IsMTextAttribute)
                    {
                        using (MText mtext = attribute.MTextAttribute)
                        {
                            if (mtext != null)
                            {
                                mtext.Contents = "";
                                attribute.MTextAttribute = mtext;
                            }
                        }
                        attribute.UpdateMTextAttribute();
                    }
                    cleared++;
                }
                if (!block.IsDynamicBlock) continue;
                foreach (DynamicBlockReferenceProperty property
                    in block.DynamicBlockReferencePropertyCollection)
                {
                    if (property.ReadOnly || !IsJsonTag(property.PropertyName)
                        || !(property.Value is string)) continue;
                    if (string.IsNullOrEmpty(Convert.ToString(property.Value))) continue;
                    block.UpgradeOpen();
                    property.Value = "";
                    cleared++;
                }
            }
            return cleared;
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

        /// <summary>标签是否承载 frameinfo_json 载荷（FRAMEINFO/含 JSON）。
        /// 2026-11-06 复制框清理用：CadBlockAttributeWriter.ClearStaleInfo
        /// 判断要清空的属性时调用。</summary>
        internal static bool IsJsonTagName(string tag)
            => IsJsonTag(tag);

        private static bool IsJsonTag(string tag)
        {
            string value = tag ?? "";
            // FRAMEINFO：2026-11-02 用户模板（FRAME_20261102）的块内 JSON 属性名；
            // 旧体系用 FRAMEINFO_JSON / 含 JSON 的标签。
            return value.IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(value, "FRAMEINFO", StringComparison.OrdinalIgnoreCase);
        }

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

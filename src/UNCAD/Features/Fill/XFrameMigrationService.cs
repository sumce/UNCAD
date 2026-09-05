using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;

namespace UNCAD.Features.Fill
{
    internal readonly struct XFrameMigrationResult
    {
        public XFrameMigrationResult(int frames, int boqTables, int drawingInfoTables)
        {
            Frames = frames;
            BoqTables = boqTables;
            DrawingInfoTables = drawingInfoTables;
        }

        public int Frames { get; }
        public int BoqTables { get; }
        public int DrawingInfoTables { get; }
    }

    /// <summary>Upgrades selected legacy frames without changing their insert transform.</summary>
    internal static class XFrameMigrationService
    {
        internal const string TemplateFileName = "XFrameTemplate.dwg";
        internal const string ProjectNameTag = "PROJECT_NAME";
        internal const string DefaultProjectName = "新项目二次配电力工程(Power)";

        internal static XFrameMigrationResult Migrate(CadContext ctx,
            Transaction transaction, IEnumerable<FillSelection> selections)
        {
            FillSelection[] targets = (selections ?? Enumerable.Empty<FillSelection>())
                .Where(selection => selection?.FrameBlockIds?.Length > 0)
                .ToArray();
            if (targets.Length == 0) return new XFrameMigrationResult(0, 0, 0);

            // IsLegacyFrame opens the frame reference and its definition each call;
            // the same frames were being re-tested up to three times below.
            var legacyCache = targets.SelectMany(selection => selection.FrameBlockIds)
                .Distinct().ToDictionary(id => id,
                    id => IsLegacyFrame(transaction, id));
            bool Legacy(ObjectId id) => legacyCache.TryGetValue(id, out bool legacy) && legacy;

            int repairedInfoTables = targets.Where(selection =>
                    !selection.FrameBlockIds.Any(Legacy))
                .Sum(selection => RepairSplitDrawingInfoTables(transaction, selection));
            bool needsTemplate = targets.Any(selection =>
                !HasCurrentDrawingInfoTable(transaction, selection));
            bool hasLegacyFrame = legacyCache.Values.Any(legacy => legacy);
            if (!needsTemplate && !hasLegacyFrame)
            {
                EnsureProjectNameDefaults(ctx.Db, transaction, targets);
                return new XFrameMigrationResult(0, 0, repairedInfoTables);
            }

            string templatePath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                "Resources", TemplateFileName);
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("新版图框模板不存在，无法自动升级旧图框。",
                    templatePath);

            using (var template = new Database(false, true))
            {
                template.ReadDwgFile(templatePath,
                    FileOpenMode.OpenForReadAndAllShare, false, null);
                template.CloseInput(true);
                using (Transaction source = template.TransactionManager.StartTransaction())
                {
                    BlockTable sourceBlocks = (BlockTable)source.GetObject(
                        template.BlockTableId, OpenMode.ForRead);
                    if (!sourceBlocks.Has(FrameRegionCollector.XFrameName))
                        throw new InvalidDataException("新版图框模板中缺少 xframe 定义。");
                    ObjectId sourceDefinitionId = sourceBlocks[
                        FrameRegionCollector.XFrameName];
                    ObjectId targetDefinitionId = EnsureDefinition(ctx.Db, transaction,
                        template, sourceDefinitionId);
                    FindTemplateTables(source, sourceBlocks, out ObjectId sourceBoqTableId,
                        out ObjectId sourceInfoTableId);

                    int migrated = 0;
                    int boqTables = 0;
                    int infoTables = repairedInfoTables;
                    foreach (FillSelection selection in targets)
                    {
                        ObjectId[] legacyFrames = selection.FrameBlockIds.Distinct()
                            .Where(Legacy).ToArray();
                        foreach (ObjectId frameId in legacyFrames)
                        {
                            ReplaceDefinition(ctx.Db, transaction, frameId,
                                targetDefinitionId);
                            migrated++;
                        }

                        ObjectId frame = selection.FrameBlockIds.FirstOrDefault();
                        if (frame.IsNull) continue;
                        if (legacyFrames.Length > 0)
                        {
                            ObjectId[] oldTables = (selection.TableIds ?? Array.Empty<ObjectId>())
                                .Concat(selection.DrawingInfoTableIds
                                    ?? Array.Empty<ObjectId>()).Distinct().ToArray();
                            EraseTables(transaction, oldTables);
                            ObjectId boqId = CloneTable(ctx.Db, transaction, template,
                                sourceBoqTableId, frame);
                            ObjectId infoId = CloneTable(ctx.Db, transaction, template,
                                sourceInfoTableId, frame);
                            selection.TableIds = new[] { boqId };
                            selection.DrawingInfoTableIds = new[] { infoId };
                            ReplaceSourceIds(selection, oldTables, boqId, infoId);
                            boqTables++;
                            infoTables++;
                            continue;
                        }

                        if (HasCurrentDrawingInfoTable(transaction, selection)) continue;
                        ObjectId[] oldInfoTables = selection.DrawingInfoTableIds
                            ?? Array.Empty<ObjectId>();
                        EraseTables(transaction, oldInfoTables);
                        ObjectId tableId = CloneTable(ctx.Db, transaction, template,
                            sourceInfoTableId, frame);
                        selection.DrawingInfoTableIds = new[] { tableId };
                        ReplaceSourceIds(selection, oldInfoTables, tableId);
                        infoTables++;
                    }
                    EnsureProjectNameDefaults(ctx.Db, transaction, targets);
                    source.Commit();
                    return new XFrameMigrationResult(migrated, boqTables, infoTables);
                }
            }
        }

        private static bool IsLegacyFrame(Transaction transaction, ObjectId id)
        {
            BlockReference frame = transaction.GetObject(id, OpenMode.ForRead, true)
                as BlockReference;
            return FrameRegionCollector.IsLegacyFrame(transaction, frame);
        }

        private static bool HasCurrentDrawingInfoTable(Transaction transaction,
            FillSelection selection)
        {
            return (selection.DrawingInfoTableIds ?? Array.Empty<ObjectId>()).Any(id =>
                CadDrawingInfoTableWriter.IsCurrentDrawingInfoTable(
                    transaction.GetObject(id, OpenMode.ForRead, true) as Table));
        }

        private static int RepairSplitDrawingInfoTables(Transaction transaction,
            FillSelection selection)
        {
            return (selection.DrawingInfoTableIds ?? Array.Empty<ObjectId>()).Count(id =>
                CadDrawingInfoTableWriter.RepairSplitFloorLayout(transaction, id));
        }

        private static ObjectId EnsureDefinition(Database target,
            Transaction transaction, Database source, ObjectId sourceDefinitionId)
        {
            BlockTable blocks = (BlockTable)transaction.GetObject(target.BlockTableId,
                OpenMode.ForRead);
            if (blocks.Has(FrameRegionCollector.XFrameName))
                return blocks[FrameRegionCollector.XFrameName];

            var ids = new ObjectIdCollection { sourceDefinitionId };
            var mapping = new IdMapping();
            source.WblockCloneObjects(ids, target.BlockTableId, mapping,
                DuplicateRecordCloning.Ignore, false);
            ObjectId cloned = mapping[sourceDefinitionId].Value;
            if (cloned.IsNull)
                throw new InvalidOperationException("无法导入 xframe 图框定义。");
            return cloned;
        }

        private static void FindTemplateTables(Transaction transaction, BlockTable blocks,
            out ObjectId boqTableId, out ObjectId infoTableId)
        {
            boqTableId = ObjectId.Null;
            infoTableId = ObjectId.Null;
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                Table table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                if (table == null) continue;
                if (CadDrawingInfoTableWriter.IsDrawingInfoTable(table)) infoTableId = id;
                else if (table.Columns.Count >= 6 && table.Rows.Count >= 2) boqTableId = id;
            }
            if (boqTableId.IsNull || infoTableId.IsNull)
                throw new InvalidDataException(
                    "新版图框模板必须包含 BOQ 表和制图信息表。");
        }

        private static ObjectId CloneTable(Database target, Transaction transaction,
            Database source, ObjectId sourceTableId, ObjectId frameId)
        {
            var mapping = new IdMapping();
            source.WblockCloneObjects(new ObjectIdCollection { sourceTableId },
                target.CurrentSpaceId, mapping, DuplicateRecordCloning.Ignore, false);
            ObjectId cloneId = mapping[sourceTableId].Value;
            Table table = transaction.GetObject(cloneId, OpenMode.ForWrite, true) as Table;
            BlockReference frame = transaction.GetObject(frameId, OpenMode.ForRead, true)
                as BlockReference;
            if (table == null || frame == null)
                throw new InvalidOperationException("无法创建新版图框表格。");
            table.TransformBy(frame.BlockTransform);
            table.RecordGraphicsModified(true);
            return cloneId;
        }

        private static void EraseTables(Transaction transaction, IEnumerable<ObjectId> ids)
        {
            foreach (ObjectId id in ids ?? Enumerable.Empty<ObjectId>())
            {
                Table table = transaction.GetObject(id, OpenMode.ForWrite, true) as Table;
                if (table != null && !table.IsErased) table.Erase();
            }
        }

        private static void ReplaceSourceIds(FillSelection selection,
            IEnumerable<ObjectId> removed, params ObjectId[] added)
        {
            var removedIds = new HashSet<ObjectId>(removed ?? Enumerable.Empty<ObjectId>());
            selection.SourceIds = (selection.SourceIds ?? Array.Empty<ObjectId>())
                .Where(id => !removedIds.Contains(id))
                .Concat(added ?? Array.Empty<ObjectId>()).Distinct().ToArray();
        }

        private static void EnsureProjectNameDefaults(Database database,
            Transaction transaction, IEnumerable<FillSelection> selections)
        {
            // Definitions are shared across frames; check the PROJECT_NAME tag
            // once per definition before ever write-opening a reference.
            var definitionsWithoutTag = new HashSet<ObjectId>();
            foreach (ObjectId id in selections.SelectMany(selection =>
                selection.FrameBlockIds ?? Array.Empty<ObjectId>()).Distinct())
            {
                BlockReference frame = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (frame == null) continue;
                BlockTableRecord definition = null;
                AttributeDefinition projectDefinition = null;
                foreach (ObjectId definitionId in FrameRegionCollector.DefinitionIds(frame))
                {
                    if (definitionsWithoutTag.Contains(definitionId)) continue;
                    try
                    {
                        definition = transaction.GetObject(definitionId,
                            OpenMode.ForRead, true) as BlockTableRecord;
                    }
                    catch
                    {
                        definitionsWithoutTag.Add(definitionId);
                        continue;
                    }
                    projectDefinition = definition?.Cast<ObjectId>()
                        .Select(attributeId => transaction.GetObject(attributeId,
                            OpenMode.ForRead, true) as AttributeDefinition)
                        .FirstOrDefault(attribute => attribute != null
                            && !attribute.Constant && string.Equals(attribute.Tag,
                                ProjectNameTag, StringComparison.OrdinalIgnoreCase));
                    if (projectDefinition != null) break;
                    definitionsWithoutTag.Add(definitionId);
                }
                if (projectDefinition == null) continue;
                frame.UpgradeOpen();
                if (!string.Equals(projectDefinition.TextString, DefaultProjectName,
                    StringComparison.Ordinal))
                {
                    projectDefinition.UpgradeOpen();
                    projectDefinition.TextString = DefaultProjectName;
                }

                AttributeReference project = frame.AttributeCollection.Cast<ObjectId>()
                    .Select(attributeId => transaction.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference)
                    .FirstOrDefault(attribute => attribute != null
                        && string.Equals(attribute.Tag, ProjectNameTag,
                            StringComparison.OrdinalIgnoreCase));
                if (project == null)
                {
                    project = new AttributeReference();
                    project.SetDatabaseDefaults(database);
                    project.SetAttributeFromBlock(projectDefinition, frame.BlockTransform);
                    SetAttributeText(project, DefaultProjectName);
                    frame.AttributeCollection.AppendAttribute(project);
                    transaction.AddNewlyCreatedDBObject(project, true);
                    continue;
                }
                string value = AttributeText(project).Trim();
                if (value.Length > 0 && !string.Equals(value, "POWER",
                    StringComparison.OrdinalIgnoreCase)) continue;
                project.UpgradeOpen();
                SetAttributeText(project, DefaultProjectName);
                project.AdjustAlignment(database);
            }
        }

        private static void ReplaceDefinition(Database database, Transaction transaction,
            ObjectId frameId, ObjectId targetDefinitionId)
        {
            BlockReference frame = transaction.GetObject(frameId, OpenMode.ForWrite, true)
                as BlockReference;
            if (frame == null) return;
            var existing = frame.AttributeCollection.Cast<ObjectId>()
                .Select(id => transaction.GetObject(id, OpenMode.ForRead, true)
                    as AttributeReference)
                .Where(attribute => attribute != null)
                .GroupBy(attribute => attribute.Tag ?? "",
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

            frame.BlockTableRecord = targetDefinitionId;
            BlockTableRecord definition = transaction.GetObject(targetDefinitionId,
                OpenMode.ForRead) as BlockTableRecord;
            foreach (ObjectId id in definition)
            {
                AttributeDefinition attributeDefinition = transaction.GetObject(id,
                    OpenMode.ForRead, true) as AttributeDefinition;
                if (attributeDefinition == null || attributeDefinition.Constant) continue;
                if (existing.TryGetValue(attributeDefinition.Tag ?? "",
                    out AttributeReference attribute))
                {
                    string value = AttributeText(attribute);
                    attribute.UpgradeOpen();
                    attribute.SetAttributeFromBlock(attributeDefinition,
                        frame.BlockTransform);
                    SetAttributeText(attribute, value);
                    attribute.AdjustAlignment(database);
                    continue;
                }

                var created = new AttributeReference();
                created.SetDatabaseDefaults(database);
                created.SetAttributeFromBlock(attributeDefinition, frame.BlockTransform);
                frame.AttributeCollection.AppendAttribute(created);
                transaction.AddNewlyCreatedDBObject(created, true);
            }
            frame.RecordGraphicsModified(true);
        }

        private static string AttributeText(AttributeReference attribute)
        {
            if (!attribute.IsMTextAttribute) return attribute.TextString ?? "";
            using (MText text = attribute.MTextAttribute)
                return text?.Contents ?? attribute.TextString ?? "";
        }

        private static void SetAttributeText(AttributeReference attribute, string value)
        {
            attribute.TextString = value ?? "";
            if (!attribute.IsMTextAttribute) return;
            using (MText text = attribute.MTextAttribute)
            {
                if (text == null) return;
                text.Contents = value ?? "";
                attribute.MTextAttribute = text;
            }
            attribute.UpdateMTextAttribute();
        }
    }
}

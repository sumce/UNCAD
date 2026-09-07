using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Features.XLayout;

namespace UNCAD.CadIntegration
{
    public sealed class XmergeSelfTestCommand
    {
        private static readonly Regex MangledName = new Regex(@"\$\d+\$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [CommandMethod("UNCAD_XMERGE_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            string tempRoot = Path.Combine(Path.GetTempPath(),
                "uncad-xmerge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                ClearCurrentSpace(document.Database);
                RenameSupportedFrameDefinitions(document.Database);
                AddExistingTargetLine(document.Database);
                HashSet<string> originalMangledBlocks = MangledBlockNames(document.Database);
                string[] names = { "$12$frame", "frame_20260812", "xframe", "xframe" };
                var sources = new List<string>();
                for (int index = 0; index < names.Length; index++)
                {
                    string path = Path.Combine(tempRoot, "source-" + index + ".dwg");
                    CreateSource(path, names[index], "M0" + (index + 1),
                        "DEV0" + (index + 1), 100d, index == names.Length - 1);
                    sources.Add(path);
                }
                Dictionary<string, string> hashes = sources.ToDictionary(path => path,
                    FileHash, StringComparer.OrdinalIgnoreCase);

                XmergeResult result = XmergeService.Merge(ctx, sources);
                Require(result.FileCount == 4, "source count");
                Require(result.FrameCount == 4, "frame count");
                Require(result.EntityCount == 20, "imported entity count");
                Require(result.UnplacedEntityCount == 0, "unplaced entity count");
                foreach (string path in sources)
                    Require(hashes[path] == FileHash(path), "source file was modified");

                var frameIds = new List<ObjectId>();
                var frameDefinitions = new HashSet<ObjectId>();
                var machineValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var frameInfoValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var textValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tableValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tableDefinitions = new HashSet<ObjectId>();
                var writtenBlockNames = new List<string>();
                int lines = 0;
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                        document.Database.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        Entity entity = transaction.GetObject(id,
                            OpenMode.ForRead, true) as Entity;
                        if (entity is Line) lines++;
                        if (entity is DBText text) textValues.Add(text.TextString ?? "");
                        if (entity is Table table)
                        {
                            tableValues.Add(table.Cells[0, 0].TextString ?? "");
                            tableDefinitions.Add(table.BlockTableRecord);
                        }
                        if (!(entity is BlockReference frame) || entity is Table) continue;
                        string definitionName = DefinitionName(transaction, frame);
                        writtenBlockNames.Add(definitionName);
                        if (!FrameRegionCollector.IsSupportedFrame(transaction, frame)) continue;
                        frameIds.Add(id);
                        frameDefinitions.Add(frame.IsDynamicBlock
                            ? frame.DynamicBlockTableRecord : frame.BlockTableRecord);
                        Require(!definitionName.StartsWith("$", StringComparison.Ordinal),
                            "prefixed frame definition: " + definitionName);
                        foreach (ObjectId attributeId in frame.AttributeCollection)
                        {
                            AttributeReference attribute = transaction.GetObject(attributeId,
                                OpenMode.ForRead, true) as AttributeReference;
                            if (attribute != null && string.Equals(attribute.Tag,
                                "MACHINEID-POWER", StringComparison.OrdinalIgnoreCase))
                                machineValues.Add(attribute.TextString ?? "");
                            if (attribute != null && string.Equals(attribute.Tag,
                                "FRAMEINFO_JSON", StringComparison.OrdinalIgnoreCase))
                                frameInfoValues.Add(attribute.TextString ?? "");
                        }
                    }
                }

                Require(frameIds.Count == 4, "written frame count; blocks="
                    + string.Join(",", writtenBlockNames));
                Require(frameDefinitions.Count == 3,
                    "same-name frame definitions were duplicated per source");
                Require(!MangledBlockNames(document.Database).Except(originalMangledBlocks,
                    StringComparer.OrdinalIgnoreCase).Any(),
                    "numeric-mangled block definitions were created");
                Require(lines == 9, "source model-space lines were lost");
                Require(machineValues.SetEquals(new[]
                {
                    "M01-POWER", "M02-POWER", "M03-POWER", "M04-POWER"
                }), "frame attributes were not preserved");
                Require(textValues.SetEquals(new[] { "M01", "M02", "M03", "M04" }),
                    "frame text was not preserved");
                Require(tableValues.SetEquals(new[]
                {
                    "TABLE-M01", "TABLE-M02", "TABLE-M03", "TABLE-M04"
                }), "frame tables were not preserved");
                Require(tableDefinitions.Count == tableValues.Count,
                    "table display definitions were reused");
                Require(frameInfoValues.SetEquals(new[] { "{\"machineId\":\"M04\"}" }),
                    "new-frame metadata attribute was not preserved");
                FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx,
                    frameIds.ToArray());
                Require(regions.Errors.Count == 0 && regions.Groups.Count == 4,
                    "written frames are not XLAYOUT-readable");

                string incompatible = Path.Combine(tempRoot, "incompatible-xframe.dwg");
                CreateSource(incompatible, "xframe", "M05", "DEV05", 120d);
                int beforeRejectedMerge = CurrentEntityCount(document.Database);
                bool rejected = false;
                try
                {
                    XmergeService.Merge(ctx, new[] { sources[2], incompatible });
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Require(rejected, "incompatible same-name definitions were accepted");
                Require(CurrentEntityCount(document.Database) == beforeRejectedMerge,
                    "rejected merge changed the target drawing");
                string realSource = Environment.GetEnvironmentVariable(
                    "UNCAD_XMERGE_REAL_SOURCE");
                if (!string.IsNullOrWhiteSpace(realSource)
                    && (File.Exists(realSource) || Directory.Exists(realSource)))
                    VerifyRealSource(ctx, document.Database, tempRoot, realSource);
                Pass(document);
            }
            catch (System.Exception ex)
            {
                string resultPath = Environment.GetEnvironmentVariable(
                    "UNCAD_CAD_INTEGRATION_RESULT");
                if (!string.IsNullOrWhiteSpace(resultPath))
                    File.WriteAllText(resultPath, "FAIL\r\n" + ex);
                throw;
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static void CreateSource(string path, string frameName,
            string machineId, string deviceName, double width = 100d,
            bool includeFrameInfo = false)
        {
            using (var database = new Database(true, true))
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var blocks = (BlockTable)transaction.GetObject(database.BlockTableId,
                        OpenMode.ForWrite);
                    var styles = (TextStyleTable)transaction.GetObject(
                        database.TextStyleTableId, OpenMode.ForRead);
                    var standard = (TextStyleTableRecord)transaction.GetObject(
                        styles["Standard"], OpenMode.ForWrite);
                    standard.FileName = "arial.ttf";
                    standard.XScale = 0.85d;
                    var definition = new BlockTableRecord { Name = frameName };
                    ObjectId definitionId = blocks.Add(definition);
                    transaction.AddNewlyCreatedDBObject(definition, true);

                    var border = new Polyline(4) { Closed = true };
                    border.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    border.AddVertexAt(1, new Point2d(width, 0), 0, 0, 0);
                    border.AddVertexAt(2, new Point2d(width, 50), 0, 0, 0);
                    border.AddVertexAt(3, new Point2d(0, 50), 0, 0, 0);
                    definition.AppendEntity(border);
                    transaction.AddNewlyCreatedDBObject(border, true);

                    AttributeDefinition power = AddAttribute(transaction, definition,
                        "MACHINEID-POWER", new Point3d(10, 10, 0));
                    AttributeDefinition device = AddAttribute(transaction, definition,
                        "MACHINEID-DEVICE", new Point3d(10, 20, 0));
                    AttributeDefinition frameInfo = includeFrameInfo
                        ? AddAttribute(transaction, definition, "FRAMEINFO_JSON",
                            Point3d.Origin) : null;
                    if (frameInfo != null)
                    {
                        frameInfo.Invisible = true;
                        frameInfo.Preset = true;
                    }

                    var model = (BlockTableRecord)transaction.GetObject(
                        blocks[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var frame = new BlockReference(Point3d.Origin, definitionId);
                    model.AppendEntity(frame);
                    transaction.AddNewlyCreatedDBObject(frame, true);
                    AddAttributeReference(database, transaction, frame, power,
                        machineId + "-POWER");
                    AddAttributeReference(database, transaction, frame, device,
                        machineId + "-" + deviceName);
                    if (frameInfo != null)
                        AddAttributeReference(database, transaction, frame, frameInfo,
                            "{\"machineId\":\"" + machineId + "\"}");

                    var inside = new Line(new Point3d(20, 30, 0),
                        new Point3d(width - 20, 30, 0));
                    model.AppendEntity(inside);
                    transaction.AddNewlyCreatedDBObject(inside, true);
                    var text = new DBText();
                    text.SetDatabaseDefaults(database);
                    text.TextStyleId = standard.ObjectId;
                    text.TextString = machineId;
                    text.Position = new Point3d(30, 40, 0);
                    text.Height = 2.5d;
                    model.AppendEntity(text);
                    transaction.AddNewlyCreatedDBObject(text, true);
                    var table = new Table();
                    table.SetDatabaseDefaults(database);
                    table.SetSize(2, 2);
                    table.Position = new Point3d(5, 25, 0);
                    table.Cells[0, 0].TextString = "TABLE-" + machineId;
                    table.Cells[0, 1].TextString = "项目名称";
                    table.Cells[1, 0].TextString = "1";
                    table.Cells[1, 1].TextString = "电缆";
                    table.GenerateLayout();
                    model.AppendEntity(table);
                    transaction.AddNewlyCreatedDBObject(table, true);
                    var outside = new Line(new Point3d(200, 200, 0),
                        new Point3d(250, 200, 0));
                    model.AppendEntity(outside);
                    transaction.AddNewlyCreatedDBObject(outside, true);
                    transaction.Commit();
                }
                database.SaveAs(path, DwgVersion.Current);
            }
        }

        private static AttributeDefinition AddAttribute(Transaction transaction,
            BlockTableRecord definition, string tag, Point3d position)
        {
            var attribute = new AttributeDefinition
            {
                Tag = tag,
                TextString = "",
                Position = position,
                Height = 2.5
            };
            definition.AppendEntity(attribute);
            transaction.AddNewlyCreatedDBObject(attribute, true);
            return attribute;
        }

        private static void AddAttributeReference(Database database,
            Transaction transaction, BlockReference block,
            AttributeDefinition definition, string value)
        {
            var attribute = new AttributeReference();
            attribute.SetDatabaseDefaults(database);
            attribute.SetAttributeFromBlock(definition, block.BlockTransform);
            attribute.TextString = value;
            block.AttributeCollection.AppendAttribute(attribute);
            transaction.AddNewlyCreatedDBObject(attribute, true);
        }

        private static void ClearCurrentSpace(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForWrite);
                foreach (ObjectId id in space.Cast<ObjectId>().ToArray())
                {
                    Entity entity = transaction.GetObject(id,
                        OpenMode.ForWrite, true) as Entity;
                    if (entity != null && !entity.IsErased) entity.Erase();
                }
                transaction.Commit();
            }
        }

        private static void AddExistingTargetLine(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForWrite);
                var line = new Line(new Point3d(-1000, -1000, 0),
                    new Point3d(-900, -1000, 0));
                space.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
                transaction.Commit();
            }
        }

        private static void RenameSupportedFrameDefinitions(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)transaction.GetObject(database.BlockTableId,
                    OpenMode.ForRead);
                int index = 0;
                foreach (ObjectId id in blocks.Cast<ObjectId>().ToArray())
                {
                    var definition = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockTableRecord;
                    if (definition == null || definition.IsLayout
                        || !FrameRegionCollector.IsSupportedFrameName(definition.Name)) continue;
                    string name;
                    do { name = "UNCAD_SELFTEST_EXISTING_FRAME_" + index++; }
                    while (blocks.Has(name));
                    definition.UpgradeOpen();
                    definition.Name = name;
                }
                transaction.Commit();
            }
        }

        private static HashSet<string> MangledBlockNames(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)transaction.GetObject(database.BlockTableId,
                    OpenMode.ForRead);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId id in blocks)
                {
                    var definition = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockTableRecord;
                    if (definition != null && MangledName.IsMatch(definition.Name ?? ""))
                        names.Add(definition.Name);
                }
                return names;
            }
        }

        private static int CurrentEntityCount(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForRead);
                return space.Cast<ObjectId>().Count();
            }
        }

        private static void VerifyRealSource(CadContext ctx, Database target,
            string tempRoot, string source)
        {
            ClearCurrentSpace(target);
            RenameNamedBlockDefinitions(target);
            AddExistingTargetLine(target);
            string[] sourceFiles = Directory.Exists(source)
                ? Directory.GetFiles(source, "*.dwg", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
                : new[] { source, source };
            var copies = new List<string>();
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string copy = Path.Combine(tempRoot, "real-"
                    + index.ToString("D3") + "-" + Path.GetFileName(sourceFiles[index]));
                File.Copy(sourceFiles[index], copy, true);
                copies.Add(copy);
                hashes[copy] = FileHash(copy);
            }

            var sourceTables = new List<string>();
            int sourceEntityCount = 0;
            foreach (string path in copies)
                sourceEntityCount += ReadModelSpaceSnapshot(path, sourceTables);
            string[] sourceTableSignatures = sourceTables
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            int sourceTableCount = sourceTableSignatures.Length;
            XmergeResult result = XmergeService.Merge(ctx, copies);
            Require(result.FrameCount > 0, "real source frame count");
            Require(result.UnplacedEntityCount == 0,
                "real source entities were left at source coordinates");
            Require(result.EntityCount == sourceEntityCount,
                "real source entity count " + sourceEntityCount
                + " became " + result.EntityCount);
            Require(CurrentEntityCount(target) == sourceEntityCount + 1,
                "real source model-space entities were lost");
            Require(copies.All(path => hashes[path] == FileHash(path)),
                "real source file was modified");
            var frameIds = new List<ObjectId>();
            var targetTableSignatures = new List<string>();
            var targetTableDefinitions = new HashSet<ObjectId>();
            int dynamicBlocks = 0;
            int targetTableCount = 0;
            using (Transaction transaction = target.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                    target.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    Entity entity = transaction.GetObject(id,
                        OpenMode.ForRead, true) as Entity;
                    if (entity is Table table)
                    {
                        targetTableCount++;
                        targetTableSignatures.Add(TableSignature(table));
                        targetTableDefinitions.Add(table.BlockTableRecord);
                    }
                    BlockReference frame = entity as BlockReference;
                    if (frame == null || frame is Table) continue;
                    if (frame.IsDynamicBlock) dynamicBlocks++;
                    if (!FrameRegionCollector.IsSupportedFrame(transaction, frame)) continue;
                    Require(!DefinitionName(transaction, frame).StartsWith("$",
                        StringComparison.Ordinal), "real frame retained a mangled prefix");
                    frameIds.Add(id);
                }
            }
            Require(frameIds.Count == result.FrameCount,
                "real source frames are not XLAYOUT-readable");
            Require(targetTableCount == sourceTableCount,
                "real source table count " + sourceTableCount
                + " became " + targetTableCount);
            Require(targetTableDefinitions.Count == targetTableCount,
                "real source table display definitions were reused: "
                + targetTableDefinitions.Count + "/" + targetTableCount);
            Require(targetTableSignatures.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(sourceTableSignatures, StringComparer.Ordinal),
                "real source table contents changed during merge");
            FrameRegionCollection regions = FrameRegionCollector.CollectForLayout(ctx,
                frameIds.ToArray());
            int groupedTableCount;
            using (Transaction transaction = target.TransactionManager.StartTransaction())
                groupedTableCount = regions.Groups.SelectMany(group => group.EntityIds)
                    .Distinct().Count(id => transaction.GetObject(id,
                        OpenMode.ForRead, true) is Table);
            Require(groupedTableCount == sourceTableCount,
                "real source tables were not moved with their frames: "
                + groupedTableCount + "/" + sourceTableCount);
            Require(dynamicBlocks > 0, "real source contains no dynamic block coverage");
        }

        private static int ReadModelSpaceSnapshot(string path,
            ICollection<string> tableSignatures)
        {
            using (var database = new Database(false, true))
            {
                database.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, "");
                database.CloseInput(true);
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    BlockTable blocks = (BlockTable)transaction.GetObject(
                        database.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                        blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        Table table = transaction.GetObject(id,
                            OpenMode.ForRead, true) as Table;
                        if (table != null) tableSignatures.Add(TableSignature(table));
                    }
                    return space.Cast<ObjectId>().Count();
                }
            }
        }

        private static string TableSignature(Table table)
        {
            var values = new List<string>
            {
                table.Rows.Count.ToString(), table.Columns.Count.ToString()
            };
            for (int row = 0; row < table.Rows.Count; row++)
                for (int column = 0; column < table.Columns.Count; column++)
                {
                    string value = table.Cells[row, column].TextString ?? "";
                    values.Add(value.Length + ":" + value);
                }
            return string.Join("|", values);
        }

        private static void RenameNamedBlockDefinitions(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)transaction.GetObject(database.BlockTableId,
                    OpenMode.ForRead);
                int index = 0;
                foreach (ObjectId id in blocks.Cast<ObjectId>().ToArray())
                {
                    var definition = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockTableRecord;
                    if (definition == null || definition.IsLayout
                        || definition.Name.StartsWith("*", StringComparison.Ordinal)
                        || definition.IsDependent) continue;
                    string name;
                    do { name = "UNCAD_SELFTEST_EXISTING_BLOCK_" + index++; }
                    while (blocks.Has(name));
                    definition.UpgradeOpen();
                    definition.Name = name;
                }
                transaction.Commit();
            }
        }

        private static string DefinitionName(Transaction transaction, BlockReference block)
        {
            ObjectId id = block.IsDynamicBlock
                ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            BlockTableRecord definition = transaction.GetObject(id,
                OpenMode.ForRead, true) as BlockTableRecord;
            return definition?.Name ?? "";
        }

        private static string FileHash(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Xmerge self-test: " + message);
        }

        private static void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_XMERGE_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                File.WriteAllText(resultPath, "PASS");
        }

    }
}

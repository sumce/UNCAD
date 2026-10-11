using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 软管块族（Ruanguan / Ruanguan_GW）的真机自检。
    ///
    /// 断言三件事：
    /// 1. 图框选择把 Ruanguan 与 Ruanguan_GW 都识别为软管块，而黏连名 RuanguanXYZ 不是；
    /// 2. 两者的软管长度都能读出来（同一套属性规则）；
    /// 3. U1C/U1F 的写入路径能给两者写入"管径+长度"标签，且不碰非软管块。
    ///
    /// 之所以要真机跑：识别发生在块定义读取与属性集合遍历上，
    /// 单元测试只能覆盖块名判定本身（BlockNameNormalizerTests）。
    /// </summary>
    public sealed class RuanguanBlockFamilySelfTestCommand
    {
        private const string FamilyName = "Ruanguan";
        private const string VariantName = "Ruanguan_GW";
        private const string StrangerName = "RuanguanXYZ";
        private const string LengthTag = "长度";
        private const string LengthValue = "2000mm";
        private const double OriginY = 91000000d;

        [CommandMethod("UNCAD_RUANGUAN_GW_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            var inserted = new List<ObjectId>();
            try
            {
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    inserted.Add(InsertFixture(ctx, document.Database, transaction,
                        FamilyName, 91000000d));
                    inserted.Add(InsertFixture(ctx, document.Database, transaction,
                        VariantName, 91001000d));
                    inserted.Add(InsertFixture(ctx, document.Database, transaction,
                        StrangerName, 91002000d));
                    transaction.Commit();
                }

                ObjectId familyId = inserted[0];
                ObjectId variantId = inserted[1];
                ObjectId strangerId = inserted[2];

                FillSelection selection = FillSelectionCollector.Split(ctx,
                    inserted.ToArray());
                Info("识别到的软管块：" + selection.RuanguanBlockIds.Length + " 个");
                Require(selection.RuanguanBlockIds.Contains(familyId),
                    "Ruanguan 没有被识别为软管块。");
                Require(selection.RuanguanBlockIds.Contains(variantId),
                    "Ruanguan_GW 没有被识别为软管块（本版新增的家族变体）。");
                Require(!selection.RuanguanBlockIds.Contains(strangerId),
                    "RuanguanXYZ 被误识别为软管块（黏连名不属于家族）。");
                Require(selection.RuanguanBlockIds.Length == 2,
                    "软管块数量应为 2，实际 " + selection.RuanguanBlockIds.Length + "。");

                string meters = FillSelectionCollector.ReadRuanguanLengthMeters(ctx,
                    selection.RuanguanBlockIds);
                Info("读取到的软管长度：" + meters + "M");
                Require(meters == "2", "软管长度应为 2（" + LengthValue + "），实际 '"
                    + meters + "'。");

                FillWriteResult written;
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    written = RuanguanBlockWriter.FillModelAndLength(ctx, transaction,
                        new[] { familyId, variantId, strangerId }, "20");
                    transaction.Commit();
                }
                Info("写入的软管块：" + written.Blocks + " 个块 / " + written.Values + " 项属性");
                Require(written.Blocks == 2,
                    "写入路径应更新 2 个软管块（Ruanguan 与 Ruanguan_GW），实际 "
                    + written.Blocks + "。");

                string expected = "20mm软管:2000mm";
                AssertLabel(document.Database, familyId, expected, FamilyName);
                AssertLabel(document.Database, variantId, expected, VariantName);
                AssertUnchanged(document.Database, strangerId, StrangerName);

                Pass(document);
            }
            catch (System.Exception ex)
            {
                WriteResult("FAIL\r\n" + ex);
                throw;
            }
            finally
            {
                Erase(document.Database, inserted);
            }
        }

        /// <summary>插入一个软管块夹具：块定义带一个长度属性，插入后属性值为 2000mm。</summary>
        private static ObjectId InsertFixture(CadContext ctx, Database database,
            Transaction transaction, string name, double x)
        {
            var blocks = (BlockTable)transaction.GetObject(database.BlockTableId,
                OpenMode.ForRead);
            ObjectId definitionId;
            AttributeDefinition definitionAttribute;
            if (blocks.Has(name))
            {
                definitionId = blocks[name];
                definitionAttribute = FindLengthDefinition(transaction, definitionId);
            }
            else
            {
                blocks.UpgradeOpen();
                var record = new BlockTableRecord { Name = name };
                definitionId = blocks.Add(record);
                transaction.AddNewlyCreatedDBObject(record, true);
                definitionAttribute = new AttributeDefinition
                {
                    Tag = LengthTag,
                    TextString = LengthValue,
                    Position = Point3d.Origin,
                    Height = 5d
                };
                record.AppendEntity(definitionAttribute);
                transaction.AddNewlyCreatedDBObject(definitionAttribute, true);
                var outline = new Line(Point3d.Origin, new Point3d(1000d, 0d, 0d));
                record.AppendEntity(outline);
                transaction.AddNewlyCreatedDBObject(outline, true);
            }

            var reference = new BlockReference(new Point3d(x, OriginY, 0d), definitionId);
            ObjectId referenceId = ctx.AddToCurrentSpace(transaction, reference);
            if (definitionAttribute != null)
            {
                var attribute = new AttributeReference();
                attribute.SetDatabaseDefaults(database);
                attribute.SetAttributeFromBlock(definitionAttribute, reference.BlockTransform);
                attribute.TextString = LengthValue;
                reference.AttributeCollection.AppendAttribute(attribute);
                transaction.AddNewlyCreatedDBObject(attribute, true);
            }
            return referenceId;
        }

        private static AttributeDefinition FindLengthDefinition(Transaction transaction,
            ObjectId definitionId)
        {
            var record = transaction.GetObject(definitionId, OpenMode.ForRead)
                as BlockTableRecord;
            if (record == null) return null;
            foreach (ObjectId id in record)
                if (transaction.GetObject(id, OpenMode.ForRead) is AttributeDefinition
                    attribute)
                    return attribute;
            return null;
        }

        private static void AssertLabel(Database database, ObjectId blockId,
            string expected, string label)
        {
            string text = ReadAttribute(database, blockId);
            Require(string.Equals(text, expected, StringComparison.Ordinal),
                label + " 的软管标签应为 '" + expected + "'，实际 '" + text + "'。");
        }

        private static void AssertUnchanged(Database database, ObjectId blockId,
            string label)
        {
            string text = ReadAttribute(database, blockId);
            Require(string.Equals(text, LengthValue, StringComparison.Ordinal),
                label + " 不是软管块，不应被写入；实际 '" + text + "'。");
        }

        private static string ReadAttribute(Database database, ObjectId blockId)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var block = transaction.GetObject(blockId, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) return "";
                foreach (ObjectId id in block.AttributeCollection)
                    if (transaction.GetObject(id, OpenMode.ForRead, true)
                        is AttributeReference attribute)
                        return attribute.TextString ?? "";
                return "";
            }
        }

        private static void Erase(Database database, IEnumerable<ObjectId> ids)
        {
            try
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids ?? Enumerable.Empty<ObjectId>())
                    {
                        if (id.IsNull || !id.IsValid) continue;
                        var entity = transaction.GetObject(id, OpenMode.ForWrite, true)
                            as Entity;
                        if (entity != null && !entity.IsErased) entity.Erase();
                    }
                    transaction.Commit();
                }
            }
            catch (System.Exception) { }
        }

        private static void Info(string line)
            => Application.DocumentManager.MdiActiveDocument.Editor
                .WriteMessage("\n[RUANGUAN-SELFTEST] " + line);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(
                "软管块族自检: " + message);
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, text);
        }

        private static void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_RUANGUAN_GW_SELFTEST:PASS\n");
            WriteResult("PASS");
        }
    }
}

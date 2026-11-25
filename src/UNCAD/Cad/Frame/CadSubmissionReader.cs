using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Submission;
using UNCAD.Infra;

#pragma warning disable 618 // AutoCAD 2022 direct table reads avoid stale Cell wrappers.
namespace UNCAD.Cad.Frame
{
    /// <summary>Reads blocks and BOQ tables through a read-only CAD transaction.</summary>
    internal static class CadSubmissionReader
    {
        public static SubmissionSourceData Read(CadContext ctx, ObjectId[] ids)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                SubmissionSourceData source = Read(transaction, ids);
                transaction.Commit();
                return source;
            }
        }

        public static SubmissionSourceData Read(Transaction transaction, ObjectId[] ids,
            CadBlockDefinitionReader definitions = null, bool tablesOnly = false)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            definitions = definitions ?? new CadBlockDefinitionReader(transaction);
            var source = new SubmissionSourceData();
            foreach (ObjectId id in ids ?? Array.Empty<ObjectId>())
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                // AutoCAD Table inherits BlockReference, so tables must be classified first.
                if (entity is Table table)
                {
                    if (!CadTableLayoutClassifier.IsDrawingInfoTable(table))
                        ReadTable(table, source);
                }
                else if (entity is BlockReference block)
                {
                    if (!tablesOnly) ReadBlock(transaction, block, source, definitions);
                }
                else if (entity is DBText || entity is MText) source.TextEntityCount++;
            }
            return source;
        }

        private static void ReadBlock(Transaction transaction, BlockReference block,
            SubmissionSourceData source, CadBlockDefinitionReader definitions)
        {
            var presentTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = transaction.GetObject(attributeId, OpenMode.ForRead, true)
                    as AttributeReference;
                if (attribute == null) continue;
                presentTags.Add((attribute.Tag ?? "").Trim());
                string value = attribute.TextString ?? "";
                if (attribute.IsMTextAttribute)
                {
                    using (MText mtext = attribute.MTextAttribute)
                        if (mtext != null) value = mtext.Contents ?? value;
                }
                source.AddAttribute(attribute.Tag, value);
            }
            if (block.IsDynamicBlock)
            {
                foreach (DynamicBlockReferenceProperty property
                    in block.DynamicBlockReferencePropertyCollection)
                {
                    presentTags.Add((property.PropertyName ?? "").Trim());
                    string value = Convert.ToString(property.Value) ?? "";
                    if (value.Trim().Length == 0) continue;
                    source.DynamicValues.Add(value);
                    source.AddAttribute(property.PropertyName, value);
                }
            }

            // Some legacy/static inserts expose only the attribute definition (no reference),
            // while U1U's identity reader has always accepted that value.  Mirror that
            // compatibility fallback here so XLAYOUT/U1S/DWG export see the same identity.
            foreach (ObjectId definitionId in FrameRegionCollector.DefinitionIds(block))
            {
                if (definitionId.IsNull || !definitionId.IsValid) continue;
                foreach (KeyValuePair<string, string> attribute in definitions.Read(definitionId).Attributes)
                {
                    string tag = attribute.Key.Trim();
                    if (presentTags.Contains(tag)) continue;
                    presentTags.Add(tag);
                    source.AddAttribute(tag, attribute.Value);
                }
            }
        }

        private static void ReadTable(Table table, SubmissionSourceData source)
        {
            for (int row = 0; row < table.Rows.Count; row++)
            {
                var values = new string[table.Columns.Count];
                for (int column = 0; column < table.Columns.Count; column++)
                {
                    try { values[column] = table.TextString(row, column) ?? ""; }
                    catch (System.Exception ex)
                    {
                        values[column] = "";
                        source.TableReadErrors.Add("清单表 " + table.Handle + " 第 "
                            + (row + 1) + " 行第 " + (column + 1) + " 列读取失败: " + ex.Message);
                        Log.Warn("automatic Excel read table cell failed: " + ex.Message);
                    }
                }
                source.AddTableRow(values);
            }
        }
    }
}
#pragma warning restore 618

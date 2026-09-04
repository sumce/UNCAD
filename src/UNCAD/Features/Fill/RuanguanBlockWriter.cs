using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// Writes the mapped hose diameter into the visible length attribute of a
    /// Ruanguan block. Geometry properties are deliberately read only: dynamic
    /// blocks often expose coordinates and distances that are not hose length.
    /// </summary>
    internal static class RuanguanBlockWriter
    {
        private static readonly string[] LengthTagTokens =
        {
            "LENGTH", "DISTANCE", "HOSE", "软管", "长度"
        };

        private static readonly string[] LabelPropertyTokens =
        {
            "LABEL", "TEXT", "MODEL", "型号", "标注", "说明"
        };

        private static readonly string[] ModelPropertyTokens =
        {
            "MODEL", "型号", "DIA", "DIAMETER", "直径", "SPEC", "规格"
        };

        private static readonly Regex BareUnitValue = new Regex(
            @"^\s*[0-9]+(?:[.,][0-9]+)?\s*(?:mm|m)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static FillWriteResult FillModelAndLength(CadContext ctx,
            ObjectId[] blockIds, string diameter)
            => RunStandalone(ctx, transaction => FillModelAndLength(ctx, transaction,
                blockIds, diameter));

        internal static FillWriteResult FillModelAndLength(CadContext ctx,
            Transaction transaction, ObjectId[] blockIds, string diameter)
        {
            string normalizedDiameter = ConduitDiameter.NormalizeOrEmpty(diameter);
            if (ctx == null || transaction == null || blockIds == null
                || blockIds.Length == 0 || normalizedDiameter.Length == 0)
                return FillWriteResult.Empty;

            int blocks = 0;
            int values = 0;
            var visited = new HashSet<ObjectId>();
            foreach (ObjectId id in blockIds)
            {
                if (!visited.Add(id)) continue;
                var block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (!IsRuanguan(transaction, block)) continue;

                bool touched = false;
                double millimetres;
                var strongLengthAttributes = new List<AttributeReference>();
                var weakLengthAttributes = new List<AttributeReference>();
                if (block.AttributeCollection != null)
                {
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = transaction.GetObject(attributeId,
                            OpenMode.ForRead, true) as AttributeReference;
                        if (attribute == null) continue;
                        if (!TryReadLength(attribute.TextString, attribute.Tag,
                            out millimetres)) continue;
                        if (IsStrongLengthCandidate(attribute.TextString, attribute.Tag))
                            strongLengthAttributes.Add(attribute);
                        else
                            weakLengthAttributes.Add(attribute);
                    }
                }

                List<AttributeReference> lengthAttributes = strongLengthAttributes.Count > 0
                    ? strongLengthAttributes
                    : weakLengthAttributes.Count == 1
                        ? weakLengthAttributes
                        : new List<AttributeReference>();
                if (lengthAttributes.Count > 0)
                {
                    // Update every recognized length attribute. This covers blocks
                    // that expose the same visible value in more than one language
                    // or attribute presentation without touching unrelated fields.
                    foreach (AttributeReference attribute in lengthAttributes)
                    {
                        if (!TryReadLength(attribute.TextString, attribute.Tag,
                            out millimetres)) continue;
                        string label = RuanguanLabelFormatter.Build(normalizedDiameter,
                            millimetres);
                        if (!string.Equals(attribute.TextString, label,
                            StringComparison.Ordinal))
                        {
                            SetAttributeValue(ctx, attribute, label);
                            values++;
                        }
                        touched = true;
                    }
                }
                else
                {
                    // A few versions of the block stored the visible caption as a
                    // writable string dynamic property. Only string label-like
                    // properties are eligible; numeric geometry remains untouched.
                    foreach (DynamicBlockReferenceProperty property
                        in GetDynamicProperties(block))
                    {
                        if (property.ReadOnly || !IsLabelProperty(property.PropertyName)
                            || !(property.Value is string)) continue;
                        string source = Convert.ToString(property.Value) ?? "";
                        if (!TryReadLength(source, property.PropertyName,
                            out millimetres)) continue;
                        string label = RuanguanLabelFormatter.Build(normalizedDiameter,
                            millimetres);
                        if (string.Equals(source, label, StringComparison.Ordinal))
                        {
                            touched = true;
                            continue;
                        }
                        try
                        {
                            block.UpgradeOpen();
                            property.Value = label;
                            values++;
                            touched = true;
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                "Ruanguan 块型号/长度动态属性写入失败。", ex);
                        }
                    }
                }

                if (touched) blocks++;
            }
            return new FillWriteResult(blocks, values);
        }

        /// <summary>Reads a length while rejecting unqualified geometry numbers.</summary>
        internal static bool TryReadLength(string value, string tag,
            out double millimetres)
        {
            if (RuanguanLengthParser.TryParseMillimetres(value, out millimetres))
            {
                // A bare diameter such as MODEL=20mm is not a hose length.
                if (IsModelProperty(tag) && BareUnitValue.IsMatch(value ?? ""))
                {
                    millimetres = 0;
                    return false;
                }
                return true;
            }
            if (!IsLengthProperty(tag)
                || !TryParseNumber(value, out millimetres)) return false;
            return millimetres > 0;
        }

        private static bool IsStrongLengthCandidate(string value, string tag)
            => IsLengthProperty(tag)
                || (value ?? "").IndexOf("软管", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsModelProperty(string name)
        {
            string value = (name ?? "").Trim();
            foreach (string token in ModelPropertyTokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        internal static bool IsLengthProperty(string name)
        {
            string value = (name ?? "").Trim();
            foreach (string token in LengthTagTokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsLabelProperty(string name)
        {
            string value = (name ?? "").Trim();
            foreach (string token in LabelPropertyTokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool TryParseNumber(string value, out double number)
        {
            string text = (value ?? "").Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out number)
                && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static IEnumerable<DynamicBlockReferenceProperty> GetDynamicProperties(
            BlockReference block)
        {
            if (block == null || !block.IsDynamicBlock)
                yield break;
            foreach (DynamicBlockReferenceProperty property
                in block.DynamicBlockReferencePropertyCollection)
                yield return property;
        }

        private static bool IsRuanguan(Transaction transaction, BlockReference block)
        {
            if (block == null) return false;
            ObjectId definitionId = block.IsDynamicBlock
                ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            var definition = transaction.GetObject(definitionId, OpenMode.ForRead, true)
                as BlockTableRecord;
            return definition != null && string.Equals(
                BlockNameNormalizer.RemoveMangledSuffix(definition.Name), "Ruanguan",
                StringComparison.OrdinalIgnoreCase);
        }

        private static void SetAttributeValue(CadContext ctx,
            AttributeReference attribute, string value)
        {
            attribute.UpgradeOpen();
            attribute.TextString = value ?? "";
            attribute.AdjustAlignment(ctx.Db);
        }

        private static FillWriteResult RunStandalone(CadContext ctx,
            Func<Transaction, FillWriteResult> write)
        {
            if (ctx == null || write == null) return FillWriteResult.Empty;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                FillWriteResult result = write(transaction);
                transaction.Commit();
                return result;
            }
        }
    }
}

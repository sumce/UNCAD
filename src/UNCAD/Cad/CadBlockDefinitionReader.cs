using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace UNCAD.Cad
{
    /// <summary>
    /// Caches definition names and attribute defaults during one caller-owned read phase.
    /// Recreate after editing definitions; reference attributes and dynamic values stay live.
    /// Unreadable definitions are skipped, matching the legacy attribute fallback.
    /// </summary>
    internal sealed class CadBlockDefinitionReader
    {
        internal sealed class Definition
        {
            internal string Name { get; set; } = "";
            internal List<KeyValuePair<string, string>> Attributes { get; } =
                new List<KeyValuePair<string, string>>();
        }

        private readonly Transaction _transaction;
        private readonly RXClass _attributeClass;
        private readonly Dictionary<ObjectId, Definition> _definitions =
            new Dictionary<ObjectId, Definition>();

        internal CadBlockDefinitionReader(Transaction transaction)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _attributeClass = RXObject.GetClass(typeof(AttributeDefinition));
        }

        internal Definition Read(ObjectId id)
        {
            if (_definitions.TryGetValue(id, out Definition cached)) return cached;
            var result = new Definition();
            BlockTableRecord record;
            try { record = _transaction.GetObject(id, OpenMode.ForRead, true) as BlockTableRecord; }
            catch { return result; }
            if (record == null) return result;
            result.Name = record.Name;
            foreach (ObjectId entityId in record)
            {
                AttributeDefinition attribute;
                try
                {
                    if (!entityId.ObjectClass.IsDerivedFrom(_attributeClass)) continue;
                    attribute = _transaction.GetObject(entityId, OpenMode.ForRead, true)
                        as AttributeDefinition;
                }
                catch { continue; }
                if (attribute != null)
                    result.Attributes.Add(new KeyValuePair<string, string>(
                        attribute.Tag ?? "", attribute.TextString ?? ""));
            }
            _definitions.Add(id, result);
            return result;
        }
    }
}

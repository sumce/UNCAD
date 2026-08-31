using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace UNCAD.Cad
{
    /// <summary>Metadata used to make U1Q/U1C parallel annotations idempotent.</summary>
    internal static class ParallelAnnotationMetadata
    {
        public const string ApplicationName = "UNCAD_PARALLEL";
        private const string KindKey = "KIND";
        private const string SourceKey = "SOURCE";

        public static bool EnsureApplication(Database database, Transaction transaction)
        {
            try
            {
                var table = transaction.GetObject(database.RegAppTableId,
                    OpenMode.ForRead, false) as RegAppTable;
                if (table == null) return false;
                if (!table.Has(ApplicationName))
                {
                    table.UpgradeOpen();
                    var record = new RegAppTableRecord { Name = ApplicationName };
                    table.Add(record);
                    transaction.AddNewlyCreatedDBObject(record, true);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Set(Entity entity, string kind, string sourceHandle)
        {
            if (entity == null || string.IsNullOrWhiteSpace(kind)
                || string.IsNullOrWhiteSpace(sourceHandle)) return;
            var values = new List<TypedValue>();
            try
            {
                using (ResultBuffer existing = entity.XData)
                {
                    if (existing != null)
                    {
                        bool own = false;
                        foreach (TypedValue value in existing)
                        {
                            if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                            {
                                string app = Convert.ToString(value.Value,
                                    CultureInfo.InvariantCulture) ?? "";
                                own = string.Equals(app, ApplicationName,
                                    StringComparison.OrdinalIgnoreCase);
                                if (own) continue;
                            }
                            if (!own) values.Add(value);
                        }
                    }
                }
            }
            catch
            {
                values.Clear();
            }
            values.Add(new TypedValue((int)DxfCode.ExtendedDataRegAppName,
                ApplicationName));
            values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, KindKey));
            values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind.Trim()));
            values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, SourceKey));
            values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                sourceHandle.Trim()));
            try { entity.XData = new ResultBuffer(values.ToArray()); }
            catch { /* Annotation metadata is optional; geometry remains valid. */ }
        }

        public static bool TryRead(Entity entity, out string kind, out string sourceHandle)
        {
            kind = "";
            sourceHandle = "";
            if (entity == null) return false;
            try
            {
                using (ResultBuffer data = entity.GetXDataForApplication(ApplicationName))
                {
                    if (data == null) return false;
                    string key = "";
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode != (int)DxfCode.ExtendedDataAsciiString) continue;
                        string text = Convert.ToString(value.Value,
                            CultureInfo.InvariantCulture) ?? "";
                        if (string.Equals(text, KindKey,
                            StringComparison.OrdinalIgnoreCase)
                            || string.Equals(text, SourceKey,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            key = text;
                            continue;
                        }
                        if (string.Equals(key, KindKey,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            kind = text;
                            key = "";
                        }
                        else if (string.Equals(key, SourceKey,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            sourceHandle = text;
                            key = "";
                        }
                    }
                }
            }
            catch
            {
                kind = "";
                sourceHandle = "";
            }
            return kind.Length > 0 && sourceHandle.Length > 0;
        }
    }
}

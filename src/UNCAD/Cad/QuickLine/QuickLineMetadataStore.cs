using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace UNCAD.Cad.QuickLine
{
    internal static class QuickLineMetadataStore
    {
        public const string ApplicationName = "UNCAD_U1LX";
        private const string LineAssociationKey = "LINE";
        private const string CompletedKey = "COMPLETED";

        public static string ReadAssociatedLineHandle(DBObject entity)
        {
            if (entity == null) return string.Empty;
            try
            {
                using (ResultBuffer data = entity.GetXDataForApplication(ApplicationName))
                {
                    if (data == null) return string.Empty;
                    bool lineKey = false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            string token = Convert.ToString(value.Value,
                                CultureInfo.InvariantCulture) ?? string.Empty;
                            lineKey = string.Equals(token, LineAssociationKey,
                                StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        if (lineKey && value.TypeCode == (int)DxfCode.ExtendedDataHandle)
                            return Convert.ToString(value.Value,
                                CultureInfo.InvariantCulture) ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Legacy drawings without registered or valid XData use geometry matching.
            }
            return string.Empty;
        }

        public static bool HasCompletionMarker(DBObject entity)
        {
            if (entity == null) return false;
            try
            {
                using (ResultBuffer data = entity.GetXDataForApplication(ApplicationName))
                {
                    if (data == null) return false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode != (int)DxfCode.ExtendedDataAsciiString) continue;
                        string token = Convert.ToString(value.Value,
                            CultureInfo.InvariantCulture) ?? string.Empty;
                        if (string.Equals(token, CompletedKey,
                            StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch
            {
                // Unreadable metadata is treated as incomplete.
            }
            return false;
        }

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

        public static ResultBuffer Build(DBObject entity, string associatedLineHandle,
            bool completed)
        {
            var values = new List<TypedValue>();
            try
            {
                using (ResultBuffer existing = entity.XData)
                {
                    if (existing != null)
                    {
                        bool skipOwn = false;
                        foreach (TypedValue value in existing)
                        {
                            if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                            {
                                string app = Convert.ToString(value.Value,
                                    CultureInfo.InvariantCulture) ?? string.Empty;
                                skipOwn = string.Equals(app, ApplicationName,
                                    StringComparison.OrdinalIgnoreCase);
                                if (skipOwn) continue;
                            }
                            if (!skipOwn) values.Add(value);
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
            if (!string.IsNullOrWhiteSpace(associatedLineHandle))
            {
                values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    LineAssociationKey));
                values.Add(new TypedValue((int)DxfCode.ExtendedDataHandle,
                    associatedLineHandle));
            }
            if (completed)
                values.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    CompletedKey));
            return new ResultBuffer(values.ToArray());
        }
    }
}

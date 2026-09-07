using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Infra;

namespace UNCAD.Features.XLayout
{
    /// <summary>Identifies yellow duplicate-circuit frames created by XLAYOUT.</summary>
    internal static class XLayoutDuplicateMarker
    {
        internal const string ApplicationName = "UNCAD_XLAYOUT";
        private const string Kind = "DUPLICATE_CIRCUIT";

        internal static bool EnsureApplication(Database database, Transaction transaction)
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
            catch { return false; }
        }

        internal static void Set(Entity entity, string sourceHandle)
        {
            if (entity == null || string.IsNullOrWhiteSpace(sourceHandle)) return;
            try
            {
                entity.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, ApplicationName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, Kind),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                        sourceHandle.Trim()));
            }
            catch (Exception ex)
            {
                // Marker metadata is optional; the yellow geometry remains valid.
                // But a silent swallow here makes stale markers unremovable, so log it.
                Log.Warn("XLAYOUT 重复回路标记元数据写入失败: " + ex.Message);
            }
        }

        internal static bool IsMarker(Entity entity)
        {
            return TryGetSourceHandle(entity, out _);
        }

        internal static bool TryGetSourceHandle(Entity entity, out string sourceHandle)
        {
            sourceHandle = "";
            if (entity == null) return false;
            try
            {
                using (ResultBuffer data = entity.GetXDataForApplication(ApplicationName))
                {
                    if (data == null) return false;
                    var values = new List<string>();
                    foreach (TypedValue value in data)
                        if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                            values.Add(Convert.ToString(value.Value,
                                CultureInfo.InvariantCulture) ?? "");
                    int kind = values.FindIndex(value =>
                        string.Equals(value, Kind, StringComparison.OrdinalIgnoreCase));
                    if (kind < 0 || kind + 1 >= values.Count) return false;
                    sourceHandle = values[kind + 1].Trim();
                    return sourceHandle.Length > 0;
                }
            }
            catch { return false; }
        }
    }
}

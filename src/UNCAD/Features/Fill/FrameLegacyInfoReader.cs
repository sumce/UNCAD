using System;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 旧图框没有 frameinfo_json 记录时的"上次更新时间"回退:读制图信息表
    /// 日期列(yyyy-MM-dd)。仅用于 U1U 更新对比展示,不写回图纸。
    /// </summary>
    internal static class FrameLegacyInfoReader
    {
        internal static string ReadLastUpdatedText(Transaction transaction,
            FillSelection selection)
        {
            foreach (ObjectId id in selection?.DrawingInfoTableIds ?? new ObjectId[0])
            {
                var table = transaction.GetObject(id, OpenMode.ForRead, true) as Table;
                int headerRow;
                if (table == null
                    || !CadTableLayoutClassifier.TryFindCurrentDrawingInfoHeader(table,
                        out headerRow)
                    || headerRow + 1 >= table.Rows.Count
                    || table.Columns.Count <= 4) continue;
                string value = (table.Cells[headerRow + 1, 4].TextString ?? "").Trim();
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsed))
                    return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return "";
        }

        internal static string ReadLastUpdatedText(global::UNCAD.Cad.CadContext ctx,
            FillSelection selection)
        {
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                string result = ReadLastUpdatedText(transaction, selection);
                transaction.Commit();
                return result;
            }
        }
    }
}

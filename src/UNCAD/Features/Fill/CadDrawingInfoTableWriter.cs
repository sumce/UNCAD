using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Features.Fill
{
#pragma warning disable 618 // AutoCAD 2022 direct Table setters avoid stale Cell wrappers.
    internal static class CadDrawingInfoTableWriter
    {
        internal static bool IsDrawingInfoTable(Table table)
            => CadTableLayoutClassifier.IsDrawingInfoTable(table);

        internal static bool IsCurrentDrawingInfoTable(Table table)
            => CadTableLayoutClassifier.IsCurrentDrawingInfoTable(table);

        internal static bool RepairSplitFloorLayout(Transaction transaction, ObjectId id)
        {
            Table table = transaction.GetObject(id, OpenMode.ForWrite, true) as Table;
            if (table == null || table.Columns.Count != 7
                || !CadTableLayoutClassifier.TryFindDrawingInfoHeader(table,
                    out int headerRow)
                || CadTableLayoutClassifier.IsCurrentDrawingInfoTable(table)) return false;

            int valueRow = headerRow + 1;
            string deviceFloor = CellText(table, valueRow, 1);
            string upstreamFloor = CellText(table, valueRow, 2);
            string floor = JoinFloors(deviceFloor, upstreamFloor);
            string drafter = CellText(table, valueRow, 3);
            string reviewer = CellText(table, valueRow, 4);
            string date = CellText(table, valueRow, 5);
            string version = CellText(table, valueRow, 6);

            table.SetTextString(headerRow, 1, "楼层");
            table.SetTextString(headerRow, 2, "制图");
            table.SetTextString(headerRow, 3, "审核");
            table.SetTextString(headerRow, 4, "日期");
            table.SetTextString(headerRow, 5, "版本");
            table.SetTextString(headerRow, 6, "");
            if (valueRow < table.Rows.Count)
            {
                table.SetTextString(valueRow, 1, floor);
                table.SetTextString(valueRow, 2, drafter);
                table.SetTextString(valueRow, 3, reviewer);
                table.SetTextString(valueRow, 4, date);
                table.SetTextString(valueRow, 5, version);
                table.SetTextString(valueRow, 6, "");
            }
            table.RecordGraphicsModified(true);
            return true;
        }

        /// <summary>
        /// 写制图信息表的值行（D-057：六列内容来自模板
        /// <see cref="DrawingInfoTemplates"/>）。模板留空的列不写——图框里现有的
        /// 内容保留，可以手工在图框里改。默认模板与 3.5.0 行为一致：楼层 =
        /// 设备楼层/上游楼层、日期 = 运行当天，专业/制图/审核/版本不碰。
        /// <paramref name="now"/> 为 UTC；表里的日期/时间按本机时区显示。
        /// previous/commandName 决定 (U1F创建时间)/(U1U更新时间) 的取值，与写进
        /// frameinfo_json 的时间同源。
        /// </summary>
        internal static void Write(Transaction transaction, ObjectId[] tableIds,
            MachineRow machine, DateTime now, DrawingInfoTemplates templates = null,
            FrameInfoJsonRecord previous = null, string commandName = "U1F")
        {
            DrawingInfoTemplates effective = templates ?? DrawingInfoTemplates.Default;
            DrawingInfoContext context = DrawingInfoContext.ForRun(machine, previous,
                commandName, now);
            IReadOnlyList<string> values = DrawingInfoTemplateRenderer.Render(
                effective, context);

            foreach (ObjectId id in tableIds ?? Array.Empty<ObjectId>())
            {
                Table table = transaction.GetObject(id, OpenMode.ForWrite, true) as Table;
                if (!CadTableLayoutClassifier.TryFindCurrentDrawingInfoHeader(table,
                        out int headerRow)
                    || headerRow + 1 >= table.Rows.Count) continue;
                for (int column = 0; column < DrawingInfoTemplates.ColumnCount
                        && column < table.Columns.Count; column++)
                {
                    if (!effective.IsSet(column)) continue;
                    table.SetTextString(headerRow + 1, column, values[column]);
                }
                table.RecordGraphicsModified(true);
            }
        }

        private static string CellText(Table table, int row, int column)
            => row >= 0 && row < table.Rows.Count && column >= 0
                && column < table.Columns.Count
                ? TextParser.CleanMText(table.TextString(row, column) ?? "").Trim()
                : "";

        private static string JoinFloors(string deviceFloor, string upstreamFloor)
            => string.Join("/", new[] { deviceFloor, upstreamFloor }
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0));
    }
#pragma warning restore 618
}

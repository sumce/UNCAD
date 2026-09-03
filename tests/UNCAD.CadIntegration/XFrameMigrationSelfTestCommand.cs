using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
    public sealed class XFrameMigrationSelfTestCommand
    {
        [CommandMethod("UNCAD_XFRAME_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            var ctx = new CadContext(document);
            ObjectId frameId;
            ObjectId oldBoqTableId;
            Dictionary<string, string> beforeAttributes;
            int beforeInfoTables;
            int beforeBoqTables;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                frameId = FindFrame(database, transaction,
                    FrameRegionCollector.SupportedFrameName);
                oldBoqTableId = FindBoqTable(database, transaction);
                beforeAttributes = Attributes(transaction, frameId);
                beforeInfoTables = CountInfoTables(database, transaction);
                beforeBoqTables = CountBoqTables(database, transaction);
            }

            var selection = new FillSelection
            {
                SourceIds = new[] { frameId, oldBoqTableId },
                FrameBlockIds = new[] { frameId },
                TableIds = new[] { oldBoqTableId },
                DrawingInfoTableIds = Array.Empty<ObjectId>()
            };
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                XFrameMigrationResult result = XFrameMigrationService.Migrate(ctx,
                    transaction, new[] { selection });
                if (result.Frames != 1 || result.BoqTables != 1
                    || result.DrawingInfoTables != 1)
                    throw new InvalidOperationException("Unexpected migration counts.");
                if (DefinitionName(transaction, frameId) != FrameRegionCollector.XFrameName)
                    throw new InvalidOperationException("Legacy frame was not replaced.");

                Dictionary<string, string> afterAttributes = Attributes(transaction, frameId);
                foreach (KeyValuePair<string, string> item in beforeAttributes)
                    if (!afterAttributes.TryGetValue(item.Key, out string value)
                        || !string.Equals(value, item.Value, StringComparison.Ordinal))
                        throw new InvalidOperationException("Attribute was not preserved: "
                            + item.Key);
                if (!afterAttributes.TryGetValue(XFrameMigrationService.ProjectNameTag,
                    out string projectName) || projectName
                        != XFrameMigrationService.DefaultProjectName)
                    throw new InvalidOperationException("PROJECT_NAME default is incorrect.");
                if (selection.DrawingInfoTableIds.Length != 1)
                    throw new InvalidOperationException("Drawing info table was not returned.");
                if (selection.TableIds.Length != 1
                    || selection.TableIds[0] == oldBoqTableId)
                    throw new InvalidOperationException("BOQ table was not replaced.");
                Table oldBoq = transaction.GetObject(oldBoqTableId,
                    OpenMode.ForRead, true) as Table;
                if (oldBoq == null || !oldBoq.IsErased)
                    throw new InvalidOperationException("Old BOQ table was not erased.");
                Table boq = transaction.GetObject(selection.TableIds[0],
                    OpenMode.ForRead, true) as Table;
                if (!IsBoqTable(boq))
                    throw new InvalidOperationException("Cloned BOQ table has the wrong layout.");
                if (selection.SourceIds.Contains(oldBoqTableId)
                    || !selection.SourceIds.Contains(selection.TableIds[0])
                    || !selection.SourceIds.Contains(selection.DrawingInfoTableIds[0]))
                    throw new InvalidOperationException("Selection source IDs were not replaced.");
                Table info = transaction.GetObject(selection.DrawingInfoTableIds[0],
                    OpenMode.ForRead, true) as Table;
                if (!CadDrawingInfoTableWriter.IsDrawingInfoTable(info))
                    throw new InvalidOperationException("Cloned table has the wrong layout.");
                AssertCurrentInfoHeader(info);
                double[] columnWidths = Enumerable.Range(0, info.Columns.Count)
                    .Select(index => info.Columns[index].Width).ToArray();
                double[] rowHeights = Enumerable.Range(0, info.Rows.Count)
                    .Select(index => info.Rows[index].Height).ToArray();
                CadDrawingInfoTableWriter.Write(transaction,
                    selection.DrawingInfoTableIds, new MachineRow
                    {
                        DeviceFloor = "3F",
                        PanelFloor = "2F"
                    }, new DateTime(2026, 9, 3));
                if (info.Cells[1, 1].TextString != "3F/2F"
                    || info.Cells[1, 4].TextString != "2026-09-03")
                    throw new InvalidOperationException(
                        "Combined floors or date were written to the wrong columns.");
                if (!columnWidths.SequenceEqual(Enumerable.Range(0, info.Columns.Count)
                        .Select(index => info.Columns[index].Width))
                    || !rowHeights.SequenceEqual(Enumerable.Range(0, info.Rows.Count)
                        .Select(index => info.Rows[index].Height)))
                    throw new InvalidOperationException(
                        "Writing drawing information changed the table dimensions.");
                BlockReference frame = (BlockReference)transaction.GetObject(frameId,
                    OpenMode.ForRead);
                Point3d expected = new Point3d(1500, -82100.9316532705, 0)
                    .TransformBy(frame.BlockTransform);
                if (info.Position.DistanceTo(expected) > 0.01)
                    throw new InvalidOperationException("Drawing info table is misplaced.");
                Point3d expectedBoq = new Point3d(83402.8206691741, -1500, 0)
                    .TransformBy(frame.BlockTransform);
                if (boq.Position.DistanceTo(expectedBoq) > 0.01)
                    throw new InvalidOperationException("BOQ table is misplaced.");

                AttributeReference project = FindAttribute(transaction, frameId,
                    XFrameMigrationService.ProjectNameTag);
                project.UpgradeOpen();
                project.TextString = "CUSTOM PROJECT";

                XFrameMigrationResult repeated = XFrameMigrationService.Migrate(ctx,
                    transaction, new[] { selection });
                if (repeated.Frames != 0 || repeated.BoqTables != 0
                    || repeated.DrawingInfoTables != 0)
                    throw new InvalidOperationException("Repeated migration was not idempotent.");
                if (FindAttribute(transaction, frameId,
                    XFrameMigrationService.ProjectNameTag).TextString != "CUSTOM PROJECT")
                    throw new InvalidOperationException("Custom PROJECT_NAME was overwritten.");

                ObjectId oldInfoId = selection.DrawingInfoTableIds[0];
                info.UpgradeOpen();
                double originalFloorWidth = info.Columns[1].Width;
                info.InsertColumns(2, originalFloorWidth / 2d, 1);
                info.Columns[1].Width = originalFloorWidth / 2d;
                string[] splitHeader =
                {
                    "专业", "设备楼层", "上游楼层", "制图", "审核", "日期", "版本"
                };
                for (int column = 0; column < splitHeader.Length; column++)
                    info.Cells[0, column].TextString = splitHeader[column];
                info.Cells[1, 1].TextString = "3F";
                info.Cells[1, 2].TextString = "2F";
                info.Cells[1, 4].TextString = "CHECKER";
                double splitWidth = info.Width;
                XFrameMigrationResult infoUpgrade = XFrameMigrationService.Migrate(ctx,
                    transaction, new[] { selection });
                if (infoUpgrade.Frames != 0 || infoUpgrade.BoqTables != 0
                    || infoUpgrade.DrawingInfoTables != 1)
                    throw new InvalidOperationException(
                        "Legacy drawing-info table was not upgraded.");
                if (selection.DrawingInfoTableIds[0] != oldInfoId
                    || ((Table)transaction.GetObject(oldInfoId,
                        OpenMode.ForRead, true)).IsErased)
                    throw new InvalidOperationException(
                        "Legacy drawing-info table was not upgraded in place.");
                Table upgradedInfo = (Table)transaction.GetObject(
                    selection.DrawingInfoTableIds[0], OpenMode.ForRead);
                AssertCurrentInfoHeader(upgradedInfo);
                if (upgradedInfo.Cells[1, 1].TextString != "3F/2F"
                    || upgradedInfo.Cells[1, 3].TextString != "CHECKER"
                    || Math.Abs(upgradedInfo.Columns[1].Width - originalFloorWidth) > 0.01
                    || Math.Abs(upgradedInfo.Width - splitWidth) > 0.01)
                    throw new InvalidOperationException(
                        "Split drawing-info table was not restored without resizing.");
            }

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                if (DefinitionName(transaction, frameId)
                    != FrameRegionCollector.SupportedFrameName)
                    throw new InvalidOperationException("Aborted frame migration was retained.");
                if (CountInfoTables(database, transaction) != beforeInfoTables)
                    throw new InvalidOperationException("Aborted info table clone was retained.");
                if (CountBoqTables(database, transaction) != beforeBoqTables)
                    throw new InvalidOperationException("Aborted BOQ table replacement was retained.");
            }

            document.Editor.WriteMessage("\nUNCAD_XFRAME_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                File.WriteAllText(resultPath, "PASS");
        }

        private static void AssertCurrentInfoHeader(Table table)
        {
            string[] expected =
            {
                "专业", "楼层", "制图", "审核", "日期", "版本"
            };
            if (table == null || table.Columns.Count != expected.Length)
                throw new InvalidOperationException(
                    "Drawing-info table must contain six columns.");
            for (int column = 0; column < expected.Length; column++)
                if (TextParser.CleanMText(table.Cells[0, column].TextString).Trim()
                    != expected[column])
                    throw new InvalidOperationException(
                        "Unexpected drawing-info header at column " + column + ".");
        }

        private static ObjectId FindFrame(Database database, Transaction transaction,
            string name)
        {
            BlockTable blocks = (BlockTable)transaction.GetObject(database.BlockTableId,
                OpenMode.ForRead);
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
                if (transaction.GetObject(id, OpenMode.ForRead, true)
                        is BlockReference block
                    && !(block is Table)
                    && string.Equals(DefinitionName(transaction, id), name,
                        StringComparison.OrdinalIgnoreCase)) return id;
            throw new InvalidOperationException("Legacy frame was not found.");
        }

        private static string DefinitionName(Transaction transaction, ObjectId id)
        {
            BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                as BlockReference;
            if (block == null) return "";
            BlockTableRecord definition = transaction.GetObject(
                block.IsDynamicBlock ? block.DynamicBlockTableRecord
                    : block.BlockTableRecord, OpenMode.ForRead, true) as BlockTableRecord;
            return definition?.Name ?? "";
        }

        private static Dictionary<string, string> Attributes(Transaction transaction,
            ObjectId frameId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            BlockReference frame = (BlockReference)transaction.GetObject(frameId,
                OpenMode.ForRead);
            foreach (ObjectId id in frame.AttributeCollection)
            {
                AttributeReference attribute = transaction.GetObject(id,
                    OpenMode.ForRead, true) as AttributeReference;
                if (attribute != null) result[attribute.Tag ?? ""] = attribute.TextString ?? "";
            }
            return result;
        }

        private static AttributeReference FindAttribute(Transaction transaction,
            ObjectId frameId, string tag)
        {
            BlockReference frame = (BlockReference)transaction.GetObject(frameId,
                OpenMode.ForRead);
            foreach (ObjectId id in frame.AttributeCollection)
            {
                AttributeReference attribute = transaction.GetObject(id,
                    OpenMode.ForRead, true) as AttributeReference;
                if (attribute != null && string.Equals(attribute.Tag, tag,
                    StringComparison.OrdinalIgnoreCase)) return attribute;
            }
            throw new InvalidOperationException("Attribute was not found: " + tag);
        }

        private static int CountInfoTables(Database database, Transaction transaction)
        {
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId, OpenMode.ForRead);
            return space.Cast<ObjectId>().Count(id =>
                CadDrawingInfoTableWriter.IsDrawingInfoTable(
                    transaction.GetObject(id, OpenMode.ForRead, true) as Table));
        }

        private static ObjectId FindBoqTable(Database database, Transaction transaction)
        {
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
                if (IsBoqTable(transaction.GetObject(id, OpenMode.ForRead, true) as Table))
                    return id;
            throw new InvalidOperationException("Legacy BOQ table was not found.");
        }

        private static int CountBoqTables(Database database, Transaction transaction)
        {
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId, OpenMode.ForRead);
            return space.Cast<ObjectId>().Count(id => IsBoqTable(
                transaction.GetObject(id, OpenMode.ForRead, true) as Table));
        }

        private static bool IsBoqTable(Table table)
            => table != null && table.Rows.Count == 13 && table.Columns.Count >= 6
                && !CadDrawingInfoTableWriter.IsDrawingInfoTable(table);
    }
}

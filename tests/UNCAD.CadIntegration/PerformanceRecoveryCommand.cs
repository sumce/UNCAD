using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace UNCAD.CadIntegration
{
    public sealed class PerformanceRecoveryCommand
    {
        [CommandMethod("UNCAD_PERFORMANCE_RECOVERY")]
        public void Run()
        {
            Database database = Application.DocumentManager.MdiActiveDocument.Database;
            string output = Environment.GetEnvironmentVariable("UNCAD_PERFORMANCE_RECOVERY_OUTPUT");
            if (string.IsNullOrWhiteSpace(output) || File.Exists(output))
                throw new InvalidOperationException("A new recovery destination is required.");
            int removed = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
                ObjectId[] ids = space.Cast<ObjectId>().ToArray();
                var erase = new List<ObjectId>();
                var definitions = new HashSet<ObjectId>();
                int groupIndex = 0;
                for (int index = 0; index < ids.Length; index++)
                {
                    var first = transaction.GetObject(ids[index], OpenMode.ForRead) as BlockReference;
                    if (first == null || first is Table) continue;
                    var record = (BlockTableRecord)transaction.GetObject(first.BlockTableRecord, OpenMode.ForRead);
                    if (!record.Name.StartsWith("UNCAD_PERF_", StringComparison.Ordinal)) continue;
                    if (!Guid.TryParseExact(record.Name.Substring(11), "N", out _))
                        throw new InvalidOperationException("Unexpected fixture definition.");
                    string[][] tags = {
                        new[] { "MACHINEID-POWER", "MACHINEID-DEVICE" }, new[] { "DEVICENAME" },
                        new[] { "US", "US_NUMBER" }, new[] { "DS" }, new[] { "OTHER" }
                    };
                    int expected = groupIndex % 1000;
                    for (int part = 0; part < 5; part++)
                    {
                        var block = transaction.GetObject(ids[index + part], OpenMode.ForRead) as BlockReference;
                        if (block == null || block is Table || block.Position.DistanceTo(
                            new Point3d(100000000 + expected * 1000, 100000000, 0)) > 0.001)
                            throw new InvalidOperationException("Fixture insert does not match.");
                        var definition = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                        if (!definition.Name.StartsWith("UNCAD_PERF_", StringComparison.Ordinal))
                            throw new InvalidOperationException("Fixture definition does not match.");
                        string[] actual = definition.Cast<ObjectId>().Select(id =>
                            transaction.GetObject(id, OpenMode.ForRead) as AttributeDefinition)
                            .Where(attribute => attribute != null).Select(attribute => attribute.Tag).ToArray();
                        if (!actual.SequenceEqual(tags[part]))
                            throw new InvalidOperationException("Fixture tags do not match.");
                        definitions.Add(block.BlockTableRecord);
                    }
                    var boq = transaction.GetObject(ids[index + 5], OpenMode.ForRead) as Table;
                    var info = transaction.GetObject(ids[index + 6], OpenMode.ForRead) as Table;
                    var line = transaction.GetObject(ids[index + 7], OpenMode.ForRead) as Line;
                    var text = transaction.GetObject(ids[index + 8], OpenMode.ForRead) as DBText;
                    var start = new Point3d(1000000000 + expected * 1000, 1000000000, 0);
                    if (boq == null || boq.Rows.Count != 13 || boq.Columns.Count != 6
                        || boq.Cells[2, 2].TextString != "3*2.5" || boq.Cells[2, 4].TextString != "12.5"
                        || info == null || info.Rows.Count != 3 || info.Columns.Count != 6
                        || line == null || line.StartPoint.DistanceTo(start) > 0.001
                        || line.EndPoint.DistanceTo(start + new Vector3d(1000, 0, 0)) > 0.001
                        || text == null || text.TextString != (3000 + expected) + "mm"
                        || text.Position.DistanceTo(start + new Vector3d(500, 20, 0)) > 0.001)
                        throw new InvalidOperationException("Fixture table/line/text does not match.");
                    erase.AddRange(ids.Skip(index).Take(9));
                    groupIndex++;
                    index += 8;
                }
                if (groupIndex == 0 || groupIndex % 1000 != 0)
                    throw new InvalidOperationException("Incomplete fixture set: " + groupIndex);
                foreach (ObjectId id in erase)
                    transaction.GetObject(id, OpenMode.ForWrite).Erase();
                foreach (ObjectId id in definitions)
                    transaction.GetObject(id, OpenMode.ForWrite).Erase();
                removed = erase.Count;
                transaction.Commit();
            }
            database.SaveAs(output, DwgVersion.Current);
            File.WriteAllText(output + ".txt", "Removed only validated benchmark objects: " + removed);
            File.WriteAllText(Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT"), "PASS");
        }
    }
}

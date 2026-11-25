using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Features.Fill;
using UNCAD.Features.Submit;
using UNCAD.Cad.Frame;

namespace UNCAD.CadIntegration
{
    public sealed class CommandPerformanceSelfTestCommand
    {
        [CommandMethod("UNCAD_PERFORMANCE_SELFTEST")]
        public void Run()
        {
            try { RunCore(); }
            catch (System.Exception ex)
            {
                string resultPath = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
                if (!string.IsNullOrWhiteSpace(resultPath)) File.WriteAllText(resultPath, ex.ToString());
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + ex + "\n");
            }
        }

        private static void RunCore()
        {
            var ctx = new CadContext(Application.DocumentManager.MdiActiveDocument);
            var groups = new List<ObjectId[]>();
            var route = new List<ObjectId>();
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                ObjectId frame = Definition(ctx.Db, transaction,
                    Pair(FrameBlockFiller.TagPower, "PERF-POWER"),
                    Pair(FrameBlockFiller.TagDevice, "PERF-CIRCUIT"));
                ObjectId device = Definition(ctx.Db, transaction,
                    Pair(DeviceBlockFiller.TagDeviceName, "CIRCUIT"));
                ObjectId upstream = Definition(ctx.Db, transaction,
                    Pair(ConnectionBlockFiller.TagUpstreamAxis, "A/1"),
                    Pair("US_NUMBER", "PANEL"));
                ObjectId downstream = Definition(ctx.Db, transaction,
                    Pair(ConnectionBlockFiller.TagDownstreamAxis, "B/2"));
                ObjectId helper = Definition(ctx.Db, transaction, Pair("OTHER", "UNCHANGED"));
                using (var boq = new Table())
                using (var info = new Table())
                {
                    boq.SetSize(13, 6);
                    string[] row = { "1", "Cable", "3*2.5", "m", "12.5", "1.1" };
                    for (int column = 0; column < row.Length; column++)
                        boq.Cells[2, column].TextString = row[column];
                    info.SetSize(3, 6);
                    string[] header = { "\u4e13\u4e1a", "\u697c\u5c42", "\u5236\u56fe",
                        "\u5ba1\u6838", "\u65e5\u671f", "\u7248\u672c" };
                    for (int column = 0; column < header.Length; column++)
                        info.Cells[1, column].TextString = header[column];
                    for (int index = 0; index < 1000; index++)
                    {
                        var point = new Point3d(100000000 + index * 1000, 100000000, 0);
                        groups.Add(new[]
                        {
                            ctx.AddToCurrentSpace(transaction, new BlockReference(point, frame)),
                            ctx.AddToCurrentSpace(transaction, new BlockReference(point, device)),
                            ctx.AddToCurrentSpace(transaction, new BlockReference(point, upstream)),
                            ctx.AddToCurrentSpace(transaction, new BlockReference(point, downstream)),
                            ctx.AddToCurrentSpace(transaction, new BlockReference(point, helper)),
                            ctx.AddToCurrentSpace(transaction, (Table)boq.Clone()),
                            ctx.AddToCurrentSpace(transaction, (Table)info.Clone())
                        });
                        var start = new Point3d(1000000000 + index * 1000, 1000000000, 0);
                        route.Add(ctx.AddToCurrentSpace(transaction,
                            new Line(start, start + new Vector3d(1000, 0, 0))));
                        ctx.AddToCurrentSpace(transaction, new DBText
                        {
                            Position = start + new Vector3d(500, 20, 0),
                            TextString = (3000 + index) + "mm", Height = 10
                        });
                    }
                }
                transaction.Commit();
            }

            var report = new List<string>();
            Measure(report, "Split 1000 frame groups", () =>
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var definitions = new CadBlockDefinitionReader(transaction);
                    foreach (ObjectId[] ids in groups)
                    {
                        FillSelection selection = FillSelectionCollector.Split(transaction, ids, true, definitions);
                        Require(selection.FrameBlockIds.SequenceEqual(new[] { ids[0] }), "frame");
                        Require(selection.DeviceBlockIds.SequenceEqual(new[] { ids[1] }), "device");
                        Require(selection.UpstreamAxisBlockIds.SequenceEqual(new[] { ids[2] }), "upstream");
                        Require(selection.DownstreamAxisBlockIds.SequenceEqual(new[] { ids[3] }), "downstream");
                        Require(selection.DeviceColorBlockIds.SequenceEqual(new[] { ids[1], ids[3] }), "device colors");
                        Require(selection.UpstreamColorBlockIds.SequenceEqual(new[] { ids[2] }), "upstream colors");
                        Require(selection.TableIds.SequenceEqual(new[] { ids[5] }), "BOQ table");
                        Require(selection.DrawingInfoTableIds.SequenceEqual(new[] { ids[6] }), "info table");
                    }
                }
            });
            Measure(report, "Read 1000 submission records", () =>
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var definitions = new CadBlockDefinitionReader(transaction);
                    foreach (ObjectId[] ids in groups)
                    {
                        var record = FrameIdentityReader.Read(ctx, transaction, ids, false, definitions);
                        Require(record.MachineId == "PERF" && record.DeviceName == "CIRCUIT", "identity");
                        Require(record.CableMeters == "12.5" && record.Materials.Count == 1, "quantity");
                        Require(record.UpstreamAxis == "A/1" && record.DownstreamAxis == "B/2", "axis values");
                    }
                }
            });
            // The XSTS detail export must retain a drawn item without a catalog code.
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var table = (Table)transaction.GetObject(groups[0][5], OpenMode.ForWrite);
                string[] manual = { "manual", "Custom item", "User model", "set", "0", "CUSTOM-01" };
                for (int column = 0; column < manual.Length; column++)
                    table.Cells[3, column].TextString = manual[column];
                transaction.Commit();
            }
            Measure(report, "Read 1000 XSTS frame lists", () =>
            {
                var selected = new List<XstsCircuitRecord>();
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    var definitions = new CadBlockDefinitionReader(transaction);
                    foreach (ObjectId[] ids in groups)
                    {
                        var source = CadSubmissionReader.Read(transaction, ids, definitions,
                            tablesOnly: true);
                        Require(source.Attributes.Count == 0, "XSTS must not reread block attributes");
                        Require(source.TableRows.Count == 13, "drawing info table must be excluded");
                        Require(source.TableReadErrors.Count == 0, "XSTS table cell read");
                        var frame = new XstsCircuitRecord("PERF", "CIRCUIT")
                        {
                            FrameHandle = ids[0].Handle.ToString()
                        };
                        frame.TableRows.AddRange(source.TableRows);
                        selected.Add(frame);
                    }
                }
                XstsReport details = XstsReportBuilder.Build(selected,
                    new[] { new XstsCircuitRecord("PERF", "CIRCUIT") });
                Require(details.FrameCount == groups.Count, "XSTS physical frame count");
                Require(details.SelectedCircuitCount == 1, "XSTS circuit deduplication");
                Require(details.Frames.Count == groups.Count, "XSTS one row per physical frame");
                Require(details.Frames.Sum(frame => frame.Items.Count) == groups.Count + 1,
                    "XSTS all drawn items");
                Require(details.Frames[0].Items.Count == 2, "XSTS same-frame items");
                Require(details.Frames[0].Items[0].Model == "3*2.5", "XSTS cable model");
                Require(details.Frames[0].Items[1].Code == "CUSTOM-01"
                    && details.Frames[0].Items[1].Quantity == "0", "XSTS custom item and zero quantity");
                Require(details.Frames[0].FrameHandle == groups[0][0].Handle.ToString(),
                    "XSTS frame ID");
                Require(details.Frames.All(frame => frame.Cells.Count == details.MaterialColumns.Count),
                    "XSTS material column alignment");
                int customColumn = details.Frames[0].Cells.FindIndex(item => item?.Code == "CUSTOM-01");
                Require(customColumn >= 0 && details.Frames[1].Cells[customColumn] == null,
                    "XSTS absent material stays blank");
            });
            Measure(report, "Scan 1000 connected line labels", () =>
            {
                var segments = QuickLineCadService.Scan(ctx,
                    new QuickLineScanOptions { RootLineId = route[0] });
                Require(segments.Count == route.Count, "connected component: "
                    + segments.Count + " / " + route.Count + "; root=" + route[0].Handle);
                for (int index = 0; index < route.Count; index++)
                    Require(segments[index].LineId == route[index]
                        && segments[index].LabelMillimetres == 3000 + index, "line label");
            });

            string text = string.Join(Environment.NewLine, report);
            ctx.Write("\n" + text + "\n");
            string reportPath = Environment.GetEnvironmentVariable("UNCAD_CAD_PERFORMANCE_REPORT");
            if (!string.IsNullOrWhiteSpace(reportPath)) File.WriteAllText(reportPath, text);
            string resultPath = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath)) File.WriteAllText(resultPath, "PASS");
        }

        private static void Measure(List<string> report, string name, Action action)
        {
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nMeasuring: " + name + "\n");
            action();
            var samples = new List<long>();
            for (int index = 0; index < 3; index++)
            {
                var timer = Stopwatch.StartNew();
                action();
                samples.Add(timer.ElapsedMilliseconds);
            }
            samples.Sort();
            report.Add(name + ": " + samples[1] + " ms (median of 3)");
        }

        private static ObjectId Definition(Database database, Transaction transaction,
            params KeyValuePair<string, string>[] attributes)
        {
            var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForWrite);
            var definition = new BlockTableRecord { Name = "UNCAD_PERF_" + Guid.NewGuid().ToString("N") };
            ObjectId id = blocks.Add(definition);
            transaction.AddNewlyCreatedDBObject(definition, true);
            for (int index = 0; index < 40; index++)
            {
                var line = new Line(new Point3d(index, 0, 0), new Point3d(index, 10, 0));
                definition.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
            }
            foreach (var pair in attributes)
            {
                var attribute = new AttributeDefinition
                {
                    Tag = pair.Key, TextString = pair.Value, Height = 1
                };
                definition.AppendEntity(attribute);
                transaction.AddNewlyCreatedDBObject(attribute, true);
            }
            return id;
        }

        private static KeyValuePair<string, string> Pair(string tag, string value)
            => new KeyValuePair<string, string>(tag, value);

        private static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Performance regression: " + name);
        }
    }
}

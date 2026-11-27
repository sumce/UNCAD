using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_PLOT_SURVEY（诊断，只读）：导出「CAD 里看得见、出图会丢」的实体事实——
    /// 图层不打印（LayerTableRecord.IsPlottable=false）、图层/实体颜色是 255 号白或
    /// 真彩白（白纸上不可见），以及擦除遮罩（Wipeout）范围。只记事实，不做判定。
    /// 现场口径：同一图框在 CAD 里线齐全、U1PDF 出的 PDF 缺线（2026-11-26）。
    /// 环境变量：UNCAD_SURVEY_OUT=导出文本路径。始终报 PASS。
    /// </summary>
    public sealed class PlotSurveyProbeCommand
    {
        [CommandMethod("UNCAD_PLOT_SURVEY")]
        public void Run()
        {
            string result = "PASS";
            try { RunCore(); }
            catch (System.Exception ex) { result = ex.ToString(); }
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, result);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + result + "\n");
        }

        private static void RunCore()
        {
            string output = Environment.GetEnvironmentVariable("UNCAD_SURVEY_OUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException(
                "FAILED: UNCAD_SURVEY_OUT is not set");
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database db = document.Database;
            var builder = new StringBuilder();
            string fixReport = string.Equals(
                Environment.GetEnvironmentVariable("UNCAD_SURVEY_FIX"), "1",
                StringComparison.Ordinal) ? RunGovernorFix(document) : "";
            var layerRows = new List<string>();
            var layerLookup = new Dictionary<ObjectId, LayerTableRecord>();
            var whiteCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var whiteSamples = new List<string>();
            var nonPlotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var nonPlotSamples = new List<string>();
            var wipeouts = new List<string>();
            var upstreamSamples = new List<string>();
            int entities = 0;
            int blocks = 0;
            int blockWhite = 0;
            int blockNonPlot = 0;
            var blockFindings = new Dictionary<string, int>(StringComparer.Ordinal);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)tr.GetObject(db.LayerTableId,
                    OpenMode.ForRead, false);
                foreach (ObjectId layerId in layerTable)
                {
                    var layer = tr.GetObject(layerId, OpenMode.ForRead, false)
                        as LayerTableRecord;
                    if (layer == null) continue;
                    layerLookup[layerId] = layer;
                    Color layerColor = layer.Color;
                    bool layerWhite = IsWhite(layerColor);
                    if (!layer.IsPlottable || layerWhite
                        || layer.Name.StartsWith("U_", StringComparison.OrdinalIgnoreCase))
                    {
                        layerRows.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}\tcolor={1}\tplot={2}\toff={3}\tfrozen={4}\tlocked={5}",
                            layer.Name, DescribeColor(layerColor), layer.IsPlottable,
                            layer.IsOff, layer.IsFrozen, layer.IsLocked));
                    }
                }

                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId,
                    OpenMode.ForRead, false);
                builder.AppendLine("Drawing: " + document.Name);
                builder.AppendLine("Space: " + (space.IsLayout ? "Layout" : "Model") + " " + space.Name);
                builder.AppendLine("CECOLOR: " + DescribeColor(db.Cecolor));
                if (fixReport.Length > 0) builder.AppendLine(fixReport);

                foreach (ObjectId id in space)
                {
                    if (id.IsNull || !id.IsValid || id.IsErased) continue;
                    Entity entity;
                    try { entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                    catch { continue; }
                    if (entity == null || entity.IsErased) continue;
                    entities++;
                    LayerTableRecord layer = null;
                    layerLookup.TryGetValue(entity.LayerId, out layer);
                    string spot = DescribeSpot(entity);
                    Color own = entity.Color;
                    Color effective = ResolveEffective(own, layer);
                    if (IsWhite(effective))
                    {
                        string key = (layer != null ? layer.Name : "?") + "\t"
                            + entity.GetType().Name + "\t" + DescribeColor(effective);
                        whiteCounts.TryGetValue(key, out int count);
                        whiteCounts[key] = count + 1;
                        if (whiteSamples.Count < 300)
                            whiteSamples.Add(entity.Handle + "\t" + entity.GetType().Name
                                + "\t" + (layer != null ? layer.Name : "?")
                                + "\t" + DescribeColor(own) + "\t" + DescribeColor(effective)
                                + "\t" + spot);
                    }
                    if (layer != null && !layer.IsPlottable)
                    {
                        string key = layer.Name + "\t" + entity.GetType().Name;
                        nonPlotCounts.TryGetValue(key, out int count);
                        nonPlotCounts[key] = count + 1;
                        if (nonPlotSamples.Count < 200)
                            nonPlotSamples.Add(entity.Handle + "\t" + entity.GetType().Name
                                + "\t" + layer.Name + "\t" + spot);
                    }
                    if (entity is Wipeout wipeout)
                    {
                        wipeouts.Add(wipeout.Handle + "\t" + (layer != null ? layer.Name : "?")
                            + "\t" + spot);
                    }
                    var block = entity as BlockReference;
                    if (block != null)
                    {
                        blocks++;
                        foreach (ObjectId attributeId in block.AttributeCollection)
                        {
                            var attribute = tr.GetObject(attributeId,
                                OpenMode.ForRead, false) as AttributeReference;
                            if (attribute == null
                                || !string.Equals(attribute.Tag, "UPSTREAM_INFO",
                                    StringComparison.OrdinalIgnoreCase))
                                continue;
                            string first = (attribute.TextString ?? "")
                                .Split(new[] { "\\P" }, StringSplitOptions.None)
                                .Select(line => line.Trim())
                                .FirstOrDefault(line => line.Length > 0) ?? "";
                            if (upstreamSamples.Count < 40)
                                upstreamSamples.Add(block.Handle + "\t" + first);
                        }
                        var definition = tr.GetObject(block.BlockTableRecord,
                            OpenMode.ForRead, false) as BlockTableRecord;
                        if (definition != null && !definition.IsAnonymous
                            && !definition.Name.StartsWith("*", StringComparison.Ordinal))
                        {
                            int white = 0;
                            int nonPlot = 0;
                            foreach (ObjectId subId in definition)
                            {
                                var sub = tr.GetObject(subId, OpenMode.ForRead, false) as Entity;
                                if (sub == null) continue;
                                LayerTableRecord subLayer = null;
                                layerLookup.TryGetValue(sub.LayerId, out subLayer);
                                if (IsWhite(ResolveEffective(sub.Color, subLayer))) white++;
                                if (subLayer != null && !subLayer.IsPlottable) nonPlot++;
                            }
                            if (white > 0 || nonPlot > 0)
                            {
                                string key = definition.Name + "\twhite=" + white
                                    + "\tnonPlot=" + nonPlot;
                                blockFindings.TryGetValue(key, out int count);
                                blockFindings[key] = count + 1;
                                blockWhite += white;
                                blockNonPlot += nonPlot;
                            }
                        }
                    }
                }
                tr.Commit();
            }

            builder.AppendLine("Entities in space: " + entities + ", block references: " + blocks);
            builder.AppendLine();
            builder.AppendLine("== Layers (plot=false / color=255 or true white / U_*) ==");
            foreach (string row in layerRows.OrderBy(item => item, StringComparer.Ordinal))
                builder.AppendLine(row);
            builder.AppendLine();
            builder.AppendLine("== Entity effective white (255 or true white): "
                + whiteCounts.Values.Sum() + " ==");
            foreach (var pair in whiteCounts.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal))
                builder.AppendLine(pair.Value + "\t" + pair.Key);
            builder.AppendLine();
            builder.AppendLine("== White samples (max 300): handle, type, layer, own, effective, spot ==");
            foreach (string row in whiteSamples) builder.AppendLine(row);
            builder.AppendLine();
            builder.AppendLine("== Entity on non-plottable layers: "
                + nonPlotCounts.Values.Sum() + " ==");
            foreach (var pair in nonPlotCounts.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal))
                builder.AppendLine(pair.Value + "\t" + pair.Key);
            builder.AppendLine();
            builder.AppendLine("== Non-plot samples (max 200) ==");
            foreach (string row in nonPlotSamples) builder.AppendLine(row);
            builder.AppendLine();
            builder.AppendLine("== UPSTREAM_INFO samples (first line, max 40): "
                + upstreamSamples.Count + " ==");
            foreach (string row in upstreamSamples) builder.AppendLine(row);
            builder.AppendLine();
            builder.AppendLine("== Wipeouts: " + wipeouts.Count + " ==");
            foreach (string row in wipeouts) builder.AppendLine(row);
            builder.AppendLine();
            builder.AppendLine("== Block definitions with white/non-plot content ==");
            foreach (var pair in blockFindings.OrderByDescending(item => item.Value))
                builder.AppendLine(pair.Value + "\t" + pair.Key);
            builder.AppendLine("block white sub-entities: " + blockWhite
                + ", non-plot sub-entities: " + blockNonPlot);
            File.WriteAllText(output, builder.ToString(), Encoding.UTF8);
        }

        private static string RunGovernorFix(Document document)
        {
            var ctx = new CadContext(document);
            var scanned = QuickLineCadService.Scan(ctx);
            using (Transaction tr = document.Database.TransactionManager.StartTransaction())
            {
                LayerGovernorPlan plan = LayerGovernorService.Plan(ctx, tr,
                    scanned, null, null);
                int moved = LayerGovernorService.Apply(ctx, tr, plan);
                tr.Commit();
                return "FIX: scanned=" + scanned.Count
                    + ", moves=" + plan.Moves.Count
                    + ", whitePlanned=" + plan.WhiteColorFixes.Count
                    + ", whiteReported=" + plan.Counters.PlotWhiteEntities
                    + ", moved=" + moved;
            }
        }

        private static bool HasU1lx(Entity entity)
        {
            try
            {
                using (ResultBuffer data = entity.GetXDataForApplication("UNCAD_U1LX"))
                    return data != null;
            }
            catch
            {
                return false;
            }
        }

        private static Color ResolveEffective(Color own, LayerTableRecord layer)
        {
            if (own == null) return null;
            if (own.ColorMethod == ColorMethod.ByLayer)
                return layer != null ? layer.Color : null;
            return own;
        }

        /// <summary>
        /// 白纸上打印不可见的白色：255 号白（ACI 255）与真彩白。7 号色是
        /// 「黑/白自适应」，白纸出图变黑，绝不能算进来。
        /// </summary>
        private static bool IsWhite(Color color)
        {
            if (color == null) return false;
            if (color.ColorMethod == ColorMethod.ByAci && color.ColorIndex == 255) return true;
            if (color.ColorMethod == ColorMethod.ByColor && Rgb(color) == 0xFFFFFF) return true;
            return false;
        }

        private static string DescribeColor(Color color)
        {
            if (color == null) return "null";
            try
            {
                return color.ColorMethod + "/" + color.ColorIndex
                    + "/#" + Rgb(color).ToString("X6", CultureInfo.InvariantCulture);
            }
            catch
            {
                return color.ColorMethod.ToString();
            }
        }

        private static int Rgb(Color color)
            => (color.Red << 16) | (color.Green << 8) | color.Blue;

        private static string DescribeSpot(Entity entity)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                Point3d min = extents.MinPoint;
                Point3d max = extents.MaxPoint;
                return "x=" + Num(min.X) + ".." + Num(max.X)
                    + " y=" + Num(min.Y) + ".." + Num(max.Y)
                    + " z=" + Num(min.Z) + ".." + Num(max.Z);
            }
            catch
            {
                return "extents?";
            }
        }

        private static string Num(double value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

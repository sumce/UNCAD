using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 探针 v5：最后两个问题
    /// (1) 在布局自身的打印设置上出图后，用 Abort 回滚 vs 显式还原，哪一种能保证
    ///     用户设置与图纸"未修改"状态都不受影响；
    /// (2) A3 产出到底是不是空图 —— 检查页数、内容流长度、路径算子、颜色算子。
    /// </summary>
    public sealed class PlotVariantProbeCommand
    {
        private const string Device = "DWG To PDF.pc3";
        private const string A3 = "ISO_A3_(420.00_x_297.00_MM)";
        private static readonly Extents2d Window =
            new Extents2d(50000d, 30000d, 50420d, 30297d);
        private static readonly Regex MediaBoxPattern = new Regex(
            @"/MediaBox\s*\[\s*(-?[0-9.]+)\s+(-?[0-9.]+)\s+(-?[0-9.]+)\s+(-?[0-9.]+)\s*\]",
            RegexOptions.Compiled);
        private static readonly Regex PagePattern = new Regex(
            @"/Type\s*/Page(?![s])", RegexOptions.Compiled);
        private static readonly Regex ColorPattern = new Regex(
            @"(-?[0-9]*\.?[0-9]+)\s+(-?[0-9]*\.?[0-9]+)\s+(-?[0-9]*\.?[0-9]+)\s+(RG|rg)",
            RegexOptions.Compiled);
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        [CommandMethod("UNCAD_PLOTPROBE")]
        public void Run()
        {
            var log = new List<string>();
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-plotprobe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                CreateGeometry(database);
                // 先另存一份，把"已修改"标志清零，之后才能看出到底是谁弄脏了图纸。
                database.SaveAs(Path.Combine(directory, "probe.dwg"), DwgVersion.Current);
                log.Add("另存后 DBMOD=" + Application.GetSystemVariable("DBMOD"));

                ObjectId layoutId = ModelLayoutId(database, out string original);
                log.Add("原始布局设置：" + original);

                log.Add("—— 变体 A：出图后 Abort 回滚 ——");
                Run(log, database, layoutId, Path.Combine(directory, "a.pdf"), true, false,
                    original);
                log.Add("    Abort 后 DBMOD=" + Application.GetSystemVariable("DBMOD"));
                log.Add("    Abort 后布局设置：" + Read(database, layoutId));

                log.Add("—— 变体 B：出图后显式还原并提交 ——");
                Run(log, database, layoutId, Path.Combine(directory, "b.pdf"), false, true,
                    original);
                log.Add("    还原后 DBMOD=" + Application.GetSystemVariable("DBMOD"));
                log.Add("    还原后布局设置：" + Read(database, layoutId));
            }
            catch (System.Exception ex)
            {
                log.Add("探针异常：" + ex);
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
                Write(log);
                document.Editor.WriteMessage("\nUNCAD_PLOTPROBE:DONE\n");
            }
        }

        private static void Run(List<string> log, Database database, ObjectId layoutId,
            string file, bool abort, bool restore, string original)
        {
            var text = new StringBuilder();
            var transaction = database.TransactionManager.StartTransaction();
            try
            {
                var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForWrite);
                var info = new PlotInfo { Layout = layoutId };
                PlotSettingsValidator validator = PlotSettingsValidator.Current;
                validator.SetPlotConfigurationName(layout, Device, null);
                validator.SetPlotPaperUnits(layout, PlotPaperUnit.Millimeters);
                validator.RefreshLists(layout);
                validator.SetCanonicalMediaName(layout, A3);
                validator.SetPlotWindowArea(layout, Window);
                validator.SetPlotType(layout,
                    Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                validator.SetPlotCentered(layout, true);
                validator.SetUseStandardScale(layout, true);
                validator.SetStdScaleType(layout, StdScaleType.ScaleToFit);
                layout.PlotPlotStyles = false;
                new PlotInfoValidator
                {
                    MediaMatchingPolicy = MatchingPolicy.MatchEnabled
                }.Validate(info);
                text.Append("校验后介质=")
                    .Append(info.ValidatedSettings == null ? "<null>"
                        : info.ValidatedSettings.CanonicalMediaName).Append(' ');
                Plot(info, file);
                text.Append("plot=OK ");
                if (abort) transaction.Abort();
                else
                {
                    if (restore) Restore(layout, original);
                    transaction.Commit();
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                text.Append("FAIL[").Append(ex.ErrorStatus).Append("]");
            }
            catch (System.Exception ex)
            {
                text.Append("FAIL[").Append(ex.GetType().Name).Append(':')
                    .Append(ex.Message).Append(']');
            }
            finally
            {
                try { transaction.Dispose(); } catch { }
            }
            text.Append(Describe(file));
            log.Add("    " + text);
        }

        /// <summary>把布局打印设置还原成原始快照（设备|介质|单位|类型|旋转|居中|标准比例|比例类型|打印样式|样式表）。</summary>
        private static void Restore(Layout layout, string snapshot)
        {
            string[] parts = snapshot.Split('|');
            if (parts.Length < 10) return;
            PlotSettingsValidator validator = PlotSettingsValidator.Current;
            validator.SetPlotConfigurationName(layout, parts[0], parts[1]);
            validator.SetPlotPaperUnits(layout,
                (PlotPaperUnit)Enum.Parse(typeof(PlotPaperUnit), parts[2]));
            validator.SetPlotRotation(layout,
                (PlotRotation)Enum.Parse(typeof(PlotRotation), parts[4]));
            validator.SetPlotCentered(layout, parts[5] == "True");
            validator.SetUseStandardScale(layout, parts[6] == "True");
            validator.SetStdScaleType(layout,
                (StdScaleType)Enum.Parse(typeof(StdScaleType), parts[7]));
            layout.PlotPlotStyles = parts[8] == "True";
            validator.SetPlotType(layout,
                (Autodesk.AutoCAD.DatabaseServices.PlotType)Enum.Parse(
                    typeof(Autodesk.AutoCAD.DatabaseServices.PlotType), parts[3]));
        }

        private static string Read(Database database, ObjectId layoutId)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForRead);
                string text = Snapshot(layout);
                transaction.Commit();
                return text;
            }
        }

        private static string Snapshot(PlotSettings settings)
            => string.Join("|", new[]
            {
                settings.PlotConfigurationName ?? "",
                settings.CanonicalMediaName ?? "",
                settings.PlotPaperUnits.ToString(),
                settings.PlotType.ToString(),
                settings.PlotRotation.ToString(),
                settings.PlotCentered.ToString(),
                settings.UseStandardScale.ToString(),
                settings.StdScaleType.ToString(),
                settings.PlotPlotStyles.ToString(),
                settings.CurrentStyleSheet ?? ""
            });

        private static void Plot(PlotInfo info, string file)
        {
            using (PlotEngine engine = PlotFactory.CreatePublishEngine())
            {
                engine.BeginPlot(null, null);
                engine.BeginDocument(info, "probe", null, 1, true, file);
                engine.BeginPage(new PlotPageInfo(), info, true, null);
                engine.BeginGenerateGraphics(null);
                engine.EndGenerateGraphics(null);
                engine.EndPage(null);
                engine.EndDocument(null);
                engine.EndPlot(null);
            }
        }

        private static string Describe(string file)
        {
            if (!File.Exists(file)) return "无产出";
            byte[] bytes = File.ReadAllBytes(file);
            string latin = Latin1.GetString(bytes);
            Match box = MediaBoxPattern.Match(latin);
            string size = box.Success
                ? (Num(box.Groups[3].Value) - Num(box.Groups[1].Value)).ToString(
                        "0.##", CultureInfo.InvariantCulture) + "×"
                    + (Num(box.Groups[4].Value) - Num(box.Groups[2].Value)).ToString(
                        "0.##", CultureInfo.InvariantCulture) + "pt"
                : "无 MediaBox";
            string content = ExtractStreams(bytes, latin);
            int colors = 0;
            var palette = new List<string>();
            foreach (Match match in ColorPattern.Matches(content))
            {
                double red = Num(match.Groups[1].Value);
                double green = Num(match.Groups[2].Value);
                double blue = Num(match.Groups[3].Value);
                if (Math.Abs(red - green) < 0.02d && Math.Abs(green - blue) < 0.02d) continue;
                colors++;
                string entry = red.ToString("0.##", CultureInfo.InvariantCulture) + ","
                    + green.ToString("0.##", CultureInfo.InvariantCulture) + ","
                    + blue.ToString("0.##", CultureInfo.InvariantCulture);
                if (!palette.Contains(entry) && palette.Count < 6) palette.Add(entry);
            }
            int paths = Count(content, " l\n") + Count(content, " l\r\n")
                + Count(content, " re\n") + Count(content, " re\r\n")
                + Count(content, " c\n") + Count(content, " c\r\n");
            int texts = Count(content, "Tj") + Count(content, "TJ");
            return "产出=" + bytes.Length + " 字节 页数=" + PagePattern.Matches(latin).Count
                + " MediaBox=" + size + " 内容流=" + content.Length + " 字节 路径算子="
                + paths + " 文字算子=" + texts + " 非灰度颜色=" + colors
                + (palette.Count == 0 ? "" : " [" + string.Join(" / ", palette.ToArray()) + "]");
        }

        private static string ExtractStreams(byte[] bytes, string latin)
        {
            var text = new StringBuilder();
            int index = 0;
            while (index < latin.Length)
            {
                int start = latin.IndexOf("stream", index, StringComparison.Ordinal);
                if (start < 0) break;
                if (start >= 3 && string.CompareOrdinal(latin, start - 3, "end", 0, 3) == 0)
                {
                    index = start + 6;
                    continue;
                }
                int dataStart = start + 6;
                if (dataStart < latin.Length && latin[dataStart] == '\r') dataStart++;
                if (dataStart < latin.Length && latin[dataStart] == '\n') dataStart++;
                int end = latin.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                if (end < 0) break;
                var raw = new byte[end - dataStart];
                Array.Copy(bytes, dataStart, raw, 0, raw.Length);
                text.Append(TryInflate(raw));
                index = end + 9;
            }
            return text.ToString();
        }

        private static string TryInflate(byte[] raw)
        {
            if (raw.Length > 2 && raw[0] == 0x78)
            {
                try
                {
                    using (var input = new MemoryStream(raw, 2, raw.Length - 2))
                    using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        inflate.CopyTo(output);
                        return Latin1.GetString(output.ToArray());
                    }
                }
                catch (System.Exception) { }
            }
            return Latin1.GetString(raw);
        }

        private static int Count(string text, string token)
        {
            int count = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(token, index, StringComparison.Ordinal);
                if (index < 0) return count;
                count++;
                index += token.Length;
            }
        }

        private static double Num(string value)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double parsed) ? parsed : 0d;

        private static void Write(List<string> log)
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_PLOT_PROBE_DUMP");
            if (string.IsNullOrWhiteSpace(path)) return;
            try { File.WriteAllText(path, string.Join("\r\n", log.ToArray()), Encoding.UTF8); }
            catch { }
        }

        private static ObjectId ModelLayoutId(Database database, out string snapshot)
        {
            snapshot = "";
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId,
                    OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var candidate = transaction.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    if (candidate == null || !candidate.ModelType) continue;
                    snapshot = Snapshot(candidate);
                    transaction.Commit();
                    return entry.Value;
                }
                transaction.Commit();
            }
            return ObjectId.Null;
        }

        private static void CreateGeometry(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId,
                    OpenMode.ForWrite);
                var points = new[]
                {
                    new Point3d(Window.MinPoint.X, Window.MinPoint.Y, 0d),
                    new Point3d(Window.MaxPoint.X, Window.MinPoint.Y, 0d),
                    new Point3d(Window.MaxPoint.X, Window.MaxPoint.Y, 0d),
                    new Point3d(Window.MinPoint.X, Window.MaxPoint.Y, 0d)
                };
                for (int index = 0; index < points.Length; index++)
                {
                    var line = new Line(points[index], points[(index + 1) % points.Length])
                    {
                        ColorIndex = 1
                    };
                    space.AppendEntity(line);
                    transaction.AddNewlyCreatedDBObject(line, true);
                }
                var circle = new Circle(new Point3d(50210d, 30150d, 0d), Vector3d.ZAxis, 80d)
                {
                    ColorIndex = 3
                };
                space.AppendEntity(circle);
                transaction.AddNewlyCreatedDBObject(circle, true);
                transaction.Commit();
            }
        }
    }
}

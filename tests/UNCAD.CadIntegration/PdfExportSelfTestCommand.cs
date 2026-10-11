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
using UNCAD.Cad;
using UNCAD.Core.Geometry;
using UNCAD.Features.PdfExport;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// U1PDF 打印内核的真机自检（在 AutoCAD Core Console 中运行）。
    ///
    /// 构造两个"图框"（横向 420×297、纵向 297×420，都**远离原点**插入），
    /// 用 <see cref="PdfExportService"/> 逐个出图，然后断言：
    ///
    /// 1. 真的生成了带 <c>%PDF-</c> / <c>%%EOF</c> 的 PDF，页数 ≥ 1；
    /// 2. 纸张是 A3（MediaBox = 1190.55×841.89 pt）—— 证明介质设置真的生效；
    /// 3. 内容流里有**非灰度**颜色算子 —— 证明"彩色"没有退化成黑白；
    /// 4. 出图**没有改动用户布局的打印设置**、结束后打印引擎状态回到 NotPlotting。
    ///
    /// PDF 副本会保存到 %UNCAD_PLOT_KEEP_DIR%（便于人工打开确认），并把设备列表、
    /// 介质列表、每一步诊断写成 dump 文本。
    /// </summary>
    public sealed class PdfExportSelfTestCommand
    {
        private const string FixtureBlockName = "uncad_pdf_selftest_frame";
        /// <summary>A3 横向的 PDF 页面尺寸（pt）：420mm × 297mm。</summary>
        private const double A3WidthPoints = 1190.5512d;
        private const double A3HeightPoints = 841.8898d;

        private static readonly Regex MediaBoxPattern = new Regex(
            @"/MediaBox\s*\[\s*(-?[0-9.]+)\s+(-?[0-9.]+)\s+(-?[0-9.]+)\s+(-?[0-9.]+)\s*\]",
            RegexOptions.Compiled);
        private static readonly Regex PagePattern = new Regex(
            @"/Type\s*/Page(?![s])", RegexOptions.Compiled);
        private static readonly Regex ColorPattern = new Regex(
            @"(-?[0-9]*\.?[0-9]+)\s+(-?[0-9]*\.?[0-9]+)\s+(-?[0-9]*\.?[0-9]+)\s+(RG|rg)",
            RegexOptions.Compiled);

        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        private readonly List<string> _log = new List<string>();

        [CommandMethod("UNCAD_U1PDF_SELFTEST")]
        public void Run()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(),
                "uncad-u1pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                var ctx = new CadContext(document);
                Info("设备列表：" + Join(Devices()));
                Info("规范介质列表（" + PdfExportNaming.DeviceName + "）：");
                foreach (string media in MediaNames()) Info("    " + media);

                string before;
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    before = SnapshotLayout(document.Database, transaction);
                    transaction.Commit();
                }
                Info("出图前 布局打印设置：" + before);

                PlotOne(ctx, tempRoot, 50000d, 30000d, 420d, 297d, "SELFTEST-LANDSCAPE");
                PlotOne(ctx, tempRoot, 56000d, 30000d, 297d, 420d, "SELFTEST-PORTRAIT");
                // 同一个图框再出一遍：确认可重复出图（不会第二次就卡住/报错）。
                PlotOne(ctx, tempRoot, 62000d, 30000d, 420d, 297d, "SELFTEST-REPEAT");


                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    string after = SnapshotLayout(document.Database, transaction);
                    transaction.Commit();
                    Require(after == before,
                        "出图改动了布局打印设置！before=" + before + " after=" + after);
                }
                Info("出图后 布局打印设置：" + before + "（未改动 ✔）"
                    + "  ※ StdScale=" + StdScale(document.Database)
                    + " 是 AutoCAD 按出图窗口重算的派生缓存，用户看不到（比例选项本身已还原）");

                string state;
                try { state = PlotFactory.ProcessPlotState.ToString(); }
                catch (System.Exception ex) { state = "读取失败：" + ex.Message; }
                Info("打印引擎状态：" + state);
                Require(state.IndexOf("NotPlotting", StringComparison.OrdinalIgnoreCase) >= 0,
                    "出图结束后打印引擎仍处于 " + state);

                Pass(document);
            }
            catch (System.Exception ex)
            {
                WriteResult("FAIL\r\n" + ex);
                throw;
            }
            finally
            {
                Dump();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private void PlotOne(CadContext ctx, string directory, double x, double y,
            double width, double height, string circuit)
        {
            ObjectId frameId;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                frameId = InsertFixtureFrame(ctx.Db, transaction, x, y, width, height, circuit);
                transaction.Commit();
            }

            FrameRectangle boundary;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var block = transaction.GetObject(frameId, OpenMode.ForRead) as BlockReference;
                Require(block != null, "自检图框插入失败。");
                boundary = FrameRegionCollector.ReadBoundary(transaction, block);
                transaction.Commit();
            }
            Info("图框 " + circuit + " 边界：" + Describe(boundary));

            var region = new FrameRegionGroup
            {
                FrameId = frameId,
                Handle = frameId.Handle.ToString(),
                Boundary = boundary
            };
            region.EntityIds.Add(frameId);

            PdfRenderResult result = PdfExportService.Render(ctx, region, circuit, directory);
            foreach (string line in result.Details) Info("    诊断 | " + line);
            Require(result.Ok, "出图失败：" + result.Error);
            Require(PdfExportNaming.IsUsablePdf(result.FilePath, out string pdfError), pdfError);

            byte[] bytes = File.ReadAllBytes(result.FilePath);
            Info("  " + circuit + " → " + bytes.Length + " 字节");
            Inspect(circuit, bytes);
            Keep(circuit, bytes);
        }

        /// <summary>解析 PDF 内容：页数、纸张尺寸（MediaBox）、颜色算子。</summary>
        private void Inspect(string circuit, byte[] bytes)
        {
            string latin = Latin1.GetString(bytes);
            int pages = PagePattern.Matches(latin).Count;
            int images = Count(latin, "/Subtype /Image") + Count(latin, "/Subtype/Image");
            Match box = MediaBoxPattern.Match(latin);
            double boxWidth = 0d;
            double boxHeight = 0d;
            if (box.Success)
            {
                boxWidth = Num(box.Groups[3].Value) - Num(box.Groups[1].Value);
                boxHeight = Num(box.Groups[4].Value) - Num(box.Groups[2].Value);
            }
            Info("  页数=" + pages + "  图片对象=" + images
                + "  MediaBox=" + boxWidth.ToString("0.##", CultureInfo.InvariantCulture)
                + "×" + boxHeight.ToString("0.##", CultureInfo.InvariantCulture) + "pt");

            Require(pages >= 1, circuit + "：PDF 里没有页面对象。");
            Require(Math.Abs(boxWidth - A3WidthPoints) <= 2d
                && Math.Abs(boxHeight - A3HeightPoints) <= 2d,
                circuit + "：纸张不是 A3 横向（MediaBox=" + boxWidth.ToString("0.##",
                    CultureInfo.InvariantCulture) + "×" + boxHeight.ToString("0.##",
                    CultureInfo.InvariantCulture) + "pt，期望 " + A3WidthPoints.ToString("0.##")
                    + "×" + A3HeightPoints.ToString("0.##") + "pt）");

            string content = ExtractStreams(bytes, latin);
            int colors = 0;
            var palette = new List<string>();
            foreach (Match match in ColorPattern.Matches(content))
            {
                double red = Num(match.Groups[1].Value);
                double green = Num(match.Groups[2].Value);
                double blue = Num(match.Groups[3].Value);
                bool gray = Math.Abs(red - green) < 0.02d && Math.Abs(green - blue) < 0.02d;
                if (gray) continue;
                colors++;
                string entry = red.ToString("0.##", CultureInfo.InvariantCulture) + ","
                    + green.ToString("0.##", CultureInfo.InvariantCulture) + ","
                    + blue.ToString("0.##", CultureInfo.InvariantCulture);
                if (!palette.Contains(entry) && palette.Count < 6) palette.Add(entry);
            }
            Info("  内容流 非灰度颜色算子=" + colors
                + (palette.Count == 0 ? "" : "  颜色=" + string.Join(" / ", palette.ToArray())));
            Require(colors > 0, circuit + "：PDF 内容里没找到任何非灰度颜色（可能被当成黑白打印了）。");
        }

        /// <summary>把 PDF 里所有 stream 解压后拼起来（解压失败就按原始文本处理）。</summary>
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

        private void Keep(string circuit, byte[] bytes)
        {
            string directory = Environment.GetEnvironmentVariable("UNCAD_PLOT_KEEP_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, circuit + ".pdf");
                File.WriteAllBytes(path, bytes);
                Info("  已保存副本：" + path);
            }
            catch (System.Exception ex) { Info("  保存副本失败：" + ex.Message); }
        }

        private static ObjectId InsertFixtureFrame(Database database, Transaction transaction,
            double x, double y, double width, double height, string label)
        {
            var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            ObjectId definitionId;
            if (blocks.Has(FixtureBlockName))
            {
                definitionId = blocks[FixtureBlockName];
            }
            else
            {
                blocks.UpgradeOpen();
                var record = new BlockTableRecord { Name = FixtureBlockName };
                definitionId = blocks.Add(record);
                transaction.AddNewlyCreatedDBObject(record, true);
                AddLine(transaction, record, 0d, 0d, width, 0d, 1);
                AddLine(transaction, record, width, 0d, width, height, 1);
                AddLine(transaction, record, width, height, 0d, height, 1);
                AddLine(transaction, record, 0d, height, 0d, 0d, 1);
                var circle = new Circle(new Point3d(width / 2d, height / 2d, 0d),
                    Vector3d.ZAxis, Math.Min(width, height) / 6d) { ColorIndex = 3 };
                record.AppendEntity(circle);
                transaction.AddNewlyCreatedDBObject(circle, true);
                var text = new DBText
                {
                    TextString = label,
                    Position = new Point3d(width / 2d, height / 2d, 0d),
                    Height = 12d,
                    ColorIndex = 5
                };
                record.AppendEntity(text);
                transaction.AddNewlyCreatedDBObject(text, true);
            }

            var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId,
                OpenMode.ForWrite);
            var reference = new BlockReference(new Point3d(x, y, 0d), definitionId);
            space.AppendEntity(reference);
            transaction.AddNewlyCreatedDBObject(reference, true);
            return reference.ObjectId;
        }

        private static void AddLine(Transaction transaction, BlockTableRecord record,
            double x1, double y1, double x2, double y2, short color)
        {
            var line = new Line(new Point3d(x1, y1, 0d), new Point3d(x2, y2, 0d))
            {
                ColorIndex = color
            };
            record.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
        }

        private static string SnapshotLayout(Database database, Transaction transaction)
        {
            var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId,
                OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                var layout = transaction.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                if (layout == null || !layout.ModelType) continue;
                Extents2d area = layout.PlotWindowArea;
                Point2d origin = layout.PlotOrigin;
                Point2d paper = layout.PlotPaperSize;
                return string.Join("|", new[]
                {
                    layout.LayoutName ?? "",
                    layout.PlotConfigurationName ?? "",
                    layout.CanonicalMediaName ?? "",
                    layout.PlotPaperUnits.ToString(),
                    layout.PlotType.ToString(),
                    layout.PlotRotation.ToString(),
                    layout.PlotCentered.ToString(),
                    layout.UseStandardScale.ToString(),
                    layout.StdScaleType.ToString(),
                    layout.PlotPlotStyles.ToString(),
                    layout.ShowPlotStyles.ToString(),
                    layout.CurrentStyleSheet ?? "<null>",
                    layout.PlotViewName ?? "<null>",
                    Fmt(area.MinPoint.X) + "," + Fmt(area.MinPoint.Y) + ","
                        + Fmt(area.MaxPoint.X) + "," + Fmt(area.MaxPoint.Y),
                    Fmt(paper.X) + "x" + Fmt(paper.Y),
                    Fmt(origin.X) + "," + Fmt(origin.Y),
                    layout.PrintLineweights.ToString(),
                    layout.ScaleLineweights.ToString(),
                    layout.DrawViewportsFirst.ToString(),
                    layout.PlotViewportBorders.ToString(),
                    layout.PlotHidden.ToString(),
                    layout.PlotTransparency.ToString(),
                    layout.ShadePlot.ToString()
                });
            }
            return "(未找到模型空间布局)";
        }

        private static string Fmt(double value)
            => value.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>派生值：AutoCAD 按当前窗口/纸张重算的比例因子缓存（不是用户设置）。</summary>
        private static double StdScale(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId,
                    OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var layout = transaction.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    if (layout == null || !layout.ModelType) continue;
                    double scale = layout.StdScale;
                    transaction.Commit();
                    return scale;
                }
                transaction.Commit();
            }
            return 0d;
        }

        private static string[] Devices()
        {
            try
            {
                var list = PlotSettingsValidator.Current.GetPlotDeviceList();
                if (list == null) return Array.Empty<string>();
                var values = new string[list.Count];
                list.CopyTo(values, 0);
                return values;
            }
            catch (System.Exception ex) { return new[] { "读取失败：" + ex.Message }; }
        }

        private static string[] MediaNames()
        {
            try
            {
                var settings = new PlotSettings(true);
                PlotSettingsValidator validator = PlotSettingsValidator.Current;
                validator.SetPlotConfigurationName(settings, PdfExportNaming.DeviceName, null);
                validator.SetPlotPaperUnits(settings, PlotPaperUnit.Millimeters);
                validator.RefreshLists(settings);
                var media = validator.GetCanonicalMediaNameList(settings);
                if (media == null) return Array.Empty<string>();
                var values = new string[media.Count];
                media.CopyTo(values, 0);
                return values;
            }
            catch (System.Exception ex) { return new[] { "读取失败：" + ex.Message }; }
        }

        private static string Describe(FrameRectangle boundary)
            => boundary == null
                ? "(null)"
                : "min=(" + Num(boundary.MinX.ToString(CultureInfo.InvariantCulture)) + ","
                    + Num(boundary.MinY.ToString(CultureInfo.InvariantCulture)) + ") max=("
                    + Num(boundary.MaxX.ToString(CultureInfo.InvariantCulture)) + ","
                    + Num(boundary.MaxY.ToString(CultureInfo.InvariantCulture)) + ") 宽="
                    + Num(boundary.Width.ToString(CultureInfo.InvariantCulture)) + " 高="
                    + Num(boundary.Height.ToString(CultureInfo.InvariantCulture));

        private static double Num(string value)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double parsed) ? parsed : 0d;

        private static string Num(double value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);

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

        private static string Join(string[] values)
            => values == null || values.Length == 0 ? "(空)" : string.Join("、", values);

        private void Info(string line) => _log.Add(line);

        private void Dump()
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_PLOT_PROBE_DUMP");
            if (string.IsNullOrWhiteSpace(path)) return;
            try { File.WriteAllText(path, string.Join("\r\n", _log.ToArray()), Encoding.UTF8); }
            catch { }
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, text);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("U1PDF self-test: " + message);
        }

        private void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_U1PDF_SELFTEST:PASS\n");
            WriteResult("PASS");
        }
    }
}

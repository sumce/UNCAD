using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FixtureSanitizer
{
    /// <summary>
    /// 把真实客户台账脱敏成可入库的测试夹具。
    ///
    /// 规则：
    ///  - 统一 U_ 表头和「回路名称」是结构 token，原样保留（保住 ExcelMachineReader 绑定）；
    ///    其余所有字符串（含公式缓存字符串、批注文本、sheet 名、docProps）经确定性映射
    ///    替换为 S001、S002…（同一原串 → 同一占位符，按首次出现顺序编号）。
    ///  - 数值/日期/公式单元格本体不动；合并单元格、列宽、样式结构天然保留（只换值）。
    ///  - docProps 全部替换为最小干净 XML；customXml/* 与 docProps/custom.xml 直接剔除。
    ///  - 原文件删除线行为 0 时，在统一表末尾追加一行复制的样本数据，回路名 S-STRIKE，
    ///    套带删除线字体，保证夹具覆盖 D-002 删除线规则。
    ///  - 写出后对最终 zip 内全部文本部件做关键词自检，命中即退出码 2。
    /// </summary>
    internal class Program
    {
        private static readonly string[] RequiredHeaders =
        {
            "U_区域", "U_机台ID", "U_设备楼层", "U_设备轴位", "U_上游编号",
            "U_上游楼层", "U_上游轴位", "U_配电信息", "U_电缆型号", "U_上游类型", "回路名称"
        };

        private readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private int _counter;

        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: FixtureSanitizer <input.xlsx> <output.xlsx> [--keywords 词1,词2,...]");
                return 64;
            }
            string input = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            List<string> keywords = new List<string>();
            foreach (string a in args.Skip(2))
            {
                if (!a.StartsWith("--keywords", StringComparison.OrdinalIgnoreCase)) continue;
                string payload = "";
                int sep = a.IndexOfAny(new[] { '=', ':' });
                if (sep >= 0) payload = a.Substring(sep + 1);
                foreach (string k in payload.Split(new[] { ',', ';', '|' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = k.Trim();
                    if (t.Length > 0) keywords.Add(t);
                }
            }
            if (!File.Exists(input)) { Console.Error.WriteLine("输入不存在: " + input); return 64; }
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var program = new Program();
            return program.Run(input, output, keywords);
        }

        private int Run(string input, string output, List<string> keywords)
        {
            Console.WriteLine("读取工作簿: " + input);
            IWorkbook workbook;
            using (FileStream fs = new FileStream(input, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                workbook = new XSSFWorkbook(fs);
            }

            // ---- 1) 先在原值上做统计（与 ExcelMachineReader 语义一致） ----
            UnifiedInfo info = AnalyzeUnifiedSheet(workbook);
            Console.WriteLine("U_ 统一表头命中: " + (info != null ? "是" : "否"));
            if (info == null)
            {
                Console.Error.WriteLine("未找到统一 U_ 数据表，拒绝生成夹具。");
                workbook.Close();
                return 3;
            }
            Console.WriteLine("统一表: sheet#" + info.SheetIndex + " \"" + info.SheetName
                + "\"，表头行=" + info.HeaderRow
                + "，回路列=" + ColLetter(info.CircuitColumn) + "，机台ID列=" + ColLetter(info.MachineIdColumn));
            Console.WriteLine("数据行数(回路名非空): " + info.DataRows);
            Console.WriteLine("删除线行数: " + info.StrikeoutRows);
            Console.WriteLine("预计解析行数(回路非空且未删且机台ID非空): " + info.ParsedRows);
            Console.WriteLine("机台数(去重): " + info.Machines.Count);
            foreach (string m in info.Machines)
                Console.WriteLine("  机台: " + m);

            // ---- 2) 公式缓存字符串与批注在 NPOI 阶段替换 ----
            // 注意：普通字符串单元格在此阶段一律不动。NPOI 的 SetCellValue 会向
            // sharedStrings 追加新条目而保留旧条目（孤儿表项会泄漏原文），
            // 因此 sharedStrings 表在 zip 级后处理里整表重写。
            var sanitizer = new CellSanitizer(MapValue);
            sanitizer.RewriteFormulaCachesAndComments(workbook);

            // ---- 3) sheet 名映射（供 zip 级后处理重命名，不在 NPOI 里改名以免触发公式重解析） ----
            var sheetNameMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < workbook.NumberOfSheets; i++)
            {
                string original = workbook.GetSheetName(i);
                string mapped = MapValue(original);
                sheetNameMap[original] = mapped;
                Console.WriteLine("sheet 重命名: \"" + original + "\" -> \"" + mapped + "\"");
            }

            // ---- 4) 删除线样本（原文件无删除线行时补一行，保证夹具覆盖 D-002） ----
            bool appended = false;
            if (info.StrikeoutRows == 0)
            {
                AppendStrikeoutSample(workbook, info, MapValue);
                appended = true;
                Console.WriteLine("已补一行删除线样本（回路名 S-STRIKE，带删除线字体，解析器应忽略该行）。");
            }

            // ---- 5) NPOI 写出到临时文件 ----
            string poiPath = output + ".poi.tmp";
            using (FileStream outFs = new FileStream(poiPath, FileMode.Create, FileAccess.Write))
            {
                workbook.Write(outFs);
            }
            workbook.Close();

            // ---- 6) zip 级清洗：sharedStrings / docProps / customXml / sheet 名 / 泄漏自检 ----
            try
            {
                int leaks = ScourZip(poiPath, output, sheetNameMap, keywords);
                if (leaks > 0) return 2;
            }
            finally
            {
                if (File.Exists(poiPath)) File.Delete(poiPath);
            }

            Console.WriteLine("映射字符串总数(去重): " + _map.Count);

            Console.WriteLine("夹具已写出: " + output
                + " (" + new FileInfo(output).Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes)");
            Console.WriteLine(appended ? "结果: 已补一行删除线样本" : "结果: 原文件本含删除线行");
            return 0;
        }

        // ------------------------------------------------------------------
        // 占位符映射
        // ------------------------------------------------------------------
        private string MapValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string trimmed = value.Trim();
            if (trimmed.StartsWith("U_", StringComparison.Ordinal) || trimmed == "回路名称")
                return value; // 结构 token：表头原样保留
            string mapped;
            if (_map.TryGetValue(value, out mapped)) return mapped;
            _counter++;
            mapped = "S" + _counter.ToString("D3", CultureInfo.InvariantCulture);
            _map[value] = mapped;
            return mapped;
        }

        // ------------------------------------------------------------------
        // 统一表分析（镜像 ExcelMachineReader.TryBindUnified / ReadBoundRows）
        // ------------------------------------------------------------------
        private sealed class UnifiedInfo
        {
            public int SheetIndex;
            public string SheetName;
            public int HeaderRow;
            public int CircuitColumn;
            public int MachineIdColumn;
            public int DataRows;
            public int StrikeoutRows;
            public int ParsedRows;
            public List<string> Machines = new List<string>();
        }

        private static string Normalize(string value)
        {
            return new string((value ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        }

        private static UnifiedInfo AnalyzeUnifiedSheet(IWorkbook workbook)
        {
            for (int sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
            {
                ISheet sheet = workbook.GetSheetAt(sheetIndex);
                int maxRow = Math.Min(50, sheet.LastRowNum);
                for (int rowIndex = 0; rowIndex <= maxRow; rowIndex++)
                {
                    IRow header = sheet.GetRow(rowIndex);
                    if (header == null) continue;
                    var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (string h in RequiredHeaders)
                        found[h] = -1;
                    for (int c = 0; c < header.LastCellNum; c++)
                    {
                        string text = Normalize(header.GetCell(c) == null
                            ? "" : CellText(header.GetCell(c)));
                        foreach (string h in RequiredHeaders)
                        {
                            if (found[h] >= 0 || !string.Equals(text, Normalize(h),
                                    StringComparison.OrdinalIgnoreCase)) continue;
                            if (h == "回路名称") continue; // 多列时保最左，先到先得
                            found[h] = c;
                        }
                        if (string.Equals(text, Normalize("回路名称"),
                                StringComparison.OrdinalIgnoreCase) && found["回路名称"] < 0)
                            found["回路名称"] = c;
                    }
                    if (found.Values.Any(v => v < 0)) continue;

                    var info = new UnifiedInfo
                    {
                        SheetIndex = sheetIndex,
                        SheetName = sheet.SheetName,
                        HeaderRow = rowIndex,
                        CircuitColumn = found["回路名称"],
                        MachineIdColumn = found["U_机台ID"]
                    };
                    var strikeoutByFont = new Dictionary<short, bool>();
                    var machines = new List<string>();
                    for (int r = rowIndex + 1; r <= sheet.LastRowNum; r++)
                    {
                        IRow row = sheet.GetRow(r);
                        if (row == null) continue;
                        ICell circuit = row.GetCell(info.CircuitColumn);
                        string circuitText = CellText(circuit);
                        if (string.IsNullOrWhiteSpace(circuitText)) continue;
                        info.DataRows++;
                        bool struck = HasStrikeout(workbook, circuit, strikeoutByFont);
                        if (struck) { info.StrikeoutRows++; continue; }
                        string machineId = CellText(row.GetCell(info.MachineIdColumn)).Trim();
                        if (machineId.Length == 0) continue;
                        info.ParsedRows++;
                        if (!machines.Contains(machineId, StringComparer.OrdinalIgnoreCase))
                            machines.Add(machineId);
                    }
                    info.Machines = machines;
                    return info;
                }
            }
            return null;
        }

        private static string CellText(ICell cell)
        {
            if (cell == null) return "";
            switch (cell.CellType)
            {
                case CellType.String:
                    return (cell.StringCellValue ?? "").Trim();
                case CellType.Boolean:
                    return cell.BooleanCellValue ? "TRUE" : "FALSE";
                case CellType.Numeric:
                    return cell.NumericCellValue.ToString("0.##", CultureInfo.InvariantCulture);
                case CellType.Formula:
                    switch (cell.CachedFormulaResultType)
                    {
                        case CellType.String:
                            return (cell.StringCellValue ?? "").Trim();
                        case CellType.Boolean:
                            return cell.BooleanCellValue ? "TRUE" : "FALSE";
                        case CellType.Numeric:
                            return cell.NumericCellValue.ToString("0.##", CultureInfo.InvariantCulture);
                        default:
                            return "";
                    }
                default:
                    return "";
            }
        }

        private static bool HasStrikeout(IWorkbook workbook, ICell cell,
            IDictionary<short, bool> strikeoutByFontIndex)
        {
            if (cell == null) return false;
            ICellStyle style = cell.CellStyle;
            if (style != null)
            {
                short fontIndex = style.FontIndex;
                bool isStrikeout;
                if (!strikeoutByFontIndex.TryGetValue(fontIndex, out isStrikeout))
                {
                    IFont font = workbook.GetFontAt(fontIndex);
                    isStrikeout = font != null && font.IsStrikeout;
                    strikeoutByFontIndex[fontIndex] = isStrikeout;
                }
                if (isStrikeout) return true;
            }
            if (cell.CellType != CellType.String) return false;
            var xssf = cell.RichStringCellValue as XSSFRichTextString;
            if (xssf == null) return false;
            for (int run = 0; run < xssf.NumFormattingRuns; run++)
            {
                IFont font = xssf.GetFontOfFormattingRun(run);
                if (font != null && font.IsStrikeout) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 删除线样本追加
        // ------------------------------------------------------------------
        private static void AppendStrikeoutSample(IWorkbook workbook, UnifiedInfo info,
            Func<string, string> map)
        {
            ISheet sheet = workbook.GetSheetAt(info.SheetIndex);
            IRow source = null;
            for (int r = sheet.LastRowNum; r > info.HeaderRow; r--)
            {
                IRow candidate = sheet.GetRow(r);
                if (candidate == null) continue;
                if (!string.IsNullOrWhiteSpace(CellText(candidate.GetCell(info.CircuitColumn)))
                    && !string.IsNullOrWhiteSpace(CellText(candidate.GetCell(info.MachineIdColumn))))
                {
                    source = candidate;
                    break;
                }
            }
            if (source == null)
                throw new InvalidDataException("统一表中找不到可复制的样本数据行。");

            IRow newRow = sheet.CreateRow(sheet.LastRowNum + 1);
            IFont strikeFont = null;
            for (int c = 0; c < source.LastCellNum; c++)
            {
                ICell sc = source.GetCell(c);
                if (sc == null) continue;
                ICell dc = newRow.CreateCell(c);
                dc.CellStyle = sc.CellStyle;
                switch (sc.CellType)
                {
                    case CellType.String:
                        dc.SetCellValue(map(sc.StringCellValue));
                        break;
                    case CellType.Numeric:
                        dc.SetCellValue(sc.NumericCellValue);
                        break;
                    case CellType.Boolean:
                        dc.SetCellValue(sc.BooleanCellValue);
                        break;
                    case CellType.Formula:
                        switch (sc.CachedFormulaResultType)
                        {
                            case CellType.String:
                                dc.SetCellValue(map(sc.StringCellValue));
                                break;
                            case CellType.Numeric:
                                dc.SetCellValue(sc.NumericCellValue);
                                break;
                            case CellType.Boolean:
                                dc.SetCellValue(sc.BooleanCellValue);
                                break;
                        }
                        break;
                }
                if (c != info.CircuitColumn) continue;

                IFont baseFont = workbook.GetFontAt(
                    sc.CellStyle != null ? sc.CellStyle.FontIndex : (short)0);
                strikeFont = workbook.CreateFont();
                if (baseFont != null)
                {
                    strikeFont.FontName = baseFont.FontName;
                    strikeFont.FontHeightInPoints = baseFont.FontHeightInPoints;
                    strikeFont.IsBold = baseFont.IsBold;
                }
                strikeFont.IsStrikeout = true;
                ICellStyle strikeStyle = workbook.CreateCellStyle();
                strikeStyle.CloneStyleFrom(sc.CellStyle);
                strikeStyle.SetFont(strikeFont);
                dc.CellStyle = strikeStyle;
                dc.SetCellValue("S-STRIKE");
            }
            if (strikeFont == null)
                throw new InvalidDataException("样本行缺少回路名单元格，无法套删除线。");
        }

        private static string ColLetter(int column)
        {
            int c = column;
            var sb = new StringBuilder();
            while (c >= 0) { sb.Insert(0, (char)('A' + c % 26)); c = c / 26 - 1; }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // zip 级清洗 + 泄漏自检
        // ------------------------------------------------------------------
        private int ScourZip(string srcPath, string dstPath,
            Dictionary<string, string> sheetNameMap, List<string> keywords)
        {
            using (FileStream srcFs = File.OpenRead(srcPath))
            using (ZipArchive src = new ZipArchive(srcFs, ZipArchiveMode.Read))
            using (FileStream dstFs = File.Create(dstPath))
            using (ZipArchive dst = new ZipArchive(dstFs, ZipArchiveMode.Create))
            {
                foreach (ZipArchiveEntry entry in src.Entries)
                {
                    string name = entry.FullName;
                    if (name.Length == 0 || name.EndsWith("/")) continue;
                    if (name.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase)) continue;

                    string text = null;
                    if (name.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase))
                        text = MinimalCoreXml();
                    else if (name.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase))
                        text = MinimalAppXml();
                    else if (name.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        int mappedCount;
                        text = SanitizeSharedStrings(ReadEntryText(entry), out mappedCount);
                        Console.WriteLine("sharedStrings 重写条目数: " + mappedCount);
                    }
                    else if (name.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
                        text = RenameSheets(ReadEntryText(entry), sheetNameMap);
                    else if (name.Equals("_rels/.rels", StringComparison.OrdinalIgnoreCase))
                        text = StripPackageRels(ReadEntryText(entry));
                    else if (name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                        text = StripContentTypes(ReadEntryText(entry));

                    ZipArchiveEntry outEntry = dst.CreateEntry(name, CompressionLevel.Optimal);
                    using (Stream os = outEntry.Open())
                    {
                        if (text != null)
                        {
                            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                            os.Write(bytes, 0, bytes.Length);
                        }
                        else
                        {
                            using (Stream es = entry.Open())
                                es.CopyTo(os);
                        }
                    }
                }
            }

            int hits = ScanForLeaks(dstPath, keywords);
            return hits;
        }

        private static int ScanForLeaks(string path, List<string> keywords)
        {
            int hits = 0;
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = entry.FullName;
                    if (!(name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))) continue;
                    string text;
                    using (Stream s = entry.Open())
                    using (var reader = new StreamReader(s, new UTF8Encoding(false)))
                        text = reader.ReadToEnd();
                    foreach (string keyword in keywords)
                    {
                        if (text.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                        {
                            Console.Error.WriteLine("泄漏命中: " + name + " 含关键词 \"" + keyword + "\"");
                            hits++;
                        }
                    }
                }
            }
            Console.WriteLine(hits == 0
                ? "泄漏自检: 0 命中 (" + keywords.Count + " 个关键词扫过全部文本部件)"
                : "泄漏自检: " + hits + " 处命中，已拦截，请勿使用该输出。");
            return hits;
        }

        private static string ReadEntryText(ZipArchiveEntry entry)
        {
            using (Stream s = entry.Open())
            using (var reader = new StreamReader(s, new UTF8Encoding(false)))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// 整表重写 sharedStrings：每个 si 的文本经映射替换，多 run 条目保 run 结构
        /// （删除线等 run 级字体必须保留，D-002 依赖它）；U_ token 与「回路名称」除外。
        /// </summary>
        private string SanitizeSharedStrings(string xml, out int mappedCount)
        {
            XDocument doc = XDocument.Parse(xml);
            XNamespace ns = doc.Root.Name.Namespace;
            mappedCount = 0;
            foreach (XElement si in doc.Root.Elements(ns + "si").ToList())
            {
                List<XElement> ts = si.Descendants(ns + "t").ToList();
                if (ts.Count == 0) continue;
                string full = string.Concat(ts.Select(t => t.Value));
                string trimmed = full.Trim();
                if (trimmed.StartsWith("U_", StringComparison.Ordinal) || trimmed == "回路名称")
                    continue; // 结构 token：表头原样保留
                bool changed = false;
                foreach (XElement t in ts)
                {
                    if (string.IsNullOrWhiteSpace(t.Value)) continue;
                    t.Value = MapValue(t.Value);
                    changed = true;
                }
                if (changed) mappedCount++;
            }
            return doc.ToString(SaveOptions.DisableFormatting);
        }

        private static string RenameSheets(string workbookXml, Dictionary<string, string> sheetNameMap)
        {
            XDocument doc = XDocument.Parse(workbookXml);
            XNamespace ns = doc.Root.Name.Namespace;
            foreach (XElement sheet in doc.Root.Descendants(ns + "sheet"))
            {
                XAttribute nameAttr = sheet.Attribute("name");
                if (nameAttr == null) continue;
                string mapped;
                if (sheetNameMap.TryGetValue(nameAttr.Value, out mapped) && mapped != nameAttr.Value)
                    nameAttr.Value = mapped;
            }
            return doc.ToString(SaveOptions.DisableFormatting);
        }

        private static string StripPackageRels(string relsXml)
        {
            XDocument doc = XDocument.Parse(relsXml);
            XNamespace ns = doc.Root.Name.Namespace;
            foreach (XElement rel in doc.Root.Elements(ns + "Relationship").ToList())
            {
                XAttribute target = rel.Attribute("Target");
                if (target == null) continue;
                string t = target.Value;
                if (t.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase))
                    rel.Remove();
            }
            return doc.ToString(SaveOptions.DisableFormatting);
        }

        private static string StripContentTypes(string xml)
        {
            XDocument doc = XDocument.Parse(xml);
            XNamespace ns = doc.Root.Name.Namespace;
            foreach (XElement o in doc.Root.Elements(ns + "Override").ToList())
            {
                XAttribute part = o.Attribute("PartName");
                if (part == null) continue;
                string p = part.Value.TrimStart('/');
                if (p.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase)
                    || p.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase))
                    o.Remove();
            }
            return doc.ToString(SaveOptions.DisableFormatting);
        }

        private const string CoreXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<cp:coreProperties" +
            " xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\"" +
            " xmlns:dc=\"http://purl.org/dc/elements/1.1/\"" +
            " xmlns:dcterms=\"http://purl.org/dc/terms/\"" +
            " xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\"" +
            " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            "<dc:creator>FixtureSanitizer</dc:creator>" +
            "<cp:lastModifiedBy>FixtureSanitizer</cp:lastModifiedBy>" +
            "<dcterms:created xsi:type=\"dcterms:W3CDTF\">2026-01-01T00:00:00Z</dcterms:created>" +
            "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">2026-01-01T00:00:00Z</dcterms:modified>" +
            "</cp:coreProperties>";

        private const string AppXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Properties" +
            " xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"" +
            " xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            "<Application>FixtureSanitizer</Application>" +
            "</Properties>";

        private static string MinimalCoreXml() { return CoreXml; }
        private static string MinimalAppXml() { return AppXml; }
    }

    /// <summary>
    /// 只处理两类内容：公式缓存字符串（inline t="str"，不进 sharedStrings）与批注文本。
    /// 普通字符串单元格不动，sharedStrings 由 zip 级后处理整表重写。
    /// </summary>
    internal sealed class CellSanitizer
    {
        private readonly Func<string, string> _map;

        public CellSanitizer(Func<string, string> map) { _map = map; }

        public void RewriteFormulaCachesAndComments(IWorkbook workbook)
        {
            for (int i = 0; i < workbook.NumberOfSheets; i++)
            {
                ISheet sheet = workbook.GetSheetAt(i);
                for (int r = 0; r <= sheet.LastRowNum; r++)
                {
                    IRow row = sheet.GetRow(r);
                    if (row == null) continue;
                    foreach (ICell cell in row.Cells.ToList())
                    {
                        if (cell.CellType == CellType.Formula
                            && cell.CachedFormulaResultType == CellType.String)
                        {
                            string cached = cell.StringCellValue;
                            if (!string.IsNullOrWhiteSpace(cached))
                                cell.SetCellValue(_map(cached));
                        }
                        SanitizeComment(cell);
                    }
                }
            }
        }

        private void SanitizeComment(ICell cell)
        {
            IComment comment;
            try { comment = cell.CellComment; }
            catch (Exception) { return; } // NPOI 对孤立批注部件容错
            if (comment == null) return;
            string text = comment.String == null ? "" : comment.String.String;
            if (!string.IsNullOrWhiteSpace(text))
                comment.String = new XSSFRichTextString(_map(text));
            if (!string.IsNullOrWhiteSpace(comment.Author))
                comment.Author = _map(comment.Author);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// U1PDF 的文件命名与有效性校验（纯逻辑，可单测）。
    ///
    /// 一份 PDF 对应**一个回路（一个图框）**；文件名由"机台 + 回路"组成并做安全化，
    /// 同一批里若两个图框得到相同文件名，后来者加 <c>-2</c>、<c>-3</c> 后缀，
    /// 避免后出的图静默覆盖前一份。
    ///
    /// 产物校验不止看 <c>%PDF-</c> 文件头：出图中途失败时驱动可能留下一个
    /// 头部合法但**没有结束标记**的残件，那种文件打开只能看到空白，
    /// 所以还要看 <c>%%EOF</c> 与文件大小。
    /// </summary>
    internal static class PdfExportNaming
    {
        internal const string PdfExtension = ".pdf";
        internal const string PdfMagic = "%PDF-";
        internal const string PdfTrailer = "%%EOF";
        /// <summary>AutoCAD 自带 PDF 打印设备；缺失时必须给中文提示而不是静默失败。</summary>
        internal const string DeviceName = "DWG To PDF.pc3";
        /// <summary>小于该字节数的 PDF 一定是空图（一页正常出图远大于此）。</summary>
        internal const int MinimumPdfBytes = 1024;
        /// <summary>文件名主体（不含扩展名）的最大长度；+4 后仍在 260 路径限制内。</summary>
        internal const int MaximumNameLength = 100;
        /// <summary>机台与回路之间的分隔符。</summary>
        internal const string NameSeparator = "_";
        internal const string EmptyNameFallback = "circuit";
        private const int MagicLength = 5;
        private const int TrailerSearchLength = 4096;

        /// <summary>
        /// 机台 + 回路 → 安全的 PDF 文件名：
        /// <c>&lt;机台&gt;_&lt;回路&gt;.pdf</c>；机台或回路为空时只保留另一方，
        /// 两者都为空回退为 <c>circuit.pdf</c>。非法文件名字符替换为 '_'，主体截断到
        /// <see cref="MaximumNameLength"/> 字符。
        /// </summary>
        internal static string BuildFileName(string machineId, string circuitName)
        {
            string machine = SanitizePart(machineId);
            string circuit = SanitizePart(circuitName);
            string name;
            if (machine.Length == 0) name = circuit;
            else if (circuit.Length == 0) name = machine;
            else name = machine + NameSeparator + circuit;
            if (name.Length > MaximumNameLength)
                name = name.Substring(0, MaximumNameLength).Trim().TrimEnd('.');
            if (name.Length == 0) name = EmptyNameFallback;
            return name + PdfExtension;
        }

        /// <summary>
        /// 同一批出图内分配唯一文件名：<paramref name="usedNames"/> 里已有同名时依次尝试
        /// <c>-2</c>、<c>-3</c>…；分配成功后把结果加入集合。集合应为忽略大小写
        /// （Windows 文件系统不区分大小写）。
        /// </summary>
        internal static string AllocateFileName(string machineId, string circuitName,
            ISet<string> usedNames)
        {
            string baseName = BuildFileName(machineId, circuitName);
            if (usedNames == null) return baseName;
            string candidate = baseName;
            int suffix = 2;
            while (usedNames.Contains(candidate))
            {
                string suffixText = "-" + suffix.ToString(System.Globalization.CultureInfo
                    .InvariantCulture);
                int stemLength = MaximumNameLength - suffixText.Length;
                if (stemLength < 1) stemLength = 1;
                string stem = baseName.Substring(0, baseName.Length - PdfExtension.Length);
                if (stem.Length > stemLength) stem = stem.Substring(0, stemLength).Trim();
                candidate = stem + suffixText + PdfExtension;
                suffix++;
            }
            usedNames.Add(candidate);
            return candidate;
        }

        /// <summary>单段名称安全化：去掉首尾空白与结尾的点、替换非法字符、去掉遗留的 .pdf 扩展名。</summary>
        private static string SanitizePart(string value)
        {
            string name = (value ?? "").Trim();
            if (name.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - PdfExtension.Length);
            if (name.Length == 0) return "";
            var builder = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char character in name)
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            name = builder.ToString().Trim().TrimEnd('.');
            return name;
        }

        internal static bool HasPdfExtension(string fileName)
            => (fileName ?? "").Trim().EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase);

        /// <summary>读文件头 5 字节判断是否 <c>%PDF-</c>；不存在/读不到一律视为不合法。</summary>
        internal static bool HasPdfMagic(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                var buffer = new byte[MagicLength];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite))
                {
                    return stream.Read(buffer, 0, buffer.Length) == MagicLength
                        && Encoding.ASCII.GetString(buffer) == PdfMagic;
                }
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>文件结尾是否有 <c>%%EOF</c> 结束标记（出图被中断时不会有）。</summary>
        internal static bool HasPdfTrailer(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite))
                {
                    long length = stream.Length;
                    if (length <= 0) return false;
                    long start = Math.Max(0L, length - TrailerSearchLength);
                    stream.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[length - start];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    for (int index = 0; index + PdfTrailer.Length <= read; index++)
                    {
                        bool match = true;
                        for (int offset = 0; offset < PdfTrailer.Length; offset++)
                            if (buffer[index + offset] != (byte)PdfTrailer[offset])
                            {
                                match = false;
                                break;
                            }
                        if (match) return true;
                    }
                    return false;
                }
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 出图产物是否**可以交付**：存在、有 <c>%PDF-</c> 头、有 <c>%%EOF</c> 尾、
        /// 且大小不小于 <see cref="MinimumPdfBytes"/>。不通过时给出中文原因。
        /// </summary>
        internal static bool IsUsablePdf(string path, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "打印引擎没有生成 PDF 文件。";
                return false;
            }
            if (!HasPdfMagic(path))
            {
                error = "生成的文件不是有效的 PDF（缺少 " + PdfMagic + " 文件头）。";
                return false;
            }
            long size;
            try { size = new FileInfo(path).Length; }
            catch (System.Exception ex)
            {
                error = "无法读取生成的 PDF：" + ex.Message;
                return false;
            }
            if (size < MinimumPdfBytes)
            {
                error = "生成的 PDF 只有 " + size + " 字节，可能是空白图，已放弃输出。";
                return false;
            }
            if (!HasPdfTrailer(path))
            {
                error = "生成的 PDF 不完整（缺少 " + PdfTrailer + " 结束标记），已放弃输出。";
                return false;
            }
            return true;
        }

        internal static string DescribeFile(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length + " 字节" : "不存在"; }
            catch (System.Exception) { return "无法读取"; }
        }
    }
}

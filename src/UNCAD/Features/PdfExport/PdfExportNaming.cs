using System;
using System.IO;
using System.Text;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// U1PDF 的文件命名与有效性校验（纯逻辑，可单测）。
    ///
    /// 一份 PDF 对应**一个回路（一个图框）**；文件名必须以 .pdf 结尾并做安全化。
    /// 上传前的校验不止看 <c>%PDF-</c> 文件头：出图中途失败时驱动可能留下一个
    /// 头部合法但**没有结束标记**的残件，那种文件上传上去网页只能预览到空白，
    /// 所以还要看 <c>%%EOF</c> 与文件大小。
    /// </summary>
    internal static class PdfExportNaming
    {
        internal const string PdfExtension = ClientSubmissionUploader.PdfExtension;
        internal const string PdfMagic = "%PDF-";
        internal const string PdfTrailer = "%%EOF";
        /// <summary>AutoCAD 自带 PDF 打印设备；缺失时必须给中文提示而不是静默失败。</summary>
        internal const string DeviceName = "DWG To PDF.pc3";
        /// <summary>小于该字节数的 PDF 一定是空图（一页正常出图远大于此）。</summary>
        internal const int MinimumPdfBytes = 1024;
        private const int MaximumNameLength = 100;
        private const int MagicLength = 5;
        private const int TrailerSearchLength = 4096;

        /// <summary>回路名 → 安全的 PDF 文件名（始终以 .pdf 结尾，空名回退为 circuit.pdf）。</summary>
        internal static string BuildFileName(string circuitName)
        {
            string name = (circuitName ?? "").Trim();
            if (name.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - PdfExtension.Length);
            var builder = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char character in name)
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            name = builder.ToString().Trim().TrimEnd('.');
            if (name.Length > MaximumNameLength)
                name = name.Substring(0, MaximumNameLength).Trim();
            if (name.Length == 0) name = "circuit";
            return name + PdfExtension;
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
        /// 出图产物是否**可以上传**：存在、有 <c>%PDF-</c> 头、有 <c>%%EOF</c> 尾、
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
                error = "生成的 PDF 只有 " + size + " 字节，可能是空白图，已放弃上传。";
                return false;
            }
            if (!HasPdfTrailer(path))
            {
                error = "生成的 PDF 不完整（缺少 " + PdfTrailer + " 结束标记），已放弃上传。";
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

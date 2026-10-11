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
    /// 上传前必须确认文件头是 <c>%PDF-</c>：服务端同样校验该魔数，半成品会被 400 拒绝，
    /// 因此出图未完成时绝不能上传。
    /// </summary>
    internal static class PdfExportNaming
    {
        internal const string PdfExtension = ClientSubmissionUploader.PdfExtension;
        internal const string PdfMagic = "%PDF-";
        /// <summary>AutoCAD 自带 PDF 打印设备；缺失时必须给中文提示而不是静默失败。</summary>
        internal const string DeviceName = "DWG To PDF.pc3";
        private const int MaximumNameLength = 100;
        private const int MagicLength = 5;

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
    }
}

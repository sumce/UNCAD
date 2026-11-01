using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UNCAD.Core.Submission
{
    /// <summary>
    /// 输出目录布局与写占用预检的唯一实现（纯逻辑，可单测；不读注册表、不依赖 AutoCAD）。
    ///
    /// 默认布局（OutputByDrawing=1 且 OutputSeparateKinds=1）：
    /// <code>
    /// &lt;root&gt;\&lt;图纸&gt;\BOQ\&lt;机台&gt;\[BOQ]&lt;机台&gt;.xlsx
    /// &lt;root&gt;\&lt;图纸&gt;\DWG\&lt;机台&gt;\&lt;机台&gt;.dwg
    /// &lt;root&gt;\&lt;图纸&gt;\PDF\&lt;机台&gt;_&lt;回路&gt;.pdf
    /// </code>
    /// 关闭"按图纸"时去掉 &lt;图纸&gt; 一级；关闭"分类"时去掉 BOQ/DWG/PDF 一级。
    ///
    /// root 为空时默认 &lt;我的文档&gt;\UNCAD输出；把该默认值写回 UNC_SUBMIT_FOLDER 由
    /// Features 层负责（Core 不碰注册表）。
    /// </summary>
    internal static class OutputLayout
    {
        /// <summary>未配置输出目录时的默认根目录名（位于"我的文档"下）。</summary>
        internal const string DefaultRootFolderName = "UNCAD输出";
        internal const string KindBoq = "BOQ";
        internal const string KindDwg = "DWG";
        internal const string KindPdf = "PDF";
        /// <summary>图纸名作为一级子目录，截断到该长度（含中文也按字符数）。</summary>
        internal const int MaximumDrawingNameLength = 60;
        internal const string UnnamedDrawing = "未命名图纸";

        /// <summary>默认输出根目录 &lt;我的文档&gt;\UNCAD输出；无法确定"我的文档"时给出中文原因。</summary>
        internal static string DefaultRootDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
                throw new DirectoryNotFoundException(
                    "无法确定输出文件夹（我的文档不可用），请先在 U1SET 中设置输出文件夹。");
            return Path.Combine(documents, DefaultRootFolderName);
        }

        /// <summary>
        /// 把配置值归一化为完整输出根目录：空值→默认 &lt;我的文档&gt;\UNCAD输出；
        /// 相对路径→当前目录下的完整路径。只做路径计算，不创建目录。
        /// </summary>
        internal static string NormalizeRoot(string configured)
        {
            string value = (configured ?? "").Trim();
            return Path.GetFullPath(value.Length == 0 ? DefaultRootDirectory() : value);
        }

        /// <summary>配置值为空（需要使用默认输出根目录）时返回 true。</summary>
        internal static bool UsesDefaultRoot(string configured)
            => (configured ?? "").Trim().Length == 0;

        /// <summary>
        /// 图纸名 → 安全的目录名：非法文件名字符替换为 '_'，去掉首尾空白与结尾的点，
        /// 超过 60 字符截断；空名回退为"未命名图纸"。
        /// </summary>
        internal static string SanitizeDrawingName(string drawingName)
        {
            string name = (drawingName ?? "").Trim();
            var builder = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char character in name)
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            name = builder.ToString().Trim().TrimEnd('.');
            if (name.Length > MaximumDrawingNameLength)
                name = name.Substring(0, MaximumDrawingNameLength).Trim().TrimEnd('.');
            return name.Length == 0 ? UnnamedDrawing : name;
        }

        /// <summary>
        /// 某一类输出（BOQ/DWG/PDF）的目录：
        /// root[\图纸][\类别]。只拼路径，不创建目录。
        /// </summary>
        internal static string ResolveKindDirectory(string root, string drawingName,
            string kind, bool outputByDrawing, bool separateKinds)
        {
            string directory = Path.GetFullPath(
                string.IsNullOrWhiteSpace(root) ? DefaultRootDirectory() : root.Trim());
            if (outputByDrawing)
                directory = Path.Combine(directory, SanitizeDrawingName(drawingName));
            if (separateKinds)
            {
                if (string.IsNullOrWhiteSpace(kind))
                    throw new ArgumentException("输出类别不能为空。", nameof(kind));
                directory = Path.Combine(directory, kind);
            }
            return directory;
        }

        internal static string ResolveBoqDirectory(string root, string drawingName,
            bool outputByDrawing, bool separateKinds)
            => ResolveKindDirectory(root, drawingName, KindBoq, outputByDrawing, separateKinds);

        internal static string ResolveDwgDirectory(string root, string drawingName,
            bool outputByDrawing, bool separateKinds)
            => ResolveKindDirectory(root, drawingName, KindDwg, outputByDrawing, separateKinds);

        internal static string ResolvePdfDirectory(string root, string drawingName,
            bool outputByDrawing, bool separateKinds)
            => ResolveKindDirectory(root, drawingName, KindPdf, outputByDrawing, separateKinds);

        /// <summary>BOQ 目标文件：&lt;BOQ 目录&gt;\&lt;机台&gt;\[BOQ]&lt;机台&gt;.xlsx（与 BoqWorkbookWriter 同一套命名）。</summary>
        internal static string ComposeBoqPath(string root, string drawingName, string machineId,
            bool outputByDrawing, bool separateKinds)
            => BoqWorkbookWriter.ComposeTargetPath(
                ResolveBoqDirectory(root, drawingName, outputByDrawing, separateKinds),
                machineId);

        /// <summary>DWG 目标文件：&lt;DWG 目录&gt;\&lt;机台&gt;\&lt;机台&gt;.dwg（按机台一份，与 U1DWG 粒度一致）。</summary>
        internal static string ComposeDwgPath(string root, string drawingName, string machineId,
            bool outputByDrawing, bool separateKinds)
        {
            string machine = (machineId ?? "").Trim();
            if (machine.Length == 0)
                throw new ArgumentException("机台ID不能为空。", nameof(machineId));
            return Path.Combine(
                ResolveDwgDirectory(root, drawingName, outputByDrawing, separateKinds),
                machine, machine + ".dwg");
        }

        /// <summary>PDF 目标文件：&lt;PDF 目录&gt;\&lt;文件名&gt;（按回路一份，文件名由 PdfExportNaming 生成）。</summary>
        internal static string ComposePdfPath(string root, string drawingName, string fileName,
            bool outputByDrawing, bool separateKinds)
        {
            string name = (fileName ?? "").Trim();
            if (name.Length == 0)
                throw new ArgumentException("PDF 文件名不能为空。", nameof(fileName));
            return Path.Combine(
                ResolvePdfDirectory(root, drawingName, outputByDrawing, separateKinds), name);
        }

        /// <summary>
        /// 写占用预检：对每个已存在的目标文件以 FileShare.None 试打开一次，
        /// 被其它程序（Excel、AutoCAD 等）占用时抛出带中文原因与路径的 IOException。
        /// 必须在 CAD 事务/写盘之前调用；目标文件尚不存在时无需预检。
        /// </summary>
        internal static void ValidateNotLocked(IEnumerable<string> paths)
        {
            foreach (string path in paths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path.Trim());
                }
                catch (Exception ex)
                {
                    throw new IOException("输出路径无效：" + path + "（" + ex.Message + "）", ex);
                }
                if (!File.Exists(fullPath)) continue;   // 目标还不存在：创建时由写盘逻辑报错
                try
                {
                    using (new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite,
                        FileShare.None))
                    {
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new IOException("没有权限写入输出文件：" + fullPath
                        + "。请关闭占用该文件的程序，或用有写权限的账号重试。", ex);
                }
                catch (IOException ex)
                {
                    throw new IOException("输出文件正被其他程序占用：" + fullPath
                        + "。请先关闭占用该文件的程序（例如 Excel 或 AutoCAD）后重试。", ex);
                }
            }
        }
    }
}

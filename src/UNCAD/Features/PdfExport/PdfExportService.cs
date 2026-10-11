using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// U1PDF 的出图实现：把一个图框（= 一个回路）用 **AutoCAD 自带的 PlotEngine**
    /// 打印成一份彩色 PDF。
    ///
    /// 为什么必须自己出图（不用第三方批量打印插件）：
    /// 第三方批量打印会接管 AutoCAD 的界面与当前布局，用户正画着图会被打断；
    /// 这里全程只用 PlotEngine 输出到文件，**不改动用户的布局打印设置**
    /// （布局只读打开 + <c>PlotSettings.CopyFrom</c> + <c>PlotInfo.OverrideSettings</c>），
    /// 无对话框、无 SendCommand、不切布局、不缩放视图。
    ///
    /// 具体设置与逐步诊断见 <see cref="PdfPlotPipeline"/> / <see cref="AutoCadPlotSession"/>。
    /// </summary>
    internal static class PdfExportService
    {
        internal const string DeviceName = PdfExportNaming.DeviceName;

        /// <summary>检查 PDF 打印设备是否可用；不可用时给出可直接展示的中文原因。</summary>
        internal static bool TryGetDeviceError(out string error)
        {
            error = "";
            try
            {
                var devices = PlotSettingsValidator.Current.GetPlotDeviceList();
                if (devices == null) return true;   // 拿不到列表时不武断拒绝，交给打印引擎报错
                foreach (string device in devices)
                    if (string.Equals(device, DeviceName, StringComparison.OrdinalIgnoreCase))
                        return true;
                error = "找不到打印设备“" + DeviceName + "”。请在 AutoCAD 中确认已安装该设备"
                    + "（打印 → 打印设备下拉列表），或安装后重试。当前可用设备："
                    + Join(devices) + "。";
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Warn("读取 PDF 打印设备列表失败: " + ex.Message);
                error = "无法读取 AutoCAD 打印设备列表：" + ex.Message;
                return false;
            }
        }

        /// <summary>把图框窗口打印成一份彩色 PDF；失败返回中文原因 + 逐步诊断，不抛异常。</summary>
        internal static PdfRenderResult Render(CadContext ctx, FrameRegionGroup region,
            string circuitName, string outputDirectory)
        {
            if (ctx == null) return PdfRenderResult.Fail("缺少图形上下文。");
            if (region == null || region.Boundary == null)
                return PdfRenderResult.Fail("找不到图框边界。");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return PdfRenderResult.Fail("缺少 PDF 临时输出目录。");
            if (!TryGetDeviceError(out string deviceError))
                return PdfRenderResult.Fail(deviceError);

            PdfPlotWindow window = PdfPlotWindow.FromBoundary(region.Boundary);
            if (!PdfPlotPolicy.TryValidateWindow(window, out string windowError))
                return PdfRenderResult.Fail(windowError);

            string file = Path.Combine(outputDirectory,
                PdfExportNaming.BuildFileName(circuitName));
            var diagnostics = new PdfPlotDiagnostics();
            string note = "";
            try
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    if (!TryResolvePlotLayout(ctx.Db, transaction, out ObjectId layoutId,
                        out string layoutName, out bool isModel, out string layoutError))
                    {
                        transaction.Commit();
                        return PdfRenderResult.Fail(layoutError);
                    }

                    using (AutoCadPlotSession session = AutoCadPlotSession.Create(transaction,
                        layoutId, out string sessionError))
                    {
                        if (session == null)
                        {
                            transaction.Commit();
                            return PdfRenderResult.Fail(sessionError);
                        }
                        if (!PdfPlotPipeline.Run(session, circuitName, file, window, diagnostics))
                        {
                            transaction.Commit();
                            ReportFailure(circuitName, diagnostics);
                            TryDelete(file);
                            return PdfRenderResult.Fail(diagnostics.FailureReason, diagnostics.Lines);
                        }
                        note = session.Describe();
                    }
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("U1PDF 出图异常（" + circuitName + "）", ex);
                diagnostics.Fail("出图（未预期的异常）", ex.GetType().Name + "：" + ex.Message);
                ReportFailure(circuitName, diagnostics);
                TryDelete(file);
                return PdfRenderResult.Fail(diagnostics.FailureReason, diagnostics.Lines);
            }

            if (!PdfExportNaming.IsUsablePdf(file, out string pdfError))
            {
                Log.Warn("U1PDF 出图产物不可用（" + circuitName + "）：" + pdfError);
                TryDelete(file);
                return PdfRenderResult.Fail(pdfError, diagnostics.Lines);
            }
            Log.Info("U1PDF 出图完成（" + circuitName + "）：" + note
                + " 文件=" + PdfExportNaming.DescribeFile(file));
            return PdfRenderResult.Success(file, note + "  文件=" + PdfExportNaming.DescribeFile(file));
        }

        /// <summary>
        /// 找**当前空间**对应的布局：图框是在当前空间里收集的，出图窗口也必须是同一坐标系的
        /// 布局，否则窗口坐标会落在图纸之外（真机上就表现为 eInvalidInput）。
        /// 找不到时回退到模型空间布局。
        /// </summary>
        internal static bool TryResolvePlotLayout(Database database, Transaction transaction,
            out ObjectId layoutId, out string layoutName, out bool isModel, out string error)
        {
            layoutId = ObjectId.Null;
            layoutName = "";
            isModel = false;
            error = "";
            if (database == null || transaction == null)
            {
                error = "缺少图形数据库，无法出图。";
                return false;
            }

            ObjectId spaceId = database.CurrentSpaceId;
            ObjectId fallback = ObjectId.Null;
            string fallbackName = "";
            Layout layout;
            try
            {
                var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId,
                    OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    layout = transaction.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    if (layout == null) continue;
                    if (layout.ModelType && fallback.IsNull)
                    {
                        fallback = entry.Value;
                        fallbackName = layout.LayoutName ?? "";
                    }
                    if (!spaceId.IsNull && layout.BlockTableRecordId == spaceId)
                    {
                        layoutId = entry.Value;
                        layoutName = layout.LayoutName ?? "";
                        isModel = layout.ModelType;
                        return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                error = "读取布局字典失败：" + ex.Message;
                return false;
            }

            if (!fallback.IsNull)
            {
                layoutId = fallback;
                layoutName = fallbackName;
                isModel = true;
                return true;
            }
            error = "找不到与当前空间对应的布局，无法出图。";
            return false;
        }

        private static void ReportFailure(string circuitName, PdfPlotDiagnostics diagnostics)
        {
            Log.Warn("U1PDF 出图失败（" + circuitName + "）：" + diagnostics.FailureReason);
            foreach (string line in diagnostics.Lines)
                Log.Warn("U1PDF 诊断 | " + line);
        }

        private static string Join(System.Collections.Specialized.StringCollection values)
        {
            if (values == null || values.Count == 0) return "(空)";
            var items = new string[values.Count];
            values.CopyTo(items, 0);
            return string.Join("、", items);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception ex) { Log.Warn("删除无效 PDF 失败: " + ex.Message); }
        }
    }
}

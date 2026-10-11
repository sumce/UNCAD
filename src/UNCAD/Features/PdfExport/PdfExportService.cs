using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using UNCAD.Cad;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// 用 AutoCAD 的 PlotEngine 把一个图框（= 一个回路）打印成**彩色 PDF**。
    ///
    /// - 打印设备固定为 AutoCAD 自带的 <c>DWG To PDF.pc3</c>；缺失时返回中文错误，
    ///   由命令提示用户（绝不静默失败）；
    /// - **彩色**：<c>PlotPlotStyles = false</c>，不套用任何打印样式表（尤其不是
    ///   monochrome.ctb），因此保留图层颜色；
    /// - 打印范围 = **图框窗口**（图框外包矩形的 WCS 坐标），缩放 = Fit，居中；
    /// - 输出到调用方指定的临时目录，**出图完成后**再校验 <c>%PDF-</c> 文件头，
    ///   确认是完整 PDF 才交给上传（半成品会被服务端拒绝）。
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
                    + "（打印 → 打印设备下拉列表），或安装后重试。";
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Warn("读取 PDF 打印设备列表失败: " + ex.Message);
                error = "无法读取 AutoCAD 打印设备列表：" + ex.Message;
                return false;
            }
        }

        /// <summary>把图框窗口打印成一份彩色 PDF；失败返回中文原因，不抛异常。</summary>
        internal static PdfRenderResult Render(CadContext ctx, FrameRegionGroup region,
            string circuitName, string outputDirectory)
        {
            if (ctx == null) return PdfRenderResult.Fail("缺少图形上下文。");
            if (region == null || region.Boundary == null) return PdfRenderResult.Fail("找不到图框边界。");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return PdfRenderResult.Fail("缺少 PDF 临时输出目录。");
            if (!TryGetDeviceError(out string deviceError))
                return PdfRenderResult.Fail(deviceError);

            string file = Path.Combine(outputDirectory,
                PdfExportNaming.BuildFileName(circuitName));
            try
            {
                using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
                {
                    ObjectId layoutId = ResolveModelLayoutId(ctx.Db, transaction);
                    if (layoutId.IsNull) return PdfRenderResult.Fail("找不到模型空间布局，无法出图。");

                    using (PlotEngine engine = PlotFactory.CreatePublishEngine())
                    {
                        var info = new PlotInfo { Layout = layoutId };
                        var settings = new PlotSettings(true);
                        PlotSettingsValidator validator = PlotSettingsValidator.Current;
                        validator.SetPlotConfigurationName(settings, DeviceName, null);
                        validator.SetPlotType(settings,
                            Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                        validator.SetPlotWindowArea(settings, new Extents2d(
                            region.Boundary.MinX, region.Boundary.MinY,
                            region.Boundary.MaxX, region.Boundary.MaxY));
                        validator.SetUseStandardScale(settings, true);
                        validator.SetStdScaleType(settings, StdScaleType.ScaleToFit);
                        validator.SetPlotCentered(settings, true);
                        // 彩色：不套用打印样式表（保留图层颜色），指定单色 ctb 会失去颜色。
                        settings.PlotPlotStyles = false;
                        info.OverrideSettings = settings;

                        var infoValidator = new PlotInfoValidator
                        {
                            MediaMatchingPolicy = MatchingPolicy.MatchEnabled
                        };
                        infoValidator.Validate(info);

                        engine.BeginPlot(null, null);
                        engine.BeginDocument(info, circuitName ?? "", null, 1, true, file);
                        var pageInfo = new PlotPageInfo();
                        engine.BeginPage(pageInfo, info, true, null);
                        engine.BeginGenerateGraphics(null);
                        engine.EndGenerateGraphics(null);
                        engine.EndPage(null);
                        engine.EndDocument(null);
                        engine.EndPlot(null);
                    }
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("U1PDF 出图失败（" + circuitName + "）: " + ex.Message);
                return PdfRenderResult.Fail(ex.Message);
            }

            if (!File.Exists(file))
                return PdfRenderResult.Fail("打印引擎没有生成 PDF 文件。");
            if (!PdfExportNaming.HasPdfMagic(file))
            {
                TryDelete(file);
                return PdfRenderResult.Fail("生成的文件不是有效的 PDF（缺少 %PDF- 文件头）。");
            }
            return PdfRenderResult.Success(file);
        }

        /// <summary>找模型空间对应的布局：用 <c>Layout.ModelType</c> 判断，避免依赖布局名语言。</summary>
        internal static ObjectId ResolveModelLayoutId(Database database, Transaction transaction)
        {
            if (database == null || transaction == null) return ObjectId.Null;
            var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId,
                OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                var layout = transaction.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                if (layout != null && layout.ModelType) return entry.Value;
            }
            return ObjectId.Null;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception ex) { Log.Warn("删除无效 PDF 失败: " + ex.Message); }
        }
    }
}

using System;
using System.Collections.Specialized;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// 真实打印会话：把 <see cref="IPlotSession"/> 映射到 AutoCAD 的
    /// PlotSettingsValidator / PlotInfoValidator / PlotEngine。
    ///
    /// 用户明确提过：出图不能打扰正在画图的人。所以这里有两条硬约束：
    ///
    /// 1. **无界面**：只用 PlotEngine 直接输出到文件，不弹"打印"对话框、
    ///    不 SendCommand、不切布局、不缩放视图。
    /// 2. **不留下改动**：出图必须在布局自身的 <c>PlotSettings</c> 上设置
    ///    （真机实测：用 <c>PlotInfo.OverrideSettings</c> 时校验器会把请求的 A3
    ///    重新匹配成布局自带的 A4，出图纸张就错了 —— 见
    ///    <see cref="PdfPlotSettingsSnapshot"/>），因此本类在构造时拍下完整快照，
    ///    在 <see cref="Dispose"/> 时逐字段还原，用户打开"页面设置"看到的仍是原样。
    ///
    /// 所有 validator / engine 调用都经过 <see cref="Guard"/>：把 AutoCAD 的
    /// <c>eInvalidInput</c> 之类 <c>Autodesk.AutoCAD.Runtime.Exception</c> 翻译成
    /// "哪一步 + ErrorStatus"的中文说明，交给 <see cref="PdfPlotDiagnostics"/> 汇总。
    /// </summary>
    internal sealed class AutoCadPlotSession : IPlotSession
    {
        private readonly Layout _layout;
        private readonly PlotInfo _info;
        private readonly PlotInfoValidator _infoValidator;
        private readonly PdfPlotSettingsSnapshot _original;
        private PlotEngine _engine;
        private int _mediaCount;
        private PdfPlotWindow _window;
        private PdfPlotRotation _rotation;

        private AutoCadPlotSession(Layout layout, ObjectId layoutId)
        {
            _layout = layout;
            _original = PdfPlotSettingsSnapshot.Capture(layout);
            // 关键：不设置 OverrideSettings —— 出图必须直接用布局自身的打印设置。
            _info = new PlotInfo { Layout = layoutId };
            _infoValidator = new PlotInfoValidator
            {
                MediaMatchingPolicy = MatchingPolicy.MatchEnabled
            };
        }

        /// <summary>
        /// 用布局 id 建立会话。布局以 <c>OpenMode.ForWrite</c> 打开（必须写：
        /// 设备和介质只有写在布局自身的打印设置上才会被打印引擎采用），
        /// 原设置快照会在 <see cref="Dispose"/> 时还原。
        /// </summary>
        internal static AutoCadPlotSession Create(Transaction transaction, ObjectId layoutId,
            out string error)
        {
            error = "";
            if (transaction == null || layoutId.IsNull)
            {
                error = "找不到要出图的布局。";
                return null;
            }
            Layout layout;
            try
            {
                layout = transaction.GetObject(layoutId, OpenMode.ForWrite, true) as Layout;
            }
            catch (System.Exception ex)
            {
                error = "打开布局失败：" + ex.Message;
                return null;
            }
            if (layout == null)
            {
                error = "布局对象无效，无法出图。";
                return null;
            }
            try
            {
                return new AutoCadPlotSession(layout, layoutId);
            }
            catch (System.Exception ex)
            {
                error = "建立打印会话失败：" + ex.Message;
                return null;
            }
        }

        public string LayoutName => _layout?.LayoutName ?? "";
        public bool IsModelLayout => _layout != null && _layout.ModelType;
        public string ConfiguredDeviceName => _layout?.PlotConfigurationName ?? "";
        public string ConfiguredMediaName => _layout?.CanonicalMediaName ?? "";
        public bool UseStandardScale => _layout != null && _layout.UseStandardScale;
        public int MediaCount => _mediaCount;

        public string[] GetPlotDeviceList()
            => ToArray(PlotSettingsValidator.Current.GetPlotDeviceList());

        public void SetPlotConfigurationName(string deviceName)
            => Guard("设置打印设备“" + deviceName + "”", () =>
            {
                PlotSettingsValidator validator = PlotSettingsValidator.Current;
                validator.SetPlotConfigurationName(_layout, deviceName, null);
                validator.RefreshLists(_layout);
            });

        public void SetPlotPaperUnitsMillimeters()
            => Guard("设置纸张单位为毫米", () => PlotSettingsValidator.Current
                .SetPlotPaperUnits(_layout, PlotPaperUnit.Millimeters));

        public string[] GetCanonicalMediaNames()
        {
            string[] names = Array.Empty<string>();
            Guard("读取规范介质列表", () =>
            {
                PlotSettingsValidator validator = PlotSettingsValidator.Current;
                validator.RefreshLists(_layout);
                names = ToArray(validator.GetCanonicalMediaNameList(_layout));
            });
            _mediaCount = names.Length;
            return names;
        }

        public void SetCanonicalMediaName(string mediaName)
            => Guard("设置规范介质“" + mediaName + "”", () => PlotSettingsValidator.Current
                .SetCanonicalMediaName(_layout, mediaName));

        public void SetPlotRotation(PdfPlotRotation rotation)
        {
            _rotation = rotation;
            PlotRotation value = rotation == PdfPlotRotation.Quarter
                ? PlotRotation.Degrees090
                : PlotRotation.Degrees000;
            Guard("设置打印旋转", () => PlotSettingsValidator.Current
                .SetPlotRotation(_layout, value));
        }

        /// <summary>
        /// 打印范围设为窗口。
        /// **调用顺序要求（真机实测）**：必须先用 <see cref="SetWindowArea"/> 设好窗口，
        /// 再设置 <c>PlotType.Window</c>；反过来 AutoCAD 会抛 eInvalidInput
        /// —— 2.6.0 真机出图失败的根因就是这个。
        /// </summary>
        public void SetPlotTypeWindow()
            => Guard("设置打印范围为窗口", () => PlotSettingsValidator.Current
                .SetPlotType(_layout, Autodesk.AutoCAD.DatabaseServices.PlotType.Window));

        public void SetWindowArea(PdfPlotWindow window)
        {
            _window = window;
            Guard("设置打印窗口", () => PlotSettingsValidator.Current.SetPlotWindowArea(_layout,
                new Extents2d(window.MinX, window.MinY, window.MaxX, window.MaxY)));
        }

        public void SetPlotCentered(bool centered)
            => Guard("设置居中打印", () => PlotSettingsValidator.Current
                .SetPlotCentered(_layout, centered));

        public void SetUseStandardScale(bool useStandardScale)
            => Guard("设置使用标准比例", () => PlotSettingsValidator.Current
                .SetUseStandardScale(_layout, useStandardScale));

        public void SetStdScaleTypeFit()
            => Guard("设置比例为布满图纸(Fit)", () => PlotSettingsValidator.Current
                .SetStdScaleType(_layout, StdScaleType.ScaleToFit));

        /// <summary>
        /// 彩色：不套用打印样式表（<c>PlotPlotStyles</c> 即"打印样式表(笔指定)"开关）。
        /// 指定 monochrome.ctb 之类会把图层颜色全部变成黑色，用户要的是彩色 PDF。
        /// 顺手把打印样式表也清空 —— 这一步失败只记日志，不因此放弃出图。
        /// </summary>
        public void SetColorPreserving()
        {
            Guard("设置彩色输出（不套用打印样式表）", () => _layout.PlotPlotStyles = false);
            try { PlotSettingsValidator.Current.SetCurrentStyleSheet(_layout, ""); }
            catch (System.Exception ex)
            {
                Log.Warn("清空打印样式表失败（按“不套用打印样式表”继续出图）: " + ex.Message);
            }
        }

        /// <summary>PlotInfoValidator.Validate：非法时抛 eInvalidInput。</summary>
        public void Validate() => Guard("校验打印设置", () => _infoValidator.Validate(_info));

        /// <summary>校验是否真的通过（Validate 返回 void，只能看 PlotInfo.IsValidated）。</summary>
        public bool IsValidated
        {
            get
            {
                try { return _info.IsValidated; }
                catch (System.Exception) { return false; }
            }
        }

        /// <summary>校验后 AutoCAD 实际采用的介质（诊断用；取不到返回空串）。</summary>
        public string ValidatedMediaName
        {
            get
            {
                try
                {
                    PlotSettings validated = _info.ValidatedSettings;
                    return validated == null ? "" : (validated.CanonicalMediaName ?? "");
                }
                catch (System.Exception) { return ""; }
            }
        }

        public void BeginPlot()
        {
            EnsureEngine();
            Guard("BeginPlot", () => _engine.BeginPlot(null, null));
        }

        public void BeginDocument(string documentName, string fileName)
        {
            EnsureEngine();
            Guard("BeginDocument", () => _engine.BeginDocument(_info, documentName ?? "",
                null, 1, true, fileName));
        }

        public void BeginPage()
        {
            EnsureEngine();
            Guard("BeginPage", () => _engine.BeginPage(new PlotPageInfo(), _info, true, null));
        }

        public void BeginGenerateGraphics()
        {
            EnsureEngine();
            Guard("BeginGenerateGraphics", () => _engine.BeginGenerateGraphics(null));
        }

        public void EndGenerateGraphics()
        {
            EnsureEngine();
            Guard("EndGenerateGraphics", () => _engine.EndGenerateGraphics(null));
        }

        public void EndPage()
        {
            EnsureEngine();
            Guard("EndPage", () => _engine.EndPage(null));
        }

        public void EndDocument()
        {
            EnsureEngine();
            Guard("EndDocument", () => _engine.EndDocument(null));
        }

        public void EndPlot()
        {
            EnsureEngine();
            Guard("EndPlot", () => _engine.EndPlot(null));
        }

        public string Describe()
            => "布局=" + LayoutName + "  模型空间=" + IsModelLayout
                + "  设备=" + ConfiguredDeviceName
                + "  介质=" + ConfiguredMediaName
                + "  介质数量=" + _mediaCount
                + (_window == null ? "" : "  " + _window.Describe())
                + "  旋转=" + PdfPlotPolicy.DescribeRotation(_rotation)
                + "  打印样式表=" + (_layout != null && _layout.PlotPlotStyles
                    ? "套用" : "不套用(彩色)")
                + "  校验=" + (IsValidated ? "通过" : "未通过");

        /// <summary>
        /// 结束会话：先还原用户的布局打印设置，再释放打印对象。
        /// 还原失败只记日志（并在 <see cref="PdfPlotSettingsSnapshot.Problems"/> 里留证），
        /// 绝不抛出 —— 出图结果不能被清理阶段的异常改变。
        /// </summary>
        public void Dispose()
        {
            if (_engine != null)
            {
                try { _engine.Dispose(); }
                catch (System.Exception ex) { Log.Warn("释放打印引擎失败: " + ex.Message); }
                _engine = null;
            }
            if (_original != null && _layout != null)
            {
                try { _original.Restore(_layout); }
                catch (System.Exception ex)
                {
                    Log.Warn("还原布局打印设置失败: " + ex.Message);
                }
            }
            TryDispose(_infoValidator);
            TryDispose(_info);
        }

        private void EnsureEngine()
            => _engine = _engine ?? PlotFactory.CreatePublishEngine();

        /// <summary>把 AutoCAD 的异常翻译成"步骤 + ErrorStatus"的一行中文。</summary>
        private static void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new PdfPlotException(what + "失败：" + ex.ErrorStatus
                    + (string.IsNullOrWhiteSpace(ex.Message) ? "" : "（" + ex.Message + "）"), ex);
            }
            catch (System.Exception ex)
            {
                throw new PdfPlotException(what + "失败：" + ex.GetType().Name
                    + "：" + ex.Message, ex);
            }
        }

        private static void TryDispose(IDisposable value)
        {
            if (value == null) return;
            try { value.Dispose(); }
            catch (System.Exception ex) { Log.Warn("释放打印对象失败: " + ex.Message); }
        }

        private static string[] ToArray(StringCollection values)
        {
            if (values == null) return Array.Empty<string>();
            var result = new string[values.Count];
            values.CopyTo(result, 0);
            return result;
        }
    }
}

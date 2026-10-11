using System;
using System.Collections.Generic;

namespace UNCAD.Features.PdfExport
{
    /// <summary>打印链路里由 AutoCAD 抛出的异常（已翻译成中文步骤说明，含 ErrorStatus）。</summary>
    internal sealed class PdfPlotException : Exception
    {
        internal PdfPlotException(string message) : base(message) { }
        internal PdfPlotException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// 打印管线的最小抽象：真实实现包住 PlotSettingsValidator / PlotInfoValidator / PlotEngine，
    /// 测试用假实现即可断言"调用顺序"以及"某一步失败后绝不再往下调"。
    /// 刻意只用基础类型（string/double/bool），让单测不依赖 AutoCAD 程序集。
    /// </summary>
    internal interface IPlotSession : IDisposable
    {
        string LayoutName { get; }
        bool IsModelLayout { get; }
        /// <summary>当前生效的设备名（SetPlotConfigurationName 之后）。</summary>
        string ConfiguredDeviceName { get; }
        /// <summary>当前生效的规范介质名。</summary>
        string ConfiguredMediaName { get; }
        bool UseStandardScale { get; }
        /// <summary>最近一次读取到的规范介质数量（诊断用）。</summary>
        int MediaCount { get; }

        string[] GetPlotDeviceList();
        void SetPlotConfigurationName(string deviceName);
        void SetPlotPaperUnitsMillimeters();
        string[] GetCanonicalMediaNames();
        void SetCanonicalMediaName(string mediaName);
        void SetPlotRotation(PdfPlotRotation rotation);
        void SetPlotTypeWindow();
        void SetWindowArea(PdfPlotWindow window);
        void SetPlotCentered(bool centered);
        void SetUseStandardScale(bool useStandardScale);
        void SetStdScaleTypeFit();
        /// <summary>彩色：不套用打印样式表（否则 monochrome.ctb 会吃掉图层颜色）。</summary>
        void SetColorPreserving();
        /// <summary>PlotInfoValidator.Validate（AutoCAD 中返回 void，非法时抛 eInvalidInput）。</summary>
        void Validate();
        /// <summary>校验是否真的通过（Validate 返回 void，只能看 PlotInfo.IsValidated）。</summary>
        bool IsValidated { get; }
        /// <summary>校验后 AutoCAD 实际采用的介质（诊断用；取不到为空串）。</summary>
        string ValidatedMediaName { get; }
        void BeginPlot();
        void BeginDocument(string documentName, string fileName);
        void BeginPage();
        void BeginGenerateGraphics();
        void EndGenerateGraphics();
        void EndPage();
        void EndDocument();
        void EndPlot();
        /// <summary>当前全部生效设置的只读描述（诊断用）。</summary>
        string Describe();
    }

    /// <summary>
    /// 逐步骤诊断：记录每一步的状态与首个失败原因。
    /// 用户真机上出图失败时，日志里能直接看到"是哪一步、什么输入状态"导致的
    /// <c>eInvalidInput</c>，不用再靠猜。
    /// </summary>
    internal sealed class PdfPlotDiagnostics
    {
        private readonly List<string> _state = new List<string>();
        private readonly List<string> _lines = new List<string>();

        /// <summary>首个失败原因（用于命令汇总）；未失败时为空。</summary>
        internal string FailureReason { get; private set; } = "";

        /// <summary>首个失败发生在哪一步；未失败时为空。</summary>
        internal string FailedStep { get; private set; } = "";

        /// <summary>给日志看的详细行（含失败首行与状态明细）。</summary>
        internal IReadOnlyList<string> Lines => _lines;

        internal void State(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) _state.Add(text);
        }

        internal void Info(string line)
        {
            if (!string.IsNullOrWhiteSpace(line)) _lines.Add(line);
        }

        /// <summary>
        /// 记录一次失败并返回 false，便于 <c>return diagnostics.Fail(...)</c>。
        /// 只把**第一次**失败作为主原因（清理阶段的附带失败不会覆盖它）。
        /// </summary>
        internal bool Fail(string step, string reason, params string[] detail)
        {
            string text = step + "：" + (reason ?? "");
            if (FailureReason.Length == 0)
            {
                FailureReason = text;
                FailedStep = step ?? "";
                _lines.Insert(0, "出图失败 → " + text);
                foreach (string state in _state) _lines.Add("  " + state);
            }
            else
            {
                _lines.Add("附带失败 → " + text);
            }
            foreach (string item in detail ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(item)) _lines.Add("  " + item);
            return false;
        }
    }

    /// <summary>
    /// U1PDF 的出图步骤编排（不依赖 AutoCAD，可用假 session 单测）。
    ///
    /// 顺序固定：① 布局 → ② 打印设备（并确认真的生效）→ ③ 纸张单位 →
    /// ④ 规范介质（显式选择，绝不放任默认）→ ⑤ 方向/窗口/居中 →
    /// ⑥ 比例（**先 UseStandardScale 再 StdScaleType**）→ ⑦ 彩色 + 单份 →
    /// ⑧ PlotInfoValidator.Validate → ⑨ 打印引擎序列。
    ///
    /// 任何一步失败都**立即停止**，绝不带着半套设置去启动打印引擎 —— 那正是用户真机上
    /// 只能拿到一个 `eInvalidInput` 的原因。
    /// </summary>
    internal static class PdfPlotPipeline
    {
        internal static bool Run(IPlotSession session, string documentName, string filePath,
            PdfPlotWindow window, PdfPlotDiagnostics diagnostics)
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            if (session == null) return diagnostics.Fail("① 取 Layout", "缺少打印会话。");
            if (window == null) return diagnostics.Fail("① 取 Layout", "缺少图框窗口。");

            // ① 布局
            diagnostics.State("布局=" + session.LayoutName + "  模型空间=" + session.IsModelLayout);
            if (!session.IsModelLayout)
                diagnostics.Info("提示：当前空间是图纸空间布局 " + session.LayoutName
                    + "，按图纸空间坐标窗口出图。");
            diagnostics.Info("① 布局=" + session.LayoutName
                + "  模型空间=" + session.IsModelLayout);

            // ② 打印设备（必须出现在设备列表里，且设置后必须真的生效）
            string[] devices;
            if (!Step(() => session.GetPlotDeviceList(), out devices,
                "② 读取打印设备列表", diagnostics)) return false;
            bool available = false;
            foreach (string device in devices ?? Array.Empty<string>())
                if (string.Equals(device, PdfExportNaming.DeviceName,
                    StringComparison.OrdinalIgnoreCase)) { available = true; break; }
            if (!available)
                return diagnostics.Fail("② 检查打印设备",
                    "设备列表中没有“" + PdfExportNaming.DeviceName + "”",
                    "设备数量=" + (devices?.Length ?? 0),
                    "设备列表=" + Join(devices));
            if (!Step(() => session.SetPlotConfigurationName(PdfExportNaming.DeviceName),
                "② SetPlotConfigurationName", diagnostics,
                "请求设备=" + PdfExportNaming.DeviceName)) return false;
            if (!string.Equals(session.ConfiguredDeviceName, PdfExportNaming.DeviceName,
                    StringComparison.OrdinalIgnoreCase))
                return diagnostics.Fail("② SetPlotConfigurationName",
                    "设置后生效的设备不是 PDF 设备（实际：" + session.ConfiguredDeviceName + "）",
                    "为避免出图到别的设备，已停止。");

            // ③ 纸张单位（毫米，与图框坐标一致）
            if (!Step(() => session.SetPlotPaperUnitsMillimeters(), "③ SetPlotPaperUnits(毫米)",
                diagnostics)) return false;

            // ④ 规范介质：显式选择，绝不放任设备默认介质（默认介质往往是 0×0 的 UserDefined，
            //    或者干脆是别的纸型）。方向与图框一致的那份优先，PDF 页面就不用转屏看。
            string[] media;
            if (!Step(() => session.GetCanonicalMediaNames(), out media,
                "④ 读取规范介质列表", diagnostics)) return false;
            PdfPlotMediaChoice choice = PdfPlotPolicy.SelectMedia(media, window);
            if (choice == null)
                return diagnostics.Fail("④ 选择规范介质",
                    "设备“" + PdfExportNaming.DeviceName + "”没有可用规范介质（列表为空）");
            if (!Step(() => session.SetCanonicalMediaName(choice.MediaName),
                "④ SetCanonicalMediaName", diagnostics, "介质=" + choice.MediaName)) return false;
            if (!string.Equals(session.ConfiguredMediaName, choice.MediaName,
                    StringComparison.OrdinalIgnoreCase))
                return diagnostics.Fail("④ SetCanonicalMediaName",
                    "设置后生效的介质与请求不一致（请求：" + choice.MediaName
                    + "，实际：" + session.ConfiguredMediaName + "）");

            // ⑤ 纸张方向 + 窗口 + 打印范围 + 居中
            //
            // **顺序不能变（真机实测）**：必须先把窗口区域设置好，再 SetPlotType(Window)。
            // 反过来（先设 Window 再设窗口）AutoCAD 会以 eInvalidInput 拒绝
            // —— 2.6.0 真机出图失败的根因就在这里。
            diagnostics.State("设备=" + PdfExportNaming.DeviceName
                + "  介质=" + choice.Describe()
                + "  介质数量=" + (media?.Length ?? 0));
            diagnostics.State(window.Describe());
            if (!Step(() => session.SetPlotRotation(choice.Rotation), "⑤ SetPlotRotation",
                diagnostics, "旋转=" + PdfPlotPolicy.DescribeRotation(choice.Rotation)))
                return false;
            if (!Step(() => session.SetWindowArea(window), "⑤ SetPlotWindowArea",
                diagnostics, window.Describe())) return false;
            if (!Step(() => session.SetPlotTypeWindow(), "⑤ SetPlotType(Window)",
                diagnostics)) return false;
            if (!Step(() => session.SetPlotCentered(true), "⑤ SetPlotCentered",
                diagnostics)) return false;

            // ⑥ 比例：必须先 UseStandardScale(true)，否则 SetStdScaleType 会抛 eInvalidInput
            if (!Step(() => session.SetUseStandardScale(true), "⑥ SetUseStandardScale",
                diagnostics)) return false;
            if (!Step(() => session.SetStdScaleTypeFit(), "⑥ SetStdScaleType(ScaleToFit)",
                diagnostics)) return false;
            if (!session.UseStandardScale)
                return diagnostics.Fail("⑥ SetUseStandardScale",
                    "标准比例未生效，SetStdScaleType(ScaleToFit) 会被忽略。");

            // ⑦ 彩色（不套用打印样式表，保留图层颜色）；份数由 BeginDocument 固定为 1
            if (!Step(() => session.SetColorPreserving(), "⑦ 彩色（不套用打印样式表）",
                diagnostics)) return false;

            // ⑧ 校验：非法时 AutoCAD 抛 eInvalidInput，这里必须停止而不是继续
            if (!Step(() => session.Validate(), "⑧ PlotInfoValidator.Validate",
                diagnostics, session.Describe())) return false;
            if (!session.IsValidated)
                return diagnostics.Fail("⑧ PlotInfoValidator.Validate",
                    "校验没有通过（PlotInfo.IsValidated=false），已停止出图",
                    session.Describe());
            diagnostics.Info("⑧ PlotInfoValidator.Validate 通过  生效介质="
                + (string.IsNullOrWhiteSpace(session.ValidatedMediaName)
                    ? choice.MediaName : session.ValidatedMediaName));

            // ⑨ 打印引擎序列：每一步单独包装，失败时能指出具体调用
            bool plotBegun = false;
            bool documentBegun = false;
            bool pageBegun = false;
            try
            {
                if (!Step(() => { session.BeginPlot(); plotBegun = true; },
                    "⑨ BeginPlot", diagnostics)) return false;
                if (!Step(() => { session.BeginDocument(documentName ?? "", filePath);
                        documentBegun = true; }, "⑨ BeginDocument", diagnostics,
                    "文件=" + filePath)) return false;
                if (!Step(() => { session.BeginPage(); pageBegun = true; },
                    "⑨ BeginPage", diagnostics)) return false;
                if (!Step(() => session.BeginGenerateGraphics(), "⑨ BeginGenerateGraphics",
                    diagnostics)) return false;
                if (!Step(() => session.EndGenerateGraphics(), "⑨ EndGenerateGraphics",
                    diagnostics)) return false;
                if (!Step(() => session.EndPage(), "⑨ EndPage", diagnostics)) return false;
                pageBegun = false;
                if (!Step(() => session.EndDocument(), "⑨ EndDocument", diagnostics)) return false;
                documentBegun = false;
                if (!Step(() => session.EndPlot(), "⑨ EndPlot", diagnostics)) return false;
                plotBegun = false;
            }
            finally
            {
                // 失败时尽力收尾，避免打印引擎停留在打开状态；清理失败不覆盖主失败原因。
                if (pageBegun) Step(() => session.EndPage(), "⑨ EndPage(清理)", diagnostics);
                if (documentBegun)
                    Step(() => session.EndDocument(), "⑨ EndDocument(清理)", diagnostics);
                if (plotBegun) Step(() => session.EndPlot(), "⑨ EndPlot(清理)", diagnostics);
            }
            return true;
        }

        private static string Join(IEnumerable<string> values)
        {
            var items = new List<string>();
            foreach (string value in values ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(value)) items.Add(value);
            return items.Count == 0 ? "(空)" : string.Join("、", items.ToArray());
        }

        private static bool Step(Action action, string step, PdfPlotDiagnostics diagnostics,
            params string[] state)
        {
            try
            {
                action();
                return true;
            }
            catch (System.Exception ex)
            {
                return diagnostics.Fail(step, Describe(ex), state);
            }
        }

        private static bool Step<T>(Func<T> action, out T value, string step,
            PdfPlotDiagnostics diagnostics, params string[] state)
        {
            try
            {
                value = action();
                return true;
            }
            catch (System.Exception ex)
            {
                value = default(T);
                return diagnostics.Fail(step, Describe(ex), state);
            }
        }

        /// <summary>异常 → 一行说明。<see cref="PdfPlotException"/> 已经带中文步骤与 ErrorStatus。</summary>
        private static string Describe(System.Exception ex)
        {
            if (ex is PdfPlotException) return ex.Message;
            return ex.GetType().Name + "：" + ex.Message;
        }
    }
}

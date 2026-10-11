using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Infra;

namespace UNCAD.Features.PdfExport
{
    /// <summary>
    /// 布局打印设置的完整快照：出图前拍一份，出图结束后**原样还原**。
    ///
    /// 为什么必须走"改布局设置再还原"这条路（真机实测结论）：
    /// 出图必须把设备和介质设置到**布局自身的** <c>PlotSettings</c> 上，
    /// 打印引擎才认；用 <c>PlotInfo.OverrideSettings</c> 传一份独立的
    /// <c>PlotSettings</c> 副本时，<c>PlotInfoValidator.Validate</c> 会把请求的
    /// A3 介质重新匹配成布局自带的 A4（校验通过但出的是 A4），
    /// 用户拿到的 PDF 纸张就不是我们要的。
    ///
    /// 代价是用户布局设置被临时改动，所以这里负责把它完整还原 —— 用户视角
    /// "打印设置没被动过"由 <see cref="Restore"/> 保证，并由真机自检
    /// （UNCAD_U1PDF_SELFTEST）逐字段比对锁定。
    ///
    /// 唯一无法写回的是 <c>PlotSettings.StdScale</c>：它是 AutoCAD 按当前窗口与纸张
    /// 算出来的比例因子缓存（"布满图纸"模式下用户看不到它），出图后会被重算，
    /// 且 <c>SetStdScale</c> 也写不回去。用户能看到的比例选项
    /// （UseStandardScale / StdScaleType）都是原样还原的。
    /// </summary>
    internal sealed class PdfPlotSettingsSnapshot
    {
        private readonly List<string> _problems = new List<string>();

        private PdfPlotSettingsSnapshot() { }

        private string Device { get; set; } = "";
        private string Media { get; set; } = "";
        private PlotPaperUnit PaperUnits { get; set; }
        private PlotType PlotType { get; set; }
        private PlotRotation Rotation { get; set; }
        private bool Centered { get; set; }
        private bool UseStandardScale { get; set; }
        private StdScaleType StandardScaleType { get; set; }
        private double StandardScale { get; set; }
        private Extents2d WindowArea { get; set; }
        private string ViewName { get; set; } = "";
        private bool PlotPlotStyles { get; set; }
        private bool ShowPlotStyles { get; set; }
        private string StyleSheet { get; set; } = "";
        private Point2d Origin { get; set; }
        private Point2d PaperSize { get; set; }
        private bool PrintLineweights { get; set; }
        private bool ScaleLineweights { get; set; }
        private bool DrawViewportsFirst { get; set; }
        private bool PlotViewportBorders { get; set; }
        private bool PlotHidden { get; set; }
        private bool PlotTransparency { get; set; }
        private PlotSettingsShadePlotType ShadePlot { get; set; }

        /// <summary>还原过程中遇到的问题（正常情况下为空）。</summary>
        internal IReadOnlyList<string> Problems => _problems;

        internal static PdfPlotSettingsSnapshot Capture(PlotSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var snapshot = new PdfPlotSettingsSnapshot();
            snapshot.Device = settings.PlotConfigurationName ?? "";
            snapshot.Media = settings.CanonicalMediaName ?? "";
            snapshot.PaperUnits = settings.PlotPaperUnits;
            snapshot.PlotType = settings.PlotType;
            snapshot.Rotation = settings.PlotRotation;
            snapshot.Centered = settings.PlotCentered;
            snapshot.UseStandardScale = settings.UseStandardScale;
            snapshot.StandardScaleType = settings.StdScaleType;
            snapshot.StandardScale = settings.StdScale;
            snapshot.WindowArea = settings.PlotWindowArea;
            snapshot.ViewName = settings.PlotViewName ?? "";
            snapshot.PlotPlotStyles = settings.PlotPlotStyles;
            snapshot.ShowPlotStyles = settings.ShowPlotStyles;
            snapshot.StyleSheet = settings.CurrentStyleSheet ?? "";
            snapshot.Origin = settings.PlotOrigin;
            snapshot.PaperSize = settings.PlotPaperSize;
            snapshot.PrintLineweights = settings.PrintLineweights;
            snapshot.ScaleLineweights = settings.ScaleLineweights;
            snapshot.DrawViewportsFirst = settings.DrawViewportsFirst;
            snapshot.PlotViewportBorders = settings.PlotViewportBorders;
            snapshot.PlotHidden = settings.PlotHidden;
            snapshot.PlotTransparency = settings.PlotTransparency;
            snapshot.ShadePlot = settings.ShadePlot;
            return snapshot;
        }

        /// <summary>还原成快照状态。逐项独立还原，单项失败只记录，不影响其余项。</summary>
        internal void Restore(PlotSettings settings)
        {
            if (settings == null) return;
            PlotSettingsValidator validator = PlotSettingsValidator.Current;

            Step("打印设备/介质", () => validator.SetPlotConfigurationName(settings,
                Device, string.IsNullOrWhiteSpace(Media) ? null : Media));
            Step("纸张单位", () => validator.SetPlotPaperUnits(settings, PaperUnits));
            Step("打印旋转", () => validator.SetPlotRotation(settings, Rotation));
            Step("打印份数以外的线宽设置", () =>
            {
                settings.PrintLineweights = PrintLineweights;
                settings.ScaleLineweights = ScaleLineweights;
                settings.DrawViewportsFirst = DrawViewportsFirst;
                settings.PlotViewportBorders = PlotViewportBorders;
                settings.PlotHidden = PlotHidden;
                settings.PlotTransparency = PlotTransparency;
                settings.ShadePlot = ShadePlot;
                settings.PlotPlotStyles = PlotPlotStyles;
                settings.ShowPlotStyles = ShowPlotStyles;
            });
            Step("打印原点", () => validator.SetPlotOrigin(settings, Origin));
            if (!string.IsNullOrWhiteSpace(StyleSheet))
                Step("打印样式表", () => validator.SetCurrentStyleSheet(settings, StyleSheet));
            if (!string.IsNullOrWhiteSpace(ViewName))
                Step("命名视图", () => validator.SetPlotViewName(settings, ViewName));
            Step("打印窗口", () => validator.SetPlotWindowArea(settings, WindowArea));
            Step("居中打印", () => validator.SetPlotCentered(settings, Centered));
            Step("使用标准比例", () => validator.SetUseStandardScale(settings, UseStandardScale));
            if (UseStandardScale)
                Step("标准比例类型", () => validator.SetStdScaleType(settings, StandardScaleType));
            // 打印范围类型放最后：Window 需要窗口区域已经就位。
            Step("打印范围类型", () => validator.SetPlotType(settings, PlotType));
        }

        /// <summary>一行诊断文案（含"本来是什么"，便于对照）。</summary>
        internal string Describe()
            => "原设置：设备=" + Device + " 介质=" + Media + " 单位=" + PaperUnits
                + " 范围=" + PlotType + " 窗口=" + PdfPlotWindow.Num(WindowArea.MinPoint.X)
                + "," + PdfPlotWindow.Num(WindowArea.MinPoint.Y) + "-"
                + PdfPlotWindow.Num(WindowArea.MaxPoint.X) + ","
                + PdfPlotWindow.Num(WindowArea.MaxPoint.Y)
                + " 比例=" + (UseStandardScale ? StandardScaleType.ToString()
                    : StandardScale.ToString("0.####"))
                + " 居中=" + Centered + " 旋转=" + Rotation
                + " 打印样式表=" + (PlotPlotStyles ? "套用" : "不套用")
                + " 样式表名=" + (StyleSheet.Length == 0 ? "(无)" : StyleSheet);

        private void Step(string name, Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception ex)
            {
                string message = "还原“" + name + "”失败：" + ex.Message;
                _problems.Add(message);
                Log.Warn("U1PDF 还原布局打印设置 | " + message);
            }
        }
    }
}

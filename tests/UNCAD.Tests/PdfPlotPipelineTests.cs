using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UNCAD.Features.PdfExport;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1PDF 出图内核的契约（不依赖 AutoCAD，用假会话驱动管线）。
    ///
    /// 这里锁的是**真机踩过的坑**：
    /// - 窗口区域必须先于 <c>SetPlotType(Window)</c> 设置，否则 AutoCAD 抛 eInvalidInput
    ///   （2.6.0 真机出图失败的根因）；
    /// - 任何一步失败都必须**立即停止**，绝不带着半套设置去启动打印引擎；
    /// - 失败时要逐步诊断（哪一步 + eInvalidInput），不能再让用户只看到一个 eInvalidInput；
    /// - 纸张方向要尽量与图框一致，PDF 页面不用转屏看。
    /// </summary>
    public sealed class PdfPlotPipelineTests
    {
        private const string A3Landscape = "ISO_A3_(420.00_x_297.00_MM)";
        private const string A3Portrait = "ISO_A3_(297.00_x_420.00_MM)";
        private const string A4Landscape = "ISO_A4_(297.00_x_210.00_MM)";
        private const string A3FullBleed = "ISO_full_bleed_A3_(420.00_x_297.00_MM)";

        // ---- 真机根因 1：窗口必须先于打印范围 ----

        [Fact]
        public void Pipeline_SetsWindowAreaBeforePlotTypeWindow()
        {
            var session = new FakePlotSession();
            PdfPlotDiagnostics diagnostics = Run(session);

            int window = session.Index("SetWindowArea");
            int type = session.Index("SetPlotTypeWindow");
            Assert.True(window >= 0, "必须设置打印窗口");
            Assert.True(type >= 0, "必须设置打印范围为窗口");
            Assert.True(window < type,
                "SetPlotWindowArea 必须先于 SetPlotType(Window)："
                + "反过来 AutoCAD 会抛 eInvalidInput（真机实测）。实际顺序="
                + string.Join(" → ", session.Calls));
            Assert.True(diagnostics.FailureReason.Length == 0, diagnostics.FailureReason);
        }

        [Fact]
        public void Pipeline_UsesStandardScaleBeforeStdScaleType()
        {
            var session = new FakePlotSession();
            Run(session);
            Assert.True(session.Index("SetUseStandardScale") < session.Index("SetStdScaleTypeFit"),
                "SetUseStandardScale(true) 必须先于 SetStdScaleType(ScaleToFit)");
        }

        [Fact]
        public void Pipeline_PreservesColorAndValidatesBeforePlotting()
        {
            var session = new FakePlotSession();
            Run(session);
            Assert.True(session.Index("SetColorPreserving") < session.Index("Validate"),
                "彩色设置必须在校验之前完成");
            Assert.True(session.Index("Validate") < session.Index("BeginPlot"),
                "必须先校验再启动打印引擎");
        }

        // ---- 任何一步失败都必须立即停止 ----

        [Fact]
        public void Pipeline_StopsBeforeEngineWhenTheDeviceIsMissing()
        {
            var session = new FakePlotSession { Devices = new[] { "无", "Microsoft Print to PDF" } };
            PdfPlotDiagnostics diagnostics = Run(session);

            Assert.False(diagnostics.FailureReason.Length == 0);
            Assert.Contains("设备列表中没有", diagnostics.FailureReason);
            Assert.DoesNotContain("BeginPlot", session.Calls);
            Assert.DoesNotContain("SetPlotConfigurationName", session.Calls);
        }

        [Fact]
        public void Pipeline_StopsWhenTheDeviceDidNotTakeEffect()
        {
            var session = new FakePlotSession { DeviceAfterSet = "无" };
            PdfPlotDiagnostics diagnostics = Run(session);

            Assert.Contains("生效的设备不是 PDF 设备", diagnostics.FailureReason);
            Assert.DoesNotContain("BeginPlot", session.Calls);
        }

        [Fact]
        public void Pipeline_StopsWhenTheDeviceHasNoMedia()
        {
            var session = new FakePlotSession { Media = Array.Empty<string>() };
            PdfPlotDiagnostics diagnostics = Run(session);

            Assert.Contains("没有可用规范介质", diagnostics.FailureReason);
            Assert.DoesNotContain("BeginPlot", session.Calls);
            Assert.DoesNotContain("SetPlotWindowArea", session.Calls);
        }

        [Fact]
        public void Pipeline_StopsWhenValidationThrowsAndNamesTheStep()
        {
            var session = new FakePlotSession
            {
                ThrowOn = "Validate",
                ThrowMessage = "校验打印设置失败：InvalidInput（eInvalidInput）"
            };
            PdfPlotDiagnostics diagnostics = Run(session);

            Assert.Contains("⑧ PlotInfoValidator.Validate", diagnostics.FailureReason);
            Assert.Contains("eInvalidInput", diagnostics.FailureReason);
            Assert.DoesNotContain("BeginPlot", session.Calls);
            Assert.DoesNotContain("BeginDocument", session.Calls);
        }

        [Fact]
        public void Pipeline_StopsWhenValidationSilentlyDidNotApply()
        {
            var session = new FakePlotSession { Validated = false };
            PdfPlotDiagnostics diagnostics = Run(session);

            Assert.Contains("校验没有通过", diagnostics.FailureReason);
            Assert.DoesNotContain("BeginPlot", session.Calls);
        }

        // ---- 打印引擎序列与收尾 ----

        [Fact]
        public void Pipeline_RunsTheEngineSequenceInOrder()
        {
            var session = new FakePlotSession();
            Run(session);

            string[] engine = session.Calls.Where(call => call.StartsWith("Begin", StringComparison.Ordinal)
                || call.StartsWith("End", StringComparison.Ordinal)).ToArray();
            Assert.Equal(new[]
            {
                "BeginPlot", "BeginDocument", "BeginPage", "BeginGenerateGraphics",
                "EndGenerateGraphics", "EndPage", "EndDocument", "EndPlot"
            }, engine);
        }

        [Fact]
        public void Pipeline_CleansUpTheEngineWhenPageGenerationFails()
        {
            var session = new FakePlotSession
            {
                ThrowOn = "BeginPage",
                ThrowMessage = "BeginPage失败：InvalidInput（eInvalidInput）"
            };
            PdfPlotDiagnostics diagnostics = Run(session);

            Assert.Contains("⑨ BeginPage", diagnostics.FailureReason);
            // 收尾必须尝试过，避免打印引擎卡在打开状态。
            Assert.Contains("EndDocument", session.Calls);
            Assert.Contains("EndPlot", session.Calls);
            // 主失败原因不能被清理阶段的失败覆盖。
            Assert.DoesNotContain("清理", diagnostics.FailureReason);
        }

        [Fact]
        public void Pipeline_ReportsFailureDetailsForTheLog()
        {
            var session = new FakePlotSession { ThrowOn = "SetWindowArea" };
            PdfPlotDiagnostics diagnostics = Run(session);
            Assert.Contains("⑤ SetPlotWindowArea", diagnostics.FailureReason);
            Assert.Contains(diagnostics.Lines, line => line.Contains("出图失败"));
            Assert.Contains(diagnostics.Lines, line => line.Contains("窗口="));
        }

        // ---- 介质与方向 ----

        [Fact]
        public void Pipeline_PicksTheMediaWhoseOrientationMatchesTheFrame()
        {
            var landscape = new FakePlotSession { Media = new[] { A3Portrait, A3Landscape } };
            Run(landscape, Landscape());
            Assert.Equal(A3Landscape, landscape.LastMedia);
            Assert.Equal(PdfPlotRotation.None, landscape.LastRotation);

            var portrait = new FakePlotSession { Media = new[] { A3Landscape, A3Portrait } };
            Run(portrait, Portrait());
            Assert.Equal(A3Portrait, portrait.LastMedia);
            Assert.Equal(PdfPlotRotation.None, portrait.LastRotation);
        }

        [Fact]
        public void Pipeline_RotatesWhenOnlyTheOtherOrientationExists()
        {
            var session = new FakePlotSession { Media = new[] { A3Landscape } };
            Run(session, Portrait());
            Assert.Equal(A3Landscape, session.LastMedia);
            Assert.Equal(PdfPlotRotation.Quarter, session.LastRotation);
        }

        // ---- 纯策略 ----

        [Theory]
        [InlineData(A3Landscape, 420d, 297d)]
        [InlineData("ISO_full_bleed_B5_(250.00_x_176.00_MM)", 250d, 176d)]
        [InlineData("ANSI_A_(8.50_x_11.00_Inches)", 8.5d, 11d)]
        public void TryParseMediaSize_ReadsTheSizeFromTheCanonicalName(string name,
            double width, double height)
        {
            Assert.True(PdfPlotPolicy.TryParseMediaSize(name, out double w, out double h));
            Assert.Equal(width, w, 3);
            Assert.Equal(height, h, 3);
            Assert.False(PdfPlotPolicy.TryParseMediaSize("UserDefined", out _, out _));
            Assert.False(PdfPlotPolicy.TryParseMediaSize("", out _, out _));
        }

        [Fact]
        public void SelectMedia_PrefersA3ThenA4AndSkipsUserDefinedPixels()
        {
            string picked = PdfPlotPolicy.SelectMedia(new[]
            {
                "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
                A3Landscape,
                "UserDefined_(0.00_x_0.00_MM)",
                "Sun_Hi-Res_(1600.00_x_1280.00_Pixels)"
            }, Landscape())?.MediaName;
            Assert.Equal(A3Landscape, picked);

            string fallback = PdfPlotPolicy.SelectMedia(new[]
            {
                "UserDefined_(0.00_x_0.00_MM)",
                "Sun_Hi-Res_(1600.00_x_1280.00_Pixels)"
            }, Landscape())?.MediaName;
            Assert.Equal("UserDefined_(0.00_x_0.00_MM)", fallback);
        }

        [Fact]
        public void SelectMedia_PrefersThePlainSheetOverFullBleed()
        {
            string picked = PdfPlotPolicy.SelectMedia(new[] { A3FullBleed, A3Landscape },
                Landscape())?.MediaName;
            Assert.Equal(A3Landscape, picked);

            // 只有 full_bleed 版本时也接受（好过没有）。
            string only = PdfPlotPolicy.SelectMedia(new[] { A3FullBleed }, Landscape())?.MediaName;
            Assert.Equal(A3FullBleed, only);
        }

        [Fact]
        public void SelectMedia_ReturnsNullForAnEmptyList()
            => Assert.Null(PdfPlotPolicy.SelectMedia(Array.Empty<string>(), Landscape()));

        [Theory]
        [InlineData(420d, 297d, 420d, 297d, 0)]   // 图框横向 / 纸张横向 → 不旋转
        [InlineData(297d, 420d, 297d, 420d, 0)]   // 图框纵向 / 纸张纵向 → 不旋转
        [InlineData(420d, 297d, 297d, 420d, 90)]   // 图框横向 / 纸张纵向 → 旋转 90°
        [InlineData(297d, 420d, 420d, 297d, 90)]   // 图框纵向 / 纸张横向 → 旋转 90°
        public void SelectRotation_MatchesTheFrameOrientation(double frameWidth,
            double frameHeight, double paperWidth, double paperHeight, int expected)
            => Assert.Equal((PdfPlotRotation)expected, PdfPlotPolicy.SelectRotation(
                new PdfPlotWindow(0, 0, frameWidth, frameHeight), paperWidth, paperHeight));

        [Fact]
        public void SelectRotation_DoesNotRotateWhenSizesAreUnknown()
        {
            Assert.Equal(PdfPlotRotation.None,
                PdfPlotPolicy.SelectRotation(Landscape(), 0d, 0d));
            Assert.Equal(PdfPlotRotation.None,
                PdfPlotPolicy.SelectRotation(null, 420d, 297d));
        }

        [Fact]
        public void TryValidateWindow_RejectsDegenerateAndNonFiniteWindows()
        {
            Assert.False(PdfPlotPolicy.TryValidateWindow(null, out string missing));
            Assert.Contains("找不到图框边界", missing);

            Assert.False(PdfPlotPolicy.TryValidateWindow(new PdfPlotWindow(0, 0, 0, 0),
                out string degenerate));
            Assert.Contains("接近 0", degenerate);

            Assert.False(PdfPlotPolicy.TryValidateWindow(
                new PdfPlotWindow(0, 0, double.NaN, 10), out string nan));
            Assert.Contains("NaN", nan);

            Assert.False(PdfPlotPolicy.TryValidateWindow(
                new PdfPlotWindow(0, 0, 1e13, 10), out string huge));
            Assert.Contains("超出合理范围", huge);

            Assert.True(PdfPlotPolicy.TryValidateWindow(Landscape(), out string ok));
            Assert.Equal("", ok);
        }

        [Fact]
        public void Window_NormalizesCornersAndDescribesItself()
        {
            var window = PdfPlotWindow.FromBoundary(
                new UNCAD.Core.Geometry.FrameRectangle("f", 50420, 30297, 50000, 30000));
            Assert.Equal(50000d, window.MinX);
            Assert.Equal(30000d, window.MinY);
            Assert.Equal(420d, window.Width);
            Assert.Equal(297d, window.Height);
            Assert.True(window.IsLandscape);
            Assert.Contains("宽=420", window.Describe());
            Assert.Null(PdfPlotWindow.FromBoundary(null));
        }

        // ---- PDF 产物校验 ----

        [Fact]
        public void IsUsablePdf_RequiresMagicTrailerAndNonTrivialSize()
        {
            string good = WriteTemp("%PDF-1.7\n" + new string('x', 2048) + "\n%%EOF\n");
            string magicOnly = WriteTemp("%PDF-1.7\n" + new string('x', 2048) + "\n");
            string tiny = WriteTemp("%PDF-1.7\n%%EOF\n");
            try
            {
                Assert.True(PdfExportNaming.IsUsablePdf(good, out string ok));
                Assert.Equal("", ok);

                Assert.False(PdfExportNaming.IsUsablePdf(magicOnly, out string incomplete));
                Assert.Contains("%%EOF", incomplete);

                Assert.False(PdfExportNaming.IsUsablePdf(tiny, out string small));
                Assert.Contains("空白图", small);

                Assert.False(PdfExportNaming.IsUsablePdf(
                    Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N")),
                    out string absent));
                Assert.Contains("没有生成 PDF", absent);
            }
            finally
            {
                TryDelete(good);
                TryDelete(magicOnly);
                TryDelete(tiny);
            }
        }

        // ---- 辅助 ----

        private static PdfPlotWindow Landscape() => new PdfPlotWindow(0, 0, 420, 297);
        private static PdfPlotWindow Portrait() => new PdfPlotWindow(0, 0, 297, 420);

        private static PdfPlotDiagnostics Run(FakePlotSession session,
            PdfPlotWindow window = null)
        {
            var diagnostics = new PdfPlotDiagnostics();
            PdfPlotPipeline.Run(session, "测试回路", Path.Combine(Path.GetTempPath(), "x.pdf"),
                window ?? Landscape(), diagnostics);
            return diagnostics;
        }

        private static string WriteTemp(string content)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
            File.WriteAllText(path, content, Encoding.ASCII);
            return path;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception) { }
        }

        /// <summary>假打印会话：记录调用顺序，并可按需在指定步骤抛异常。</summary>
        private sealed class FakePlotSession : IPlotSession
        {
            internal List<string> Calls { get; } = new List<string>();
            internal string[] Devices { get; set; } = { PdfExportNaming.DeviceName };
            internal string[] Media { get; set; } = { A3Landscape, A4Landscape };
            internal string DeviceAfterSet { get; set; } = PdfExportNaming.DeviceName;
            internal bool Validated { get; set; } = true;
            internal string ThrowOn { get; set; } = "";
            internal string ThrowMessage { get; set; } = "步骤失败：InvalidInput（eInvalidInput）";
            internal string LastMedia { get; private set; } = "";
            internal PdfPlotRotation LastRotation { get; private set; }

            public string LayoutName => "Model";
            public bool IsModelLayout => true;
            public string ConfiguredDeviceName { get; private set; } = "";
            public string ConfiguredMediaName { get; private set; } = "";
            public bool UseStandardScale { get; private set; }
            public int MediaCount => Media.Length;
            public bool IsValidated { get; private set; }
            public string ValidatedMediaName => ConfiguredMediaName;

            public string[] GetPlotDeviceList()
            {
                Record("GetPlotDeviceList");
                return Devices;
            }

            public void SetPlotConfigurationName(string deviceName)
            {
                Record("SetPlotConfigurationName");
                ConfiguredDeviceName = DeviceAfterSet;
            }

            public void SetPlotPaperUnitsMillimeters() => Record("SetPlotPaperUnits");

            public string[] GetCanonicalMediaNames()
            {
                Record("GetCanonicalMediaNames");
                return Media;
            }

            public void SetCanonicalMediaName(string mediaName)
            {
                Record("SetCanonicalMediaName");
                ConfiguredMediaName = mediaName;
                LastMedia = mediaName;
            }

            public void SetPlotRotation(PdfPlotRotation rotation)
            {
                Record("SetPlotRotation");
                LastRotation = rotation;
            }

            public void SetPlotTypeWindow() => Record("SetPlotTypeWindow");

            public void SetWindowArea(PdfPlotWindow window) => Record("SetWindowArea");

            public void SetPlotCentered(bool centered) => Record("SetPlotCentered");

            public void SetUseStandardScale(bool useStandardScale)
            {
                Record("SetUseStandardScale");
                UseStandardScale = useStandardScale;
            }

            public void SetStdScaleTypeFit() => Record("SetStdScaleTypeFit");

            public void SetColorPreserving() => Record("SetColorPreserving");

            public void Validate()
            {
                Record("Validate");
                if (ThrowOn == "Validate")
                    throw new PdfPlotException(ThrowMessage);
                IsValidated = Validated;
            }

            public void BeginPlot() => Record("BeginPlot");
            public void BeginDocument(string documentName, string fileName)
                => Record("BeginDocument");
            public void BeginPage() => Record("BeginPage");
            public void BeginGenerateGraphics() => Record("BeginGenerateGraphics");
            public void EndGenerateGraphics() => Record("EndGenerateGraphics");
            public void EndPage() => Record("EndPage");
            public void EndDocument() => Record("EndDocument");
            public void EndPlot() => Record("EndPlot");

            public string Describe() => "假会话";

            public void Dispose() { }

            internal int Index(string call) => Calls.IndexOf(call);

            private void Record(string call)
            {
                Calls.Add(call);
                if (ThrowOn == call && call != "Validate")
                    throw new PdfPlotException(ThrowMessage);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Features.DwgExport;
using UNCAD.Features.PdfExport;
using UNCAD.Infra;
using UNCAD.UI;
using Wpf.Ui.Appearance;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// UI 全家福截图器（2026-11-03）：设置环境变量 UNCAD_UI_SHOTS 后按
    /// --filter UiScreenshotGallery 运行，把全部真实窗体 × 深/浅主题截图到
    /// 指定目录（未设置时立即通过，不影响常规测试）。
    /// demo/UiPreview 独立宿主加载 AcDbMgd 失败，故借用测试宿主的运行环境。
    /// </summary>
    [Collection("WpfUiSerial")]
    public sealed class UiScreenshotGalleryTests
    {
        [Trait("Category", "Manual")]
        [Fact]
        public void GenerateUiScreenshotGallery()
        {
            string target = Environment.GetEnvironmentVariable("UNCAD_UI_SHOTS");
            if (string.IsNullOrWhiteSpace(target)) return; // 常规跑测：不出图。
            if (target == "1")
            {
                target = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "tmp", "ui-shots"));
            }
            Directory.CreateDirectory(target);
            RunSta(() => Generate(target));
        }

        private static void Generate(string dir)
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                var appTheme = theme == "dark"
                    ? ApplicationTheme.Dark : ApplicationTheme.Light;

                // WPF-UI NavigationView 的页面服务在进程内只有首个实例生效，
                // 所以 11 页共用一个窗口顺序导航、逐页截图（与真实使用一致）；
                // 测试宿主没有渲染线程，过渡动画不会推进——禁掉过渡瞬时切换。
                var settings = new SettingsWindow(0);
                settings.Navi.Transition = Wpf.Ui.Animations.Transition.None;
                settings.Navi.TransitionDuration = 0;
                settings.Show();
                Pump(6);
                Thread.Sleep(450);
                for (int page = 0; page < 11; page++)
                {
                    settings.ShowPage(page);
                    Pump(4);
                    Thread.Sleep(700);
                    ScreenCopy.CaptureWindow(settings, Path.Combine(dir,
                        "settings-" + page + "-" + theme + ".png"));
                    Pump(2);
                }
                settings.Close();
                Pump(2);

                var picker = new MachinePickerWindow(
                    new[] { "MECTT03", "MDEGN01", "MPAMS07" },
                    id => string.Equals(id, "MECTT03",
                        StringComparison.OrdinalIgnoreCase)
                        ? SampleMachineRows() : new List<MachineRow>(),
                    row => row.CircuitName + " → " + row.Cable);
                picker.MachineInput.Text = "MECTT03";
                picker.CommitMachine();
                Capture(picker, appTheme,
                    Path.Combine(dir, "machine-picker-" + theme + ".png"));

                Capture(new XLayoutSelectionWindow(SampleLayoutItems()), appTheme,
                    Path.Combine(dir, "xlayout-select-" + theme + ".png"));
                Capture(new XLayoutSummaryWindow(SampleSummaries(), 8), appTheme,
                    Path.Combine(dir, "xlayout-summary-" + theme + ".png"));
                Capture(new XstsWindow(SampleXstsReport(), null), appTheme,
                    Path.Combine(dir, "xsts-" + theme + ".png"));

                var skipped = new BatchUpdateMachineRow("M-3F-07");
                skipped.Frames = 2; skipped.TableRows = 6; skipped.AttributeValues = 14;
                skipped.BoqStatus = "跳过（回路不全）";
                skipped.AddNote("缺少回路：货梯 L2、货梯 L5；本次照常填写图框数据、不生成 BOQ。");
                var filled = new BatchUpdateMachineRow("M-2F-01");
                filled.Frames = 5; filled.TableRows = 15; filled.AttributeValues = 42;
                filled.BoqStatus = "已生成";
                filled.BoqPath = Path.Combine(dir, "M-2F-01", "[BOQ]M-2F-01.xlsx");
                filled.AddNote("5 个图框完成批量更新；BOQ 数据行 15 行。");
                Capture(new BatchUpdateResultWindow(new[] { filled, skipped },
                        7, 21, 7, 56, 1, 0, dir), appTheme,
                    Path.Combine(dir, "batch-u1u-result-" + theme + ".png"));

                var dwgMachine = new DwgExportMachine
                {
                    MachineId = "M-2F-01",
                    FilePath = Path.Combine(dir, "M-2F-01", "M-2F-01.dwg"),
                    Frames = 5
                };
                dwgMachine.Circuits.AddRange(new[] { "AC Rack 2-1", "AC Rack 2-2",
                    "Bus Plug 3-1", "Fan L4", "Crane L1" });
                Capture(new DwgExportResultWindow(new[] { dwgMachine }, 5, dir),
                    appTheme, Path.Combine(dir, "dwg-result-" + theme + ".png"));

                var pdfOk = new PdfExportTarget
                { MachineId = "M-2F-01", CircuitName = "AC Rack 2-1", FrameHandle = "1" };
                var pdfFail = new PdfExportTarget
                { MachineId = "M-3F-07", CircuitName = "货梯 L1", FrameHandle = "4" };
                pdfFail.LastError = "输出目录不可写（被 Excel 占用）：layout.pdf";
                Capture(new PdfExportResultWindow(new[] { pdfOk, pdfFail },
                        1, 1, dir), appTheme,
                    Path.Combine(dir, "pdf-result-" + theme + ".png"));

                Capture(new FillReviewWindow(FillReviewData.Create(
                        new MachineRow
                        {
                            MachineId = "MECTT03", CircuitName = "AC Rack 2-1",
                            Region = "LAB", Next = "I-Line盘", Dia = ""
                        }, SampleFillRows()), SampleCatalog()), appTheme,
                    Path.Combine(dir, "fill-review-" + theme + ".png"));

                var compareItem = new FillUpdateCompareItem
                {
                    MachineId = "MECTT03",
                    DeviceName = "AC Rack 2-1",
                    HasHistory = true,
                    PreviousRows = new List<FillRowDiff>
                    {
                        new FillRowDiff { Order = 1, Name = "ZB-YJVR-4*35", Unit = "m",
                            OldQuantity = "28.5", NewQuantity = "28.5", Code = "1.1",
                            Status = FillRowDiff.StatusKept }
                    },
                    PlannedRows = new List<FillRowDiff>
                    {
                        new FillRowDiff { Order = 1, Name = "ZB-YJVR-4*35", Unit = "m",
                            OldQuantity = "28.5", NewQuantity = "31.2", Code = "1.1",
                            Status = FillRowDiff.StatusQuantity },
                        new FillRowDiff { Order = 2, Name = "25mm软管", Unit = "m",
                            OldQuantity = "", NewQuantity = "1.5", Code = "3.6",
                            Status = FillRowDiff.StatusAdded }
                    }
                };
                Capture(new FillUpdateCompareWindow(
                        new List<FillUpdateCompareItem> { compareItem }), appTheme,
                    Path.Combine(dir, "fill-compare-" + theme + ".png"));

                Capture(new BatchFillConfirmationWindow(SampleBatchRows()), appTheme,
                    Path.Combine(dir, "batch-confirm-" + theme + ".png"));

                var reserve = new DeviceReserveWindow(SampleReserveRows());
                reserve.UniformBox.Text = "2000";
                Capture(reserve, appTheme,
                    Path.Combine(dir, "device-reserve-" + theme + ".png"));

                Capture(new CommandHelpWindow(CommandHelpCatalog.All), appTheme,
                    Path.Combine(dir, "command-help-" + theme + ".png"));
                Capture(new UpdateNotesWindow(VersionChangeLog.Entries), appTheme,
                    Path.Combine(dir, "update-notes-" + theme + ".png"));
                Capture(new AboutWindow(), appTheme,
                    Path.Combine(dir, "about-" + theme + ".png"));
                Capture(new XmergeWindow(), appTheme,
                    Path.Combine(dir, "xmerge-" + theme + ".png"));

                var progress = new ProgressWindow("U1PDF 出图");
                progress.Update("出图 M-2F-01_AC Rack 2-1", 7, 12);
                Capture(progress, appTheme,
                    Path.Combine(dir, "progress-" + theme + ".png"));

                Capture(new ManualListItemWindow(SampleCatalog(), false, "电缆"),
                    appTheme, Path.Combine(dir, "manual-list-" + theme + ".png"));
                Capture(new CableCatalogSelectionWindow(
                        SampleCatalog().Where(item => item.Category == "电缆"),
                        "ZB-YJVR-3*95"), appTheme,
                    Path.Combine(dir, "cable-catalog-" + theme + ".png"));

                var shareItems = new Dictionary<string, string>
                {
                    { "UNC_FILL_TABLE_ROW", "2" },
                    { "UNC_FILL_CABLE_OWNER_RESERVE", "3000" }
                };
                var shareChanges = new List<ConfigShareChange>
                {
                    new ConfigShareChange("UNC_FILL_TABLE_ROW", "2", "3"),
                    new ConfigShareChange("UNC_FILL_CABLE_OWNER_RESERVE", "3000", "2000")
                };
                Capture(new ConfigSharePreviewWindow(new ConfigSharePreview(
                        "UNCAD Pro", ProductMetadata.VersionLabel, 2,
                        DateTimeOffset.Now, shareItems, shareChanges,
                        new[] { "UNC_LICENSE_ACCOUNT" })), appTheme,
                    Path.Combine(dir, "config-share-" + theme + ".png"));

                Capture(new OnlineLicenseWindow("本机尚未授权，请先登录。"), appTheme,
                    Path.Combine(dir, "online-license-" + theme + ".png"));
            }
        }

        // ---------- 样本数据（与窗口测试同风格） ----------

        private static List<BatchFillConfirmationRow> SampleBatchRows()
            => new List<BatchFillConfirmationRow>
            {
                new BatchFillConfirmationRow("3A5F", "M-2F-01",
                    "注塑机 · 海天 MA1600 · L1-L5", "Running", 42),
                new BatchFillConfirmationRow("4B02", "M-2F-02",
                    "模温机 · L1", "552/OK", 8),
                new BatchFillConfirmationRow("7C11", "M-2F-05",
                    "组装线 · L1-L3", "605/OK", 21),
                new BatchFillConfirmationRow("9D44", "M-3F-07",
                    "货梯 · L1, L4（缺 L2, L5）", "208/缺回路", 0)
            };

        private static List<DeviceReserveRow> SampleReserveRows()
            => new List<DeviceReserveRow>
            {
                new DeviceReserveRow("M-2F-01", "注塑机 · 海天 MA1600 · L3"),
                new DeviceReserveRow("M-2F-02", "模温机 · L1"),
                new DeviceReserveRow("M-2F-05", "组装线 · L2"),
                new DeviceReserveRow("M-3F-07", "货梯 · L4"),
                new DeviceReserveRow("M-3F-11", "空压机 · L1")
            };

        private static List<XLayoutMachineSummary> SampleSummaries()
            => new List<XLayoutMachineSummary>
            {
                new XLayoutMachineSummary("M-2F-01")
                {
                    CircuitCount = 5, ExpectedCircuitCount = 5,
                    StatusKnown = true, IsComplete = true
                },
                new XLayoutMachineSummary("M-2F-05")
                {
                    CircuitCount = 3, ExpectedCircuitCount = 3,
                    StatusKnown = true, IsComplete = true
                },
                new XLayoutMachineSummary("M-3F-07")
                {
                    CircuitCount = 2, ExpectedCircuitCount = 4,
                    StatusKnown = true, IsComplete = false
                }
            };

        private static List<MachineRow> SampleMachineRows()
            => new List<MachineRow>
            {
                new MachineRow
                {
                    Region = "LAB", MachineId = "MECTT03", CircuitName = "AC Rack 2-1",
                    Cable = "ZB-YJVR-4*35", Dia = "", Next = "I-Line盘",
                    Detail = "N480 3P4W 3P350A", Fr = "P-01"
                },
                new MachineRow
                {
                    Region = "LAB", MachineId = "MECTT03", CircuitName = "AC Rack 2-2",
                    Cable = "ZB-YJVR-5*16", Dia = "25", Next = "插座盘",
                    Detail = "U220 1P3W 1P63A", Fr = "P-01"
                },
                new MachineRow
                {
                    Region = "LAB", MachineId = "MECTT03", CircuitName = "Bus Plug 3-1",
                    Cable = "ZB-YJVR-4*10", Dia = "", Next = "母线插接箱",
                    Detail = "N208 3P4W 3P100A", Fr = "P-02"
                }
            };

        private static XstsReport SampleXstsReport()
        {
            var report = new XstsReport { FrameCount = 5 };
            var complete = new XstsMachineSummary("M-2F-01")
            {
                SelectedCircuitCount = 5,
                ExpectedCircuitCount = 5,
                ExpectedDataAvailable = true
            };
            complete.SelectedCircuits.AddRange(new[] { "AC Rack 2-1", "AC Rack 2-2",
                "Bus Plug 3-1", "Fan L4", "Crane L1" });
            report.Machines.Add(complete);
            var missing = new XstsMachineSummary("M-3F-07")
            {
                SelectedCircuitCount = 2,
                ExpectedCircuitCount = 4,
                ExpectedDataAvailable = true
            };
            missing.SelectedCircuits.AddRange(new[] { "货梯 L1", "货梯 L4" });
            missing.MissingCircuits.AddRange(new[] { "货梯 L2", "货梯 L5" });
            report.Machines.Add(missing);
            report.Issues.Add(new XstsFrameIssue
            {
                FrameNumber = 4,
                MachineId = "M-3F-07",
                CircuitName = "货梯 L2",
                Description = "未在图纸中找到对应回路"
            });
            return report;
        }

        private static List<XLayoutFrameItem> SampleLayoutItems()
            => new List<XLayoutFrameItem>
            {
                new XLayoutFrameItem("M-2F-01", "AC Rack 2-1",
                    new FrameRectangle("1", 0, 0, 2000, 1000), "1"),
                new XLayoutFrameItem("M-2F-01", "AC Rack 2-2",
                    new FrameRectangle("2", 2200, 0, 3400, 1000), "2"),
                new XLayoutFrameItem("M-2F-05", "Bus Plug 3-1",
                    new FrameRectangle("3", 0, 1200, 1200, 2000), "3"),
                new XLayoutFrameItem("M-3F-07", "货梯 L1",
                    new FrameRectangle("4", 3600, 0, 4600, 1000), "4"),
                new XLayoutFrameItem("M-3F-07", "货梯 L4",
                    new FrameRectangle("5", 1400, 1200, 2400, 2000), "5")
            };

        private static List<ListItem> SampleCatalog()
            => new List<ListItem>
            {
                new ListItem { Category = "电缆", Code = "1.1", Name = "ZB-YJVR-4*35",
                    Feature = "I-Line 至电盘", Unit = "m", Spec = "4×35" },
                new ListItem { Category = "电缆", Code = "1.2", Name = "ZB-YJVR-5*16",
                    Feature = "插座盘支路", Unit = "m", Spec = "5×16" },
                new ListItem { Category = "软管", Code = "3.6", Name = "25mm软管",
                    Feature = "25mm 新模板", Unit = "m", Spec = "25mm" },
                new ListItem { Category = "母线插接箱", Code = "5.3", Name = "630A 插接箱",
                    Feature = "N208 3P4W", Unit = "台", Spec = "630A" }
            };

        private static List<TableFillRow> SampleFillRows()
            => new List<TableFillRow>
            {
                new TableFillRow
                {
                    Category = TableFillCategory.Cable, Name = "ZB-YJVR-4*35",
                    Description = "N480 3P4W 3P350A", Unit = "M", Quantity = "28.5",
                    Code = "1.1"
                },
                new TableFillRow
                {
                    Category = TableFillCategory.FlexibleConduit, Name = "25mm软管",
                    Description = "25mm 新模板", Unit = "M", Quantity = "1.5",
                    Code = "3.6"
                },
                new TableFillRow
                {
                    Category = TableFillCategory.Bridge, Name = "桥架200*100",
                    Description = "12格", Unit = "M", Quantity = "6.0", Code = "2.1"
                }
            };

        // ---------- 截图（GDI 屏幕复制，与 demo/UiPreview 同法） ----------

        private static void Capture(Window window, ApplicationTheme theme, string path,
            Action extraSettle = null)
        {
            UiShell.ApplyFluentTheme(window, theme);
            window.Left = 40;
            window.Top = 40;
            window.Show();
            Pump(6);
            Thread.Sleep(450);
            if (extraSettle != null) extraSettle();
            Pump(2);
            ScreenCopy.CaptureWindow(window, path);
            window.Close();
            Pump(2);
        }

        private static void Pump(int rounds)
        {
            for (var i = 0; i < rounds; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(40);
            }
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        /// <summary>
        /// RenderTargetBitmap 直渲窗口视觉树（FluentWindow 的标题栏是 WPF 内容，
        /// 一并渲染）：与桌面状态/遮挡/DPI 无关。Show 只为保证布局度量完成。
        /// </summary>
        private static class ScreenCopy
        {
            public static void CaptureWindow(Window window, string path)
            {
                int width = Math.Max(1,
                    (int)Math.Ceiling(window.ActualWidth));
                int height = Math.Max(1,
                    (int)Math.Ceiling(window.ActualHeight));
                var bitmap = new RenderTargetBitmap(
                    width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path))
                {
                    encoder.Save(stream);
                }
            }
        }
    }
}

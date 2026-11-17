using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.Dwg;
using UNCAD.Core.Stat;
using UNCAD.Core.Submission;
using UNCAD.Infra;
using UNCAD.UI;
using Wpf.Ui.Appearance;

namespace UNCAD.UiPreview
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0 && e.Args[0] == "--shots")
            {
                RunShots(e.Args.Length > 1 ? e.Args[1] : "shots");
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--orbit")
            {
                RunOrbitProbe(e.Args.Length > 1 ? e.Args[1] : "shots");
                return;
            }

            // 交互模式：双击打开批量确认窗，可逐个查看三个真实窗体。
            new BatchFillConfirmationWindow(SampleBatchRows()).ShowDialog();
            Shutdown();
        }

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
            complete.SelectedCircuits.AddRange(new[]
                { "AC Rack 2-1", "AC Rack 2-2", "Bus Plug 3-1", "Fan L4", "Crane L1" });
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

        /// <summary>把主工程里全部真实窗体逐个 × 深/浅主题截图。</summary>
        /// <summary>
        /// 3.3.6 现场回归探针：真实 FillReviewWindow + 真实 QuickLineWebPreviewControl
        /// （WebView2 真初始化），注入带通道/柱网/参照层的夹具模型，等待页面加载
        /// 后截图 + 转储控件内部状态——在 AutoCAD 外复现/排除"预览舱空白"。
        /// </summary>
        private void RunOrbitProbe(string dir)
        {
            Directory.CreateDirectory(dir);

            var preview = new UNCAD.Core.QuickLine.QuickLineOrbitPreviewModel(
                new List<UNCAD.Core.QuickLine.QuickLineOrbitPreviewSegment>
                {
                    new UNCAD.Core.QuickLine.QuickLineOrbitPreviewSegment("s1",
                        new UNCAD.Core.QuickLine.QuickLineSpatialPoint(0, 0, 0),
                        new UNCAD.Core.QuickLine.QuickLineSpatialPoint(9600, 0, 0),
                        UNCAD.Core.QuickLine.QuickLineSpatialAxis.X, 9600, true,
                        "9600mm",
                        new UNCAD.Core.QuickLine.QuickLineOrbitPreviewChannel(
                            400.0, false, false)),
                    new UNCAD.Core.QuickLine.QuickLineOrbitPreviewSegment("s2",
                        new UNCAD.Core.QuickLine.QuickLineSpatialPoint(9600, 0, 0),
                        new UNCAD.Core.QuickLine.QuickLineSpatialPoint(9600, 0, 2500),
                        UNCAD.Core.QuickLine.QuickLineSpatialAxis.Z, 2500, true,
                        "2500mm",
                        new UNCAD.Core.QuickLine.QuickLineOrbitPreviewChannel(
                            null, false, true))
                }, "上游 48/V ↔ 下游 37/V", new List<string>(), "48/V", "37/V");

            var data = FillReviewData.Create(
                new MachineRow { MachineId = "M1", CircuitName = "设备A" },
                new List<TableFillRow>());
            var window = new FillReviewWindow(data,
                new Core.Excel.BoqCatalogIndex(null),
                FillPlanningOptions.Default, true, preview,
                "图层矫正：6 个实体移正（电缆 3/桥架 2/线管 1/无法判定 0）");
            window.Show();

            // 泵消息循环给 WebView2 异步初始化留时间（最多 ~15 秒）。
            var control = window.OrbitPreview;
            for (int i = 0; i < 150; i++)
            {
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Thread.Sleep(100);
            }

            ScreenCapture.CaptureWindow(window, Path.Combine(dir, "orbit-probe.png"));

            // 内部状态转储（InternalsVisibleTo + 少量反射）。
            var report = new System.Text.StringBuilder();
            report.AppendLine("BuiltSegmentLineCount=" + control.BuiltSegmentLineCount);
            report.AppendLine("BuiltLabelCount=" + control.BuiltLabelCount);
            report.AppendLine("BuiltTrayChannelCount=" + control.BuiltTrayChannelCount);
            report.AppendLine("BuiltColumnGridLineCount=" + control.BuiltColumnGridLineCount);
            var flags = control.GetType().GetField("_pageReady",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var fatal = control.GetType().GetField("_fatal",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var started = control.GetType().GetField("_initializationStarted",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var closed = control.GetType().GetField("_closed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var webField = control.GetType().GetField("_webView",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var webView = webField != null ? webField.GetValue(control) : null;
            var core = webView != null
                ? webView.GetType().GetProperty("CoreWebView2")?.GetValue(webView) : null;
            report.AppendLine("_initializationStarted=" + started?.GetValue(control));
            report.AppendLine("_pageReady=" + flags?.GetValue(control));
            report.AppendLine("_fatal=" + fatal?.GetValue(control));
            report.AppendLine("_closed=" + closed?.GetValue(control));
            report.AppendLine("CoreWebView2=" + (core != null ? "created" : "null"));
            report.AppendLine("webViewVisibility=" + (webView as UIElement)?.Visibility);
            File.WriteAllText(Path.Combine(dir, "orbit-probe-state.txt"),
                report.ToString());

            window.Close();
            Shutdown();
        }

        private void RunShots(string dir)
        {
            Directory.CreateDirectory(dir);

            foreach (var theme in new[] { "light", "dark" })
            {
                var appTheme = theme == "dark"
                    ? ApplicationTheme.Dark : ApplicationTheme.Light;

                // ---- U1SET 全部 11 页 ----
                for (int page = 0; page < 11; page++)
                {
                    var settings = new SettingsWindow(page);
                    Capture(settings, appTheme,
                        Path.Combine(dir, "settings-" + page + "-" + theme + ".png"));
                }

                // ---- 业务窗体 ----
                var picker = new MachinePickerWindow(
                    new[] { "MECTT03", "MDEGN01", "MPAMS07" },
                    id => string.Equals(id, "MECTT03", StringComparison.OrdinalIgnoreCase)
                        ? SampleMachineRows() : new List<MachineRow>(),
                    row => row.CircuitName + " → " + row.Cable
                        + (row.Dia.Length == 0 ? "" : " + Φ" + row.Dia + "软管"));
                picker.MachineInput.Text = "MECTT03";
                picker.CommitMachine();
                Capture(picker, appTheme, Path.Combine(dir, "machine-picker-" + theme + ".png"));

                var select = new XLayoutSelectionWindow(SampleLayoutItems());
                Capture(select, appTheme,
                    Path.Combine(dir, "xlayout-select-" + theme + ".png"));

                var summary = new XLayoutSummaryWindow(SampleSummaries(), 8);
                Capture(summary, appTheme,
                    Path.Combine(dir, "xlayout-summary-" + theme + ".png"));

                var xsts = new XstsWindow(SampleXstsReport(),
                    Path.Combine(dir, "UNCAD_XSTS.xlsx"));
                Capture(xsts, appTheme, Path.Combine(dir, "xsts-" + theme + ".png"));

                var skipped = new BatchUpdateMachineRow("M-3F-07");
                skipped.Frames = 2; skipped.TableRows = 6; skipped.AttributeValues = 14;
                skipped.BoqStatus = "跳过（回路不全）";
                skipped.AddNote("缺少回路：货梯 L2、货梯 L5；本次照常填写图框数据、不生成 BOQ。");
                var filled = new BatchUpdateMachineRow("M-2F-01");
                filled.Frames = 5; filled.TableRows = 15; filled.AttributeValues = 42;
                filled.BoqStatus = "已生成";
                filled.BoqPath = Path.Combine(dir, "M-2F-01", "[BOQ]M-2F-01.xlsx");
                filled.AddNote("4 个图框完成批量更新；BOQ 数据行 15 行。");
                var batchResult = new BatchUpdateResultWindow(
                    new[] { filled, skipped }, 7, 21, 7, 56, 1, 0,
                    Path.Combine(dir));
                Capture(batchResult, appTheme,
                    Path.Combine(dir, "batch-u1u-result-" + theme + ".png"));

                var dwgMachine = new Features.DwgExport.DwgExportMachine
                {
                    MachineId = "M-2F-01",
                    FilePath = Path.Combine(dir, "M-2F-01", "M-2F-01.dwg"),
                    Frames = 5
                };
                dwgMachine.Circuits.AddRange(new[] { "AC Rack 2-1", "AC Rack 2-2",
                    "Bus Plug 3-1", "Fan L4", "Crane L1" });
                var dwgResult = new DwgExportResultWindow(new[] { dwgMachine },
                    5, Path.Combine(dir));
                Capture(dwgResult, appTheme,
                    Path.Combine(dir, "dwg-result-" + theme + ".png"));

                var pdfOk = new Features.PdfExport.PdfExportTarget
                {
                    MachineId = "M-2F-01", CircuitName = "AC Rack 2-1", FrameHandle = "1"
                };
                var pdfFail = new Features.PdfExport.PdfExportTarget
                {
                    MachineId = "M-3F-07", CircuitName = "货梯 L1", FrameHandle = "4"
                };
                pdfFail.LastError = "输出目录不可写（被 Excel 占用）：layout.pdf";
                var pdfResult = new PdfExportResultWindow(new[] { pdfOk, pdfFail },
                    1, 1, Path.Combine(dir));
                Capture(pdfResult, appTheme,
                    Path.Combine(dir, "pdf-result-" + theme + ".png"));

                var review = new FillReviewWindow(FillReviewData.Create(
                    new MachineRow
                    {
                        MachineId = "MECTT03", CircuitName = "AC Rack 2-1",
                        Region = "LAB", Next = "I-Line盘", Dia = ""
                    }, SampleFillRows()), SampleCatalog());
                Capture(review, appTheme,
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
                var compare = new FillUpdateCompareWindow(
                    new List<FillUpdateCompareItem> { compareItem });
                Capture(compare, appTheme,
                    Path.Combine(dir, "fill-compare-" + theme + ".png"));

                var batchConfirm = new BatchFillConfirmationWindow(SampleBatchRows());
                Capture(batchConfirm, appTheme,
                    Path.Combine(dir, "batch-confirm-" + theme + ".png"));

                var reserve = new DeviceReserveWindow(SampleReserveRows());
                reserve.UniformBox.Text = "2000";
                Capture(reserve, appTheme,
                    Path.Combine(dir, "device-reserve-" + theme + ".png"));

                var help = new CommandHelpWindow(CommandHelpCatalog.All);
                Capture(help, appTheme, Path.Combine(dir, "command-help-" + theme + ".png"));

                var notes = new UpdateNotesWindow(VersionChangeLog.Entries);
                Capture(notes, appTheme,
                    Path.Combine(dir, "update-notes-" + theme + ".png"));

                var about = new AboutWindow();
                Capture(about, appTheme, Path.Combine(dir, "about-" + theme + ".png"));

                var xmerge = new XmergeWindow();
                Capture(xmerge, appTheme, Path.Combine(dir, "xmerge-" + theme + ".png"));

                var progress = new ProgressWindow("U1PDF 出图");
                progress.Update("出图 M-2F-01_AC Rack 2-1", 7, 12);
                Capture(progress, appTheme,
                    Path.Combine(dir, "progress-" + theme + ".png"));

                var manual = new ManualListItemWindow(SampleCatalog(), false, "电缆");
                Capture(manual, appTheme,
                    Path.Combine(dir, "manual-list-" + theme + ".png"));

                var cablePick = new CableCatalogSelectionWindow(
                    SampleCatalog().Where(item => item.Category == "电缆"), "ZB-YJVR-3*95");
                Capture(cablePick, appTheme,
                    Path.Combine(dir, "cable-catalog-" + theme + ".png"));

                var shareItems = new Dictionary<string, string>
                {
                    { "UNC_FILL_TABLE_ROW", "2" }, { "UNC_FILL_CABLE_OWNER_RESERVE", "3000" }
                };
                var shareChanges = new List<ConfigShareChange>
                {
                    new ConfigShareChange("UNC_FILL_TABLE_ROW", "2", "3"),
                    new ConfigShareChange("UNC_FILL_CABLE_OWNER_RESERVE", "3000", "2000")
                };
                var share = new ConfigSharePreviewWindow(new ConfigSharePreview(
                    "UNCAD Pro", ProductMetadata.VersionLabel, 2,
                    DateTimeOffset.Now, shareItems, shareChanges,
                    new[] { "UNC_LICENSE_ACCOUNT" }));
                Capture(share, appTheme,
                    Path.Combine(dir, "config-share-" + theme + ".png"));

                var license = new OnlineLicenseWindow("本机尚未授权，请先登录。");
                Capture(license, appTheme,
                    Path.Combine(dir, "online-license-" + theme + ".png"));
            }

            Shutdown();
        }

        private static void Capture(Window window,
            ApplicationTheme theme, string path)
        {
            UiShell.ApplyFluentTheme(window, theme);
            window.Left = 40;
            window.Top = 40;
            window.Show();
            Pump(6);
            Thread.Sleep(450);
            Pump(2);
            ScreenCapture.CaptureWindow(window, path);
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
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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

        /// <summary>把主工程里已迁移的真实窗体逐个 × 深/浅主题截图。</summary>
        private void RunShots(string dir)
        {
            Directory.CreateDirectory(dir);

            foreach (var theme in new[] { "light", "dark" })
            {
                var appTheme = theme == "dark"
                    ? ApplicationTheme.Dark : ApplicationTheme.Light;

                var batch = new BatchFillConfirmationWindow(SampleBatchRows());
                Capture(batch, appTheme, Path.Combine(dir, "real-batch-" + theme + ".png"));

                var reserve = new DeviceReserveWindow(SampleReserveRows());
                reserve.UniformBox.Text = "2000";
                Capture(reserve, appTheme, Path.Combine(dir, "real-reserve-" + theme + ".png"));

                var summary = new XLayoutSummaryWindow(SampleSummaries(), 8);
                Capture(summary, appTheme, Path.Combine(dir, "real-xlayout-" + theme + ".png"));
            }

            Shutdown();
        }

        private static void Capture(Window window,
            ApplicationTheme theme, string path)
        {
            UiShell.ApplyFluentTheme(window, theme);
            window.Left = 80;
            window.Top = 80;
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

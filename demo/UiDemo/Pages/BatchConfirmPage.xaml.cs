using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace UNCAD.UiDemo.Pages
{
    public partial class BatchConfirmPage : UserControl
    {
        private sealed class FrameRow
        {
            public string Handle { get; set; }
            public string MachineId { get; set; }
            public string Device { get; set; }
            public string DeviceState { get; set; }
            public int RowCount { get; set; }
            public double ReserveMm { get; set; }
            public string Status { get; set; }
            public Brush StatusBrush { get; set; }
        }

        public BatchConfirmPage()
        {
            InitializeComponent();

            var accent = (Color)System.Windows.Application.Current.FindResource("SystemAccentColor");
            var ready = new SolidColorBrush(Color.FromArgb(235, accent.R, accent.G, accent.B));
            ready.Freeze();
            var skipped = new SolidColorBrush(Color.FromArgb(235, 190, 90, 20));
            skipped.Freeze();

            DataContext = new List<FrameRow>
            {
                new FrameRow { Handle = "3A5F", MachineId = "M-2F-01",
                    Device = "注塑机 · 海天 MA1600 · L1-L5", DeviceState = "Running",
                    RowCount = 42, ReserveMm = 2000, Status = "就绪", StatusBrush = ready },
                new FrameRow { Handle = "4B02", MachineId = "M-2F-02",
                    Device = "模温机 · L1", DeviceState = "552/OK",
                    RowCount = 8, ReserveMm = 0, Status = "就绪", StatusBrush = ready },
                new FrameRow { Handle = "7C11", MachineId = "M-2F-05",
                    Device = "组装线 · L1-L3", DeviceState = "605/OK",
                    RowCount = 21, ReserveMm = 1500, Status = "就绪", StatusBrush = ready },
                new FrameRow { Handle = "9D44", MachineId = "M-3F-07",
                    Device = "货梯 · L1, L4（缺 L2, L5）", DeviceState = "208/缺回路",
                    RowCount = 0, ReserveMm = 0, Status = "跳过 BOQ", StatusBrush = skipped },
            };
        }
    }
}

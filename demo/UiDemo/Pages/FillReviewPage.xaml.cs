using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace UNCAD.UiDemo.Pages
{
    public partial class FillReviewPage : UserControl
    {
        private sealed class BoqRow
        {
            public int No { get; set; }
            public string Category { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public string Model { get; set; }
            public string Unit { get; set; }
            public string Quantity { get; set; }
            public string Source { get; set; }
            public string SourceNote { get; set; }
            public Brush SourceBrush { get; set; }
        }

        public FillReviewPage()
        {
            InitializeComponent();
            DataContext = BuildRows();
        }

        private static Color AccentColor()
        {
            try { return (Color)System.Windows.Application.Current.FindResource("SystemAccentColor"); }
            catch { return Color.FromRgb(0, 120, 212); }
        }

        private static List<BoqRow> BuildRows()
        {
            var accent = AccentColor();
            var auto = new SolidColorBrush(Color.FromArgb(230, accent.R, accent.G, accent.B));
            auto.Freeze();
            var manual = new SolidColorBrush(Color.FromArgb(220, 96, 125, 139));
            manual.Freeze();

            return new List<BoqRow>
            {
                new BoqRow { No = 1, Category = "电缆", Code = "C-014", Name = "电力电缆",
                    Model = "ZB-YJVR-5×4mm²", Unit = "m", Quantity = "10.1", Source = "自动",
                    SourceNote = "实测 + 预留合计", SourceBrush = auto },
                new BoqRow { No = 2, Category = "电缆", Code = "C-022", Name = "电力电缆",
                    Model = "ZB-YJVR-5×10mm²", Unit = "m", Quantity = "18.6", Source = "自动",
                    SourceNote = "实测 + 预留合计", SourceBrush = auto },
                new BoqRow { No = 3, Category = "断路器", Code = "Q-031", Name = "塑壳断路器",
                    Model = "NSX160N 3P 160A", Unit = "台", Quantity = "1", Source = "数据库",
                    SourceNote = "固定清单主别名匹配", SourceBrush = auto },
                new BoqRow { No = 4, Category = "电盘", Code = "P-008", Name = "I-Line 盘",
                    Model = "800A", Unit = "台", Quantity = "1", Source = "数据库",
                    SourceNote = "Downstream 自动添加", SourceBrush = auto },
                new BoqRow { No = 5, Category = "线管", Code = "G-102", Name = "JDG 线管",
                    Model = "DN32", Unit = "m", Quantity = "26", Source = "手动",
                    SourceNote = "评审窗手动加入", SourceBrush = manual },
                new BoqRow { No = 6, Category = "桥架", Code = "T-201", Name = "槽式桥架",
                    Model = "200×100 热镀锌", Unit = "m", Quantity = "12.4", Source = "自动",
                    SourceNote = "图上实测求和", SourceBrush = auto },
                new BoqRow { No = 7, Category = "插接箱", Code = "B-301", Name = "母线插接箱",
                    Model = "500A", Unit = "台", Quantity = "2", Source = "数据库",
                    SourceNote = "固定清单", SourceBrush = auto },
                new BoqRow { No = 8, Category = "软管", Code = "H-401", Name = "软管 Ruanguan_GW",
                    Model = "DN25", Unit = "m", Quantity = "1.5", Source = "自动",
                    SourceNote = "动态块读取", SourceBrush = auto },
            };
        }
    }
}

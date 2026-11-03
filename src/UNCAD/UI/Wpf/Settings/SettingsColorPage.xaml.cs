using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET 颜色页：设备端（含 Downstream）与上游端的 AutoCAD ACI 色号选择。
    /// 选项固定为 ACI 1-9（与旧 WinForms 版一致），持久化写色号索引。
    /// </summary>
    public partial class SettingsColorPage : UserControl
    {
        public SettingsColorPage()
        {
            InitializeComponent();
            DeviceColorBox.ItemsSource = CadColorChoice.Choices;
            UpstreamColorBox.ItemsSource = CadColorChoice.Choices;
        }
    }

    /// <summary>ACI 颜色选项：色号 + 中文名 + 预览色块（WPF 版，替代旧 WinForms 自绘下拉）。</summary>
    internal sealed class CadColorChoice
    {
        static CadColorChoice()
        {
            Choices = new List<CadColorChoice>
            {
                new CadColorChoice(1, "红色", Colors.Red),
                new CadColorChoice(2, "黄色", Colors.Yellow),
                new CadColorChoice(3, "绿色", Colors.Lime),
                new CadColorChoice(4, "青色", Colors.Cyan),
                new CadColorChoice(5, "蓝色", Colors.Blue),
                new CadColorChoice(6, "洋红", Colors.Fuchsia),
                new CadColorChoice(7, "白色", Colors.White),
                new CadColorChoice(8, "深灰", Colors.Gray),
                new CadColorChoice(9, "浅灰", Colors.LightGray)
            };
        }

        private CadColorChoice(short index, string name, Color preview)
        {
            Index = index;
            Name = name;
            PreviewBrush = new SolidColorBrush(preview);
            PreviewBrush.Freeze();
        }

        public short Index { get; }
        public string Name { get; }
        public Brush PreviewBrush { get; }
        public string DisplayName => Name + " (ACI " + Index + ")";
        public static IReadOnlyList<CadColorChoice> Choices { get; }

        public override string ToString() => DisplayName;
    }
}

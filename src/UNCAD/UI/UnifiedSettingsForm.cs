using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 统一配置中心对话框（UNC_SET）。
    /// 页签：线段绘制 / 桥架标注 / 线管标注 / 拱桥开洞 / 统计汇总。
    /// 确定时一次性写回全部配置键（Features 只读，这里统一写）。
    /// </summary>
    public sealed class UnifiedSettingsForm : Form
    {
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };

        // 线段绘制
        private readonly TextBox _unlText = new TextBox { Width = 200 };
        private readonly TextBox _unlHgt = new TextBox { Width = 80 };
        private readonly RadioButton _unlCenter = new RadioButton { Text = "居中" };
        private readonly RadioButton _unlBelow = new RadioButton { Text = "靠边下" };
        private readonly RadioButton _unlAbove = new RadioButton { Text = "靠边上" };
        private readonly TextBox _unlOff = new TextBox { Width = 80 };

        // 桥架标注
        private readonly TextBox _unqText = new TextBox { Width = 200 };
        private readonly TextBox _unqHgt = new TextBox { Width = 80 };
        private readonly TextBox _unqLoff = new TextBox { Width = 80 };
        private readonly TextBox _unqToff = new TextBox { Width = 80 };
        private readonly RadioButton _unqAbove = new RadioButton { Text = "上方" };
        private readonly RadioButton _unqBelow = new RadioButton { Text = "下方" };

        // 线管标注
        private readonly ComboBox _conduitDia = new ComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };

        // 拱桥开洞
        private readonly TextBox _unrDia = new TextBox { Width = 80 };

        // 统计汇总
        private readonly TextBox _statHgt = new TextBox { Width = 80 };
        private readonly TextBox _statMm = new TextBox { Width = 80 };

        // 文字样式
        private readonly TextBox _styleName = new TextBox { Width = 140 };
        private readonly TextBox _styleFont = new TextBox { Width = 140 };
        private readonly TextBox _styleBigFont = new TextBox { Width = 140 };
        private readonly TextBox _styleWidth = new TextBox { Width = 60 };

        // Excel 填充
        private readonly TextBox _fillExcel = new TextBox { Width = 310 };
        private readonly TextBox _fillCatalog = new TextBox { Width = 310 };
        private readonly TextBox _fillTblRow = new TextBox { Width = 60 };
        private readonly TextBox _fillTextHeight = new TextBox { Width = 60 };
        private readonly TextBox _fillBridge = new TextBox { Width = 220 };

        public UnifiedSettingsForm(int tabIndex)
        {
            Text = "UNCAD 配置中心 · " + Branding.Nameplate;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(620, 320);

            _conduitDia.Items.AddRange(new object[] { "20", "25", "32" });
            LoadValues();

            _tabs.TabPages.Add(BuildUnlTab());
            _tabs.TabPages.Add(BuildUnqTab());
            _tabs.TabPages.Add(BuildConduitTab());
            _tabs.TabPages.Add(BuildUnrTab());
            _tabs.TabPages.Add(BuildStatTab());
            _tabs.TabPages.Add(BuildStyleTab());
            _tabs.TabPages.Add(BuildFillTab());
            _tabs.SelectedIndex = Math.Min(Math.Max(tabIndex, 0), _tabs.TabPages.Count - 1);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 80 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
            var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 8, 8) };
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(ok);

            Controls.Add(_tabs);
            Controls.Add(btnRow);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        // ---------- 读取当前值 ----------

        private void LoadValues()
        {
            _unlText.Text = Settings.Get(ConfigKeys.UnlText, "2000mm");
            _unlHgt.Text = Settings.GetDouble(ConfigKeys.UnlHeight, 180.0).ToString("0.##", CultureInfo.InvariantCulture);
            string unlPos = Settings.Get(ConfigKeys.UnlPos, "1");
            _unlCenter.Checked = unlPos == "0";
            _unlBelow.Checked = unlPos == "1";
            _unlAbove.Checked = unlPos == "2";
            double unlOff = Settings.GetDouble(ConfigKeys.UnlOffset, 0.0);
            _unlOff.Text = unlOff > 0 ? unlOff.ToString("0.##", CultureInfo.InvariantCulture) : "";

            _unqText.Text = Settings.Get(ConfigKeys.UnqText, "桥架200*100 10格");
            _unqHgt.Text = Settings.GetDouble(ConfigKeys.UnqHeight, 180.0).ToString("0.##", CultureInfo.InvariantCulture);
            double unqLoff = Settings.GetDouble(ConfigKeys.UnqLineOff, 0.0);
            _unqLoff.Text = unqLoff > 0 ? unqLoff.ToString("0.##", CultureInfo.InvariantCulture) : "";
            double unqToff = Settings.GetDouble(ConfigKeys.UnqTextOff, 0.0);
            _unqToff.Text = unqToff > 0 ? unqToff.ToString("0.##", CultureInfo.InvariantCulture) : "";
            string unqSide = Settings.Get(ConfigKeys.UnqSide, "1");
            _unqAbove.Checked = unqSide != "0";
            _unqBelow.Checked = unqSide == "0";

            string conduitDia = Settings.Get(ConfigKeys.ConduitDiameter, "20");
            _conduitDia.SelectedItem = conduitDia == "25" || conduitDia == "32" ? conduitDia : "20";

            _unrDia.Text = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0).ToString("0.##", CultureInfo.InvariantCulture);

            _statHgt.Text = Settings.GetDouble(ConfigKeys.UnaddHeight, 180.0).ToString("0.##", CultureInfo.InvariantCulture);
            _statMm.Text = Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0).ToString("0.##", CultureInfo.InvariantCulture);

            // 文字样式
            _styleName.Text = Settings.Get(ConfigKeys.StyleName, "UNC-标注");
            _styleFont.Text = Settings.Get(ConfigKeys.StyleFont, "msyh.ttf");
            _styleBigFont.Text = Settings.Get(ConfigKeys.StyleBigFont, "");
            _styleWidth.Text = Settings.GetDouble(ConfigKeys.StyleWidth, 0.8).ToString("0.##", CultureInfo.InvariantCulture);

            // Excel 填充
            _fillExcel.Text = Settings.Get(ConfigKeys.FillExcelPath, "");
            _fillCatalog.Text = Settings.Get(ConfigKeys.FillCatalogPath, "");
            _fillTblRow.Text = ((int)Settings.GetDouble(ConfigKeys.FillTableRow, 1.0)).ToString();
            _fillTextHeight.Text = Settings.GetDouble(ConfigKeys.FillTextHeight, TableFillFormatter.DefaultTextHeight)
                .ToString("0.##", CultureInfo.InvariantCulture);
            _fillBridge.Text = Settings.Get(ConfigKeys.FillBridge, "");
        }

        // ---------- 保存（含数据校验） ----------

        public void Save()
        {
            // 线段
            Settings.Set(ConfigKeys.UnlText, _unlText.Text.Trim());
            Settings.Set(ConfigKeys.UnlHeight, Pos(_unlHgt, 180.0).ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnlPos, _unlCenter.Checked ? "0" : _unlBelow.Checked ? "1" : "2");
            Settings.Set(ConfigKeys.UnlOffset, Num(_unlOff, 0.0).ToString("0.##", CultureInfo.InvariantCulture));

            // 桥架（偏移允许 0=自动/贴线）
            Settings.Set(ConfigKeys.UnqText, _unqText.Text.Trim());
            Settings.Set(ConfigKeys.UnqHeight, Pos(_unqHgt, 180.0).ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnqLineOff, Num(_unqLoff, 0.0).ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnqTextOff, Num(_unqToff, 0.0).ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnqSide, _unqAbove.Checked ? "1" : "0");

            // 线管只保存管径；标注几何参数与桥架共用。
            Settings.Set(ConfigKeys.ConduitDiameter, _conduitDia.SelectedItem?.ToString() ?? "20");

            // 拱桥
            Settings.Set(ConfigKeys.UnrDiameter, Pos(_unrDia, 300.0).ToString("0.##", CultureInfo.InvariantCulture));

            // 统计
            Settings.Set(ConfigKeys.UnaddHeight, Pos(_statHgt, 180.0).ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnaddMmPerGrid, Pos(_statMm, 250.0).ToString("0.##", CultureInfo.InvariantCulture));

            // 文字样式
            Settings.Set(ConfigKeys.StyleName, _styleName.Text.Trim());
            Settings.Set(ConfigKeys.StyleFont, _styleFont.Text.Trim());
            Settings.Set(ConfigKeys.StyleBigFont, _styleBigFont.Text.Trim());
            double sw = Num(_styleWidth, 0.8);
            Settings.Set(ConfigKeys.StyleWidth, (sw > 0 ? sw : 0.8).ToString("0.##", CultureInfo.InvariantCulture));

            // Excel 填充
            Settings.Set(ConfigKeys.FillExcelPath, _fillExcel.Text.Trim());
            Settings.Set(ConfigKeys.FillCatalogPath, _fillCatalog.Text.Trim());
            Settings.Set(ConfigKeys.FillTableRow, Pos(_fillTblRow, 1.0).ToString("0", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillTextHeight, Pos(_fillTextHeight, TableFillFormatter.DefaultTextHeight)
                .ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillBridge, _fillBridge.Text.Trim());
        }

        /// <summary>解析数值：失败或非法时返回默认值。</summary>
        private static double Num(TextBox box, double def)
        {
            if (double.TryParse(box.Text.Trim(), out double v)) return v;
            return def;
        }

        /// <summary>正数解析：&lt;= 0 或失败时返回默认值。</summary>
        private static double Pos(TextBox box, double def)
        {
            double v = Num(box, def);
            return v > 0 ? v : def;
        }

        // ---------- 页签构建 ----------

        private TabPage BuildUnlTab()
        {
            var g = Grid(4);
            g.Controls.Add(Lbl("占位文字:"), 0, 0); g.Controls.Add(_unlText, 1, 0);
            g.Controls.Add(Lbl("文字高度:"), 0, 1); g.Controls.Add(_unlHgt, 1, 1);
            g.Controls.Add(Lbl("文字位置:"), 0, 2);
            var posPanel = new FlowLayoutPanel { AutoSize = true };
            posPanel.Controls.Add(_unlCenter);
            posPanel.Controls.Add(_unlBelow);
            posPanel.Controls.Add(_unlAbove);
            g.Controls.Add(posPanel, 1, 2);
            g.Controls.Add(Lbl("偏移距离(0=贴线):"), 0, 3); g.Controls.Add(_unlOff, 1, 3);
            return Page("线段绘制", g);
        }

        private TabPage BuildUnqTab()
        {
            var g = Grid(5);
            g.Controls.Add(Lbl("规格文字:"), 0, 0); g.Controls.Add(_unqText, 1, 0);
            g.Controls.Add(Lbl("文字高度:"), 0, 1); g.Controls.Add(_unqHgt, 1, 1);
            g.Controls.Add(Lbl("红线距基线(0=默认15):"), 0, 2); g.Controls.Add(_unqLoff, 1, 2);
            g.Controls.Add(Lbl("文字距红线(0=贴线):"), 0, 3); g.Controls.Add(_unqToff, 1, 3);
            g.Controls.Add(Lbl("标注侧:"), 0, 4);
            var sidePanel = new FlowLayoutPanel { AutoSize = true };
            sidePanel.Controls.Add(_unqAbove);
            sidePanel.Controls.Add(_unqBelow);
            g.Controls.Add(sidePanel, 1, 4);
            return Page("桥架标注", g);
        }

        private TabPage BuildConduitTab()
        {
            var g = Grid(1);
            g.Controls.Add(Lbl("默认管径:"), 0, 0); g.Controls.Add(_conduitDia, 1, 0);
            return Page("线管标注", g);
        }

        private TabPage BuildUnrTab()
        {
            var g = Grid(1);
            g.Controls.Add(Lbl("拱桥直径:"), 0, 0); g.Controls.Add(_unrDia, 1, 0);
            return Page("拱桥开洞", g);
        }

        private TabPage BuildStatTab()
        {
            var g = Grid(2);
            g.Controls.Add(Lbl("输出文字高度:"), 0, 0); g.Controls.Add(_statHgt, 1, 0);
            g.Controls.Add(Lbl("每格长度(mm):"), 0, 1); g.Controls.Add(_statMm, 1, 1);
            return Page("统计汇总", g);
        }

        private TabPage BuildStyleTab()
        {
            var g = Grid(4);
            g.Controls.Add(Lbl("样式名:"), 0, 0); g.Controls.Add(_styleName, 1, 0);
            g.Controls.Add(Lbl("字体文件(如 msyh.ttf):"), 0, 1); g.Controls.Add(_styleFont, 1, 1);
            g.Controls.Add(Lbl("大字体文件(空=TTF):"), 0, 2); g.Controls.Add(_styleBigFont, 1, 2);
            g.Controls.Add(Lbl("宽高比:"), 0, 3); g.Controls.Add(_styleWidth, 1, 3);
            return Page("文字样式", g);
        }

        private TabPage BuildFillTab()
        {
            var g = Grid(5);
            g.Controls.Add(Lbl("机台数据 Excel:"), 0, 0); g.Controls.Add(PathPicker(_fillExcel), 1, 0);
            g.Controls.Add(Lbl("固定清单 Excel(空=同文件):"), 0, 1); g.Controls.Add(PathPicker(_fillCatalog), 1, 1);
            g.Controls.Add(Lbl("起始数据行(1=No.1):"), 0, 2); g.Controls.Add(_fillTblRow, 1, 2);
            g.Controls.Add(Lbl("表格文字高度:"), 0, 3); g.Controls.Add(_fillTextHeight, 1, 3);
            g.Controls.Add(Lbl("桥架信息(块属性):"), 0, 4); g.Controls.Add(_fillBridge, 1, 4);
            return Page("Excel 填充", g);
        }

        private Control PathPicker(TextBox target)
        {
            var button = new Button { Text = "浏览...", Width = 66, Height = 26 };
            button.Click += (sender, args) =>
            {
                using (var dialog = new OpenFileDialog
                {
                    Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                    Title = "选择 Excel 数据源",
                    CheckFileExists = true,
                    FileName = File.Exists(target.Text.Trim()) ? target.Text.Trim() : ""
                })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
                }
            };
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            panel.Controls.Add(target);
            panel.Controls.Add(button);
            return panel;
        }

        private static Label Lbl(string t) => new Label { Text = t, TextAlign = System.Drawing.ContentAlignment.MiddleRight, AutoSize = true };
        private static TableLayoutPanel Grid(int rows) => new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = rows, Padding = new Padding(14, 14, 14, 4) };
        private static TabPage Page(string title, Control content)
        {
            var page = new TabPage(title) { Padding = new Padding(4) };
            page.Controls.Add(content);
            return page;
        }
    }
}

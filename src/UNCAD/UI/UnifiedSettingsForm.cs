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
        private readonly CheckBox _statText = new CheckBox { Text = "单行文字 (TEXT)", AutoSize = true };
        private readonly CheckBox _statMText = new CheckBox { Text = "多行文字 (MTEXT)", AutoSize = true };
        private readonly CheckBox _statCable = new CheckBox { Text = "电缆长度", AutoSize = true };
        private readonly CheckBox _statBridge = new CheckBox { Text = "桥架长度", AutoSize = true };
        private readonly CheckBox _statConduit = new CheckBox { Text = "线管长度", AutoSize = true };
        private readonly NumericUpDown _statHgt = NumberBox(180m, 1m, 100000m);
        private readonly NumericUpDown _statMm = NumberBox(250m, 1m, 100000m);
        private readonly ToolTip _toolTips = new ToolTip();

        // 文字样式
        private readonly TextBox _styleName = new TextBox { Width = 140 };
        private readonly TextBox _styleFont = new TextBox { Width = 140 };
        private readonly TextBox _styleBigFont = new TextBox { Width = 140 };
        private readonly TextBox _styleWidth = new TextBox { Width = 60 };

        // Excel 填充
        private readonly TextBox _fillExcel = new TextBox { Width = 310 };
        private readonly TextBox _fillCatalog = new TextBox { Width = 310 };
        private readonly TextBox _fillTblRow = new TextBox { Width = 60 };
        private readonly NumericUpDown _fillClearRows = IntegerBox(11m, 1m, 100m);
        private readonly TextBox _fillTextHeight = new TextBox { Width = 60 };
        private readonly TextBox _fillBridge = new TextBox { Width = 220 };
        private readonly TextBox _submitFolder = new TextBox { Width = 310 };

        public UnifiedSettingsForm(int tabIndex)
        {
            Text = "UNCAD 配置中心 · " + Branding.Nameplate;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(680, 400);

            _conduitDia.Items.AddRange(new object[] { "20", "25", "32" });
            ConfigureStatisticsToolTips();
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

            SetNumber(_statHgt, Settings.GetDouble(ConfigKeys.UnaddHeight, 180.0));
            SetNumber(_statMm, Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0));
            _statText.Checked = Settings.GetBool(ConfigKeys.UnaddTextEnabled, true);
            _statMText.Checked = Settings.GetBool(ConfigKeys.UnaddMTextEnabled, true);
            _statCable.Checked = Settings.GetBool(ConfigKeys.UnaddCableEnabled, true);
            _statBridge.Checked = Settings.GetBool(ConfigKeys.UnaddBridgeEnabled, true);
            _statConduit.Checked = Settings.GetBool(ConfigKeys.UnaddConduitEnabled, true);

            // 文字样式
            _styleName.Text = Settings.Get(ConfigKeys.StyleName, "UNC-标注");
            _styleFont.Text = Settings.Get(ConfigKeys.StyleFont, "msyh.ttf");
            _styleBigFont.Text = Settings.Get(ConfigKeys.StyleBigFont, "");
            _styleWidth.Text = Settings.GetDouble(ConfigKeys.StyleWidth, 0.8).ToString("0.##", CultureInfo.InvariantCulture);

            // Excel 填充
            _fillExcel.Text = Settings.Get(ConfigKeys.FillExcelPath, "");
            _fillCatalog.Text = Settings.Get(ConfigKeys.FillCatalogPath, "");
            _fillTblRow.Text = ((int)Settings.GetDouble(ConfigKeys.FillTableRow, 1.0)).ToString();
            SetNumber(_fillClearRows, Settings.GetDouble(ConfigKeys.FillClearRows, 11.0));
            _fillTextHeight.Text = Settings.GetDouble(ConfigKeys.FillTextHeight, TableFillFormatter.DefaultTextHeight)
                .ToString("0.##", CultureInfo.InvariantCulture);
            _fillBridge.Text = Settings.Get(ConfigKeys.FillBridge, "");
            _submitFolder.Text = Settings.Get(ConfigKeys.SubmitFolder, "");
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
            Settings.Set(ConfigKeys.UnaddHeight, ((double)_statHgt.Value)
                .ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnaddMmPerGrid, ((double)_statMm.Value)
                .ToString("0.##", CultureInfo.InvariantCulture));
            Settings.SetBool(ConfigKeys.UnaddTextEnabled, _statText.Checked);
            Settings.SetBool(ConfigKeys.UnaddMTextEnabled, _statMText.Checked);
            Settings.SetBool(ConfigKeys.UnaddCableEnabled, _statCable.Checked);
            Settings.SetBool(ConfigKeys.UnaddBridgeEnabled, _statBridge.Checked);
            Settings.SetBool(ConfigKeys.UnaddConduitEnabled, _statConduit.Checked);

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
            Settings.Set(ConfigKeys.FillClearRows, ((int)_fillClearRows.Value).ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillTextHeight, Pos(_fillTextHeight, TableFillFormatter.DefaultTextHeight)
                .ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillBridge, _fillBridge.Text.Trim());
            Settings.Set(ConfigKeys.SubmitFolder, _submitFolder.Text.Trim());
        }

        private void ConfigureStatisticsToolTips()
        {
            _toolTips.ShowAlways = true;
            _toolTips.SetToolTip(_statText, "读取AutoCAD单行文字实体，每个实体必须整行符合规则。");
            _toolTips.SetToolTip(_statMText, "读取AutoCAD多行文字实体，按\\P拆分后每行独立严格匹配。");
            _toolTips.SetToolTip(_statCable, "严格格式示例：2000mm；不允许前后缀或备注。");
            _toolTips.SetToolTip(_statBridge, "严格格式示例：桥架200*100 12格；不允许“共用”等附加内容。");
            _toolTips.SetToolTip(_statConduit, "严格格式示例：Φ20线管 2000mm；不允许前后缀或备注。");
            _toolTips.SetToolTip(_statMm, "只用于把桥架格数换算成毫米。");
        }

        private static NumericUpDown IntegerBox(decimal value, decimal minimum, decimal maximum)
        {
            NumericUpDown box = NumberBox(value, minimum, maximum);
            box.DecimalPlaces = 0;
            return box;
        }

        private static NumericUpDown NumberBox(decimal value, decimal minimum, decimal maximum)
        {
            return new NumericUpDown
            {
                Width = 100, DecimalPlaces = 2, Increment = 1m,
                Minimum = minimum, Maximum = maximum, Value = value,
                ThousandsSeparator = true
            };
        }

        private static void SetNumber(NumericUpDown box, double value)
        {
            decimal converted;
            try { converted = Convert.ToDecimal(value); }
            catch { converted = box.Minimum; }
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, converted));
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
            var source = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Padding = new Padding(8, 8, 8, 4)
            };
            source.Controls.Add(_statText);
            source.Controls.Add(_statMText);

            var categories = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Padding = new Padding(8, 8, 8, 4)
            };
            categories.Controls.Add(_statCable);
            categories.Controls.Add(_statBridge);
            categories.Controls.Add(_statConduit);

            var output = Grid(2);
            output.AutoSize = false;
            output.Dock = DockStyle.Fill;
            output.Padding = new Padding(8, 4, 8, 4);
            output.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            output.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            output.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            output.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            output.Controls.Add(Lbl("输出文字高度:"), 0, 0);
            output.Controls.Add(_statHgt, 1, 0);
            output.Controls.Add(Lbl("桥架每格长度 (mm):"), 0, 1);
            output.Controls.Add(_statMm, 1, 1);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(12, 10, 12, 10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Group("文字来源", source), 0, 0);
            layout.Controls.Add(Group("统计类别", categories), 0, 1);
            layout.Controls.Add(Group("换算与输出", output), 0, 2);
            return Page("统计汇总", layout);
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
            var g = Grid(7);
            g.Controls.Add(Lbl("机台数据 Excel:"), 0, 0); g.Controls.Add(PathPicker(_fillExcel), 1, 0);
            g.Controls.Add(Lbl("固定清单 Excel(空=同文件):"), 0, 1); g.Controls.Add(PathPicker(_fillCatalog), 1, 1);
            g.Controls.Add(Lbl("起始数据行(1=No.1):"), 0, 2); g.Controls.Add(_fillTblRow, 1, 2);
            g.Controls.Add(Lbl("每次清空数据行数:"), 0, 3); g.Controls.Add(_fillClearRows, 1, 3);
            g.Controls.Add(Lbl("表格文字高度:"), 0, 4); g.Controls.Add(_fillTextHeight, 1, 4);
            g.Controls.Add(Lbl("桥架信息(块属性):"), 0, 5); g.Controls.Add(_fillBridge, 1, 5);
            g.Controls.Add(Lbl("提交记录文件夹:"), 0, 6); g.Controls.Add(FolderPicker(_submitFolder), 1, 6);
            return Page("Excel 数据", g);
        }

        private Control FolderPicker(TextBox target)
        {
            var button = new Button { Text = "浏览...", Width = 66, Height = 26 };
            button.Click += (sender, args) =>
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = "选择 UNC_SUBMIT 提交表文件夹",
                    ShowNewFolderButton = true,
                    SelectedPath = Directory.Exists(target.Text.Trim()) ? target.Text.Trim() : ""
                })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
                }
            };
            var panel = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Margin = new Padding(0)
            };
            panel.Controls.Add(target);
            panel.Controls.Add(button);
            return panel;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTips.Dispose();
            base.Dispose(disposing);
        }

        private static GroupBox Group(string title, Control content)
        {
            var group = new GroupBox
            {
                Text = title, Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8), Padding = new Padding(8)
            };
            content.Dock = DockStyle.Fill;
            group.Controls.Add(content);
            return group;
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

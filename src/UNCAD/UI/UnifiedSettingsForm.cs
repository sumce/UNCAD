using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 统一配置中心对话框（U1SET）。
    /// 页签：线段绘制 / 桥架标注 / 线管标注 / 拱桥开洞 / 统计汇总 / 文字样式 / Excel 数据。
    /// 确定时一次性写回全部配置键（Features 只读，这里统一写）。
    /// </summary>
    public sealed class UnifiedSettingsForm : Form
    {
        private readonly UiNavigationTabControl _tabs = new UiNavigationTabControl
        {
            Name = "SettingsNavigation",
            Dock = DockStyle.Fill
        };

        // 线段绘制
        private readonly TextBox _unlText = new TextBox { Width = 200 };
        private readonly NumericUpDown _unlHgt = NumberBox(180m, 1m, 100000m);
        private readonly RadioButton _unlCenter = new RadioButton { Text = "居中" };
        private readonly RadioButton _unlBelow = new RadioButton { Text = "靠边下" };
        private readonly RadioButton _unlAbove = new RadioButton { Text = "靠边上" };
        private readonly NumericUpDown _unlOff = NumberBox(0m, 0m, 100000m);

        // 桥架标注
        private readonly TextBox _unqText = new TextBox { Width = 200 };
        private readonly NumericUpDown _unqHgt = NumberBox(180m, 1m, 100000m);
        private readonly NumericUpDown _unqLoff = NumberBox(15m, 0m, 100000m);
        private readonly NumericUpDown _unqToff = NumberBox(0m, 0m, 100000m);
        private readonly RadioButton _unqAbove = new RadioButton { Text = "上方" };
        private readonly RadioButton _unqBelow = new RadioButton { Text = "下方" };

        // 线管标注
        private readonly ComboBox _conduitDia = new ComboBox
        {
            Width = 100,
            DropDownStyle = ComboBoxStyle.DropDown
        };

        // 拱桥开洞
        private readonly NumericUpDown _unrDia = NumberBox(300m, 1m, 100000m);

        // 统计汇总
        private readonly CheckBox _statText = new CheckBox
        {
            Text = "单行文字 (TEXT)",
            AutoSize = true
        };
        private readonly CheckBox _statMText = new CheckBox
        {
            Text = "支持多行文本识别 (MTEXT)",
            AutoSize = true
        };
        private readonly CheckBox _statDim = new CheckBox
        {
            Text = "尺寸标注文字 (DIMENSION)",
            AutoSize = true
        };
        private readonly CheckBox _statCable = new CheckBox { Text = "电缆长度", AutoSize = true };
        private readonly CheckBox _statBridge = new CheckBox { Text = "桥架长度", AutoSize = true };
        private readonly CheckBox _statConduit = new CheckBox { Text = "线管长度", AutoSize = true };
        private readonly NumericUpDown _statHgt = NumberBox(180m, 1m, 100000m);
        private readonly NumericUpDown _statMm = NumberBox(250m, 1m, 100000m);
        private readonly ToolTip _toolTips = new ToolTip();

        // 启动动画
        private readonly CheckBox _splashEnabled =
            new CheckBox { Text = "启动时显示全屏品牌动画", AutoSize = true };

        // 文字样式
        private readonly TextBox _styleName = new TextBox { Width = 140 };
        private readonly TextBox _styleFont = new TextBox { Width = 140 };
        private readonly TextBox _styleBigFont = new TextBox { Width = 140 };
        private readonly NumericUpDown _styleWidth = NumberBox(0.8m, 0.01m, 100m);

        // Excel 填充
        private readonly TextBox _fillExcel = new TextBox { Width = 310 };
        private readonly NumericUpDown _fillTblRow = IntegerBox(1m, 1m, 1000m);
        private readonly NumericUpDown _fillClearRows = IntegerBox(11m, 1m, 100m);
        private readonly NumericUpDown _fillTextHeight = NumberBox(500m, 1m, 100000m);
        private readonly TextBox _fillBridge = new TextBox { Width = 220 };
        private readonly NumericUpDown _fillFlexibleMeters = NumberBox(1.5m, 0.1m, 100m);
        private readonly TextBox _submitFolder = new TextBox { Width = 310 };
        private readonly ComboBox _fillDeviceColor = ColorBox();
        private readonly ComboBox _fillUpstreamColor = ColorBox();
        private readonly CheckBox _autofillCable =
            new CheckBox { Text = "电缆", AutoSize = true };
        private readonly CheckBox _autofillBreaker =
            new CheckBox { Text = "断路器", AutoSize = true };
        private readonly CheckBox _autofillFlexibleConduit =
            new CheckBox { Text = "软管", AutoSize = true };
        private readonly CheckBox _autofillRigidConduit =
            new CheckBox { Text = "线管", AutoSize = true };
        private readonly CheckBox _autofillBridge =
            new CheckBox { Text = "桥架", AutoSize = true };
        private readonly CheckBox _autofillOutletPanel =
            new CheckBox { Text = "插座盘", AutoSize = true };
        private readonly CheckBox _autofillBusPlugBox =
            new CheckBox { Text = "插接箱", AutoSize = true };
        private readonly Label _snapshotStatus = new Label
        {
            Name = "MachineSnapshotStatus",
            AutoSize = true,
            ForeColor = UiTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        private bool _machineRefreshInProgress;

        public UnifiedSettingsForm(int tabIndex)
        {
            DialogLayout.Apply(this, ProductMetadata.ProductName + " · 配置中心",
                new System.Drawing.Size(1040, 700), new System.Drawing.Size(860, 580));

            _conduitDia.Items.AddRange(new object[]
                { "20", "25", "32", "38", "51", "75", "100" });
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
            StyleInteractiveControls(_tabs);
            _fillExcel.TextChanged += (sender, args) => UpdateMachineSnapshotStatus();
            UpdateMachineSnapshotStatus();

            Button ok = UiTheme.PrimaryButton("保存");
            Button cancel = UiTheme.Button("取消", DialogResult.Cancel);
            ok.Click += Confirm;
            FlowLayoutPanel btnRow = UiTheme.CommandBar();
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(ok);

            Controls.Add(_tabs);
            Controls.Add(btnRow);
            Controls.Add(BuildHeader());
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private Control BuildHeader()
        {
            return UiTheme.Header(
                ProductMetadata.ProductName + " 设置",
                "绘图、统计、清单与输出");
        }

        private void Confirm(object sender, EventArgs e)
        {
            if (_machineRefreshInProgress) return;
            if (!ConduitDiameter.TryNormalize(_conduitDia.Text, out string diameter))
            {
                _tabs.SelectedIndex = 2;
                _conduitDia.Focus();
                _conduitDia.SelectAll();
                MessageBox.Show(this, "默认管径无效，请输入1到1000毫米之间的数值。",
                    "U1SET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _conduitDia.Text = diameter;
            // 用户只需要提供机台/设备表；固定清单随插件内嵌，不再有清单路径校验。
            string machinePath = _fillExcel.Text.Trim();
            // A machine workbook may be a manually selected local file or a
            // manually entered HTTP(S) source that is refreshed from this page.
            // Do not treat a valid remote source as a missing local path.
            if (machinePath.Length > 0
                && !MachineWorkbookSource.IsRemote(machinePath)
                && !File.Exists(machinePath)
                && !MachineWorkbookSource.HasSnapshot(machinePath))
            {
                ShowPathError(_fillExcel, "机台数据 Excel 不存在。");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowPathError(TextBox field, string message)
        {
            _tabs.SelectedIndex = 6;
            field.Focus();
            field.SelectAll();
            MessageBox.Show(this, message, "U1SET",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---------- 读取当前值 ----------

        private void LoadValues()
        {
            _unlText.Text = Settings.Get(ConfigKeys.UnlText, "2000mm");
            SetNumber(_unlHgt, Settings.GetDouble(ConfigKeys.UnlHeight, 180.0));
            string unlPos = Settings.Get(ConfigKeys.UnlPos, "1");
            _unlCenter.Checked = unlPos == "0";
            _unlBelow.Checked = unlPos == "1";
            _unlAbove.Checked = unlPos == "2";
            SetNumber(_unlOff, Settings.GetDouble(ConfigKeys.UnlOffset, 0.0));

            _unqText.Text = Settings.Get(ConfigKeys.UnqText, "桥架200*100 10格");
            SetNumber(_unqHgt, Settings.GetDouble(ConfigKeys.UnqHeight, 180.0));
            SetNumber(_unqLoff, Settings.GetDouble(ConfigKeys.UnqLineOff, 15.0));
            SetNumber(_unqToff, Settings.GetDouble(ConfigKeys.UnqTextOff, 0.0));
            string unqSide = Settings.Get(ConfigKeys.UnqSide, "1");
            _unqAbove.Checked = unqSide != "0";
            _unqBelow.Checked = unqSide == "0";

            _conduitDia.Text = ConduitDiameter.NormalizeOrDefault(
                Settings.Get(ConfigKeys.ConduitDiameter, "20"), "20");

            SetNumber(_unrDia, Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0));

            SetNumber(_statHgt, Settings.GetDouble(ConfigKeys.UnaddHeight, 180.0));
            SetNumber(_statMm, Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0));
            _statText.Checked = Settings.GetBool(ConfigKeys.UnaddTextEnabled, true);
            _statMText.Checked = Settings.GetBool(ConfigKeys.UnaddMTextEnabled,
                StatisticsSettings.DefaultIncludeMText);
            _statDim.Checked = Settings.GetBool(ConfigKeys.UnaddDimensionEnabled,
                StatisticsSettings.DefaultIncludeDimension);
            _statCable.Checked = Settings.GetBool(ConfigKeys.UnaddCableEnabled, true);
            _statBridge.Checked = Settings.GetBool(ConfigKeys.UnaddBridgeEnabled, true);
            _statConduit.Checked = Settings.GetBool(ConfigKeys.UnaddConduitEnabled, true);
            _splashEnabled.Checked = Settings.GetBool(ConfigKeys.StartupSplashEnabled, true);

            // 文字样式
            _styleName.Text = Settings.Get(ConfigKeys.StyleName, "UNC-标注");
            _styleFont.Text = Settings.Get(ConfigKeys.StyleFont, "msyh.ttf");
            _styleBigFont.Text = Settings.Get(ConfigKeys.StyleBigFont, "");
            SetNumber(_styleWidth, Settings.GetDouble(ConfigKeys.StyleWidth, 0.8));

            // Excel 填充
            _fillExcel.Text = Settings.Get(ConfigKeys.FillExcelPath, "");
            SetNumber(_fillTblRow, Settings.GetDouble(ConfigKeys.FillTableRow, 1.0));
            SetNumber(_fillClearRows, Settings.GetDouble(ConfigKeys.FillClearRows, 11.0));
            SetNumber(_fillTextHeight, Settings.GetDouble(ConfigKeys.FillTextHeight,
                TableFillFormatter.DefaultTextHeight));
            _fillBridge.Text = Settings.Get(ConfigKeys.FillBridge, "");
            SetNumber(_fillFlexibleMeters, Settings.GetDouble(
                ConfigKeys.FillFlexibleConduitMeters,
                FillPlanningOptions.DefaultFlexibleConduitMeters));
            _submitFolder.Text = Settings.Get(ConfigKeys.SubmitFolder, "");
            SelectColor(_fillDeviceColor, FillColorSettings.DeviceColorIndex());
            SelectColor(_fillUpstreamColor, FillColorSettings.UpstreamColorIndex());
            FillAutoFillOptions autoFill = FillAutoFillOptions.Default;
            _autofillCable.Checked = Settings.GetBool(ConfigKeys.FillAutofillCable,
                autoFill.Cable);
            _autofillBreaker.Checked = Settings.GetBool(ConfigKeys.FillAutofillBreaker,
                autoFill.Breaker);
            _autofillFlexibleConduit.Checked = Settings.GetBool(
                ConfigKeys.FillAutofillFlexibleConduit, autoFill.FlexibleConduit);
            _autofillRigidConduit.Checked = Settings.GetBool(
                ConfigKeys.FillAutofillRigidConduit, autoFill.RigidConduit);
            _autofillBridge.Checked = Settings.GetBool(ConfigKeys.FillAutofillBridge,
                autoFill.Bridge);
            _autofillOutletPanel.Checked = Settings.GetBool(
                ConfigKeys.FillAutofillOutletPanel, autoFill.OutletPanel);
            _autofillBusPlugBox.Checked = Settings.GetBool(
                ConfigKeys.FillAutofillBusPlugBox, autoFill.BusPlugBox);
        }

        // ---------- 保存（含数据校验） ----------

        public void Save()
        {
            // 线段
            Settings.Set(ConfigKeys.UnlText, _unlText.Text.Trim());
            Settings.Set(ConfigKeys.UnlHeight, Value(_unlHgt));
            Settings.Set(ConfigKeys.UnlPos, _unlCenter.Checked ? "0" : _unlBelow.Checked ? "1" : "2");
            Settings.Set(ConfigKeys.UnlOffset, Value(_unlOff));

            // 桥架（偏移允许 0=自动/贴线）
            Settings.Set(ConfigKeys.UnqText, _unqText.Text.Trim());
            Settings.Set(ConfigKeys.UnqHeight, Value(_unqHgt));
            Settings.Set(ConfigKeys.UnqLineOff, Value(_unqLoff));
            Settings.Set(ConfigKeys.UnqTextOff, Value(_unqToff));
            Settings.Set(ConfigKeys.UnqSide, _unqAbove.Checked ? "1" : "0");

            // 线管只保存管径；标注几何参数与桥架共用。
            Settings.Set(ConfigKeys.ConduitDiameter,
                ConduitDiameter.NormalizeOrDefault(_conduitDia.Text, "20"));

            // 拱桥
            Settings.Set(ConfigKeys.UnrDiameter, Value(_unrDia));

            // 统计
            Settings.Set(ConfigKeys.UnaddHeight, ((double)_statHgt.Value)
                .ToString("0.##", CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.UnaddMmPerGrid, ((double)_statMm.Value)
                .ToString("0.##", CultureInfo.InvariantCulture));
            Settings.SetBool(ConfigKeys.UnaddTextEnabled, _statText.Checked);
            Settings.SetBool(ConfigKeys.UnaddMTextEnabled, _statMText.Checked);
            Settings.SetBool(ConfigKeys.UnaddDimensionEnabled, _statDim.Checked);
            Settings.SetBool(ConfigKeys.UnaddCableEnabled, _statCable.Checked);
            Settings.SetBool(ConfigKeys.UnaddBridgeEnabled, _statBridge.Checked);
            Settings.SetBool(ConfigKeys.UnaddConduitEnabled, _statConduit.Checked);
            Settings.SetBool(ConfigKeys.StartupSplashEnabled, _splashEnabled.Checked);

            // 文字样式
            Settings.Set(ConfigKeys.StyleName, _styleName.Text.Trim());
            Settings.Set(ConfigKeys.StyleFont, _styleFont.Text.Trim());
            Settings.Set(ConfigKeys.StyleBigFont, _styleBigFont.Text.Trim());
            Settings.Set(ConfigKeys.StyleWidth, Value(_styleWidth));

            // Excel 填充
            Settings.Set(ConfigKeys.FillExcelPath, _fillExcel.Text.Trim());
            Settings.Set(ConfigKeys.FillTableRow, ((int)_fillTblRow.Value)
                .ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillClearRows, ((int)_fillClearRows.Value).ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillTextHeight, Value(_fillTextHeight));
            Settings.Set(ConfigKeys.FillBridge, _fillBridge.Text.Trim());
            Settings.Set(ConfigKeys.FillFlexibleConduitMeters, Value(_fillFlexibleMeters));
            Settings.Set(ConfigKeys.SubmitFolder, _submitFolder.Text.Trim());
            Settings.Set(ConfigKeys.FillDeviceColorIndex,
                SelectedColorIndex(_fillDeviceColor,
                    FillColorSettings.DefaultDeviceColorIndex)
                .ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillUpstreamColorIndex,
                SelectedColorIndex(_fillUpstreamColor,
                    FillColorSettings.DefaultUpstreamColorIndex)
                .ToString(CultureInfo.InvariantCulture));
            Settings.SetBool(ConfigKeys.FillAutofillCable, _autofillCable.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillBreaker, _autofillBreaker.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillFlexibleConduit,
                _autofillFlexibleConduit.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillRigidConduit,
                _autofillRigidConduit.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillBridge, _autofillBridge.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillOutletPanel,
                _autofillOutletPanel.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillBusPlugBox,
                _autofillBusPlugBox.Checked);
        }

        private void ConfigureStatisticsToolTips()
        {
            _toolTips.ShowAlways = true;
            _toolTips.SetToolTip(_statText,
                "读取AutoCAD单行文字实体，每个实体必须整行符合规则。");
            _toolTips.SetToolTip(_statMText,
                "开启后读取 MTEXT；同一实体内的固定清单型号和长度会严格配对，其余行独立匹配。");
            _toolTips.SetToolTip(_statDim,
                "开启后，U1F/U1U/UNADD 读取对齐/转角标注中人工输入的文字（如 2000mm）；自动测量的标注不参与统计。");
            _toolTips.SetToolTip(_statCable, "严格格式示例：2000mm；不允许前后缀或备注。");
            _toolTips.SetToolTip(_statBridge, "严格格式示例：桥架200*100 12格；不允许“共用”等附加内容。");
            _toolTips.SetToolTip(_statConduit,
                "严格格式示例：Φ20线管 2000mm；兼容旧图的“线管20 2000mm”。");
            _toolTips.SetToolTip(_statMm, "只用于把桥架格数换算成毫米。");
            _toolTips.SetToolTip(_fillFlexibleMeters, "仅作为清单确认时的手动数量；自动软管长度来自 Ruanguan 动态块。");
            _toolTips.SetToolTip(_fillDeviceColor,
                "U1F/U1U 用于设备块和设备轴位块（DS）的 AutoCAD ACI 颜色。");
            _toolTips.SetToolTip(_fillUpstreamColor,
                "U1F/U1U 用于 upstream、上游信息/编号块和上游轴位块（US）的 AutoCAD ACI 颜色。");
        }

        private static ComboBox ColorBox()
        {
            var box = new ComboBox
            {
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = UiTheme.NotAutoScaled(22)
            };
            box.Items.AddRange(new object[]
            {
                new CadColorChoice(1, "红色", Color.Red),
                new CadColorChoice(2, "黄色", Color.Yellow),
                new CadColorChoice(3, "绿色", Color.Lime),
                new CadColorChoice(4, "青色", Color.Cyan),
                new CadColorChoice(5, "蓝色", Color.Blue),
                new CadColorChoice(6, "洋红", Color.Fuchsia),
                new CadColorChoice(7, "白色", Color.White),
                new CadColorChoice(8, "深灰", Color.Gray),
                new CadColorChoice(9, "浅灰", Color.LightGray)
            });
            box.DrawItem += DrawColorChoice;
            return box;
        }

        private static void DrawColorChoice(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            var box = sender as ComboBox;
            if (box == null || e.Index < 0 || e.Index >= box.Items.Count) return;
            var choice = box.Items[e.Index] as CadColorChoice;
            if (choice == null) return;

            // 实测：ComboBox.ItemHeight 不在 WinForms 自动缩放的覆盖范围内，
            // 行高恒为设计值，因此这里的固定像素内边距与色块宽度在任意 DPI 下
            // 都与行高保持同一比例，不需要额外换算。
            var swatch = new Rectangle(e.Bounds.Left + 5, e.Bounds.Top + 4, 20,
                e.Bounds.Height - 8);
            using (var brush = new SolidBrush(choice.Preview))
                e.Graphics.FillRectangle(brush, swatch);
            e.Graphics.DrawRectangle(Pens.DimGray, swatch);
            TextRenderer.DrawText(e.Graphics, choice.ToString(), e.Font,
                new Rectangle(swatch.Right + 8, e.Bounds.Top,
                    e.Bounds.Width - swatch.Width - 16, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        }

        private static void SelectColor(ComboBox box, short colorIndex)
        {
            for (int index = 0; index < box.Items.Count; index++)
            {
                if (!(box.Items[index] is CadColorChoice choice)
                    || choice.Index != colorIndex) continue;
                box.SelectedIndex = index;
                return;
            }
            box.SelectedIndex = 0;
        }

        private static short SelectedColorIndex(ComboBox box, short fallback)
            => box.SelectedItem is CadColorChoice choice ? choice.Index : fallback;

        private sealed class CadColorChoice
        {
            public CadColorChoice(short index, string name, Color preview)
            {
                Index = index;
                Name = name;
                Preview = preview;
            }

            public short Index { get; }
            public string Name { get; }
            public Color Preview { get; }
            public override string ToString() => Name + " (ACI " + Index + ")";
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
                Width = 100,
                DecimalPlaces = 2,
                Increment = 1m,
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
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

        private static string Value(NumericUpDown box)
            => ((double)box.Value).ToString("0.##", CultureInfo.InvariantCulture);

        // ---------- 页签构建 ----------

        private TabPage BuildUnlTab()
        {
            var g = Grid(4);
            AddField(g, 0, "占位文字", _unlText);
            AddField(g, 1, "文字高度", _unlHgt);
            var posPanel = new FlowLayoutPanel { AutoSize = true };
            posPanel.Controls.Add(_unlCenter);
            posPanel.Controls.Add(_unlBelow);
            posPanel.Controls.Add(_unlAbove);
            AddField(g, 2, "文字位置", posPanel);
            AddField(g, 3, "偏移距离（0 = 贴线）", _unlOff);
            return Page("线段绘制", "U1L 默认标注参数",
                Section("线段文字", null, g));
        }

        private TabPage BuildUnqTab()
        {
            var g = Grid(5);
            AddField(g, 0, "默认规格", _unqText);
            AddField(g, 1, "文字高度", _unqHgt);
            AddField(g, 2, "红线距基线（0 = 15）", _unqLoff);
            AddField(g, 3, "文字距红线（0 = 贴线）", _unqToff);
            var sidePanel = new FlowLayoutPanel { AutoSize = true };
            sidePanel.Controls.Add(_unqAbove);
            sidePanel.Controls.Add(_unqBelow);
            AddField(g, 4, "标注侧", sidePanel);
            return Page("桥架标注", "U1Q1 / U1Q2 / U1Q4 标注参数",
                Section("桥架文字与位置", "成图文字使用固定 BOQ 清单中的桥架型号。", g));
        }

        private TabPage BuildConduitTab()
        {
            var g = Grid(1);
            AddField(g, 0, "默认管径", _conduitDia);
            return Page("线管标注", "U1C 默认规格",
                Section("线管规格", "文字高度、偏移距离和标注侧与桥架标注共用。", g));
        }

        private TabPage BuildUnrTab()
        {
            var g = Grid(1);
            AddField(g, 0, "拱桥直径", _unrDia);
            return Page("拱桥开洞", "U1R 几何参数",
                Section("开洞尺寸", null, g));
        }

        private TabPage BuildStatTab()
        {
            var source = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            source.Controls.Add(_statText);
            source.Controls.Add(_statMText);
            source.Controls.Add(_statDim);

            var categories = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            categories.Controls.Add(_statCable);
            categories.Controls.Add(_statBridge);
            categories.Controls.Add(_statConduit);

            var output = Grid(2);
            AddField(output, 0, "输出文字高度", _statHgt);
            AddField(output, 1, "桥架每格长度（mm）", _statMm);

            return Page("统计汇总", "UNADD 与 U1F / U1U 的文字统计范围",
                Section("文字来源", null, source),
                Section("统计类别", null, categories),
                Section("换算与输出", null, output));
        }

        private TabPage BuildStyleTab()
        {
            var style = Grid(4);
            AddField(style, 0, "样式名", _styleName);
            AddField(style, 1, "字体文件（如 msyh.ttf）", _styleFont);
            AddField(style, 2, "大字体文件（空 = TTF）", _styleBigFont);
            AddField(style, 3, "宽高比", _styleWidth);

            var startup = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0)
            };
            startup.Controls.Add(_splashEnabled);
            return Page("文字样式", "所有生成文字的公共样式",
                Section("文字样式", null, style),
                Section("启动", null, startup));
        }

        private TabPage BuildFillTab()
        {
            var source = Grid(2);
            // 机台数据 Excel 是用户唯一需要提供的文件；固定清单内嵌在插件里。
            AddField(source, 0, "机台数据 Excel", PathPicker(_fillExcel));
            AddField(source, 1, "快照状态", _snapshotStatus);

            var table = Grid(7);
            AddField(table, 0, "起始数据行（1 = No.1）", _fillTblRow);
            AddField(table, 1, "每次清空数据行数", _fillClearRows);
            AddField(table, 2, "表格文字高度", _fillTextHeight);
            AddField(table, 3, "软管手动数量（m）", _fillFlexibleMeters);
            AddField(table, 4, "桥架信息（块属性）", _fillBridge);
            AddField(table, 5, "设备端颜色", _fillDeviceColor);
            AddField(table, 6, "上游端颜色", _fillUpstreamColor);

            var autoFill = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 4,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 0)
            };
            for (int column = 0; column < autoFill.ColumnCount; column++)
                autoFill.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            autoFill.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            autoFill.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            autoFill.Controls.Add(_autofillCable, 0, 0);
            autoFill.Controls.Add(_autofillBreaker, 1, 0);
            autoFill.Controls.Add(_autofillFlexibleConduit, 2, 0);
            autoFill.Controls.Add(_autofillRigidConduit, 3, 0);
            autoFill.Controls.Add(_autofillBridge, 0, 1);
            autoFill.Controls.Add(_autofillOutletPanel, 1, 1);
            autoFill.Controls.Add(_autofillBusPlugBox, 2, 1);

            var output = Grid(1);
            AddField(output, 0, "自动输出文件夹", FolderPicker(_submitFolder));

            return Page("Excel 数据", "机台快照、BOQ 图框与自动输出",
                Section("机台数据源", "数据只在点击刷新后写入 SQLite 快照。", source),
                Section("图框与表格", null, table),
                Section("BOQ 自动填充类别", "断路器与插座盘独立控制；软管长度读取 Ruanguan 动态块。", autoFill),
                Section("自动输出", null, output));
        }

        private Control FolderPicker(TextBox target)
        {
            var button = UiTheme.Button("浏览...");
            button.Click += (sender, args) =>
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = "选择 U1F / U1U / U1DWG 自动输出文件夹",
                    ShowNewFolderButton = true,
                    SelectedPath = Directory.Exists(target.Text.Trim()) ? target.Text.Trim() : ""
                })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
                }
            };
            return PickerPanel(target, button);
        }

        private Control PathPicker(TextBox target)
        {
            var button = UiTheme.Button("浏览...");
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
            var refresh = UiTheme.Button("刷新");
            refresh.Click += (sender, args) => RefreshMachineWorkbook(target, refresh);
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.Controls.Add(target, 0, 0);
            row.Controls.Add(button, 1, 0);
            row.Controls.Add(refresh, 2, 0);
            return row;
        }

        private async void RefreshMachineWorkbook(TextBox target, Button button)
        {
            string source = target.Text.Trim();
            if (source.Length == 0)
            {
                MessageBox.Show(this, "请先选择或输入机台数据 Excel。", "U1SET",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!MachineWorkbookSource.IsRemote(source) && !File.Exists(source))
            {
                MessageBox.Show(this, "本地机台数据 Excel 不存在。", "U1SET",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Refresh is an explicit user action, so remember the source before
            // the asynchronous import even if the settings dialog is later closed.
            Settings.Set(ConfigKeys.FillExcelPath, source);
            _machineRefreshInProgress = true;
            button.Enabled = false;
            string oldText = button.Text;
            button.Text = "刷新中...";
            try
            {
                MachineWorkbookSourceResult result = await Task.Run(() => MachineWorkbookSource.Refresh(source));
                string message = result.UsedCachedFallback
                    ? "网络暂时不可用，继续使用上次 SQLite 快照。"
                    : "机台 Excel 已解析并写入 SQLite 快照。";
                if (!result.UsedCachedFallback && result.Snapshot != null)
                    message += "\n有效回路: " + result.Snapshot.RowCount + " 行。";
                if (!string.IsNullOrWhiteSpace(result.Warning))
                    message += "\n" + result.Warning;
                if (IsDisposed || Disposing) return;
                UpdateMachineSnapshotStatus();
                MessageBox.Show(this, message, "U1SET", MessageBoxButtons.OK,
                    result.UsedCachedFallback || !string.IsNullOrWhiteSpace(result.Warning)
                        ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("U1SET refresh machine workbook failed", ex);
                if (IsDisposed || Disposing) return;
                MessageBox.Show(this, "刷新失败：" + ex.Message, "U1SET",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    button.Text = oldText;
                    button.Enabled = true;
                }
                _machineRefreshInProgress = false;
            }
        }

        private void UpdateMachineSnapshotStatus()
        {
            string source = _fillExcel.Text.Trim();
            if (source.Length == 0)
            {
                _snapshotStatus.Text = "未选择数据源";
                _snapshotStatus.ForeColor = UiTheme.TextSecondary;
                return;
            }
            if (!MachineWorkbookSource.TryGetSnapshot(source,
                out MachineWorkbookSnapshotInfo snapshot))
            {
                _snapshotStatus.Text = "尚未刷新";
                _snapshotStatus.ForeColor = UiTheme.WarningFg;
                return;
            }

            string refreshed = snapshot.RefreshedUtc;
            if (DateTime.TryParse(snapshot.RefreshedUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime utc))
                refreshed = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture);
            _snapshotStatus.Text = "已刷新 · " + snapshot.RowCount
                + " 个有效回路 · " + refreshed;
            _snapshotStatus.ForeColor = UiTheme.SuccessFg;
        }

        private static Control PickerPanel(TextBox target, Button button)
        {
            target.Dock = DockStyle.Fill;
            button.Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            layout.Controls.Add(target, 0, 0);
            layout.Controls.Add(button, 1, 0);
            return layout;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTips.Dispose();
            base.Dispose(disposing);
        }

        private static void StyleInteractiveControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is TextBox || child is ComboBox || child is NumericUpDown)
                    UiTheme.StyleInput(child);
                else if (child is CheckBox || child is RadioButton)
                {
                    child.Font = UiTheme.FontBody;
                    child.ForeColor = UiTheme.TextPrimary;
                }
                StyleInteractiveControls(child);
            }
        }

        private static Label Lbl(string text) => new Label
        {
            Text = text,
            ForeColor = UiTheme.TextSecondary,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(0, 0, 12, 0),
            Margin = new Padding(0)
        };

        private static void AddField(TableLayoutPanel grid, int row, string label,
            Control control)
        {
            control.Margin = new Padding(0, 5, 0, 5);
            if (control is TextBox || control is TableLayoutPanel)
                control.Dock = DockStyle.Fill;
            else
                control.Anchor = AnchorStyles.Left;
            grid.Controls.Add(Lbl(label), 0, row);
            grid.Controls.Add(control, 1, row);
        }

        private static TableLayoutPanel Grid(int rows)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = rows,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < rows; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            return layout;
        }

        private static UiCard Section(string title, string subtitle, Control content)
        {
            var card = UiTheme.Card();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            int rows = string.IsNullOrWhiteSpace(subtitle) ? 2 : 3;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = rows,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var header = UiTheme.SectionHeader(title);
            header.Dock = DockStyle.Fill;
            layout.Controls.Add(header, 0, 0);
            int contentRow = 1;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                layout.Controls.Add(new Label
                {
                    Text = subtitle,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ForeColor = UiTheme.TextSecondary,
                    Margin = new Padding(0, 0, 0, UiTheme.SpaceS)
                }, 0, 1);
                contentRow = 2;
            }
            content.Dock = DockStyle.Top;
            content.Margin = new Padding(0, UiTheme.SpaceXS, 0, 0);
            layout.Controls.Add(content, 0, contentRow);
            card.Controls.Add(layout);
            return card;
        }

        private static TabPage Page(string title, string subtitle,
            params UiCard[] sections)
        {
            var page = new TabPage(title)
            {
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBg,
                UseVisualStyleBackColor = false
            };
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.WindowBg
            };
            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = sections.Length + 1,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceXL, UiTheme.SpaceL,
                    UiTheme.SpaceXL, UiTheme.SpaceXL),
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var pageHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceL)
            };
            pageHeader.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0)
            }, 0, 0);
            pageHeader.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
            }, 0, 1);
            stack.Controls.Add(pageHeader, 0, 0);
            for (int index = 0; index < sections.Length; index++)
            {
                sections[index].Dock = DockStyle.Top;
                stack.Controls.Add(sections[index], 0, index + 1);
            }
            scroll.Controls.Add(stack);
            page.Controls.Add(scroll);
            return page;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// 统一配置中心对话框（U1SET）。
    /// 页签：线段绘制 / 桥架标注 / 线管标注 / 拱桥开洞 / 统计汇总 / 文字样式 / Excel 数据。
    /// 确定时一次性写回全部配置键（Features 只读，这里统一写）。
    /// </summary>
    public sealed class UnifiedSettingsForm : DpiAwareForm
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

        // 桥架标注（规格由 U1Q1/U1Q2/U1Q4 命令固定传入，不再作为可配置项）
        private readonly NumericUpDown _unqHgt = NumberBox(180m, 1m, 100000m);
        private readonly NumericUpDown _unqLoff = NumberBox(15m, 0m, 100000m);
        private readonly NumericUpDown _unqToff = NumberBox(0m, 0m, 100000m);
        private readonly RadioButton _unqAbove = new RadioButton { Text = "上方" };
        private readonly RadioButton _unqBelow = new RadioButton { Text = "下方" };

        // 线管标注
        private readonly ComboBox _conduitDia = new ComboBox
        {
            Name = "ConduitDiameter",
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
        private readonly NumericUpDown _fillFlexibleMeters = NumberBox(1.5m, 0.1m, 100m);
        private readonly NumericUpDown _fillOwnerReserve = NumberBox(3000m, 0m, 100000m);
        private readonly NumericUpDown _fillDownstreamAmps = IntegerBox(20m, 1m, 1000m);
        private readonly NumericUpDown _fillDownstreamPanelAmps = IntegerBox(1000m, 1m, 10000m);
        // Downstream 自动添加的**独立**开关：与「BOQ 自动填充类别」里的通用类别开关解耦，
        // 这样"只开 Downstream、不开上游"才做得到。
        private readonly CheckBox _downstreamSocketEnabled = new CheckBox
        {
            Text = "socket box 时自动加一行 4.x 插座箱/盘",
            AutoSize = true
        };
        private readonly CheckBox _downstreamPanelEnabled = new CheckBox
        {
            Text = "I-line_Panel 时自动加一行电盘（不带断路器）",
            AutoSize = true
        };
        private readonly TextBox _submitFolder = new TextBox { Width = 310 };
        private readonly ComboBox _fillDeviceColor = ColorBox();
        private readonly ComboBox _fillUpstreamColor = ColorBox();
        private readonly CheckBox _autofillCable =
            new CheckBox { Text = "电缆", AutoSize = true };
        private readonly CheckBox _autofillBreaker =
            new CheckBox { Text = "断路器", AutoSize = true };
        private readonly CheckBox _autofillPanel =
            new CheckBox { Text = "电盘", AutoSize = true };
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

        // 电缆盘内/箱内预留（2.10.0 可配置）：I-Line 电盘 3400 / 插座盘 1000 / 插接箱 500。
        private readonly NumericUpDown _cableReserveILine = NumberBox(3400m, 0m, 100000m);
        private readonly NumericUpDown _cableReserveSocket = NumberBox(1000m, 0m, 100000m);
        private readonly NumericUpDown _cableReserveBusbar = NumberBox(500m, 0m, 100000m);
        // 整机回路校验：缺少任一回路时禁止生成 BOQ/DWG（默认开启）。
        private readonly UiToggle _requireAllCircuits = new UiToggle(
            "整机回路校验：该机台所有回路都在本次选集中才生成 BOQ / DWG");

        // 输出与分享
        private readonly UiToggle _outputByDrawing = new UiToggle("按图纸名建立子目录");
        private readonly UiToggle _outputSeparateKinds = new UiToggle("BOQ / DWG / PDF 分目录");
        private readonly UiBanner _shareStatus = UiKit.Banner(UiTone.Neutral, "");

        // 日志与记录
        private readonly ComboBox _logLevel = new ComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        private readonly NumericUpDown _logRetentionDays = IntegerBox(30m, 1m, 3650m);
        private readonly NumericUpDown _historyRetentionMonths = IntegerBox(6m, 1m, 120m);
        private readonly DataGridView _operationGrid = new DataGridView();
        private readonly Label _operationStatus = new Label
        {
            AutoSize = true,
            ForeColor = UiTheme.TextSecondary,
            Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
        };
        private readonly UiEmptyState _operationEmpty =
            UiKit.EmptyState("还没有操作记录",
                "执行 U1F / U1U / U1S / U1DWG 等命令后，这里会按时间倒序列出最近操作。");

        public UnifiedSettingsForm(int tabIndex)
        {
            DialogLayout.Apply(this, ProductMetadata.ProductName + " · 配置中心",
                new System.Drawing.Size(1040, 700), new System.Drawing.Size(860, 580));

            _conduitDia.Items.AddRange(new object[]
                { "20", "25", "38", "51" });
            _logLevel.Items.AddRange(new object[] { "INFO", "WARN", "ERROR" });
            _shareStatus.Visible = false;
            BuildOperationGrid();
            ConfigureStatisticsToolTips();
            LoadValues();

            // 侧边栏顺序：概览 → 绘制 → 统计/样式/启动 → 机台数据 → 清单 → Downstream → 颜色 → 输出
            _tabs.TabPages.Add(BuildOverviewTab());
            _tabs.TabPages.Add(BuildDrawTab());
            _tabs.TabPages.Add(BuildStatTab());
            _tabs.TabPages.Add(BuildStyleTab());
            _tabs.TabPages.Add(BuildStartupTab());
            _tabs.TabPages.Add(BuildMachineTab());
            _tabs.TabPages.Add(BuildBomTab());
            _tabs.TabPages.Add(BuildDownstreamTab());
            _tabs.TabPages.Add(BuildColorTab());
            _tabs.TabPages.Add(BuildOutputTab());
            _tabs.TabPages.Add(BuildLogsTab());
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
                _tabs.SelectedIndex = 1;
                _conduitDia.Focus();
                _conduitDia.SelectAll();
                MessageBox.Show(this, "默认管径无效，请输入1到1000毫米之间的数值。",
                    "U1SET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ListItem conduitItem = ListItemReader.EmbeddedCatalogIndex
                .FindRigidConduit(diameter);
            if (conduitItem == null
                || BoqFeatureName.Extract(conduitItem.Feature).Length == 0)
            {
                _tabs.SelectedIndex = 1;
                _conduitDia.Focus();
                _conduitDia.SelectAll();
                MessageBox.Show(this,
                    "固定清单中没有该线管型号。可选型号为 20、25、38、51 mm。",
                    "U1SET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _conduitDia.Text = diameter;
            // 用户只需要提供机台/设备表；固定清单随插件内嵌，不再有清单路径校验。
            string machinePath = _fillExcel.Text.Trim();
            // 2.10.0 起只支持本地/UNC 机台数据文件：网络下载已移除，
            // 路径必须是现有文件，或已经解析过且保留 SQLite 快照的同一个路径。
            if (machinePath.Length > 0
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
            SetNumber(_fillFlexibleMeters, Settings.GetDouble(
                ConfigKeys.FillFlexibleConduitMeters,
                FillPlanningOptions.DefaultFlexibleConduitMeters));
            SetNumber(_fillOwnerReserve, Settings.GetDouble(ConfigKeys.FillCableOwnerReserve,
                FillRuntimeOptions.DefaultCableOwnerReserveMillimeters));
            SetNumber(_fillDownstreamAmps, Settings.GetDouble(
                ConfigKeys.FillDownstreamSocketAmps,
                FillRuntimeOptions.DefaultDownstreamSocketAmps));
            SetNumber(_fillDownstreamPanelAmps, Settings.GetDouble(
                ConfigKeys.FillDownstreamPanelAmps,
                FillRuntimeOptions.DefaultDownstreamPanelAmps));
            _downstreamSocketEnabled.Checked = Settings.GetBool(
                ConfigKeys.FillDownstreamSocketEnabled,
                FillRuntimeOptions.DefaultDownstreamEnabled);
            _downstreamPanelEnabled.Checked = Settings.GetBool(
                ConfigKeys.FillDownstreamPanelEnabled,
                FillRuntimeOptions.DefaultDownstreamEnabled);
            _submitFolder.Text = Settings.Get(ConfigKeys.SubmitFolder, "");
            SelectColor(_fillDeviceColor, FillColorSettings.DeviceColorIndex());
            SelectColor(_fillUpstreamColor, FillColorSettings.UpstreamColorIndex());
            FillAutoFillOptions autoFill = FillAutoFillOptions.Default;
            _autofillCable.Checked = Settings.GetBool(ConfigKeys.FillAutofillCable,
                autoFill.Cable);
            _autofillBreaker.Checked = Settings.GetBool(ConfigKeys.FillAutofillBreaker,
                autoFill.Breaker);
            _autofillPanel.Checked = Settings.GetBool(ConfigKeys.FillAutofillPanel,
                autoFill.Panel);
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
            // 电缆盘内/箱内预留与整机回路校验（2.10.0）
            SetNumber(_cableReserveILine, Settings.GetDouble(ConfigKeys.CableReserveILine,
                CableReserveSettings.DefaultILinePanelMillimeters));
            SetNumber(_cableReserveSocket, Settings.GetDouble(ConfigKeys.CableReserveSocket,
                CableReserveSettings.DefaultSocketPanelMillimeters));
            SetNumber(_cableReserveBusbar, Settings.GetDouble(ConfigKeys.CableReserveBusbar,
                CableReserveSettings.DefaultBusbarConnectorMillimeters));
            _requireAllCircuits.Checked = Settings.GetBool(
                ConfigKeys.FillRequireAllCircuits, true);
            // 输出结构开关（2.10.0）
            _outputByDrawing.Checked = Settings.GetBool(ConfigKeys.OutputByDrawing, true);
            _outputSeparateKinds.Checked = Settings.GetBool(ConfigKeys.OutputSeparateKinds, true);
            // 日志与操作记录（2.10.0）
            SelectText(_logLevel, Settings.Get(ConfigKeys.LogLevel,
                FileLogger.DefaultLevel), "INFO");
            SetNumber(_logRetentionDays, Settings.GetDouble(ConfigKeys.LogRetentionDays,
                FileLogger.DefaultRetentionDays));
            SetNumber(_historyRetentionMonths, Settings.GetDouble(
                ConfigKeys.HistoryRetentionMonths, OperationLog.DefaultRetentionMonths));
            RefreshOperationLog();
        }

        /// <summary>下拉框按文本选中；不受当前区域设置影响，找不到时回退。</summary>
        private static void SelectText(ComboBox box, string value, string fallback)
        {
            string wanted = (value ?? "").Trim();
            foreach (object item in box.Items)
            {
                if (!string.Equals(Convert.ToString(item), wanted,
                    StringComparison.OrdinalIgnoreCase)) continue;
                box.SelectedItem = item;
                return;
            }
            foreach (object item in box.Items)
            {
                if (!string.Equals(Convert.ToString(item), fallback,
                    StringComparison.OrdinalIgnoreCase)) continue;
                box.SelectedItem = item;
                return;
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        // ---------- 保存（含数据校验） ----------

        public void Save()
        {
            // 线段
            Settings.Set(ConfigKeys.UnlText, _unlText.Text.Trim());
            Settings.Set(ConfigKeys.UnlHeight, Value(_unlHgt));
            Settings.Set(ConfigKeys.UnlPos, _unlCenter.Checked ? "0" : _unlBelow.Checked ? "1" : "2");
            Settings.Set(ConfigKeys.UnlOffset, Value(_unlOff));

            // 桥架（偏移允许 0=自动/贴线）；规格由 U1Q1/U1Q2/U1Q4 固定传入，不写配置。
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
            Settings.Set(ConfigKeys.FillFlexibleConduitMeters, Value(_fillFlexibleMeters));
            Settings.Set(ConfigKeys.FillCableOwnerReserve, Value(_fillOwnerReserve));
            Settings.Set(ConfigKeys.FillDownstreamSocketAmps,
                ((int)_fillDownstreamAmps.Value).ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.FillDownstreamPanelAmps,
                ((int)_fillDownstreamPanelAmps.Value).ToString(CultureInfo.InvariantCulture));
            Settings.SetBool(ConfigKeys.FillDownstreamSocketEnabled,
                _downstreamSocketEnabled.Checked);
            Settings.SetBool(ConfigKeys.FillDownstreamPanelEnabled,
                _downstreamPanelEnabled.Checked);
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
            Settings.SetBool(ConfigKeys.FillAutofillPanel, _autofillPanel.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillFlexibleConduit,
                _autofillFlexibleConduit.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillRigidConduit,
                _autofillRigidConduit.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillBridge, _autofillBridge.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillOutletPanel,
                _autofillOutletPanel.Checked);
            Settings.SetBool(ConfigKeys.FillAutofillBusPlugBox,
                _autofillBusPlugBox.Checked);

            // 电缆盘内/箱内预留与整机回路校验
            Settings.Set(ConfigKeys.CableReserveILine, Value(_cableReserveILine));
            Settings.Set(ConfigKeys.CableReserveSocket, Value(_cableReserveSocket));
            Settings.Set(ConfigKeys.CableReserveBusbar, Value(_cableReserveBusbar));
            Settings.SetBool(ConfigKeys.FillRequireAllCircuits, _requireAllCircuits.Checked);

            // 输出结构与日志/操作记录
            Settings.SetBool(ConfigKeys.OutputByDrawing, _outputByDrawing.Checked);
            Settings.SetBool(ConfigKeys.OutputSeparateKinds, _outputSeparateKinds.Checked);
            Settings.Set(ConfigKeys.LogLevel, SelectedLogLevel());
            Settings.Set(ConfigKeys.LogRetentionDays,
                ((int)_logRetentionDays.Value).ToString(CultureInfo.InvariantCulture));
            Settings.Set(ConfigKeys.HistoryRetentionMonths,
                ((int)_historyRetentionMonths.Value).ToString(CultureInfo.InvariantCulture));
        }

        private string SelectedLogLevel()
        {
            string value = Convert.ToString(_logLevel.SelectedItem);
            return string.Equals(value, "WARN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "ERROR", StringComparison.OrdinalIgnoreCase)
                ? value.ToUpperInvariant() : "INFO";
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
            _toolTips.SetToolTip(_fillDownstreamAmps, "下游 Downstream 块状态为 socket box 时，"
                + "额外那一行 4.x 插座盘使用的额定电流（默认 20A；与 U_配电信息里的盘柜电流无关）。");
            _toolTips.SetToolTip(_fillDownstreamPanelAmps, "下游 Downstream 块状态为 I-line_Panel 时，"
                + "额外那一行电盘使用的额定电流（默认 1000A）。这一行不会带断路器。");
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

        /// <summary>概览：一眼看清版本、数据源与 Downstream 规则，再按需进入各页。</summary>
        private TabPage BuildOverviewTab()
        {
            var version = Grid(3);
            AddField(version, 0, "产品", ReadOnly(ProductMetadata.ProductName));
            AddField(version, 1, "版本", ReadOnly(ProductMetadata.VersionLabel
                + "（" + ProductMetadata.ReleaseDateUtc + "）"));
            AddField(version, 2, "机台数据", ReadOnly("只需提供机台/设备表；固定清单随插件内嵌"));

            var rules = Grid(3);
            AddField(rules, 0, "socket box", ReadOnly("额外一行 4.x 插座箱/盘（受“插座盘”开关控制）"));
            AddField(rules, 1, "I-line_Panel", ReadOnly("额外一行电盘（受“电盘”开关控制，不带断路器）"));
            AddField(rules, 2, "busbar connector", ReadOnly("只上色，不产生物料"));

            var hint = Grid(2);
            AddField(hint, 0, "参数在哪", ReadOnly("左侧按“绘制 / 清单 / 输出”分组，逐页调整"));
            AddField(hint, 1, "设置何时生效", ReadOnly("保存后立即生效；命令运行时不读 Excel、不联网"));

            return Page("概览", "版本、数据源与关键规则",
                Section("版本", null, version),
                Section("下游 Downstream 规则", "块名精确等于 Downstream（含 $0$ 后缀）时按可见性状态自动补清单。", rules),
                Section("提示", null, hint));
        }

        /// <summary>绘制标注：U1L 线段、U1Q 桥架、U1R 拱桥、U1C 线管合并为一页四块。</summary>
        private TabPage BuildDrawTab()
        {
            var unl = Grid(4);
            AddField(unl, 0, "占位文字", _unlText);
            AddField(unl, 1, "文字高度", _unlHgt);
            var posPanel = new FlowLayoutPanel { AutoSize = true };
            posPanel.Controls.Add(_unlCenter);
            posPanel.Controls.Add(_unlBelow);
            posPanel.Controls.Add(_unlAbove);
            AddField(unl, 2, "文字位置", posPanel);
            AddField(unl, 3, "偏移距离（0 = 贴线）", _unlOff);

            var unq = Grid(4);
            AddField(unq, 0, "文字高度", _unqHgt);
            AddField(unq, 1, "红线距基线（0 = 15）", _unqLoff);
            AddField(unq, 2, "文字距红线（0 = 贴线）", _unqToff);
            var sidePanel = new FlowLayoutPanel { AutoSize = true };
            sidePanel.Controls.Add(_unqAbove);
            sidePanel.Controls.Add(_unqBelow);
            AddField(unq, 3, "标注侧", sidePanel);

            var unr = Grid(1);
            AddField(unr, 0, "拱桥直径", _unrDia);

            var conduit = Grid(1);
            AddField(conduit, 0, "默认管径", _conduitDia);

            return Page("绘制标注", "U1L / U1Q / U1C / U1R 的默认标注参数",
                Section("线段文字（U1L）", null, unl),
                Section("桥架文字与位置（U1Q）",
                    "规格由 U1Q1 / U1Q2 / U1Q4 分别固定为 100 / 200 / 400 桥架型号，"
                    + "本页只调整文字高度、偏移和标注侧。", unq),
                Section("线管规格（U1C）", "文字高度、偏移距离和标注侧与桥架标注共用。", conduit),
                Section("拱桥开洞（U1R）", null, unr));
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

            return Page("文字样式", "所有生成文字的公共样式",
                Section("文字样式", "样式名与字体对所有生成文字生效；空字体表示使用当前样式。", style));
        }

        /// <summary>界面与启动：启动表现 + 版本信息（原来混在“文字样式”页里）。</summary>
        private TabPage BuildStartupTab()
        {
            var startup = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0)
            };
            startup.Controls.Add(_splashEnabled);

            var about = Grid(2);
            AddField(about, 0, "产品", ReadOnly(ProductMetadata.ProductName));
            AddField(about, 1, "版本", ReadOnly(ProductMetadata.VersionLabel
                + "（" + ProductMetadata.ReleaseDateUtc + "）"));

            return Page("界面与启动", "启动表现与版本信息",
                Section("启动", "打开 CAD 时是否播放全屏品牌动画。", startup),
                Section("关于", null, about));
        }

        /// <summary>机台数据：本地/UNC 数据源与快照状态（2.10.0 起不再支持 HTTP 下载）。</summary>
        private TabPage BuildMachineTab()
        {
            var source = Grid(3);
            // 机台数据 Excel 是用户唯一需要提供的文件；固定清单内嵌在插件里。
            AddField(source, 0, "机台数据 Excel", PathPicker(_fillExcel));
            AddField(source, 1, "快照状态", _snapshotStatus);
            AddField(source, 2, "说明", ReadOnly(
                "只支持本地或 UNC 共享路径；点“刷新”后写入 SQLite 快照，业务命令不再读 Excel。"));

            return Page("机台数据", "命令只读快照：运行时不读 Excel、不联网",
                Section("数据源", "本地文件手动刷新；2.10.0 起已移除网络数据源。", source));
        }

        /// <summary>清单与表格：写表参数 + 电缆预留 + 整机回路校验 + BOQ 自动填充类别。</summary>
        private TabPage BuildBomTab()
        {
            var table = Grid(10);
            AddField(table, 0, "起始数据行（1 = No.1）", _fillTblRow);
            AddField(table, 1, "每次清空数据行数", _fillClearRows);
            AddField(table, 2, "表格文字高度", _fillTextHeight);
            AddField(table, 3, "软管手动数量（m）", _fillFlexibleMeters);
            AddField(table, 5, "电缆业主预留（mm）", _fillOwnerReserve);
            // 电缆盘内/箱内预留：现场默认值来自 CableReserveSettings，随项目可调。
            AddField(table, 6, "I-Line 盘内预留（mm）", _cableReserveILine);
            AddField(table, 7, "插座盘内预留（mm）", _cableReserveSocket);
            AddField(table, 8, "母线插接箱预留（mm）", _cableReserveBusbar);
            AddField(table, 9, "整机回路校验", _requireAllCircuits);

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
            autoFill.Controls.Add(_autofillPanel, 2, 0);
            autoFill.Controls.Add(_autofillFlexibleConduit, 3, 0);
            autoFill.Controls.Add(_autofillRigidConduit, 0, 1);
            autoFill.Controls.Add(_autofillBridge, 1, 1);
            autoFill.Controls.Add(_autofillOutletPanel, 2, 1);
            autoFill.Controls.Add(_autofillBusPlugBox, 3, 1);

            return Page("清单与表格", "U1F / U1U 写表参数、电缆预留与自动填充类别",
                Section("表格参数与电缆预留",
                    "电缆总长 = 实测 + 盘内/箱内预留（I-Line 电盘 / 插座盘 / 母线插接箱）"
                    + " + 设备内预留 + 业主预留；整机回路校验要求该机台所有回路都在本次选集中。",
                    table),
                Section("BOQ 自动填充类别",
                    "这里控制的是上游/通用类别；Downstream 块的自动添加有自己的独立开关"
                    + "（见「Downstream 自动添加」页），两者互不影响。",
                    autoFill));
        }

        /// <summary>Downstream 自动添加：单独一页，只放下游端相关设置与规则。</summary>
        private TabPage BuildDownstreamTab()
        {
            var amps = Grid(2);
            AddField(amps, 0, "插座箱额定电流（A）", _fillDownstreamAmps);
            AddField(amps, 1, "电盘额定电流（A）", _fillDownstreamPanelAmps);

            var rules = Grid(3);
            AddField(rules, 0, "socket box", ReadOnly("额外一行 4.x 插座箱/盘（受“插座盘”开关控制）"));
            AddField(rules, 1, "I-line_Panel", ReadOnly("额外一行电盘（受“电盘”开关控制，不带断路器）"));
            AddField(rules, 2, "busbar connector", ReadOnly("只上色，不产生物料"));

            var switches = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            switches.Controls.Add(_downstreamSocketEnabled);
            switches.Controls.Add(_downstreamPanelEnabled);

            return Page("Downstream 自动添加", "下游端 Downstream 块（精确块名）按可见性状态自动补清单",
                Section("自动添加开关（独立）",
                    "这两项只控制 Downstream 块自己的自动添加，与「清单与表格」里的通用类别开关"
                    + "互不影响：通用开关只管上游规则。所以可以做到“只开 Downstream、不开上游”。",
                    switches),
                Section("额定电流", "不从 U_配电信息 提取：那是盘柜主开关电流，会导致整行未匹配。", amps),
                Section("三态规则", "与 upstream 共用同一套可见性状态。", rules));
        }

        /// <summary>颜色：设备端（含 Downstream）与上游端。</summary>
        private TabPage BuildColorTab()
        {
            var g = Grid(2);
            AddField(g, 0, "设备端颜色", _fillDeviceColor);
            AddField(g, 1, "上游端颜色", _fillUpstreamColor);
            return Page("颜色", "块的标注颜色（ACI 色号）",
                Section("块颜色", "设备端包含 Downstream 块；修改后对所有后续命令生效。", g));
        }

        /// <summary>输出与分享：落盘结构（按图纸/分类型）+ 配置分享码导入导出。</summary>
        private TabPage BuildOutputTab()
        {
            var output = Grid(4);
            AddField(output, 0, "输出根目录", OutputPicker(_submitFolder));
            AddField(output, 1, "目录结构", ReadOnly(
                "默认：<根目录>\\<图纸>\\BOQ|DWG|PDF\\<机台>\\…；根目录留空使用 "
                + DefaultOutputRoot()));
            AddField(output, 2, "按图纸建子目录", _outputByDrawing);
            AddField(output, 3, "BOQ / DWG / PDF 分目录", _outputSeparateKinds);

            var shareActions = UiKit.Toolbar();
            Button copy = UiTheme.Button("复制配置到剪贴板");
            copy.Click += (sender, args) => CopyConfigToClipboard();
            Button paste = UiTheme.Button("从剪贴板导入");
            paste.Click += (sender, args) => ImportConfigFromClipboard();
            shareActions.AddAction(copy).AddAction(paste);

            return Page("输出与分享", "生成文件的落盘结构，以及跨设备迁移 U1SET 配置",
                Section("输出目录",
                    "BOQ / DWG / PDF 统一输出到根目录下；两个开关分别控制“按图纸建一级子目录”"
                    + "和“按 BOQ / DWG / PDF 分目录”。", output),
                Section("配置分享",
                    "把本机 U1SET 配置导出为 Base64 分享码，或在另一台电脑粘贴导入。"
                    + "分享码不包含授权账号、时钟水位线和界面记忆。",
                    Stack(_shareStatus, shareActions)));
        }

        /// <summary>日志与记录：级别/保留策略，以及最近操作记录。</summary>
        private TabPage BuildLogsTab()
        {
            var logs = Grid(3);
            AddField(logs, 0, "日志级别", _logLevel);
            AddField(logs, 1, "日志保留天数", _logRetentionDays);
            AddField(logs, 2, "操作记录保留月数", _historyRetentionMonths);

            var logActions = UiKit.Toolbar();
            Button openLogs = UiTheme.Button("打开日志目录");
            openLogs.Click += (sender, args) => OpenFolder(LogDirectory());
            Button openHistory = UiTheme.Button("打开操作记录目录");
            openHistory.Click += (sender, args) => OperationLog.OpenFolder();
            Button refresh = UiTheme.Button("刷新记录");
            refresh.Click += (sender, args) => RefreshOperationLog();
            logActions.AddAction(openLogs).AddAction(openHistory).AddAction(refresh);

            return Page("日志与记录", "本地排障：日志级别与保留策略，以及最近的操作记录",
                Section("日志策略",
                    "日志文件位于 %APPDATA%\\UNCAD\\logs；级别越高写入越少。"
                    + "日志与操作记录只写本机，不会上传。", logs),
                Section("最近操作记录",
                    "每次 U1F / U1U / U1S / U1DWG 等操作都会记一条；最多显示最近 200 条。",
                    Stack(logActions, _operationStatus, _operationEmpty, _operationGrid)));
        }

        /// <summary>默认输出根目录文案（Core 与 Features 使用同一定义）。</summary>
        private static string DefaultOutputRoot()
        {
            try { return OutputLayout.DefaultRootDirectory(); }
            catch (Exception) { return "我的文档\\UNCAD输出"; }
        }

        private Control OutputPicker(TextBox target)
        {
            var browse = UiTheme.Button("浏览...");
            browse.Click += (sender, args) =>
            {
                using (var dialog = new FolderBrowserDialog
                {
                    Description = "选择输出根目录",
                    ShowNewFolderButton = true,
                    SelectedPath = Directory.Exists(target.Text.Trim()) ? target.Text.Trim() : ""
                })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
                }
            };
            var open = UiTheme.Button("打开目录");
            open.Click += (sender, args) => OpenFolder(CurrentOutputRoot());
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.Controls.Add(target, 0, 0);
            row.Controls.Add(browse, 1, 0);
            row.Controls.Add(open, 2, 0);
            return row;
        }

        private string CurrentOutputRoot()
        {
            string configured = _submitFolder.Text.Trim();
            if (configured.Length > 0) return configured;
            try { return OutputLayout.DefaultRootDirectory(); }
            catch (Exception) { return ""; }
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn("打开目录失败: " + ex.Message);
                ShowShareStatus(UiTone.Warning, "无法打开目录：" + ex.Message);
            }
        }

        private void ShowShareStatus(UiTone tone, string message)
        {
            _shareStatus.Tone = tone;
            _shareStatus.Text = message ?? "";
            _shareStatus.Visible = !string.IsNullOrWhiteSpace(message);
        }

        /// <summary>导出本机配置为分享码并复制到剪贴板；返回的文案同时用于提示。</summary>
        private void CopyConfigToClipboard()
        {
            try
            {
                string code = ConfigShareService.Export();
                if (string.IsNullOrWhiteSpace(code))
                {
                    ShowShareStatus(UiTone.Warning, "本机没有可导出的配置项。");
                    return;
                }
                Clipboard.SetText(code);
                ShowShareStatus(UiTone.Success, "已复制配置分享码（" + code.Length
                    + " 字符）。在另一台电脑的 U1SET 中粘贴即可导入。");
            }
            catch (Exception ex)
            {
                Log.Error("导出配置分享码失败", ex);
                ShowShareStatus(UiTone.Danger, "导出失败：" + ex.Message);
            }
        }

        /// <summary>从剪贴板读取分享码 → 预览变更 → 用户确认后原子导入。</summary>
        private void ImportConfigFromClipboard()
        {
            string code;
            try
            {
                code = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            }
            catch (Exception ex)
            {
                Log.Warn("读取剪贴板失败: " + ex.Message);
                ShowShareStatus(UiTone.Warning, "无法读取剪贴板：" + ex.Message);
                return;
            }
            if (code.Length == 0)
            {
                ShowShareStatus(UiTone.Warning,
                    "剪贴板里没有配置分享码。请先在源电脑导出并复制。");
                return;
            }
            ConfigSharePreview preview;
            try
            {
                preview = ConfigShareService.Preview(code);
            }
            catch (Exception ex)
            {
                Log.Warn("配置分享码解析失败: " + ex.Message);
                ShowShareStatus(UiTone.Danger, "分享码无效：" + ex.Message);
                return;
            }
            using (var dialog = new ConfigSharePreviewForm(preview))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            try
            {
                ConfigShareImportResult result = ConfigShareService.Import(code);
                ShowShareStatus(UiTone.Success, "已导入 " + result.AppliedCount
                    + " 项配置"
                    + (result.SkippedCount > 0
                        ? "，跳过 " + result.SkippedCount + " 项（未知或禁止分享的键）。"
                        : "。"));
                // 导入已经写入注册表；重新载入界面值，避免用户随后按“保存”用旧值覆盖。
                LoadValues();
            }
            catch (Exception ex)
            {
                Log.Error("导入配置分享码失败", ex);
                ShowShareStatus(UiTone.Danger, "导入失败：" + ex.Message);
            }
        }

        private static string LogDirectory()
        {
            string directory = FileLogger.Instance.LogDirectory;
            return string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UNCAD", "logs")
                : directory;
        }

        private void BuildOperationGrid()
        {
            _operationGrid.Name = "OperationLogGrid";
            _operationGrid.Dock = DockStyle.Top;
            _operationGrid.Height = 220;
            _operationGrid.ReadOnly = true;
            _operationGrid.AllowUserToAddRows = false;
            _operationGrid.AllowUserToDeleteRows = false;
            _operationGrid.AutoGenerateColumns = false;
            _operationGrid.RowHeadersVisible = false;
            _operationGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _operationGrid.MultiSelect = false;
            _operationGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Time",
                HeaderText = "时间",
                Width = UiTheme.NotAutoScaled(140),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _operationGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Command",
                HeaderText = "命令",
                Width = UiTheme.NotAutoScaled(90),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _operationGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Result",
                HeaderText = "结果",
                Width = UiTheme.NotAutoScaled(84),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _operationGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Summary",
                HeaderText = "摘要",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = UiTheme.NotAutoScaled(220),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _operationGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Duration",
                HeaderText = "耗时(ms)",
                Width = UiTheme.NotAutoScaled(84),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            });
            UiTheme.StyleGrid(_operationGrid);
            UiTheme.FixGridRows(_operationGrid, 28);
        }

        /// <summary>读取最近操作记录（本地 JSONL，最多 200 条）。</summary>
        private void RefreshOperationLog()
        {
            try
            {
                IReadOnlyList<OperationLogEntry> entries = OperationLog.ReadRecent(200);
                _operationGrid.Rows.Clear();
                foreach (OperationLogEntry entry in entries)
                {
                    int index = _operationGrid.Rows.Add(
                        entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture),
                        entry.Command,
                        entry.Result,
                        entry.Summary,
                        entry.DurationMs);
                    _operationGrid.Rows[index].Tag = entry;
                }
                UiTheme.FixGridRows(_operationGrid, 28);
                bool hasEntries = entries.Count > 0;
                _operationGrid.Visible = hasEntries;
                _operationEmpty.Visible = !hasEntries;
                _operationStatus.Text = hasEntries
                    ? "最近 " + entries.Count + " 条 · 目录 " + OperationLog.Directory
                    : "暂无操作记录。";
                _operationStatus.ForeColor = UiTheme.TextSecondary;
            }
            catch (Exception ex)
            {
                Log.Warn("读取操作记录失败: " + ex.Message);
                _operationStatus.Text = "读取操作记录失败：" + ex.Message;
                _operationStatus.ForeColor = UiTheme.DangerFg;
            }
        }

        /// <summary>纵向堆叠多个控件（页内分组内容）。</summary>
        private static Control Stack(params Control[] children)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = children.Length,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < children.Length; index++)
            {
                children[index].Dock = DockStyle.Top;
                children[index].Margin = new Padding(0, 0, 0, UiTheme.SpaceS);
                layout.Controls.Add(children[index], 0, index);
            }
            return layout;
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
            if (!File.Exists(source))
            {
                MessageBox.Show(this, "机台数据文件不存在：" + source, "U1SET",
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
                    // 2.10.0 起这里只刷新本地/UNC 文件；网络下载与台账上报已移除，
                // 失败时保留上一次成功快照，不阻塞用户继续使用。
                MachineWorkbookSourceResult result = null;
                await Task.Run(() => { result = MachineWorkbookSource.Refresh(source); });
                string message = result.UsedCachedFallback
                    ? "继续使用上次 SQLite 快照。" : "机台 Excel 已解析并写入 SQLite 快照。";
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
            // 标签列自适应：中文标签（如“下游插座箱额定电流（A）”）比固定 190px 宽，
            // 固定宽度会被 EndEllipsis 裁掉，看起来就是“布局不对”。
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < rows; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            return layout;
        }

        /// <summary>只读值标签（概览/规则说明用）。</summary>
        private static Label ReadOnly(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.TextPrimary,
            Margin = new Padding(0, 10, 0, 0)
        };

        private static UiCard Section(string title, string subtitle, Control content)
            => UiKit.Section(title, subtitle).AddBodyTop(content);

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
            stack.Controls.Add(UiKit.PageHeader(title, subtitle), 0, 0);
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using UNCAD.Core.Text;

namespace UNCAD.UI
{
    /// <summary>批量 U1U 待填行：一个还没有设备内预留记录的图框。</summary>
    public sealed class DeviceReserveRow
    {
        public DeviceReserveRow(string machineId, string deviceName)
        {
            MachineId = machineId ?? "";
            DeviceName = deviceName ?? "";
            ReserveMm = "0";
        }

        public string MachineId { get; }

        public string DeviceName { get; }

        /// <summary>界面上的毫米输入；确认时由窗体验证并解析。</summary>
        public string ReserveMm { get; set; }
    }

    /// <summary>
    /// 批量 U1U 首次使用：逐图框输入设备内预留（毫米）。不同设备的预留通常
    /// 不同，因此一行一框；顶部“统一值”只是快捷填充，确认仍按行取值。
    /// 只应用于本次还没有设备内预留记录的图框；已有记录保持原值、不进列表。
    /// </summary>
    public sealed class DeviceReserveForm : DpiAwareForm
    {
        /// <summary>与现场一致的上限：设备内预留最大 100000 毫米。</summary>
        internal const double MaxReserveMillimeters = 100000d;

        private const string ReserveHeader = "设备内预留 (mm)";
        private readonly DataGridView _grid;
        private readonly TextBox _uniform;
        private readonly UiFieldRow _uniformRow;
        private readonly List<DeviceReserveRow> _rows;

        public DeviceReserveForm(IReadOnlyList<DeviceReserveRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            _rows = rows.ToList();

            int gridHeight = Math.Min(Math.Max(_rows.Count, 1) * 36 + 4, 340);
            DialogLayout.Apply(this, "U1U 设备内预留",
                new Size(880, gridHeight + 330), new Size(720, 380), false);

            var frame = new UiDialogFrame(this, "设备内预留",
                "有 " + _rows.Count + " 个图框还没有设备内预留记录。逐行填写，"
                + "或先统一填入再单独调整；确认后写入 frameinfo_json，后续 U1U 不再询问。");

            // 统一值快捷行：整批同一个预留时一次填完，特殊图框再逐行改。
            _uniform = new TextBox { Dock = DockStyle.Fill };
            UiTheme.StyleInput(_uniform);
            _uniform.KeyDown += UniformKeyDown;
            Button applyAll = UiTheme.Button("应用到全部");
            applyAll.Click += ApplyAll;
            var uniformEditor = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            uniformEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            uniformEditor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            uniformEditor.Controls.Add(_uniform, 0, 0);
            uniformEditor.Controls.Add(applyAll, 1, 0);
            _uniformRow = UiKit.Field("统一值 (mm)", uniformEditor,
                "应用到全部行后仍可逐行修改；留空按 0。");

            _grid = new DataGridView
            {
                Name = "DeviceReserveGrid",
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            UiKit.StyleTable(_grid, readOnly: false);
            _grid.Columns.Add(ReadColumn("序", "#", 7));
            _grid.Columns.Add(ReadColumn("机台 ID",
                nameof(DeviceReserveRow.MachineId), 24));
            _grid.Columns.Add(ReadColumn("设备 / 回路",
                nameof(DeviceReserveRow.DeviceName), 39));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ReserveHeader,
                HeaderText = ReserveHeader,
                DataPropertyName = nameof(DeviceReserveRow.ReserveMm),
                FillWeight = 30,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    BackColor = UiTheme.SurfaceAlt
                }
            });
            _grid.CellValidating += ValidateReserveCell;
            _grid.DataSource = _rows;
            UiTheme.FixGridRows(_grid, 36);

            var gridHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = gridHeight,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
            };
            gridHost.Controls.Add(_grid);
            var section = UiKit.Section("图框设备内预留", null)
                .AddBodyTop(_uniformRow)
                .AddBodyTop(gridHost);
            frame.Body.Controls.Add(section);

            Button confirm = frame.AddPrimary("确定");
            Button cancel = frame.AddSecondary("取消", DialogResult.Cancel);
            confirm.Click += Confirm;
            frame.Wire(confirm, cancel);
            AcceptButton = confirm;
            CancelButton = cancel;
            Shown += (sender, args) => _uniform.Select();
        }

        /// <summary>第 index 行（与构造顺序一致）确认后的设备内预留（毫米）。</summary>
        public double ReserveMillimeters(int index)
        {
            return Math.Max(0d, Math.Min(MaxReserveMillimeters,
                ParseReserve((_rows[index].ReserveMm ?? "").Trim())));
        }

        internal int ReserveColumnIndex
            => _grid.Columns[ReserveHeader].Index;

        private static DataGridViewTextBoxColumn ReadColumn(string header,
            string property, float weight)
            => new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                FillWeight = weight,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = UiTheme.SurfaceAlt,
                    SelectionBackColor = UiTheme.SurfaceAlt,
                    SelectionForeColor = UiTheme.TextPrimary
                }
            };

        internal static string ReserveError(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!(double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value)
                || double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out value))
                || value < 0d || value > MaxReserveMillimeters)
                return "必须是 0~100000 的毫米数。";
            return null;
        }

        private static double ParseReserve(string text)
        {
            if (text.Length == 0) return 0d;
            if (double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value)
                || double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out value))
                return value;
            return 0d;
        }

        private void UniformKeyDown(object sender, KeyEventArgs e)
        {
            // 统一值框里按回车 = 应用到全部，而不是直接确认整窗。
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplyAll(_uniform, EventArgs.Empty);
                _grid.Focus();
            }
        }

        private void ApplyAll(object sender, EventArgs e)
        {
            string text = (_uniform.Text ?? "").Trim();
            string error = ReserveError(text);
            if (error != null)
            {
                _uniformRow.SetError("统一值" + error);
                _uniform.Focus();
                _uniform.SelectAll();
                return;
            }
            _uniformRow.SetError(null);
            string formatted = TextFormatter.FormatNum(ParseReserve(text));
            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.ErrorText = null;
                row.Cells[ReserveHeader].Value = formatted;
            }
        }

        private void ValidateReserveCell(object sender,
            DataGridViewCellValidatingEventArgs e)
        {
            // 只做即时反馈（红字），不拦提交：某些提交路径不会触发本事件，
            // 真正的权威校验在 Confirm 里按单元格最终可见值逐行执行。
            if (e.ColumnIndex != ReserveColumnIndex) return;
            string text = (e.FormattedValue ?? "").ToString().Trim();
            _grid.Rows[e.RowIndex].ErrorText = ReserveError(text);
            _rows[e.RowIndex].ReserveMm = text;
        }

        private void Confirm(object sender, EventArgs e)
        {
            // 结束悬挂编辑后，以表格单元格里的最终可见值为准做整窗校验；
            // 任一行非法就定位到那一行并留在窗口，绝不带着坏值关闭。
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                string text = (_grid.Rows[i].Cells[ReserveColumnIndex].Value
                    ?? "").ToString().Trim();
                string error = ReserveError(text);
                _grid.Rows[i].ErrorText = error;
                _rows[i].ReserveMm = text;
                if (error == null) continue;
                _grid.ClearSelection();
                _grid.CurrentCell = _grid.Rows[i].Cells[ReserveColumnIndex];
                _grid.BeginEdit(true);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

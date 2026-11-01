using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// UI 组件库（UiKit）契约：组件必须是可测试的 WinForms 控件，并保持既有测试
    /// 依赖的类型（例如开关必须仍是 CheckBox、卡片必须仍是 UiCard）。
    /// </summary>
    public class UiKitTests
    {
        [Fact]
        public void Toggle_DerivesFromCheckBoxAndKeepsCheckedState()
        {
            RunSta(() =>
            {
                using (var host = new Form())
                {
                    UiToggle toggle = UiKit.Toggle("整机回路校验");
                    host.Controls.Add(toggle);
                    host.Show();
                    Application.DoEvents();

                    Assert.IsAssignableFrom<CheckBox>(toggle);
                    Assert.True(toggle.AutoSize);
                    Assert.True(toggle.Width > 0 && toggle.Height > 0);
                    toggle.Checked = true;
                    Assert.True(toggle.Checked);
                    Assert.Equal("整机回路校验", toggle.Text);
                }
            });
        }

        [Fact]
        public void SectionCard_IsAUiCardAndStacksBodyRows()
        {
            RunSta(() =>
            {
                using (var host = new Form())
                {
                    UiSectionCard card = UiKit.Section("输出目录", "统一输出结构")
                        .AddBodyTop(new Label { Text = "根目录", AutoSize = true })
                        .AddBodyTop(new Label { Text = "分目录", AutoSize = true });
                    host.Controls.Add(card);
                    host.Show();
                    Application.DoEvents();

                    Assert.IsAssignableFrom<UiCard>(card);
                    Assert.Equal(UiTheme.RadiusCard, card.CornerRadius);
                    Assert.Equal(2, card.Body.RowCount);
                    Assert.True(card.Width > 0 && card.Height > 0);
                }
            });
        }

        [Fact]
        public void FieldRow_ShowsInlineErrorWithoutClearingInput()
        {
            RunSta(() =>
            {
                using (var host = new Form())
                {
                    var editor = new TextBox { Text = "3400" };
                    UiFieldRow row = UiKit.Field("盘内预留", editor, "毫米");
                    host.Controls.Add(row);
                    host.Show();
                    Application.DoEvents();

                    Assert.Same(editor, row.Editor);
                    Assert.Equal("毫米", row.HintLabel.Text);
                    row.SetError("必须是非负整数");
                    Application.DoEvents();
                    Assert.Equal(UiTheme.DangerFg, row.HintLabel.ForeColor);
                    Assert.Equal("3400", editor.Text);
                    row.ClearError();
                    Assert.False(row.HintLabel.Visible);
                }
            });
        }

        [Fact]
        public void BannerAndStatusPill_UseSemanticToneColors()
        {
            RunSta(() =>
            {
                using (var host = new Form())
                {
                    UiBanner banner = UiKit.Banner(UiTone.Warning, "找不到对应型号");
                    UiStatusPill pill = UiKit.StatusPill("缺少 3 个回路", UiTone.Danger);
                    host.Controls.Add(banner);
                    host.Controls.Add(pill);
                    host.Show();
                    Application.DoEvents();

                    Assert.Equal("找不到对应型号", banner.Text);
                    Assert.Equal(UiTheme.WarningBg, banner.BackColor);
                    Assert.Equal(UiTheme.WarningFg, banner.TextLabel.ForeColor);
                    Assert.True(pill.Width > 0 && pill.Height > 0);
                    Assert.Equal(UiTheme.DangerFg, UiTonePalette.Foreground(UiTone.Danger));
                }
            });
        }

        [Fact]
        public void Progress_ClampsValueIntoRange()
        {
            RunSta(() =>
            {
                using (var progress = UiKit.Progress())
                {
                    progress.Minimum = 0;
                    progress.Maximum = 100;
                    progress.Value = 150;
                    Assert.Equal(100, progress.Value);
                    progress.Value = -5;
                    Assert.Equal(0, progress.Value);
                }
            });
        }

        [Fact]
        public void DialogFrame_AddsHeaderBodyFooterAndOrderedButtons()
        {
            RunSta(() =>
            {
                using (var form = new Form())
                {
                    DialogLayout.Apply(form, "测试", new Size(600, 400), new Size(400, 300));
                    var frame = new UiDialogFrame(form, "命令帮助", "使用方法与注意事项");
                    Button confirm = frame.AddPrimary("确认");
                    Button cancel = frame.AddSecondary("取消", DialogResult.Cancel);
                    frame.Wire(confirm, cancel);
                    form.Show();
                    Application.DoEvents();

                    Assert.Contains(frame.Header, form.Controls.Cast<Control>());
                    Assert.Contains(frame.Body, form.Controls.Cast<Control>());
                    Assert.Contains(frame.Footer, form.Controls.Cast<Control>());
                    // FlowDirection.RightToLeft：先加入的“确认”在最右，“取消”在其左侧。
                    Assert.Equal(new Control[] { confirm, cancel }, frame.Footer.Controls.Cast<Control>());
                    Assert.Same(confirm, form.AcceptButton);
                    Assert.Same(cancel, form.CancelButton);
                    Assert.True(frame.Body.Height > 0);
                }
            });
        }

        [Fact]
        public void ConfigSharePreviewForm_ShowsChangesAndKeepsCancelAvailable()
        {
            RunSta(() =>
            {
                var items = new Dictionary<string, string>
                {
                    { "UNC_UNL_HEIGHT", "220" }
                };
                var changes = new List<ConfigShareChange>
                {
                    new ConfigShareChange("UNC_UNL_HEIGHT", "180", "220")
                };
                var preview = new ConfigSharePreview("UNCAD Pro", "2.10.0", 1,
                    DateTimeOffset.Now, items, changes, Array.Empty<string>());
                using (var form = new ConfigSharePreviewForm(preview))
                {
                    form.Show();
                    Application.DoEvents();
                    List<Button> buttons = FindAll<Button>(form);
                    Assert.Contains(buttons, button => button.Text == "取消");
                    Assert.Contains(buttons, button => button.Text.StartsWith("导入"));
                    DataGridView grid = FindAll<DataGridView>(form)
                        .Single(view => view.Name == "ConfigShareChanges");
                    Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                    Assert.Equal("UNC_UNL_HEIGHT", grid.Rows[0].Cells[0].Value);
                }
            });
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static List<T> FindAll<T>(Control root) where T : Control
        {
            var result = new List<T>();
            foreach (Control child in root.Controls)
            {
                if (child is T match) result.Add(match);
                result.AddRange(FindAll<T>(child));
            }
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    [CollectionDefinition("Settings UI", DisableParallelization = true)]
    public sealed class SettingsUiCollection { }

    [Collection("Settings UI")]
    public class UnifiedSettingsFormTests
    {
        [Fact]
        public void Navigation_PreservesSevenCommandTabIndices()
        {
            RunSta(() =>
            {
                // 2.10.0：原“自动输出”页升级为“输出与分享”，并新增“日志与记录”页。
                string[] titles =
                {
                    "概览", "绘制标注", "统计汇总", "文字样式", "界面与启动",
                    "机台数据", "清单与表格", "Downstream 自动添加", "颜色",
                    "输出与分享", "日志与记录"
                };
                for (int index = 0; index < titles.Length; index++)
                {
                    using (var form = new UnifiedSettingsForm(index))
                    {
                        form.Show();
                        Application.DoEvents();
                        TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                        Assert.NotNull(tabs);
                        Assert.IsType<UiNavigationTabControl>(tabs);
                        Assert.Equal(TabAlignment.Left, tabs.Alignment);
                        Assert.Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode);
                        Assert.Equal(titles, tabs.TabPages.Cast<TabPage>()
                            .Select(page => page.Text).ToArray());
                        Assert.Equal(index, tabs.SelectedIndex);
                    }
                }
            });
        }

        [Fact]
        public void StatisticsTab_UsesFlatCardsAndKeepsAllControls()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(2))
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("统计汇总", tabs.SelectedTab.Text);
                    Assert.Empty(FindAll<GroupBox>(tabs.SelectedTab));
                    List<UiCard> cards = FindAll<UiCard>(tabs.SelectedTab);
                    List<CheckBox> switches = FindAll<CheckBox>(tabs.SelectedTab);
                    Assert.Equal(3, cards.Count);
                    Assert.Equal(6, switches.Count);
                    Assert.Contains(switches, checkBox =>
                        checkBox.Text == "尺寸标注文字 (DIMENSION)");
                    Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                    Assert.All(cards, card =>
                    {
                        Assert.True(card.Visible);
                        Assert.True(card.Width > 0 && card.Height > 60);
                        Assert.Equal(8, card.CornerRadius);
                    });
                    Assert.All(switches, item =>
                    {
                        Assert.True(item.Visible);
                        Assert.True(item.Width > 0 && item.Height > 0);
                    });
                    Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                    Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
                    Assert.True(tabs.Width > 0 && tabs.Height > 0);
                }
            });
        }

        [Fact]
        public void StartupTab_ExposesSplashSwitchAndVersion()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(4))
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    // 启动开关已从"文字样式"移到独立的"界面与启动"页。
                    Assert.Equal("界面与启动", tabs.SelectedTab.Text);
                    Assert.True(FindAll<CheckBox>(tabs.SelectedTab).Any(checkBox =>
                        checkBox.Text.Contains("启动")), "启动页应包含启动动画开关");
                    Assert.True(FindAll<Label>(tabs.SelectedTab).Any(label =>
                        label.Text.Contains(UNCAD.Infra.ProductMetadata.VersionLabel)),
                        "启动页应显示当前版本");
                }

                using (var style = new UnifiedSettingsForm(3))
                {
                    style.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(style, "SettingsNavigation");
                    Assert.Equal("文字样式", tabs.SelectedTab.Text);
                    Assert.Empty(FindAll<CheckBox>(tabs.SelectedTab));
                }
            });
        }
        [Fact]
        public void ConduitTab_ListsOnlyRigidCatalogModels()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(1))
                {
                    form.Show();
                    Application.DoEvents();
                    ComboBox diameter = Find<ComboBox>(form, "ConduitDiameter");
                    Assert.NotNull(diameter);
                    Assert.Equal(new[] { "20", "25", "38", "51" },
                        diameter.Items.Cast<object>().Select(item => item.ToString()));
                    Assert.DoesNotContain("75", diameter.Items.Cast<object>());
                    Assert.DoesNotContain("100", diameter.Items.Cast<object>());
                }
            });
        }

        [Fact]
        public void MachineTab_ShowsDataSourceSnapshotAndPicker()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(5))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("机台数据", tabs.SelectedTab.Text);
                    Assert.Empty(FindAll<GroupBox>(tabs.SelectedTab));
                    Assert.Single(FindAll<UiCard>(tabs.SelectedTab));
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "浏览...");
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "刷新");
                    Label snapshot = Find<Label>(tabs.SelectedTab, "MachineSnapshotStatus");
                    Assert.NotNull(snapshot);
                    Assert.False(string.IsNullOrWhiteSpace(snapshot.Text));
                }
            });
        }

        [Fact]
        public void BomTab_ShowsTableParametersAndIndependentAutofillCategories()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(6))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("清单与表格", tabs.SelectedTab.Text);
                    Assert.Equal(2, FindAll<UiCard>(tabs.SelectedTab).Count);

                    // 表格参数 5 项 + 电缆盘内/箱内预留 3 项（2.10.0 可配置）。
                    List<NumericUpDown> numbers = FindAll<NumericUpDown>(tabs.SelectedTab);
                    Assert.Equal(8, numbers.Count);
                    Assert.Single(numbers, number => number.DecimalPlaces == 0
                        && number.Minimum == 1m && number.Maximum == 100m);
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "每次清空数据行数");
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "软管手动数量（m）");
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "I-Line 盘内预留（mm）");
                    Assert.Contains(numbers, number => number.Value == 3400m);
                    Assert.Contains(numbers, number => number.Value == 1000m);
                    Assert.Contains(numbers, number => number.Value == 500m);

                    // 8 个自动填充类别 + 1 个整机回路校验开关。
                    List<CheckBox> autoFill = FindAll<CheckBox>(tabs.SelectedTab);
                    Assert.Equal(9, autoFill.Count);
                    List<CheckBox> categoryBoxes = autoFill
                        .Where(check => !check.Text.StartsWith("整机回路校验")).ToList();
                    Assert.Equal(new[]
                    {
                        "电缆", "断路器", "电盘", "软管",
                        "线管", "桥架", "插座盘", "插接箱"
                    }, categoryBoxes.Select(check => check.Text).ToArray());
                    Assert.Contains(autoFill,
                        check => check.Text.StartsWith("整机回路校验"));
                    Assert.DoesNotContain(autoFill,
                        check => check.Text.Contains("未匹配"));
                }
            });
        }

        [Fact]
        public void DownstreamTab_KeepsRatingsAndRulesTogether()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(7))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("Downstream 自动添加", tabs.SelectedTab.Text);
                    Assert.Equal(3, FindAll<UiCard>(tabs.SelectedTab).Count);
                    Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                    // 独立开关（与通用类别开关解耦）
                    List<CheckBox> switches = FindAll<CheckBox>(tabs.SelectedTab);
                    Assert.Equal(2, switches.Count);
                    Assert.Contains(switches, check => check.Text.Contains("socket box"));
                    Assert.Contains(switches, check => check.Text.Contains("I-line_Panel"));
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "插座箱额定电流（A）");
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "电盘额定电流（A）");
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "I-line_Panel");
                }
            });
        }

        [Fact]
        public void ColorTab_ShowsTwoAciPickers()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(8))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("颜色", tabs.SelectedTab.Text);
                    List<ComboBox> colors = FindAll<ComboBox>(tabs.SelectedTab)
                        .Where(color => color.DrawMode == DrawMode.OwnerDrawFixed).ToList();
                    Assert.Equal(2, colors.Count);
                    Assert.All(colors, color =>
                    {
                        Assert.Equal(ComboBoxStyle.DropDownList, color.DropDownStyle);
                        Assert.Contains(color.Items.Cast<object>(),
                            item => item.ToString().Contains("ACI 3"));
                        Assert.Contains(color.Items.Cast<object>(),
                            item => item.ToString().Contains("ACI 6"));
                    });
                }
            });
        }

        [Fact]
        public void About_ShowsAuthorizationCardFirstAndKeepsTermsAccessible()
        {
            RunSta(() =>
            {
                using (var form = new AboutForm())
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "AboutNavigation");
                    Assert.NotNull(tabs);
                    Assert.Equal(TabAlignment.Left, tabs.Alignment);
                    Assert.Equal(new[] { "概览", "更新记录", "使用条款" }, tabs.TabPages
                        .Cast<TabPage>().Select(page => page.Text).ToArray());
                    UiCard authorization = Find<UiCard>(tabs.SelectedTab,
                        "AuthorizationCard");
                    Assert.NotNull(authorization);
                    Assert.True(authorization.Visible);
                    Assert.True(authorization.Height >= 260);
                    Assert.True(authorization.CornerRadius >= 14);
                    Assert.Contains(FindAll<Label>(authorization),
                        label => label.Text == "授权模式");
                    Assert.Contains(FindAll<Label>(authorization),
                        label => label.Text == "授权客户");
                    Label status = Find<Label>(authorization, "AuthorizationStatus");
                    Rectangle statusBounds = authorization.RectangleToClient(
                        status.RectangleToScreen(status.ClientRectangle));
                    Assert.True(statusBounds.Right <= authorization.ClientRectangle.Right,
                        "授权状态文字超出卡片: " + statusBounds);

                    tabs.SelectedIndex = 1;
                    Application.DoEvents();
                    Control updates = Find<Control>(
                        tabs.SelectedTab, "UpdateVersionPages");
                    Assert.NotNull(updates);
                    ListBox versions = Find<ListBox>(updates, "UpdateVersionList");
                    Assert.Equal(VersionChangeLog.Entries.Count, versions.Items.Count);
                    Assert.Equal(ProductMetadata.VersionLabel, versions.Items[0]);
                    Panel host = Find<Panel>(updates, "UpdatePageHost");
                    Panel latest = host.Controls.OfType<Panel>().Single(control =>
                        Equals(control.Tag, ProductMetadata.VersionLabel));
                    Assert.Contains(FindAll<RichTextBox>(latest),
                        details => details.Text.Contains(
                            VersionChangeLog.Entries[0].Changes[0]));

                    tabs.SelectedIndex = 2;
                    Application.DoEvents();
                    TextBox terms = Assert.Single(FindAll<TextBox>(tabs.SelectedTab));
                    Assert.True(terms.Multiline);
                    Assert.True(terms.ReadOnly);
                    Assert.False(string.IsNullOrWhiteSpace(terms.Text));
                }
            });
        }

        [Fact]
        public void OnlineLicense_UsesProminentCardAndKeepsValidationActions()
        {
            RunSta(() =>
            {
                using (var form = new OnlineLicenseForm("请输入授权码。"))
                {
                    form.Show();
                    Application.DoEvents();
                    UiCard card = Find<UiCard>(form, "AuthorizationCard");
                    Assert.NotNull(card);
                    Assert.Equal(16, card.CornerRadius);
                    Assert.Equal(DockStyle.Fill, card.Dock);
                    Assert.NotNull(Find<TextBox>(form, "AuthorizationCode"));
                    Assert.Contains(FindAll<Button>(form),
                        button => button.Text == "验证并保存");
                    Assert.Contains(FindAll<Button>(form),
                        button => button.Text == "稍后处理");
                    Assert.Equal(FormBorderStyle.FixedDialog, form.FormBorderStyle);
                }
            });
        }

        [Fact]
        public void OutputShareTab_ExposesDirectoryStructureAndConfigShareActions()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(9))
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("输出与分享", tabs.SelectedTab.Text);
                    List<CheckBox> switches = FindAll<CheckBox>(tabs.SelectedTab);
                    Assert.Equal(2, switches.Count);
                    Assert.Contains(switches, check => check.Text.Contains("图纸"));
                    Assert.Contains(switches, check => check.Text.Contains("分目录"));
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "复制配置到剪贴板");
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "从剪贴板导入");
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "打开目录");
                }
            });
        }

        [Fact]
        public void LogsTab_ExposesLevelRetentionAndRecentOperationTable()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(10))
                {
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("日志与记录", tabs.SelectedTab.Text);
                    List<ComboBox> levelBoxes = FindAll<ComboBox>(tabs.SelectedTab)
                        .Where(box => box.DropDownStyle == ComboBoxStyle.DropDownList).ToList();
                    Assert.Contains(levelBoxes, box => box.Items.Cast<object>()
                        .Select(item => Convert.ToString(item))
                        .SequenceEqual(new[] { "INFO", "WARN", "ERROR" }));
                    Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                    DataGridView grid = FindAll<DataGridView>(tabs.SelectedTab)
                        .Single(view => view.Name == "OperationLogGrid");
                    Assert.True(grid.Columns.Count >= 4);
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "打开日志目录");
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab),
                        button => button.Text == "打开操作记录目录");
                }
            });
        }

        [Theory]
        [InlineData(150)]
        [InlineData(200)]
        public void NewPages_KeepKeyControlsVisibleAtHighDpi(int percent)
        {
            RunSta(() =>
            {
                float designDpi = 96f * 100f / percent;
                using (var form = new UnifiedSettingsForm(9))
                {
                    form.AutoScaleDimensions = new SizeF(designDpi, designDpi);
                    form.Show();
                    form.Size = form.MinimumSize;
                    Application.DoEvents();

                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("输出与分享", tabs.SelectedTab.Text);
                    Assert.True(tabs.Width > 0 && tabs.Height > 0);
                    foreach (string text in new[]
                        { "复制配置到剪贴板", "从剪贴板导入", "打开目录" })
                    {
                        Button button = FindAll<Button>(tabs.SelectedTab)
                            .FirstOrDefault(candidate => candidate.Text == text);
                        Assert.NotNull(button);
                        Assert.True(button.Visible && button.Width > 0 && button.Height > 0,
                            text + " 在高 DPI 下不可见: " + button.Bounds);
                    }
                    Assert.All(FindAll<UiCard>(tabs.SelectedTab), card =>
                    {
                        Assert.True(card.Width > 0 && card.Height > 0);
                        Assert.True(card.Left >= 0 && card.Top >= 0);
                    });

                    tabs.SelectedIndex = 10;
                    Application.DoEvents();
                    Assert.Equal("日志与记录", tabs.SelectedTab.Text);
                    Assert.Contains(FindAll<ComboBox>(tabs.SelectedTab), box =>
                        box.Items.Cast<object>().Select(Convert.ToString)
                            .Contains("INFO"));
                    Assert.Equal(2, FindAll<NumericUpDown>(tabs.SelectedTab).Count);
                    Assert.All(FindAll<NumericUpDown>(tabs.SelectedTab), number =>
                    {
                        Assert.True(number.Width > 0 && number.Height > 0);
                        Assert.True(number.Left >= 0 && number.Top >= 0);
                    });
                    Assert.Contains(FindAll<Button>(tabs.SelectedTab), button =>
                        button.Text == "打开日志目录" && button.Width > 0);
                }
            });
        }

        [Fact]
        public void DrawTab_DoesNotExposeIneffectiveBridgeSpecSetting()
        {
            RunSta(() =>
            {
                using (var form = new UnifiedSettingsForm(1))
                {
                    form.Show();
                    Application.DoEvents();
                    TabControl tabs = Find<TabControl>(form, "SettingsNavigation");
                    Assert.Equal("绘制标注", tabs.SelectedTab.Text);
                    // U1Q1/U1Q2/U1Q4 各自传固定规格，页面上的“默认规格”改了也不生效，
                    // 2.10.0 已移除；只保留文字高度/偏移/标注侧。
                    Assert.DoesNotContain(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "默认规格");
                    Assert.DoesNotContain(FindAll<TextBox>(tabs.SelectedTab),
                        box => (box.Text ?? "").Contains("桥架"));
                    Assert.Single(FindAll<TextBox>(tabs.SelectedTab),
                        box => box.Text == "2000mm");
                    Assert.Contains(FindAll<Label>(tabs.SelectedTab),
                        label => label.Text == "文字高度");
                }
            });
        }

        [Fact]
        public void SettingsSource_HasNoRemoteSourceOrLedgerWiring()
        {
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "UNCAD", "UI", "UnifiedSettingsForm.cs")));

            // 2.10.0：远程数据源与台账上传已移除，UI 不得再引用这些类型。
            // 只断言代码接线，不断言文案字面量（否则改文案就会误报）。
            Assert.DoesNotContain("MachineWorkbookSource.IsRemote", source);
            Assert.DoesNotContain("MachineLedgerReporter", source);
            Assert.DoesNotContain("ClientSubmissionUploader", source);
            Assert.DoesNotContain("BackgroundUploadQueue", source);
            // 新页签的接线必须存在（否则按钮只是摆设）。
            Assert.Contains("ConfigShareService.Export()", source);
            Assert.Contains("ConfigShareService.Preview(", source);
            Assert.Contains("ConfigShareService.Import(", source);
            Assert.Contains("OperationLog.ReadRecent(", source);
            Assert.Contains("OperationLog.OpenFolder()", source);
            // 无效设置：UNQ_TEXT（桥架规格由命令固定传入）与 UNC_FILL_BRIDGE
            // （xframe_v2 已无 BRIDGE_FRAME_INFO 属性）都是已删除的死键，
            // U1SET 不得再引用；UNQ_TEXT_OFF 是有效键，注意精确匹配。
            Assert.DoesNotContain("ConfigKeys.UnqText,", source);
            Assert.DoesNotContain("ConfigKeys.FillBridge", source);
        }

        [Fact]
        public void ExcelRefresh_RemainsExplicitAndRunsOffTheUiThread()
        {
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "UNCAD", "UI", "UnifiedSettingsForm.cs")));

            Assert.Contains("private async void RefreshMachineWorkbook", source);
            Assert.Contains("await Task.Run", source);
            Assert.Contains("button.Text = \"刷新中...\"", source);
            Assert.Contains("MachineWorkbookSource.TryGetSnapshot", source);
            Assert.DoesNotContain("Shown += RefreshMachineWorkbook", source);
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

        private static T Find<T>(Control root, string name = null) where T : Control
        {
            return FindAll<T>(root).FirstOrDefault(control =>
                name == null || control.Name == name);
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

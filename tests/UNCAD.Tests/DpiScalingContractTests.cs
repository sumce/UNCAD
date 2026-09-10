using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// DPI 缩放契约。
    ///
    /// 背景：2.4.5 曾同时启用 WinForms 自动缩放（DialogLayout 设置
    /// AutoScaleMode.Dpi + AutoScaleDimensions）和手工换算（UiTheme.Scaled），
    /// 两者在同一属性上相乘，150% 屏幕得到 2.25 倍。这里把结论固化为回归测试：
    /// 自动缩放是唯一路径，禁止再引入手工 DPI 换算。
    /// </summary>
    public class DpiScalingContractTests
    {
        [Fact]
        public void DialogLayout_EnablesDpiAutoScaling()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new Form())
                    {
                        DialogLayout.Apply(form, "测试", new Size(800, 600), new Size(640, 480));

                        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                        // 缺省空的 AutoScaleDimensions 会让 WinForms 完全跳过缩放，
                        // 这是本契约最容易被误删的一行。
                        Assert.Equal(new SizeF(96F, 96F), form.AutoScaleDimensions);
                        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        /// <summary>
        /// 实测自动缩放的覆盖面。用 AutoScaleDimensions=(64,64) 在 96-DPI 机器上
        /// 造出 1.5 倍比例（真实 DPI / 设计基准 = 96/64），等价于 150% 屏幕。
        /// 结论：控件边界与 TableLayoutPanel 的 Absolute 行列样式**都会**被缩放，
        /// 因此任何手工 DPI 换算都是多余的，且会与自动缩放叠加。
        /// </summary>
        [Fact]
        public void WinFormsAutoScaling_CoversControlBoundsAndAbsoluteRowStyles()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new Form())
                    {
                        form.AutoScaleMode = AutoScaleMode.Dpi;
                        form.AutoScaleDimensions = new SizeF(96F, 96F);
                        form.ClientSize = new Size(900, 700);
                        Assert.Equal(new SizeF(96F, 96F), form.CurrentAutoScaleDimensions);

                        // 非 Dock 的 TLP。必须同时给出 Absolute 与 Percent 两行：
                        // 只有单个 Absolute 行时，该行会被拉伸到面板高度，读数被
                        // 几何变化污染，量不到真正的缩放倍数。
                        var panel = new TableLayoutPanel
                        {
                            Location = new Point(10, 10),
                            Size = new Size(400, 300),
                            ColumnCount = 1,
                            RowCount = 2
                        };
                        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
                        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
                        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                        var label = new Label { Location = new Point(500, 10), Width = 200, Height = 40 };
                        form.Controls.Add(panel);
                        form.Controls.Add(label);

                        // 比例 = CurrentAutoScaleDimensions(96) / 设计基准(64) = 1.5
                        form.AutoScaleDimensions = new SizeF(64F, 64F);
                        form.PerformAutoScale();

                        Assert.Equal(300, label.Width);
                        Assert.Equal(150f, panel.RowStyles[0].Height);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        /// <summary>
        /// 实测自动缩放的**盲区**。这些属性不随 AutoScaleDimensions 缩放，
        /// 正是 UiTheme.NotAutoScaled 存在的原因。若框架行为在未来版本改变，
        /// 本测试会失败并提示重新评估该辅助方法。
        /// </summary>
        [Fact]
        public void WinFormsAutoScaling_DoesNotCoverGridAndComboMetrics()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new Form())
                    {
                        form.AutoScaleMode = AutoScaleMode.Dpi;
                        form.AutoScaleDimensions = new SizeF(96F, 96F);
                        form.ClientSize = new Size(900, 700);

                        var label = new Label { Location = new Point(10, 10), Width = 200, Height = 40 };
                        var combo = new ComboBox { ItemHeight = 22 };
                        var grid = new DataGridView { RowTemplate = { Height = 28 } };
                        grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 120 });
                        var list = new ListView { View = View.Details };
                        list.Columns.Add(new ColumnHeader { Width = 120 });
                        form.Controls.Add(label);
                        form.Controls.Add(combo);
                        form.Controls.Add(grid);
                        form.Controls.Add(list);

                        form.AutoScaleDimensions = new SizeF(64F, 64F);   // 比例 1.5
                        form.PerformAutoScale();

                        // 被覆盖：控件边界随比例放大。
                        Assert.Equal(300, label.Width);
                        Assert.Equal(60, label.Height);

                        // 不被覆盖：这些必须走 UiTheme.NotAutoScaled。
                        Assert.Equal(22, combo.ItemHeight);
                        Assert.Equal(28, grid.RowTemplate.Height);
                        Assert.Equal(120, grid.Columns[0].Width);
                        Assert.Equal(120, list.Columns[0].Width);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        /// <summary>
        /// 源码契约：UI 层不得再出现手工 DPI 换算 API。
        /// 与 QuickLineFeatureContractTests 的"无 GUI 依赖"守护同一模式。
        /// 注意 NotAutoScaled 不在禁止之列——它是为上面实测的自动缩放盲区而存在的。
        /// </summary>
        [Fact]
        public void UiLayer_DoesNotHandScaleForDpi()
        {
            string srcRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "UNCAD"));
            Assert.True(Directory.Exists(srcRoot), "未找到源码目录: " + srcRoot);

            string[] offenders = Directory
                .GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.Combine("obj", string.Empty))
                    && !path.Contains(Path.Combine("bin", string.Empty)))
                .Select(path => new
                {
                    Path = path,
                    Text = File.ReadAllText(path)
                })
                .Where(file => file.Text.Contains("UiTheme.Scaled(")
                    || file.Text.Contains("UiTheme.Row(")
                    || file.Text.Contains("UiTheme.Column("))
                .Select(file => file.Path.Substring(srcRoot.Length))
                .ToArray();

            Assert.True(offenders.Length == 0,
                "检测到手工 DPI 换算，会与 WinForms 自动缩放叠加："
                    + string.Join(", ", offenders));
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    internal sealed class AboutForm : Form
    {
        private static readonly Color Ink = UiTheme.TextPrimary;
        private static readonly Color Muted = UiTheme.TextSecondary;
        private static readonly Color Accent = UiTheme.BrandBlue;

        public AboutForm()
        {
            AboutInfo info = AboutInfo.Current();
            DialogLayout.Apply(this, ProductMetadata.ProductName + " · 关于与授权",
                new Size(650, 520), new Size(580, 460));
            BackColor = UiTheme.WindowBg;

            Panel header = BuildHeader(info);
            TabControl tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 7),
                Appearance = TabAppearance.Normal,
                Multiline = false
            };
            tabs.TabPages.Add(BuildProductTab(info));
            tabs.TabPages.Add(BuildLicenseTab(info));
            tabs.TabPages.Add(BuildTermsTab());
            tabs.TabPages.Add(BuildCompanyTab(info));

            Button close = UiTheme.Button("关闭", DialogResult.Cancel);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Padding = new Padding(0, 7, 12, 2);
            commands.Controls.Add(close);

            Controls.Add(tabs);
            Controls.Add(commands);
            Controls.Add(header);
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }

        private static Panel BuildHeader(AboutInfo info)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = Color.White,
                Padding = new Padding(18, 10, 18, 8)
            };
            var mark = new BrandMarkControl
            {
                Dock = DockStyle.Left,
                Width = 142
            };
            var copy = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White
            };
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            copy.Controls.Add(new Label
            {
                Text = info.ProductName + "  /  " + info.Subtitle,
                Dock = DockStyle.Fill,
                Font = UiTheme.FontTitle,
                ForeColor = Ink,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            copy.Controls.Add(new Label
            {
                Text = info.CompanyName + "  ·  " + info.Website,
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBody,
                ForeColor = Muted,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);
            header.Controls.Add(copy);
            header.Controls.Add(mark);
            return header;
        }

        private static TabPage BuildProductTab(AboutInfo info)
        {
            var page = new TabPage("产品信息") { Padding = new Padding(18) };
            var layout = DetailLayout(7);
            AddDetail(layout, 0, "产品", info.ProductName);
            AddDetail(layout, 1, "版本", info.Version);
            AddDetail(layout, 2, "构建时间", info.BuildTime);
            AddDetail(layout, 3, "发行日期", info.UpdatedOn);
            AddDetail(layout, 4, "开发者", info.CompanyName);
            AddDetail(layout, 5, "官网", info.Website);
            AddDetail(layout, 6, "版权", info.Copyright);
            page.Controls.Add(layout);
            return page;
        }

        private static TabPage BuildLicenseTab(AboutInfo info)
        {
            LicenseSnapshot license = info.License;
            var page = new TabPage("授权状态") { Padding = new Padding(18) };
            var layout = DetailLayout(5);
            AddDetail(layout, 0, "授权状态", license.StatusText, license.IsExpired);
            AddDetail(layout, 1, "授权模式", ModeText(license.Mode));
            AddDetail(layout, 2, "有效期至", license.ExpiryText);
            AddDetail(layout, 3, "剩余天数", license.DaysRemaining.HasValue
                ? license.DaysRemaining.Value + " 天" : "无期限");
            AddDetail(layout, 4, "说明", license.IsExpired
                ? "CAD 写入、BOQ 输出和 DWG 导出已停止。"
                : license.Mode == LicenseMode.Perpetual
                    ? "当前版本无到期限制。" : "请在授权有效期内使用本软件。");
            page.Controls.Add(layout);
            return page;
        }

        private static TabPage BuildTermsTab()
        {
            var page = new TabPage("使用条款") { Padding = new Padding(12) };
            var terms = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
                ForeColor = Ink,
                ScrollBars = ScrollBars.Vertical,
                Text = LegalTerms.FullText,
                TabStop = true
            };
            page.Controls.Add(terms);
            return page;
        }

        private static TabPage BuildCompanyTab(AboutInfo info)
        {
            var page = new TabPage("关于 UNSIAO") { Padding = new Padding(18) };
            var layout = DetailLayout(5);
            AddDetail(layout, 0, "公司", info.CompanyName);
            AddDetail(layout, 1, "产品", info.ProductName + " · " + info.Subtitle);
            AddDetail(layout, 2, "官网", info.Website);
            var link = new LinkLabel
            {
                Text = info.WebsiteUrl,
                AutoSize = true,
                LinkColor = Accent,
                Margin = new Padding(0, 6, 0, 4)
            };
            link.Click += (sender, args) => OpenWebsite(info.WebsiteUrl);
            layout.Controls.Add(new Label { Text = "访问", AutoSize = true, ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 6, 8, 4) }, 0, 3);
            layout.Controls.Add(link, 1, 3);
            AddDetail(layout, 4, "版权", info.Copyright);
            page.Controls.Add(layout);
            return page;
        }

        private static TableLayoutPanel DetailLayout(int rows)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = rows,
                Padding = new Padding(4, 8, 4, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < rows; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            return layout;
        }

        private static void AddDetail(TableLayoutPanel panel, int row, string label, string value,
            bool warning = false)
        {
            panel.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 12, 4)
            }, 0, row);
            panel.Controls.Add(new Label
            {
                Text = value ?? "",
                AutoSize = true,
                ForeColor = warning ? UiTheme.WarningFg : Ink,
                Font = warning ? UiTheme.FontBodyBold : UiTheme.FontBody,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            }, 1, row);
        }

        private static string ModeText(LicenseMode mode)
        {
            switch (mode)
            {
                case LicenseMode.Trial: return "试用版";
                case LicenseMode.Project: return "项目授权版";
                default: return "正式版";
            }
        }

        private static void OpenWebsite(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("Open website failed: " + ex.Message); }
        }
    }
}

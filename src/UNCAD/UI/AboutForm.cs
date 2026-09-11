using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    internal sealed class AboutForm : Form
    {
        public AboutForm()
        {
            AboutInfo info = AboutInfo.Current();
            DialogLayout.Apply(this, ProductMetadata.ProductName + " · 关于与授权",
                new Size(780, 620), new Size(660, 520));

            var tabs = new UiNavigationTabControl
            {
                Name = "AboutNavigation",
                Dock = DockStyle.Fill
            };
            tabs.TabPages.Add(BuildOverviewTab(info));
            tabs.TabPages.Add(BuildTermsTab());

            Button close = UiTheme.Button("关闭", DialogResult.Cancel);
            FlowLayoutPanel commands = UiTheme.CommandBar();
            commands.Controls.Add(close);

            Controls.Add(tabs);
            Controls.Add(commands);
            Controls.Add(BuildHeader(info));
            AcceptButton = close;
            CancelButton = close;
            Shown += (sender, args) => close.Select();
        }

        private static Panel BuildHeader(AboutInfo info)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 88,
                BackColor = UiTheme.Surface,
                Padding = new Padding(20, 12, 20, 10)
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
                BackColor = UiTheme.Surface,
                Margin = new Padding(0)
            };
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            copy.Controls.Add(new Label
            {
                Text = info.ProductName,
                Dock = DockStyle.Fill,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            copy.Controls.Add(new Label
            {
                Text = info.Subtitle + " · " + info.CompanyName,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);
            header.Controls.Add(copy);
            header.Controls.Add(mark);
            return header;
        }

        private static TabPage BuildOverviewTab(AboutInfo info)
        {
            var page = new TabPage("概览")
            {
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(0),
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
                RowCount = 4,
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceXL, UiTheme.SpaceL,
                    UiTheme.SpaceXL, UiTheme.SpaceXL)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.Controls.Add(PageHeader("关于与授权", "产品、版本与当前设备授权"), 0, 0);
            stack.Controls.Add(BuildLicenseCard(info), 0, 1);
            stack.Controls.Add(BuildProductCard(info), 0, 2);
            stack.Controls.Add(BuildCompanyCard(info), 0, 3);
            scroll.Controls.Add(stack);
            page.Controls.Add(scroll);
            return page;
        }

        private static UiCard BuildLicenseCard(AboutInfo info)
        {
            LicenseSnapshot license = info.License;
            var card = UiTheme.Card(16);
            card.Name = "AuthorizationCard";
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.MinimumSize = new Size(0, 280);
            card.Padding = new Padding(24, 20, 24, 20);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var heading = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceM)
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var statusBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            statusBlock.Controls.Add(new Label
            {
                Text = "授权信息",
                AutoSize = true,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0)
            }, 0, 0);
            statusBlock.Controls.Add(new Label
            {
                Name = "AuthorizationStatus",
                Text = license.StatusText,
                AutoSize = true,
                Font = UiTheme.FontDisplay,
                ForeColor = license.IsExpired ? UiTheme.DangerFg : UiTheme.SuccessFg,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
            }, 0, 1);
            heading.Controls.Add(statusBlock, 0, 0);

            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0)
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < 3; row++)
                details.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            details.Controls.Add(Metric("授权模式", ModeText(license.Mode)), 0, 0);
            details.Controls.Add(Metric("有效期至", license.ExpiryText), 1, 0);
            details.Controls.Add(Metric("剩余天数", license.DaysRemaining.HasValue
                ? license.DaysRemaining.Value + " 天" : "无期限"), 0, 1);
            details.Controls.Add(Metric("授权公司", ValueOrDash(info.LicenseeCompany)), 1, 1);
            details.Controls.Add(Metric("授权客户", ValueOrDash(info.LicenseeName)), 0, 2);
            details.Controls.Add(Metric("预计授权时间", info.ExpectedAuthorizationYears.HasValue
                ? info.ExpectedAuthorizationYears.Value + " 年" : "—"), 1, 2);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, UiTheme.SpaceS, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(new Label
            {
                Text = license.IsExpired
                    ? "CAD 写入、BOQ 输出和 DWG 导出已停止。"
                    : license.Mode == LicenseMode.Perpetual
                        ? "当前版本无到期限制。" : "当前授权在有效期内。",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = license.IsExpired ? UiTheme.DangerFg : UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);
            if (ProductMetadata.RequiresOnlineLicense)
            {
                Button update = UiTheme.Button("更新授权码");
                update.Anchor = AnchorStyles.Right;
                update.Click += (sender, args) => UpdateAuthorization(card);
                footer.Controls.Add(update, 1, 0);
            }

            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(details, 0, 1);
            layout.Controls.Add(footer, 0, 2);
            card.Controls.Add(layout);
            return card;
        }

        private static UiCard BuildProductCard(AboutInfo info)
        {
            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0)
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < 3; row++)
                details.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            details.Controls.Add(Metric("产品", info.ProductName), 0, 0);
            details.Controls.Add(Metric("版本", info.Version), 1, 0);
            details.Controls.Add(Metric("构建时间", info.BuildTime), 0, 1);
            details.Controls.Add(Metric("发行日期", info.UpdatedOn), 1, 1);
            details.Controls.Add(Metric("客户代码", info.CustomerCode), 0, 2);
            details.Controls.Add(Metric("开发者", info.CompanyName), 1, 2);
            return SectionCard("产品信息", details);
        }

        private static UiCard BuildCompanyCard(AboutInfo info)
        {
            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            details.Controls.Add(Metric("公司", info.CompanyName), 0, 0);
            details.Controls.Add(Metric("版权", info.Copyright), 1, 0);
            details.Controls.Add(Metric("官网", info.Website), 0, 1);

            var website = new LinkLabel
            {
                Text = info.WebsiteUrl,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                LinkColor = UiTheme.Accent,
                ActiveLinkColor = UiTheme.AccentHover,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 0, 0, 0)
            };
            website.Click += (sender, args) => OpenWebsite(info.WebsiteUrl);
            details.Controls.Add(website, 1, 1);
            return SectionCard("UNSIAO.Ltd", details);
        }

        private static TabPage BuildTermsTab()
        {
            var page = new TabPage("使用条款")
            {
                BackColor = UiTheme.WindowBg,
                Padding = new Padding(UiTheme.SpaceXL),
                UseVisualStyleBackColor = false
            };
            var card = UiTheme.Card();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(UiTheme.SpaceL);
            var terms = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                ScrollBars = ScrollBars.Vertical,
                Text = LegalTerms.FullText,
                TabStop = true
            };
            card.Controls.Add(terms);
            page.Controls.Add(card);
            page.Controls.Add(PageHeader("使用条款", "软件许可与使用约定"));
            return page;
        }

        private static Control PageHeader(string title, string subtitle)
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceL)
            };
            header.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0)
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, UiTheme.SpaceXS, 0, 0)
            }, 0, 1);
            return header;
        }

        private static UiCard SectionCard(string title, Control content)
        {
            var card = UiTheme.Card();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            Label heading = UiTheme.SectionHeader(title);
            heading.Dock = DockStyle.Fill;
            content.Dock = DockStyle.Top;
            content.Margin = new Padding(0, UiTheme.SpaceXS, 0, 0);
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(content, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        private static Control Metric(string label, string value)
        {
            var metric = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, UiTheme.SpaceM, 0)
            };
            metric.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            metric.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            metric.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0)
            }, 0, 0);
            metric.Controls.Add(new Label
            {
                Text = value ?? "",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0, 2, 0, 0)
            }, 0, 1);
            return metric;
        }

        private static string ValueOrDash(string value)
            => string.IsNullOrWhiteSpace(value) ? "—" : value;

        private static string ModeText(LicenseMode mode)
        {
            switch (mode)
            {
                case LicenseMode.Project: return "项目授权版";
                default: return "正式版";
            }
        }

        private static void UpdateAuthorization(Control owner)
        {
            using (var form = new OnlineLicenseForm(
                OnlineLicenseMonitor.Current.FailureReason))
            {
                if (form.ShowDialog(owner.FindForm()) == DialogResult.OK)
                    owner.FindForm()?.Close();
            }
        }

        private static void OpenWebsite(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("Open website failed: " + ex.Message); }
        }
    }
}

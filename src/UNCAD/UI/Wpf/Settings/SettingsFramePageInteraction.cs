using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UNCAD.Core.Dwg;
using UNCAD.Core.Fill;

namespace UNCAD.UI
{
    /// <summary>
    /// U1SET「图框与排版」页的交互（D-057）：占位符插入条、模板预览、恢复默认。
    /// 配置值的读写不在这里（SettingsWindow.LoadValues / Save），页面本身也
    /// 不含逻辑——这里只编辑页面上已传入的控件。
    /// </summary>
    internal sealed class SettingsFramePageInteraction
    {
        private readonly SettingsFramePage _page;
        private readonly List<TextBox> _templateBoxes;
        private readonly List<TextBox> _boqTitleBoxes;
        private TextBox _lastFocused;

        internal SettingsFramePageInteraction(SettingsFramePage page)
        {
            _page = page ?? throw new ArgumentNullException(nameof(page));
            _templateBoxes = new List<TextBox>
            {
                page.InfoProfession, page.InfoFloor, page.InfoDrafter,
                page.InfoReviewer, page.InfoDate, page.InfoVersion
            };
            _boqTitleBoxes = new List<TextBox>
                { page.BoqTitle1, page.BoqTitle2, page.BoqTitle3 };
            foreach (TextBox box in _templateBoxes)
            {
                TextBox captured = box;
                box.GotFocus += (sender, args) => _lastFocused = captured;
                box.TextChanged += (sender, args) => UpdatePreview();
            }
            foreach (TextBox box in _boqTitleBoxes)
                box.TextChanged += (sender, args) => UpdatePreview();
            BuildPlaceholderBar();
            UpdatePreview();
        }

        /// <summary>接收占位符的格：最近获得焦点的模板框；都还没点过则第一格。</summary>
        internal TextBox TargetBox => _lastFocused ?? _templateBoxes[0];

        internal DrawingInfoTemplates CurrentTemplates() => DrawingInfoTemplates.Create(
            _page.InfoProfession.Text, _page.InfoFloor.Text, _page.InfoDrafter.Text,
            _page.InfoReviewer.Text, _page.InfoDate.Text, _page.InfoVersion.Text);

        internal BoqTitleTemplates CurrentBoqTitles() => BoqTitleTemplates.Create(
            _page.BoqTitle1.Text, _page.BoqTitle2.Text, _page.BoqTitle3.Text);

        /// <summary>把占位符插到目标格的光标处（选中了文字就替换选区）。</summary>
        internal void InsertPlaceholder(string name)
        {
            TextBox box = TargetBox;
            string token = "(" + (name ?? "").Trim() + ")";
            string text = box.Text ?? "";
            int start = Math.Max(0, Math.Min(text.Length, box.SelectionStart));
            int length = Math.Max(0, Math.Min(text.Length - start, box.SelectionLength));
            box.Text = text.Remove(start, length).Insert(start, token);
            box.CaretIndex = start + token.Length;
            box.Focus();
        }

        /// <summary>恢复默认模板（楼层 = (楼层)、日期 = (日期)，其余留空）。</summary>
        internal void ResetDefaults()
        {
            DrawingInfoTemplates defaults = DrawingInfoTemplates.Default;
            for (int column = 0; column < _templateBoxes.Count; column++)
                _templateBoxes[column].Text = defaults.Columns[column];
            UpdatePreview();
        }

        /// <summary>刷新两段预览文字（样例机台；未识别的占位符点名提醒）。</summary>
        internal void UpdatePreview()
        {
            DrawingInfoTemplates templates = CurrentTemplates();
            DrawingInfoContext sample = DrawingInfoContext.Sample();
            IReadOnlyList<string> values = DrawingInfoTemplateRenderer.Render(templates, sample);
            var parts = new List<string>();
            for (int column = 0; column < DrawingInfoTemplates.ColumnCount; column++)
                parts.Add(DrawingInfoTemplates.ColumnNames[column] + "="
                    + (!templates.IsSet(column) ? "（不改）"
                        : values[column].Length > 0 ? values[column] : "（空）"));
            IReadOnlyList<string> unknown = _templateBoxes
                .SelectMany(box => DrawingInfoTemplateRenderer.UnknownPlaceholders(box.Text))
                .Distinct().ToList();
            _page.TemplatePreview.Text = "预览（样例：AMHS · MEDPC01 / AC Rack，最近一次 U1U）："
                + string.Join("  ｜  ", parts)
                + (unknown.Count > 0
                    ? "\n⚠ 未识别的占位符（会原样写进图框）：" + string.Join("、",
                        unknown.Select(name => "(" + name + ")"))
                    : "");

            BoqTitleTemplates titles = CurrentBoqTitles();
            _page.BoqTitlePreview.Text = "预览（机台 MEDPC01）："
                + string.Join("  ｜  ", Enumerable.Range(0, titles.Lines.Count)
                    .Select(line => !titles.IsSet || titles.Lines[line].Length == 0
                        ? "第 " + (line + 1) + " 行=（用清单模板里的文字）"
                        : "第 " + (line + 1) + " 行=" + titles.Render(line,
                            sample.Machine.MachineId, sample.Now, sample.User)));
        }

        /// <summary>占位符按钮条：点一下插入（名称与说明来自渲染器的占位符表）。</summary>
        private void BuildPlaceholderBar()
        {
            _page.PlaceholderBar.Children.Clear();
            foreach (KeyValuePair<string, string> placeholder in
                DrawingInfoTemplateRenderer.Placeholders)
            {
                string name = placeholder.Key;
                var button = new Wpf.Ui.Controls.Button
                {
                    Content = "(" + name + ")",
                    FontSize = 12,
                    Padding = new Thickness(9, 2, 9, 2),
                    Margin = new Thickness(0, 0, 6, 6),
                    ToolTip = placeholder.Value
                };
                button.Click += (sender, args) => InsertPlaceholder(name);
                _page.PlaceholderBar.Children.Add(button);
            }
        }
    }
}

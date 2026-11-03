using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UNCAD.UI
{
    /// <summary>状态徽章：语义色文字 + 同色低透明底（深浅主题都可读）。</summary>
    internal sealed class StatusBadge
    {
        internal StatusBadge(string text, string kind)
        {
            Text = text ?? "";
            Kind = kind ?? "neutral";
        }

        public string Text { get; }
        /// <summary>success / caution / critical / neutral。</summary>
        public string Kind { get; }
    }

    /// <summary>
    /// 状态徽章行（D-042 UI 信息完整性，2026-11-03）：XLAYOUT / XSTS / 批量结果窗
    /// 共用的全局状态摘要——一眼看出“几台好、几台有问题”，明细仍在下方表格。
    /// </summary>
    internal static class StatusBadgeBar
    {
        internal static readonly IReadOnlyList<string> Kinds =
            new[] { "success", "caution", "critical", "neutral" };

        /// <summary>把徽章填进 WrapPanel（先清空）。文本格式：如“缺回路 3 台”。</summary>
        internal static void Build(WrapPanel bar, IEnumerable<StatusBadge> badges)
        {
            if (bar == null) throw new ArgumentNullException(nameof(bar));
            bar.Children.Clear();
            foreach (StatusBadge badge in badges ?? Array.Empty<StatusBadge>())
                bar.Children.Add(CreateBadge(badge));
        }

        /// <summary>徽章文本拼接（测试断言用）。</summary>
        internal static string Summary(WrapPanel bar)
        {
            if (bar == null) return "";
            var texts = new List<string>();
            for (int index = 0; index < bar.Children.Count; index++)
                if (bar.Children[index] is Border frame
                    && frame.Child is TextBlock label)
                    texts.Add(label.Text);
            return string.Join("|", texts);
        }

        private static Border CreateBadge(StatusBadge badge)
        {
            string kind = Kinds.Contains(badge.Kind) ? badge.Kind : "neutral";
            Color foreground = kind == "success" ? Color.FromRgb(0x0F, 0x7B, 0x0F)
                : kind == "caution" ? Color.FromRgb(0x9D, 0x5D, 0x00)
                : kind == "critical" ? Color.FromRgb(0xC4, 0x2B, 0x1C)
                : Color.FromRgb(0x60, 0x5E, 0x5C);
            Color background = Color.FromArgb(0x1A, foreground.R, foreground.G,
                foreground.B);
            return new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 6),
                Background = new SolidColorBrush(background),
                Child = new TextBlock
                {
                    Text = badge.Text,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(foreground)
                }
            };
        }
    }
}

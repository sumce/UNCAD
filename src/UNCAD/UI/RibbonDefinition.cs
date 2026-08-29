using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>AutoCAD-independent description of one Ribbon action or option menu.</summary>
    public sealed class RibbonItemDefinition
    {
        private RibbonItemDefinition(string text, string command, string iconCommand,
            string toolTip, IReadOnlyList<RibbonItemDefinition> children)
        {
            Text = text;
            Command = command;
            IconCommand = iconCommand;
            ToolTip = toolTip;
            Children = children;
        }

        public string Text { get; }
        public string Command { get; }
        public string IconCommand { get; }
        public string ToolTip { get; }
        public IReadOnlyList<RibbonItemDefinition> Children { get; }
        public bool IsMenu => Children.Count > 0;

        public static RibbonItemDefinition Button(string text, string command, string toolTip)
            => new RibbonItemDefinition(text, command, command, toolTip,
                Array.Empty<RibbonItemDefinition>());

        public static RibbonItemDefinition Menu(string text, string iconCommand, string toolTip,
            params RibbonItemDefinition[] children)
            => new RibbonItemDefinition(text, null, iconCommand, toolTip, children);
    }

    /// <summary>AutoCAD-independent description of one Ribbon panel.</summary>
    public sealed class RibbonPanelDefinition
    {
        public RibbonPanelDefinition(string title, params RibbonItemDefinition[] items)
        {
            Title = title;
            Items = items;
        }

        public string Title { get; }
        public IReadOnlyList<RibbonItemDefinition> Items { get; }
    }

    /// <summary>Single source of truth for the task-oriented UNCAD Ribbon layout.</summary>
    public static class RibbonCatalog
    {
        public static IReadOnlyList<RibbonPanelDefinition> Panels { get; } =
            new RibbonPanelDefinition[]
            {
                new RibbonPanelDefinition("UNSIAO Work™",
                    RibbonItemDefinition.Button("清单生成", CommandIds.Fill,
                        "根据机台和回路数据生成清单，并自动记录 Excel"),
                    RibbonItemDefinition.Button("批量更新", CommandIds.FillUpdate,
                        "按图框批量更新已填清单，并自动记录 Excel"),
                    RibbonItemDefinition.Button("DWG导出", CommandIds.DwgExport,
                        "按机台ID导出图框内容并横向排列"),
                    RibbonItemDefinition.Button("设置", CommandIds.Settings,
                        "集中设置数据源、文字和绘图参数"),
                    RibbonItemDefinition.Button("关于", CommandIds.About,
                        "查看版本、授权状态和联系方式")),
                new RibbonPanelDefinition("标注",
                    RibbonItemDefinition.Button("绘制软管", CommandIds.Conduit,
                        "按统一设置中的软管参数绘制和标注"),
                    RibbonItemDefinition.Menu("桥架规格", CommandIds.Tray200,
                        "选择 100、200 或 400 mm 桥架规格",
                        RibbonItemDefinition.Button("桥架 100", CommandIds.Tray100,
                            "使用 100 mm 桥架规格"),
                        RibbonItemDefinition.Button("桥架 200", CommandIds.Tray200,
                            "使用 200 mm 桥架规格"),
                        RibbonItemDefinition.Button("桥架 400", CommandIds.Tray400,
                            "使用 400 mm 桥架规格"))),
                new RibbonPanelDefinition("绘制",
                    RibbonItemDefinition.Button("绘制线条", CommandIds.Line,
                        "连续绘制线段并自动生成长度文字"),
                    RibbonItemDefinition.Button("开拱桥", CommandIds.Arch,
                        "在线段交叉位置生成拱桥开洞")),
                new RibbonPanelDefinition("统计",
                    RibbonItemDefinition.Button("图纸统计", CommandIds.LegacyStatistics,
                        "使用传统 UNADD 统计所选对象并生成汇总文字"))
            };

        public static IEnumerable<RibbonItemDefinition> CommandItems =>
            Panels.SelectMany(panel => panel.Items).SelectMany(Flatten).Where(item => item.Command != null);

        private static IEnumerable<RibbonItemDefinition> Flatten(RibbonItemDefinition item)
        {
            yield return item;
            foreach (RibbonItemDefinition child in item.Children.SelectMany(Flatten))
                yield return child;
        }
    }
}

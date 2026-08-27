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
                    RibbonItemDefinition.Button("清单填充", CommandIds.Fill,
                        "根据机台和回路数据自动填充图纸清单"),
                    RibbonItemDefinition.Button("提交记录", CommandIds.Submit,
                        "读取当前图纸信息并更新 Excel 提交记录"),
                    RibbonItemDefinition.Button("配置中心", CommandIds.Settings,
                        "集中设置数据源、文字和绘图参数"),
                    RibbonItemDefinition.Button("关于", CommandIds.About,
                        "查看版本、授权状态和联系方式")),
                new RibbonPanelDefinition("标注",
                    RibbonItemDefinition.Button("绘制线管", CommandIds.Conduit,
                        "按当前配置生成线管和直径标注"),
                    RibbonItemDefinition.Menu("线管规格", CommandIds.Conduit,
                        "选择常用线管直径",
                        RibbonItemDefinition.Button("直径 20", CommandIds.Conduit20, "使用 20 mm 线管规格"),
                        RibbonItemDefinition.Button("直径 25", CommandIds.Conduit25, "使用 25 mm 线管规格"),
                        RibbonItemDefinition.Button("直径 32", CommandIds.Conduit32, "使用 32 mm 线管规格")),
                    RibbonItemDefinition.Button("桥架标注", CommandIds.Tray,
                        "选择线段并生成桥架规格标注"),
                    RibbonItemDefinition.Menu("桥架规格", CommandIds.Tray,
                        "选择常用桥架规格",
                        RibbonItemDefinition.Button("桥架 100", CommandIds.Tray100, "使用 100 mm 桥架规格"),
                        RibbonItemDefinition.Button("桥架 200", CommandIds.Tray200, "使用 200 mm 桥架规格"),
                        RibbonItemDefinition.Button("桥架 400", CommandIds.Tray400, "使用 400 mm 桥架规格")),
                    RibbonItemDefinition.Menu("标注设置", CommandIds.Settings,
                        "调整线管和桥架标注参数",
                        RibbonItemDefinition.Button("线管设置", CommandIds.ConduitSettings,
                            "设置线管高度、偏移和方向"),
                        RibbonItemDefinition.Button("桥架设置", CommandIds.TraySettings,
                            "设置桥架规格、文字和偏移"))),
                new RibbonPanelDefinition("绘制",
                    RibbonItemDefinition.Button("连续画线", CommandIds.Line,
                        "连续绘制线段并自动生成长度文字"),
                    RibbonItemDefinition.Button("拱桥开洞", CommandIds.Arch,
                        "在线段交叉位置生成拱桥开洞"),
                    RibbonItemDefinition.Menu("绘制设置", CommandIds.Settings,
                        "调整画线和拱桥参数",
                        RibbonItemDefinition.Button("画线设置", CommandIds.LineSettings,
                            "设置线段文字、高度和位置"),
                        RibbonItemDefinition.Button("开洞设置", CommandIds.ArchSettings,
                            "设置拱桥开洞直径"))),
                new RibbonPanelDefinition("统计",
                    RibbonItemDefinition.Button("图纸统计", CommandIds.Statistics,
                        "统计所选对象并在图纸中生成汇总文字"),
                    RibbonItemDefinition.Button("Excel 报表", CommandIds.StatisticsExcel,
                        "统计所选对象并导出 Excel 报表"))
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

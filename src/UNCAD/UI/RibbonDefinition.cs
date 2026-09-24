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
                new RibbonPanelDefinition("清单",
                    RibbonItemDefinition.Button("U1F 生成", CommandIds.Fill,
                        "新建清单：选择机台和回路，填充 CAD 表并自动输出 BOQ"),
                    RibbonItemDefinition.Button("U1U 更新", CommandIds.FillUpdate,
                        "更新一个或多个已填图框；多个图框会先统一预检再一次性写入"),
                    RibbonItemDefinition.Button("U1S 提交", CommandIds.Submit,
                        "只读取一个或多个当前图框清单，提交项目、数量和米数到 BOQ Excel"),
                    RibbonItemDefinition.Button("U1DWG 导出", CommandIds.DwgExport,
                        "按机台 ID 导出图框内容，并将同机台图框横向排列"),
                    RibbonItemDefinition.Button("XLAYOUT 排版", CommandIds.XLayout,
                        "按机台 ID 将当前图框分行排版，并统计回路数量"),
                    RibbonItemDefinition.Button("Xmerge 合并 DWG", CommandIds.Merge,
                        "拖入 DWG 或文件夹，按 XLAYOUT 规则合并到当前图纸")),
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
                            "使用 400 mm 桥架规格")),
                    RibbonItemDefinition.Button("绘制线条", CommandIds.Line,
                        "连续绘制线段并自动生成长度文字"),
                    RibbonItemDefinition.Button("快速标注距离", CommandIds.LineQuick,
                        "点选线段后逐段快速填写毫米标注（兼容命令 UNLX）"),
                    RibbonItemDefinition.Button("标注转文字", CommandIds.DimensionText,
                        "将对齐标注的文字转换为可编辑单行文字，保留原尺寸线"),
                    RibbonItemDefinition.Button("开拱桥", CommandIds.Arch,
                        "在线段交叉位置生成拱桥开洞")),
                new RibbonPanelDefinition("统计",
                    RibbonItemDefinition.Button("图纸统计", CommandIds.LegacyStatistics,
                        "统计所选对象并生成汇总文字"),
                    RibbonItemDefinition.Button("XSTS 回路统计", CommandIds.Statistics,
                        "框选多张图纸，统计回路数量与缺少回路并导出 Excel")),
                new RibbonPanelDefinition("系统",
                    RibbonItemDefinition.Button("U1SET 设置", CommandIds.Settings,
                        "集中设置数据源、文字、绘图和输出参数"),
                    RibbonItemDefinition.Button("U1DATA 刷新", CommandIds.DataRefresh,
                        "后台刷新配置的机台数据；完成前继续使用上一次成功快照"),
                    RibbonItemDefinition.Button("关于与授权", CommandIds.About,
                        "查看版本、授权状态、使用条款和 UNSIAO.Ltd 信息"),
                    RibbonItemDefinition.Button("U1HELP 命令帮助", CommandIds.Help,
                        "查看全部命令的使用方法、功能和注意事项"))
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

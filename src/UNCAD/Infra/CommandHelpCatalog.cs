using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Infra
{
    /// <summary>Describes one public AutoCAD command for the help page.</summary>
    public sealed class CommandHelpEntry
    {
        public CommandHelpEntry(string command, string name, string usage,
            string function, string notes)
        {
            Command = command ?? "";
            Name = name ?? "";
            Usage = usage ?? "";
            Function = function ?? "";
            Notes = notes ?? "";
        }

        public string Command { get; }
        public string Name { get; }
        public string Usage { get; }
        public string Function { get; }
        public string Notes { get; }
    }

    /// <summary>Single source of truth for all registered command documentation.</summary>
    public static class CommandHelpCatalog
    {
        private static readonly IReadOnlyList<CommandHelpEntry> Entries =
            new List<CommandHelpEntry>
            {
                E(CommandIds.Line, "带标注线段", "U1L", "连续绘制独立 Line 并生成长度文字", "正式命令；长度设置必须为正数毫米，非法值回退 2000mm。"),
                E(CommandIds.LineQuick, "3D 快速绘图", "U1LX", "在独立窗口中重建相连 U1L 线段并快速配置距离", "点击线段后直接输入毫米数字并按 Enter；完成后自动跳到相连线段，支持端点方向判断、正交视图、旋转、框选和一次性写回。"),
                E(CommandIds.LegacyLineQuick, "3D 快速绘图", "UNLX", "调用 U1LX 的 3D 线路编辑功能", "兼容键盘命令名；Ribbon 继续使用 U1LX。"),
                E(CommandIds.LegacyLine, "带标注线段", "UNL", "调用 U1L 的连续线段绘制功能", "兼容旧命令名。"),
                E(CommandIds.Arch, "拱桥开洞", "U1R", "在线段交叉位置生成拱桥开洞", "按当前 U1SET 设置处理。"),
                E(CommandIds.LegacyArch, "拱桥开洞", "UNR", "调用 U1R 的拱桥开洞功能", "兼容旧命令名。"),
                E(CommandIds.Tray100, "桥架 100", "U1Q1", "为选中曲线生成 100 mm 桥架平行标注", "正式命令；标识统一为桥架型号+总长度mm。"),
                E(CommandIds.LegacyTray100, "桥架 100", "UNQ1", "调用 U1Q1 的桥架标注功能", "兼容旧命令名。"),
                E(CommandIds.Tray200, "桥架 200", "U1Q2", "为选中曲线生成 200 mm 桥架平行标注", "正式命令；标识统一为桥架型号+总长度mm。"),
                E(CommandIds.LegacyTray200, "桥架 200", "UNQ2", "调用 U1Q2 的桥架标注功能", "兼容旧命令名。"),
                E(CommandIds.Tray400, "桥架 400", "U1Q4", "为选中曲线生成 400 mm 桥架平行标注", "正式命令；标识统一为桥架型号+总长度mm。"),
                E(CommandIds.LegacyTray400, "桥架 400", "UNQ4", "调用 U1Q4 的桥架标注功能", "兼容旧命令名。"),
                E(CommandIds.Fill, "清单生成", "U1F", "读取机台数据和图纸统计，生成 CAD 清单并同步 BOQ", "写入前执行字段、容量和清单预检。"),
                E(CommandIds.FillUpdate, "清单更新", "U1U", "按当前图框和图纸统计更新已有清单", "多图框统一预检后使用一个 CAD 事务。"),
                E(CommandIds.Submit, "当前图框提交", "U1S", "只读当前图框清单并提交项目数量到 BOQ", "不修改 CAD；支持多个 frame_20260812 图框。"),
                E(CommandIds.Conduit, "线管标注", "U1C", "为选中曲线生成线管平行标注", "使用设置中的固定 2000mm 占位文字；预选 Ruanguan/图框时同步写入软管型号和长度。"),
                E(CommandIds.About, "关于与授权", "U1A", "查看版本、授权、开发者和使用条款", "只读操作。"),
                E(CommandIds.Settings, "配置中心", "U1SET", "集中设置绘图、统计、清单和输出参数", "保存前验证路径和数值范围。"),
                E(CommandIds.DwgExport, "DWG 自动导出", "U1DWG", "按机台导出选中的 frame_20260812 图框", "同机台图框在独立 DWG 中横向排列。"),
                E(CommandIds.XLayout, "图框自动排版", "XLAYOUT", "选择图框后点击排版基准点，按机台 ID 分行排版并统计回路", "直接回车使用原点；每行首框左侧生成高度 25000 的机台 ID 文字。"),
                E(CommandIds.LegacyStatistics, "文字统计汇总", "UNADD", "统计 TEXT/MTEXT 中的电缆、桥架和线管长度", "统计分类可在 U1SET 独立开关。"),
                E(CommandIds.Help, "命令帮助", "U1HELP", "显示全部公开命令的功能、用法和注意事项", "只读操作。")
            };

        public static IReadOnlyList<CommandHelpEntry> All => Entries;

        public static CommandHelpEntry Find(string command)
            => Entries.FirstOrDefault(entry => string.Equals(entry.Command, command,
                StringComparison.OrdinalIgnoreCase));

        private static CommandHelpEntry E(string command, string name, string usage,
            string function, string notes)
            => new CommandHelpEntry(command, name, usage, function, notes);
    }
}

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
                E(CommandIds.LineQuick, "快速标注距离", "U1LX", "点选一根 LINE 线段,从点击端起沿连通路线逐段填写毫米标注", "输入距离回车即写入并自动跳到下一段;直接回车保留原值,Esc 结束;点击在段中部时会询问向哪端走。"),
                E(CommandIds.DimensionText, "对齐标注转文字", "U1DT", "将选中的对齐标注文字转换为可编辑单行文字", "文字高度使用 U1SET 的电缆文字高度，位置和角度跟随原标注；原尺寸线保留。"),
                E(CommandIds.LegacyLineQuick, "快速标注距离", "UNLX", "调用 U1LX 的快速标注功能", "兼容旧键盘命令名。"),
                E(CommandIds.LegacyLine, "带标注线段", "UNL", "调用 U1L 的连续线段绘制功能", "兼容旧命令名。"),
                E(CommandIds.Arch, "拱桥开洞", "U1R", "在线段交叉位置生成拱桥开洞", "生成后将两侧直线和半圆弧合并为一条 Polyline；直径使用 U1SET 设置。"),
                E(CommandIds.LegacyArch, "拱桥开洞", "UNR", "调用 U1R 的拱桥开洞功能", "兼容旧命令名。"),
                E(CommandIds.Tray100, "桥架 100", "U1Q1", "为选中曲线生成 100 mm 桥架平行标注", "正式命令；标识统一为桥架型号+总长度mm。"),
                E(CommandIds.LegacyTray100, "桥架 100", "UNQ1", "调用 U1Q1 的桥架标注功能", "兼容旧命令名。"),
                E(CommandIds.Tray200, "桥架 200", "U1Q2", "为选中曲线生成 200 mm 桥架平行标注", "正式命令；标识统一为桥架型号+总长度mm。"),
                E(CommandIds.LegacyTray200, "桥架 200", "UNQ2", "调用 U1Q2 的桥架标注功能", "兼容旧命令名。"),
                E(CommandIds.Tray400, "桥架 400", "U1Q4", "为选中曲线生成 400 mm 桥架平行标注", "正式命令；标识统一为桥架型号+总长度mm。"),
                E(CommandIds.LegacyTray400, "桥架 400", "UNQ4", "调用 U1Q4 的桥架标注功能", "兼容旧命令名。"),
                E(CommandIds.Fill, "清单生成", "U1F", "读取机台数据和图纸统计，生成 CAD 清单并同步 BOQ", "写入前执行字段、容量和清单预检。"),
                E(CommandIds.FillUpdate, "清单更新", "U1U", "按当前图框和图纸统计更新已有清单", "多图框统一预检后使用一个 CAD 事务。"),
                E(CommandIds.Submit, "当前图框提交", "U1S", "只读当前图框清单并提交项目数量到 BOQ", "不修改 CAD；支持多个 frame_20260812/frame/xframe 图框。"),
                E(CommandIds.Conduit, "线管标注", "U1C", "为选中曲线生成线管平行标注", "使用设置中的固定 2000mm 占位文字；预选 Ruanguan/图框时同步写入软管型号和长度。"),
                E(CommandIds.About, "关于与授权", "U1A", "查看版本、授权、开发者和使用条款", "只读操作。"),
                E(CommandIds.Settings, "配置中心", "U1SET", "集中设置绘图、统计、清单和输出参数", "保存前验证路径和数值范围。"),
                E(CommandIds.DataRefresh, "刷新机台数据", "U1DATA", "在后台刷新 U1SET 配置的本地或远程机台工作簿", "刷新完成前各命令继续读取上一次成功的 SQLite 快照；失败不会替换旧数据。"),
                E(CommandIds.DwgExportShort, "DWG 自动导出快捷命令", "U1D", "调用 U1DWG 导出选中的图框", "与 U1DWG 功能完全相同。"),
                E(CommandIds.DwgExport, "DWG 自动导出", "U1DWG", "按机台导出选中的 frame_20260812/frame/xframe 图框", "同机台图框在独立 DWG 中横向排列。"),
                E(CommandIds.XLayout, "图框自动排版", "XLAYOUT", "选择图框后点击排版基准点，按机台 ID 分行排版并统计回路", "直接回车使用原点；每行首框左侧生成高度 25000 的机台 ID 文字。"),
                E(CommandIds.Statistics, "机台回路统计", "XSTS", "先统计框选图纸中的机台，再仅比较这些机台已框选、应有和缺少的回路，并导出 xlsx", "不会把未框选的其他机台计入应有回路；期望回路优先读取 U1SET 配置的机台 Excel。"),
                E(CommandIds.Merge, "DWG 批量合并", "Xmerge", "通过 GUI 拖入 DWG 或文件夹，递归收集并按 XLAYOUT 规则合并到当前图纸", "文件夹会遍历所有子目录；源文件只读，不会被修改。"),
                E(CommandIds.LegacyStatistics, "文字统计汇总", "UNADD", "统计 TEXT/MTEXT 中的电缆、桥架和线管长度", "多行文字和统计分类可在 U1SET 独立开关。"),
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

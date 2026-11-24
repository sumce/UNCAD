using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 制图信息表（图框内的小表：专业 / 楼层 / 制图 / 审核 / 日期 / 版本）六列的
    /// 内容模板（D-057）。每一列是一段文本，可以写固定内容，也可以夹占位符
    /// （全角/半角括号均可，如 <c>(机台ID)</c>、<c>（U1U更新时间）</c>）。
    ///
    /// 3.5.0 及之前这六格是写死的：只写「楼层 = 设备楼层/上游楼层」和
    /// 「日期 = 运行当天」，专业/制图/审核/版本从不碰。现在：
    /// - 模板留空 = 不改这一格（图框里现有的内容保留，可以手工在图框里改）；
    /// - 默认模板与旧行为完全一致（楼层 <c>(楼层)</c>、日期 <c>(日期)</c>，其余空）。
    /// </summary>
    public sealed class DrawingInfoTemplates
    {
        public const int ColumnCount = 6;
        public static readonly string[] ColumnNames =
            { "专业", "楼层", "制图", "审核", "日期", "版本" };

        public const string DefaultProfession = "";
        public const string DefaultFloor = "(楼层)";
        public const string DefaultDrafter = "";
        public const string DefaultReviewer = "";
        public const string DefaultDate = "(日期)";
        public const string DefaultVersion = "";
        /// <summary>单列模板的长度上限：图框小表格子放不下长文，也防误粘整段表格。</summary>
        public const int MaximumTemplateLength = 200;

        private DrawingInfoTemplates(string[] columns)
        {
            Columns = Array.AsReadOnly(columns);
        }

        /// <summary>与 3.5.0 行为一致的默认模板。</summary>
        public static DrawingInfoTemplates Default { get; } = Create(
            DefaultProfession, DefaultFloor, DefaultDrafter, DefaultReviewer,
            DefaultDate, DefaultVersion);

        public IReadOnlyList<string> Columns { get; }
        public string Profession => Columns[0];
        public string Floor => Columns[1];
        public string Drafter => Columns[2];
        public string Reviewer => Columns[3];
        public string Date => Columns[4];
        public string Version => Columns[5];

        /// <summary>这一列有模板才会写入；留空的列不动图框里现有的内容。</summary>
        public bool IsSet(int column)
            => column >= 0 && column < ColumnCount && Columns[column].Length > 0;

        public bool IsDefault => Columns.SequenceEqual(Default.Columns);

        public static DrawingInfoTemplates Create(string profession, string floor,
            string drafter, string reviewer, string date, string version)
            => new DrawingInfoTemplates(new[]
            {
                Clamp(profession), Clamp(floor), Clamp(drafter), Clamp(reviewer),
                Clamp(date), Clamp(version)
            });

        private static string Clamp(string value)
        {
            string text = (value ?? "").Trim();
            return text.Length > MaximumTemplateLength
                ? text.Substring(0, MaximumTemplateLength) : text;
        }
    }

    /// <summary>渲染制图信息表用的输入：台账行 + 本次运行的时间/命令/用户。</summary>
    public sealed class DrawingInfoContext
    {
        public MachineRow Machine { get; set; }
        /// <summary>本次运行的本地时间（(日期)/(时间) 的来源）。</summary>
        public DateTime Now { get; set; } = DateTime.Now;
        /// <summary>创建时间（最近一次 U1F，UTC “o” 文本）。</summary>
        public string FilledUtc { get; set; } = "";
        /// <summary>最近一次写入时间（UTC “o” 文本）。</summary>
        public string LastModifiedUtc { get; set; } = "";
        /// <summary>本次运行后记录里的命令名（"U1F"/"U1U"）。</summary>
        public string LastCommand { get; set; } = "";
        public string User { get; set; } = "";

        /// <summary>
        /// 某次 U1F/U1U 运行的上下文。时间/命令的取法与
        /// <see cref="FrameInfoJsonRecordUpdater"/> 写进 frameinfo_json 的完全同源：
        /// 写进图框小表的时间和 JSON 里记的时间不会对不上。
        /// </summary>
        public static DrawingInfoContext ForRun(MachineRow machine,
            FrameInfoJsonRecord previous, string commandName, DateTime utcNow,
            string user = null)
        {
            return new DrawingInfoContext
            {
                Machine = machine,
                Now = ToLocal(utcNow),
                FilledUtc = FrameInfoJsonRecordUpdater.ResolveFilledUtc(previous,
                    commandName, utcNow),
                LastModifiedUtc = utcNow.ToUniversalTime().ToString("o"),
                LastCommand = FrameInfoJsonRecordUpdater.NormalizeCommand(commandName),
                User = FirstNonEmpty(user, Environment.UserName, Environment.MachineName)
            };
        }

        /// <summary>设置页预览用的样例上下文（固定数据，结果可复现）。</summary>
        public static DrawingInfoContext Sample() => new DrawingInfoContext
        {
            Machine = new MachineRow
            {
                Region = "AMHS", MachineId = "MEDPC01", CircuitName = "AC Rack",
                Batch = "B3", Seq = "07", Fr = "3N-11B1A-PP-02",
                DeviceFloor = "3F", PanelFloor = "2F", DownstreamAxis = "C-05",
                UpstreamAxis = "A-01", FacilitySwitch = "FS-12", Next = "I-Line盘",
                Cable = "ZB-YJV-3*25", Detail = "N480 3P4W 3P100A"
            },
            Now = new DateTime(2026, 11, 25, 14, 30, 0, DateTimeKind.Unspecified),
            FilledUtc = "2026-11-24T01:05:00.0000000Z",
            LastModifiedUtc = "2026-11-25T06:30:00.0000000Z",
            LastCommand = "U1U",
            User = Environment.UserName
        };

        private static DateTime ToLocal(DateTime utcNow)
            => utcNow.ToUniversalTime().ToLocalTime();

        private static string FirstNonEmpty(params string[] values)
            => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
                ?? "";
    }

    /// <summary>占位符语法：<c>(名称)</c> 或 <c>（名称）</c>，可带格式 <c>(名称:yyyy-MM-dd)</c>。</summary>
    internal static class PlaceholderPattern
    {
        private static readonly Regex Pattern =
            new Regex(@"[（(]([^（）()]+)[）)]", RegexOptions.Compiled);

        /// <summary>
        /// 把模板里的占位符换成值。resolve 收到（名称, 格式），返回 null 表示
        /// 不是占位符——原文保留（用户写的普通括号文字不受影响）。
        /// </summary>
        public static string Replace(string template,
            Func<string, string, string> resolve)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return Pattern.Replace(template, match =>
            {
                string token = match.Groups[1].Value.Trim();
                int colon = token.IndexOf(':');
                if (colon < 0) colon = token.IndexOf('：');
                string name = colon >= 0 ? token.Substring(0, colon).Trim() : token;
                string format = colon >= 0
                    ? token.Substring(colon + 1).Trim().Replace('：', ':') : "";
                string value = resolve(name, format);
                return value ?? match.Value;
            });
        }
    }

    /// <summary>把制图信息表模板渲染成六格的文本。</summary>
    public static class DrawingInfoTemplateRenderer
    {
        /// <summary>可用占位符与一句话说明（设置页的占位符表）。</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Placeholders =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("机台ID", "台账 U_机台ID"),
                new KeyValuePair<string, string>("区域", "台账 U_区域"),
                new KeyValuePair<string, string>("回路名称", "台账 回路名称"),
                new KeyValuePair<string, string>("批次", "台账 U_批次"),
                new KeyValuePair<string, string>("序号", "台账 U_序号"),
                new KeyValuePair<string, string>("上游编号", "台账 U_上游编号（去掉槽位/轴位标注）"),
                new KeyValuePair<string, string>("设备楼层", "台账 U_设备楼层"),
                new KeyValuePair<string, string>("上游楼层", "台账 U_上游楼层"),
                new KeyValuePair<string, string>("楼层", "设备楼层/上游楼层（空的不写，与旧版一致）"),
                new KeyValuePair<string, string>("设备轴位", "台账 U_设备轴位"),
                new KeyValuePair<string, string>("上游轴位", "台账 U_上游轴位"),
                new KeyValuePair<string, string>("厂务开关", "台账 U_厂务开关"),
                new KeyValuePair<string, string>("上游类型", "台账 U_上游类型"),
                new KeyValuePair<string, string>("电缆型号", "台账 U_电缆型号"),
                new KeyValuePair<string, string>("配电信息", "台账 U_配电信息"),
                new KeyValuePair<string, string>("日期", "本次运行日期（yyyy-MM-dd，旧版「日期」格的写法）"),
                new KeyValuePair<string, string>("时间", "本次运行时刻（HH:mm:ss）"),
                new KeyValuePair<string, string>("U1F创建时间", "最近一次 U1F（清单创建）的时间"),
                new KeyValuePair<string, string>("U1U更新时间", "最近一次 U1U 的时间；做过 U1F 后还没 U1U 过则为空"),
                new KeyValuePair<string, string>("更新时间", "最近一次 U1F 或 U1U 的时间"),
                new KeyValuePair<string, string>("用户", "执行命令的 Windows 用户")
            };

        /// <summary>时间类占位符的默认格式：日期（与旧版「日期」格一致）。</summary>
        public const string DefaultTimeFormat = "yyyy-MM-dd";

        public static IReadOnlyList<string> Render(DrawingInfoTemplates templates,
            DrawingInfoContext context)
        {
            if (templates == null) templates = DrawingInfoTemplates.Default;
            return templates.Columns
                .Select(column => Resolve(column, context))
                .ToList();
        }

        public static string Resolve(string template, DrawingInfoContext context)
            => PlaceholderPattern.Replace(template,
                (name, format) => Value(name, format, context));

        /// <summary>模板里写了、但不在占位符表里的名字（设置页用来提醒拼写错误）。</summary>
        public static IReadOnlyList<string> UnknownPlaceholders(string template)
        {
            var known = new HashSet<string>(Placeholders.Select(pair => pair.Key),
                StringComparer.Ordinal);
            var unknown = new List<string>();
            PlaceholderPattern.Replace(template ?? "", (name, format) =>
            {
                if (!known.Contains(name)) unknown.Add(name);
                // 返回占位符本身：只为收集名字，不关心结果。
                return null;
            });
            return unknown;
        }

        private static string Value(string name, string format, DrawingInfoContext context)
        {
            MachineRow machine = context?.Machine;
            DateTime now = context?.Now ?? DateTime.Now;
            switch (name)
            {
                case "机台ID": return Clean(machine?.MachineId);
                case "区域": return Clean(machine?.Region);
                case "回路名称": return Clean(machine?.CircuitName);
                case "批次": return Clean(machine?.Batch);
                case "序号": return Clean(machine?.Seq);
                case "上游编号": return CircuitIdentityKey.UpstreamDisplay(machine?.Fr);
                case "设备楼层": return Clean(machine?.DeviceFloor);
                case "上游楼层": return Clean(machine?.PanelFloor);
                case "楼层": return JoinFloors(machine?.DeviceFloor, machine?.PanelFloor);
                case "设备轴位": return Clean(machine?.DownstreamAxis);
                case "上游轴位": return Clean(machine?.UpstreamAxis);
                case "厂务开关": return Clean(machine?.FacilitySwitch);
                case "上游类型": return Clean(machine?.Next);
                case "电缆型号": return Clean(machine?.Cable);
                case "配电信息": return Clean(machine?.Detail);
                case "日期": return SafeFormat(now, format, DefaultTimeFormat);
                case "时间": return SafeFormat(now, format, "HH:mm:ss");
                case "U1F创建时间": return FormatUtc(context?.FilledUtc, format);
                case "U1U更新时间": return context != null
                    && FrameInfoJsonRecordUpdater.NormalizeCommand(context.LastCommand) == "U1U"
                    ? FormatUtc(context.LastModifiedUtc, format) : "";
                case "更新时间": return FormatUtc(context?.LastModifiedUtc, format);
                case "用户": return Clean(context?.User);
                default: return null;
            }
        }

        private static string FormatUtc(string utcText, string format)
        {
            string text = (utcText ?? "").Trim();
            if (text.Length == 0) return "";
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime utc))
                return "";
            return SafeFormat(utc.ToLocalTime(), format, DefaultTimeFormat);
        }

        private static string SafeFormat(DateTime value, string format, string fallback)
        {
            string pattern = (format ?? "").Trim();
            if (pattern.Length == 0) pattern = fallback;
            try { return value.ToString(pattern, CultureInfo.InvariantCulture); }
            catch (FormatException) { return value.ToString(fallback, CultureInfo.InvariantCulture); }
        }

        private static string Clean(string value) => (value ?? "").Trim();

        /// <summary>与旧版「楼层」格同一拼法：设备楼层/上游楼层，空的不写。</summary>
        private static string JoinFloors(string deviceFloor, string upstreamFloor)
            => string.Join("/", new[] { deviceFloor, upstreamFloor }
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0));
    }

    /// <summary>
    /// 清单（BOQ）工作簿标题的三行模板（D-057）：模板 xlsx 的 A1/A2/A3 是写死的
    /// 「新项目项目 / 二次配Power工程 / 详细价目表 Breakdown Sheet」。留空 = 用
    /// 模板里现有的文字（或用户在清单文件里手改过的）；填了就按模板重写。
    /// 可用占位符：(机台ID) (日期) (时间) (用户)。
    /// </summary>
    public sealed class BoqTitleTemplates
    {
        public const string DefaultLine1 = "";
        public const string DefaultLine2 = "";
        public const string DefaultLine3 = "";
        public const int MaximumTemplateLength = 200;

        private BoqTitleTemplates(string[] lines) { Lines = Array.AsReadOnly(lines); }

        public static BoqTitleTemplates Default { get; } = Create("", "", "");

        /// <summary>三行标题（A1/A2/A3），顺序固定。</summary>
        public IReadOnlyList<string> Lines { get; }
        public bool IsSet => Lines.Any(line => line.Length > 0);

        public static BoqTitleTemplates Create(string line1, string line2, string line3)
            => new BoqTitleTemplates(new[] { line1, line2, line3 }
                .Select(line => (line ?? "").Trim())
                .Select(line => line.Length > MaximumTemplateLength
                    ? line.Substring(0, MaximumTemplateLength) : line).ToArray());

        public string Render(int line, string machineId, DateTime now, string user)
        {
            string template = line >= 0 && line < Lines.Count ? Lines[line] : "";
            return PlaceholderPattern.Replace(template, (name, format) =>
            {
                switch (name)
                {
                    case "机台ID": return (machineId ?? "").Trim();
                    case "日期": return SafeDate(now, format, "yyyy-MM-dd");
                    case "时间": return SafeDate(now, format, "HH:mm:ss");
                    case "用户": return (user ?? "").Trim();
                    default: return null;
                }
            });
        }

        private static string SafeDate(DateTime value, string format, string fallback)
        {
            string pattern = (format ?? "").Trim();
            if (pattern.Length == 0) pattern = fallback;
            try { return value.ToString(pattern, CultureInfo.InvariantCulture); }
            catch (FormatException) { return value.ToString(fallback, CultureInfo.InvariantCulture); }
        }
    }
}

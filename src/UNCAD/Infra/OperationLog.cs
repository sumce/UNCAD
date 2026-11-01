using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace UNCAD.Infra
{
    /// <summary>一次用户操作的操作记录（JSONL 一行）。只读模型，供“最近操作记录”界面显示。</summary>
    public sealed class OperationLogEntry
    {
        internal OperationLogEntry()
        {
            Command = "";
            Summary = "";
            Result = "";
            Detail = "";
            OutputPaths = Array.Empty<string>();
        }

        /// <summary>操作开始时间（本地时间并带 UTC 偏移）。</summary>
        public DateTimeOffset Timestamp { get; internal set; }
        /// <summary>命令标识（优先取 Log.Command 作用域标签）。</summary>
        public string Command { get; internal set; }
        /// <summary>用户可读的操作摘要。</summary>
        public string Summary { get; internal set; }
        /// <summary>结果：SUCCESS / FAILED / CANCELLED。</summary>
        public string Result { get; internal set; }
        /// <summary>完成/失败细节；失败时包含异常类型与消息。</summary>
        public string Detail { get; internal set; }
        /// <summary>本次操作涉及的机台/回路去重计数。</summary>
        public int MachineCount { get; internal set; }
        /// <summary>输出文件（kind: path 形式，去重）。</summary>
        public IReadOnlyList<string> OutputPaths { get; internal set; }
        /// <summary>耗时（毫秒）。</summary>
        public long DurationMs { get; internal set; }
        /// <summary>执行线程号，便于和文件日志对照。</summary>
        public int ThreadId { get; internal set; }
    }

    /// <summary>
    /// 操作记录：把一次用户操作以 JSONL 追加到 %APPDATA%\UNCAD\history\operations-YYYYMM.jsonl，
    /// 默认保留 6 个月（U1SET 可改 UNC_HISTORY_RETENTION_MONTHS）。
    /// 只做本地审计，不联网；写入失败只 Log.Warn，绝不影响命令主流程。并发写入用静态锁串行化。
    /// </summary>
    public static class OperationLog
    {
        internal const int DefaultRetentionMonths = 6;
        private const int MaxRetentionMonths = 120;
        private const string FilePrefix = "operations-";
        private const string FileSuffix = ".jsonl";
        private static readonly object Sync = new object();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static string _overrideDirectory;
        private static int? _overrideRetentionMonths;

        /// <summary>操作记录目录（测试可重定向）。目录不可写时退到临时目录，仍不可用则记录变成空操作。</summary>
        public static string Directory
        {
            get
            {
                lock (Sync)
                    return _overrideDirectory ?? ResolveDirectory();
            }
        }

        /// <summary>开始一次操作记录；using 结束仍未 Complete/Fail 时按 CANCELLED 落盘。</summary>
        public static OperationLogScope Begin(string command, string summary = null)
            => new OperationLogScope(command, summary);

        /// <summary>读取最近 max 条操作记录（新的在前），跳过损坏行；读取失败返回空列表。</summary>
        public static IReadOnlyList<OperationLogEntry> ReadRecent(int max = 200)
        {
            if (max <= 0) return Array.Empty<OperationLogEntry>();
            var entries = new List<OperationLogEntry>();
            string directory = Directory;
            if (directory.Length == 0) return entries;
            try
            {
                string[] files = System.IO.Directory.GetFiles(directory,
                    FilePrefix + "*" + FileSuffix);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int index = files.Length - 1; index >= 0 && entries.Count < max; index--)
                {
                    List<string> lines = ReadLines(files[index]);
                    for (int line = lines.Count - 1; line >= 0 && entries.Count < max; line--)
                    {
                        OperationLogEntry entry = TryParse(lines[line]);
                        if (entry != null) entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取操作记录失败: " + ex.Message);
            }
            return entries;
        }

        /// <summary>用资源管理器打开操作记录目录；失败只写日志，不打断命令。</summary>
        public static void OpenFolder()
        {
            try
            {
                string directory = Directory;
                if (directory.Length == 0) return;
                System.IO.Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn("打开操作记录目录失败: " + ex.Message);
            }
        }

        internal static void SetDirectoryForTests(string directory)
        {
            lock (Sync) _overrideDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        }

        internal static void ResetDirectoryForTests()
        {
            lock (Sync)
            {
                _overrideDirectory = null;
                _overrideRetentionMonths = null;
            }
        }

        internal static void SetRetentionMonthsForTests(int? months)
        {
            lock (Sync) _overrideRetentionMonths = months;
        }

        /// <summary>删除超过保留月数的 operations-YYYYMM.jsonl；返回删除数量（测试可直接调用）。</summary>
        internal static int PurgeOldHistory(DateTime now)
        {
            string directory = Directory;
            if (directory.Length == 0) return 0;
            int months = SafeRetentionMonths(ConfiguredRetentionMonths());
            DateTime cutoff = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));
            int removed = 0;
            lock (Sync)
            {
                try
                {
                    foreach (string file in System.IO.Directory.GetFiles(directory,
                        FilePrefix + "*" + FileSuffix))
                    {
                        if (!TryParseFileMonth(Path.GetFileNameWithoutExtension(file),
                            out DateTime month)) continue;
                        if (month >= cutoff) continue;
                        try
                        {
                            File.Delete(file);
                            removed++;
                        }
                        catch { /* 被占用时下次再清理 */ }
                    }
                }
                catch { /* 目录读取失败不影响写记录 */ }
            }
            return removed;
        }

        internal static void Append(OperationLogEntry entry)
        {
            if (entry == null) return;
            string directory = Directory;
            if (directory.Length == 0)
            {
                Log.Warn("操作记录目录不可用，已跳过记录: " + entry.Command);
                return;
            }
            DateTime now = entry.Timestamp.LocalDateTime;
            lock (Sync)
            {
                try
                {
                    PurgeOldHistoryLocked(now);
                    System.IO.Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, FileNameFor(now));
                    File.AppendAllText(path, Serialize(entry) + Environment.NewLine, Utf8NoBom);
                }
                catch (Exception ex)
                {
                    // 记录失败只提示，不影响已经完成的命令。
                    Log.Warn("写入操作记录失败: " + ex.Message);
                }
            }
        }

        private static void PurgeOldHistoryLocked(DateTime now)
        {
            // 已在 Sync 锁内；复用公开清理逻辑会重入同一把锁（lock 可重入），保持顺序简单。
            int months = SafeRetentionMonths(ConfiguredRetentionMonths());
            DateTime cutoff = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));
            try
            {
                foreach (string file in System.IO.Directory.GetFiles(Directory,
                    FilePrefix + "*" + FileSuffix))
                {
                    if (!TryParseFileMonth(Path.GetFileNameWithoutExtension(file),
                        out DateTime month)) continue;
                    if (month >= cutoff) continue;
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        private static int ConfiguredRetentionMonths()
        {
            lock (Sync)
                if (_overrideRetentionMonths.HasValue)
                    return SafeRetentionMonths(_overrideRetentionMonths.Value);
            try
            {
                return SafeRetentionMonths(Settings.GetDouble(
                    ConfigKeys.HistoryRetentionMonths, DefaultRetentionMonths));
            }
            catch { return DefaultRetentionMonths; }
        }

        private static int SafeRetentionMonths(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultRetentionMonths;
            int months = (int)value;
            if (months < 1) return DefaultRetentionMonths;
            return months > MaxRetentionMonths ? MaxRetentionMonths : months;
        }

        internal static string FileNameFor(DateTime date)
            => FilePrefix + date.ToString("yyyyMM", CultureInfo.InvariantCulture) + FileSuffix;

        private static bool TryParseFileMonth(string nameWithoutExtension, out DateTime month)
        {
            month = default;
            if (string.IsNullOrEmpty(nameWithoutExtension)
                || !nameWithoutExtension.StartsWith(FilePrefix,
                    StringComparison.OrdinalIgnoreCase)
                || nameWithoutExtension.Length != FilePrefix.Length + 6)
                return false;
            return DateTime.TryParseExact(
                nameWithoutExtension.Substring(FilePrefix.Length), "yyyyMM",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
        }

        private static List<string> ReadLines(string path)
        {
            var lines = new List<string>();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.Trim().Length > 0) lines.Add(line);
                }
            }
            catch { /* 读取失败按空文件处理 */ }
            return lines;
        }

        private static string Serialize(OperationLogEntry entry)
        {
            var payload = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["timestamp"] = entry.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                ["command"] = entry.Command ?? "",
                ["summary"] = entry.Summary ?? "",
                ["result"] = entry.Result ?? "",
                ["detail"] = entry.Detail ?? "",
                ["machineCount"] = entry.MachineCount,
                ["outputPaths"] = entry.OutputPaths?.ToArray() ?? Array.Empty<string>(),
                ["durationMs"] = entry.DurationMs,
                ["threadId"] = entry.ThreadId
            };
            return Json.Serialize(payload);
        }

        private static OperationLogEntry TryParse(string line)
        {
            try
            {
                var payload = Json.DeserializeObject(line) as Dictionary<string, object>;
                if (payload == null) return null;
                if (!DateTimeOffset.TryParse(Convert.ToString(payload["timestamp"],
                        CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
                    timestamp = DateTimeOffset.MinValue;
                var entry = new OperationLogEntry
                {
                    Timestamp = timestamp,
                    Command = Convert.ToString(payload["command"], CultureInfo.InvariantCulture)
                        ?? "",
                    Summary = Convert.ToString(payload["summary"], CultureInfo.InvariantCulture)
                        ?? "",
                    Result = Convert.ToString(payload["result"], CultureInfo.InvariantCulture)
                        ?? "",
                    Detail = Convert.ToString(payload["detail"], CultureInfo.InvariantCulture)
                        ?? "",
                    MachineCount = ReadInt(payload, "machineCount"),
                    OutputPaths = ReadStrings(payload, "outputPaths"),
                    DurationMs = ReadLong(payload, "durationMs"),
                    ThreadId = ReadInt(payload, "threadId")
                };
                return entry;
            }
            catch { return null; }
        }

        private static int ReadInt(IDictionary<string, object> payload, string key)
        {
            if (!payload.TryGetValue(key, out object raw) || raw == null) return 0;
            try { return Convert.ToInt32(raw, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static long ReadLong(IDictionary<string, object> payload, string key)
        {
            if (!payload.TryGetValue(key, out object raw) || raw == null) return 0;
            try { return Convert.ToInt64(raw, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static IReadOnlyList<string> ReadStrings(
            IDictionary<string, object> payload, string key)
        {
            if (!payload.TryGetValue(key, out object raw) || raw == null)
                return Array.Empty<string>();
            if (raw is object[] array)
                return array.Select(item => Convert.ToString(item,
                    CultureInfo.InvariantCulture) ?? "").ToArray();
            string single = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(single)
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : new[] { single };
        }

        private static string ResolveDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UNCAD", "history"),
                Path.Combine(Path.GetTempPath(), "UNCAD", "history")
            };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    System.IO.Directory.CreateDirectory(candidate);
                    return candidate;
                }
                catch { /* 换下一个候选目录 */ }
            }
            return "";
        }
    }

    /// <summary>
    /// 单次操作记录作用域：收集机台/输出后一次性落盘。同一实例可多线程调用，重复 Complete/Fail 只记一次。
    /// </summary>
    public sealed class OperationLogScope : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly HashSet<string> _machines =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _outputPaths = new List<string>();
        private readonly string _command;
        private readonly string _summary;
        private readonly int _threadId;
        private string _result;
        private string _detail;
        private bool _written;

        internal OperationLogScope(string command, string summary)
        {
            string tag = Log.CurrentCommand;
            _command = (command ?? "").Trim();
            if (_command.Length == 0) _command = tag;
            _summary = (summary ?? "").Trim();
            _threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>登记本次操作涉及的机台/回路（去重）。</summary>
        public void Machine(string machineId, string circuit = null)
        {
            string machine = (machineId ?? "").Trim();
            string circuitName = (circuit ?? "").Trim();
            if (machine.Length == 0 && circuitName.Length == 0) return;
            lock (_lock) _machines.Add(machine + "\u001f" + circuitName);
        }

        /// <summary>登记一个输出文件；同一路径只记一次。</summary>
        public void Output(string kind, string path)
        {
            string normalizedPath = (path ?? "").Trim();
            if (normalizedPath.Length == 0) return;
            string label = string.IsNullOrWhiteSpace(kind)
                ? normalizedPath
                : kind.Trim() + ": " + normalizedPath;
            lock (_lock)
            {
                if (!_outputPaths.Contains(label)) _outputPaths.Add(label);
            }
        }

        /// <summary>标记成功并立即落盘；重复调用只保留第一次结果。</summary>
        public void Complete(string detail = null)
            => Finish("SUCCESS", detail);

        /// <summary>标记用户取消（ESC/空选择）并落盘；取消不算失败，Result=CANCELLED。</summary>
        public void Cancel(string detail = null)
            => Finish("CANCELLED", detail);

        /// <summary>标记失败并立即落盘；异常只记录类型和消息，完整堆栈在文件日志。</summary>
        public void Fail(string detail, Exception ex = null)
        {
            string failure = (detail ?? "").Trim();
            if (ex != null)
            {
                string exceptionText = ex.GetType().Name + ": " + ex.Message;
                failure = failure.Length == 0 ? exceptionText : failure + " | " + exceptionText;
            }
            Finish("FAILED", failure);
        }

        private void Finish(string result, string detail)
        {
            OperationLogEntry entry;
            lock (_lock)
            {
                if (_written) return;
                _written = true;
                _result = result;
                _detail = detail ?? "";
                _stopwatch.Stop();
                entry = new OperationLogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Command = _command,
                    Summary = _summary,
                    Result = _result,
                    Detail = _detail,
                    MachineCount = _machines.Count,
                    OutputPaths = _outputPaths.ToArray(),
                    DurationMs = _stopwatch.ElapsedMilliseconds,
                    ThreadId = _threadId
                };
            }
            OperationLog.Append(entry);
        }

        /// <summary>
        /// using 结束仍未 Complete/Fail 时补一条成功记录（与 Cad/CommandBase 的约定一致：
        /// 成功路径显式 Complete，异常路径先 Fail 再抛出，这里只是兜底，保证操作不丢记录）。
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_written) return;
            }
            Finish("SUCCESS", null);
        }
    }
}

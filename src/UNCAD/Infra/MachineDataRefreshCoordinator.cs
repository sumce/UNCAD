using System;
using System.Collections.Generic;
using System.Threading;
using UNCAD.Core.Excel;

namespace UNCAD.Infra
{
    internal enum MachineDataRefreshStartStatus
    {
        Started,
        NoSource,
        LocalSourceSkipped,
        AlreadyRunning
    }

    /// <summary>
    /// Runs machine-workbook refreshes away from the AutoCAD thread while commands
    /// continue reading the last successful SQLite snapshot.
    /// </summary>
    internal sealed class MachineDataRefreshCoordinator
    {
        private readonly Func<string, MachineWorkbookSourceResult> _refresh;
        private readonly Action<Action> _queue;
        private readonly MachineLedgerReporter _ledger;
        private readonly Func<string, bool> _hasSnapshot;
        private readonly object _noticeGate = new object();
        private readonly Queue<string> _notices = new Queue<string>();
        private int _refreshing;

        internal static MachineDataRefreshCoordinator Default { get; } =
            new MachineDataRefreshCoordinator(MachineWorkbookSource.Refresh,
                work => ThreadPool.QueueUserWorkItem(_ => work()));

        internal MachineDataRefreshCoordinator(
            Func<string, MachineWorkbookSourceResult> refresh,
            Action<Action> queue,
            MachineLedgerReporter ledger = null,
            Func<string, bool> hasSnapshot = null)
        {
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _ledger = ledger ?? MachineLedgerReporter.Default;
            _hasSnapshot = hasSnapshot ?? MachineWorkbookSource.HasSnapshot;
        }

        internal MachineDataRefreshStartStatus TryStartStartupRefresh(string source)
        {
            string value = (source ?? "").Trim();
            if (value.Length == 0) return MachineDataRefreshStartStatus.NoSource;
            if (!MachineWorkbookSource.IsRemote(value))
                return MachineDataRefreshStartStatus.LocalSourceSkipped;
            return TryStart(value, true);
        }

        internal MachineDataRefreshStartStatus TryStartManualRefresh(string source)
        {
            string value = (source ?? "").Trim();
            return value.Length == 0
                ? MachineDataRefreshStartStatus.NoSource
                : TryStart(value, false);
        }

        internal bool TryTakeNotice(out string message)
        {
            lock (_noticeGate)
            {
                if (_notices.Count == 0)
                {
                    message = null;
                    return false;
                }
                message = _notices.Dequeue();
                return true;
            }
        }

        /// <summary>
        /// 把后台任务（台账上报 / U1F·U1U·U1DWG 数据上传）的提示排入同一队列，
        /// 由 Bootstrap 的 Idle 循环统一输出到命令行——后台线程不能直接写 Editor。
        /// </summary>
        internal static void EnqueueNotice(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Default.AddNotice(message);
        }

        private MachineDataRefreshStartStatus TryStart(string source, bool startup)
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
                return MachineDataRefreshStartStatus.AlreadyRunning;
            try
            {
                _queue(() => Refresh(source, startup));
                return MachineDataRefreshStartStatus.Started;
            }
            catch
            {
                Interlocked.Exchange(ref _refreshing, 0);
                throw;
            }
        }

        private void Refresh(string source, bool startup)
        {
            try
            {
                MachineWorkbookSourceResult result = _refresh(source);
                if (result.UsedCachedFallback)
                {
                    Log.Warn("机台数据后台刷新使用旧快照" + Detail(result.Warning));
                    // 启动时的网络失败只是噪音：命令照旧读上一次成功的快照。
                    // 只有连快照都没有（首次使用）才值得打断用户。
                    if (!startup || !_hasSnapshot(source))
                        AddNotice("机台数据后台刷新失败，继续使用上一次成功数据"
                            + Detail(result.Warning) + "。");
                    return;
                }

                int rows = result.Snapshot?.RowCount ?? 0;
                Log.Info("机台数据后台刷新完成: " + rows + " 行");
                if (!startup)
                    AddNotice("机台数据已后台更新，共 " + rows + " 行"
                        + Detail(result.Warning) + "。");
                ReportLedger(source, startup);
            }
            catch (Exception ex)
            {
                // Parsing and SQLite replacement are atomic, so the old snapshot
                // remains available even when download or parsing fails.
                Log.Error("机台数据后台刷新失败", ex);
                if (!startup || !_hasSnapshot(source))
                    AddNotice("机台数据后台刷新失败，原 SQLite 快照未被替换: "
                        + ex.Message + "（可在 U1SET 把数据源改为本地文件）");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private readonly HashSet<string> _announced = new HashSet<string>(
            StringComparer.Ordinal);

        /// <summary>同一条提示一个会话只出一次，避免反复刷新时刷屏。</summary>
        private void AddNotice(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            lock (_noticeGate)
            {
                if (!_announced.Add(message)) return;
                _notices.Enqueue(message);
            }
        }

        /// <summary>
        /// 刷新成功后把台账上报给服务端（机台数据的唯一写入口）。
        /// 上报失败绝不影响本次刷新结论：只记日志 + 命令行提示，下次刷新自动重试；
        /// 只有 409（本机授权已被其它设备接管）会明确要求重新登录。
        /// </summary>
        private void ReportLedger(string source, bool startup)
        {
            try
            {
                MachineLedgerReportOutcome outcome = _ledger.Report(source);
                if (!string.IsNullOrWhiteSpace(outcome.Message))
                {
                    // 启动时上报失败只记日志，避免每次开图都被"网络错误"打断。
                    if (startup) Log.Warn("台账上报提示: " + outcome.Message);
                    else AddNotice(outcome.Message);
                }
            }
            catch (Exception ex)
            {
                // 上报绝不能影响本地刷新与用户可用性。
                Log.Warn("台账上报异常（已忽略，下次刷新重试）: " + ex.Message);
            }
        }

        private static string Detail(string value)
            => string.IsNullOrWhiteSpace(value) ? "" : "（" + value.Trim() + "）";
    }
}

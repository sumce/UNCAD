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
        private readonly object _noticeGate = new object();
        private readonly Queue<string> _notices = new Queue<string>();
        private int _refreshing;

        internal static MachineDataRefreshCoordinator Default { get; } =
            new MachineDataRefreshCoordinator(MachineWorkbookSource.Refresh,
                work => ThreadPool.QueueUserWorkItem(_ => work()));

        internal MachineDataRefreshCoordinator(
            Func<string, MachineWorkbookSourceResult> refresh,
            Action<Action> queue)
        {
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        internal MachineDataRefreshStartStatus TryStartStartupRefresh(string source)
        {
            string value = (source ?? "").Trim();
            if (value.Length == 0) return MachineDataRefreshStartStatus.NoSource;
            if (!MachineWorkbookSource.IsRemote(value))
                return MachineDataRefreshStartStatus.LocalSourceSkipped;
            return TryStart(value);
        }

        internal MachineDataRefreshStartStatus TryStartManualRefresh(string source)
        {
            string value = (source ?? "").Trim();
            return value.Length == 0
                ? MachineDataRefreshStartStatus.NoSource
                : TryStart(value);
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

        private MachineDataRefreshStartStatus TryStart(string source)
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
                return MachineDataRefreshStartStatus.AlreadyRunning;
            try
            {
                _queue(() => Refresh(source));
                return MachineDataRefreshStartStatus.Started;
            }
            catch
            {
                Interlocked.Exchange(ref _refreshing, 0);
                throw;
            }
        }

        private void Refresh(string source)
        {
            try
            {
                MachineWorkbookSourceResult result = _refresh(source);
                if (result.UsedCachedFallback)
                {
                    AddNotice("机台数据后台刷新失败，继续使用上一次成功数据"
                        + Detail(result.Warning) + "。");
                    Log.Warn("机台数据后台刷新使用旧快照" + Detail(result.Warning));
                    return;
                }

                int rows = result.Snapshot?.RowCount ?? 0;
                AddNotice("机台数据已后台更新，共 " + rows + " 行"
                    + Detail(result.Warning) + "。");
                Log.Info("机台数据后台刷新完成: " + rows + " 行");
            }
            catch (Exception ex)
            {
                // Parsing and SQLite replacement are atomic, so the old snapshot
                // remains available even when download or parsing fails.
                AddNotice("机台数据后台刷新失败，原 SQLite 快照未被替换: "
                    + ex.Message);
                Log.Error("机台数据后台刷新失败", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private void AddNotice(string message)
        {
            lock (_noticeGate) _notices.Enqueue(message);
        }

        private static string Detail(string value)
            => string.IsNullOrWhiteSpace(value) ? "" : "（" + value.Trim() + "）";
    }
}

using System;
using System.Threading;
using UNCAD.Core.Excel;

namespace UNCAD.Infra
{
    internal enum MachineDataRefreshStartStatus
    {
        Started,
        NoSource,
        AlreadyRunning
    }

    /// <summary>
    /// Runs a **local/UNC** machine-workbook refresh away from the AutoCAD thread while
    /// commands continue reading the last successful SQLite snapshot.
    ///
    /// 2.10.0 起远程机台数据源、后台台账上报与"后台提示"通知队列全部移除：
    /// 刷新结果只写日志；U1DATA/U1SET 的界面反馈由命令本身负责。
    /// </summary>
    internal sealed class MachineDataRefreshCoordinator
    {
        private readonly Func<string, MachineWorkbookSourceResult> _refresh;
        private readonly Action<Action> _queue;
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

        internal MachineDataRefreshStartStatus TryStartManualRefresh(string source)
        {
            string value = (source ?? "").Trim();
            if (value.Length == 0) return MachineDataRefreshStartStatus.NoSource;
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
                return MachineDataRefreshStartStatus.AlreadyRunning;
            try
            {
                _queue(() => Refresh(value));
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
                    Log.Warn("机台数据后台刷新使用旧快照" + Detail(result.Warning));
                    return;
                }
                int rows = result.Snapshot?.RowCount ?? 0;
                Log.Info("机台数据后台刷新完成: " + rows + " 行"
                    + Detail(result.Warning));
            }
            catch (Exception ex)
            {
                // 解析与 SQLite 替换是原子的：刷新失败时上一份快照仍然可用。
                Log.Error("机台数据后台刷新失败（原 SQLite 快照未被替换）", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private static string Detail(string value)
            => string.IsNullOrWhiteSpace(value) ? "" : "（" + value.Trim() + "）";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 机台数据后台刷新契约（2.10.0）：只支持本地/共享文件夹工作簿；远程下载、
    /// 启动时远程刷新、后台台账上报与命令行通知队列都已移除，刷新结果只写日志。
    /// U1DATA 与协调器对 HTTP/HTTPS 数据源的支持方式（D-041 恢复下载）。
    /// </summary>
    public sealed class MachineDataRefreshCoordinatorTests
    {
        [Fact]
        public void ManualRefresh_QueuesTheConfiguredLocalSource()
        {
            var queued = new List<Action>();
            string requested = null;
            var coordinator = new MachineDataRefreshCoordinator(source =>
            {
                requested = source;
                return new MachineWorkbookSourceResult
                {
                    Updated = true,
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 12 }
                };
            }, queued.Add);

            Assert.Equal(MachineDataRefreshStartStatus.NoSource,
                coordinator.TryStartManualRefresh(""));
            Assert.Equal(MachineDataRefreshStartStatus.NoSource,
                coordinator.TryStartManualRefresh("   "));

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx"));
            Assert.Equal(MachineDataRefreshStartStatus.AlreadyRunning,
                coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx"));
            Action work = Assert.Single(queued);
            work();
            Assert.Equal(@"C:\data\machines.xlsx", requested);

            // 刷新完成后允许再次刷新。
            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx"));
        }

        [Fact]
        public void RefreshFailure_OnlyLogsAndAllowsRetry()
        {
            var queued = new List<Action>();
            int calls = 0;
            var coordinator = new MachineDataRefreshCoordinator(_ =>
            {
                calls++;
                if (calls == 1) throw new InvalidDataException("bad workbook");
                return new MachineWorkbookSourceResult
                {
                    Updated = true,
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 3 }
                };
            }, queued.Add);

            coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx");
            Assert.Single(queued)();   // 失败不得抛出：后台异常只写日志

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx"));
            queued[1]();
            Assert.Equal(2, calls);
        }

        [Fact]
        public void Refresh_DoesNotCreateNoticesBecauseThePipelineWasRemoved()
        {
            // 2.10.0 删除了后台通知队列：协调器不再暴露 TryTakeNotice/EnqueueNotice，
            // 刷新结果只通过 Log 记录。这里用源码契约锁住"不会重新引入通知管线"。
            string source = File.ReadAllText(Repo("src", "UNCAD", "Infra",
                "MachineDataRefreshCoordinator.cs"));
            Assert.DoesNotContain("TryTakeNotice", source);
            Assert.DoesNotContain("EnqueueNotice", source);
            Assert.DoesNotContain("MachineLedgerReporter", source);
            Assert.Contains("MachineWorkbookSource.Refresh", source);
        }

        [Fact]
        public void U1Data_RemoteSourceRefreshesViaCoordinator_NoRejectionBranch()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Features", "ConfigCenter",
                "MachineDataRefreshFeature.cs"));
            // D-041：网络地址与本地文件走同一条刷新链路，不再有拒绝分支。
            Assert.DoesNotContain("不再支持网络机台数据源", source);
            Assert.Contains("MachineDataRefreshCoordinator.Default", source);
            Assert.Contains("MachineWorkbookSource.IsRemote(source)", source);
        }

        [Fact]
        public void Bootstrap_ReadsNoWorkbookAndHasNoRefreshPipeline()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Bootstrap.cs"));
            Assert.DoesNotContain("MachineWorkbookSource.Refresh", source);
            Assert.DoesNotContain("MachineWorkbookSource.IsRemote", source);
            Assert.DoesNotContain("MachineDataRefreshCoordinator", source);
            Assert.Contains("云端上传/台账上报保持移除", source);
            Assert.Contains("启动时不自动刷新", source);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new string[] { root }.Concat(parts).ToArray());
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UNCAD.Core.Excel;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class MachineDataRefreshCoordinatorTests
    {
        [Fact]
        public void StartupRefresh_QueuesOnlyRemoteSourcesAndRejectsDuplicates()
        {
            var queued = new List<Action>();
            var coordinator = new MachineDataRefreshCoordinator(_ =>
                new MachineWorkbookSourceResult
                {
                    Updated = true,
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 12 }
                }, queued.Add, MachineLedgerReporter.None);

            Assert.Equal(MachineDataRefreshStartStatus.NoSource,
                coordinator.TryStartStartupRefresh(""));
            Assert.Equal(MachineDataRefreshStartStatus.LocalSourceSkipped,
                coordinator.TryStartStartupRefresh(@"C:\data\machines.xlsx"));
            Assert.Empty(queued);

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartStartupRefresh("https://example.test/machines.xlsx"));
            Assert.Equal(MachineDataRefreshStartStatus.AlreadyRunning,
                coordinator.TryStartManualRefresh("https://example.test/machines.xlsx"));
            Action work = Assert.Single(queued);
            work();

            // 启动时的成功刷新只写日志，不再往命令行刷提示。
            Assert.False(coordinator.TryTakeNotice(out _));
        }

        [Fact]
        public void StartupFailure_IsLogOnly_ButManualRefreshStillReports()
        {
            var queued = new List<Action>();
            var coordinator = new MachineDataRefreshCoordinator(
                _ => throw new IOException("无法解析远程名称"), queued.Add,
                MachineLedgerReporter.None, _ => true);

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartStartupRefresh("https://example.test/m.xlsx"));
            Assert.Single(queued)();
            Assert.False(coordinator.TryTakeNotice(out _));

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh("https://example.test/m.xlsx"));
            queued[1]();
            Assert.True(coordinator.TryTakeNotice(out string message));
            Assert.Contains("无法解析远程名称", message);
            Assert.Contains("U1SET", message);
        }

        [Fact]
        public void DuplicateNotices_AreAnnouncedOnlyOnce()
        {
            var queued = new List<Action>();
            var coordinator = new MachineDataRefreshCoordinator(_ =>
                new MachineWorkbookSourceResult
                {
                    Updated = true,
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 12 }
                }, queued.Add, MachineLedgerReporter.None);

            coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx");
            queued[0]();
            coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx");
            queued[1]();

            Assert.True(coordinator.TryTakeNotice(out _));
            Assert.False(coordinator.TryTakeNotice(out _));
        }

        [Fact]
        public void FailedRefresh_ReportsPreservedSnapshotAndAllowsRetry()
        {
            var queued = new List<Action>();
            var coordinator = new MachineDataRefreshCoordinator(
                _ => throw new InvalidDataException("bad workbook"), queued.Add,
                MachineLedgerReporter.None);

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx"));
            Assert.Single(queued)();

            Assert.True(coordinator.TryTakeNotice(out string message));
            Assert.Contains("原 SQLite 快照未被替换", message);
            Assert.Contains("bad workbook", message);
            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh(@"C:\data\machines.xlsx"));
        }

        [Fact]
        public void Bootstrap_StartsRefreshWithoutCallingWorkbookCodeOnTheCadThread()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            string source = File.ReadAllText(Path.Combine(root, "src", "UNCAD",
                "Bootstrap.cs"));

            Assert.Contains("TryStartStartupRefresh(source)", source);
            Assert.Contains("Application.Idle += OnMachineDataRefreshIdle", source);
            Assert.DoesNotContain("MachineWorkbookSource.Refresh", source);
            // 命令进行中绝不写命令行：Idle 在等输入时也会触发，写入会打断框选/命令。
            Assert.Contains("IsCommandActive()", source);
            Assert.Contains("CMDACTIVE", source);
        }
    }
}

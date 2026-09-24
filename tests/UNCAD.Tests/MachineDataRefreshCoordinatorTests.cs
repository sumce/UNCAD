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
                }, queued.Add);

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

            Assert.True(coordinator.TryTakeNotice(out string message));
            Assert.Contains("12 行", message);
        }

        [Fact]
        public void FailedRefresh_ReportsPreservedSnapshotAndAllowsRetry()
        {
            var queued = new List<Action>();
            var coordinator = new MachineDataRefreshCoordinator(
                _ => throw new InvalidDataException("bad workbook"), queued.Add);

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
        }
    }
}

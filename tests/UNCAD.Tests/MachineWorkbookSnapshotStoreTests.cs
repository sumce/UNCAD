using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class MachineWorkbookSnapshotStoreTests
    {
        [Fact]
        public void ReadRowsForMachines_SplitsLargeSelectionsAndPreservesFields()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_sqlite_中文_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                var rows = Enumerable.Range(0, 1005).Select(index => new MachineRow
                {
                    MachineId = "M" + index,
                    CircuitName = "C" + index,
                    Region = "R" + index,
                    Cable = "Cable" + index,
                    Fr = "FR" + index,
                    Detail = "D" + index,
                    Batch = "BATCH-" + (index / 10),
                    Seq = index.ToString(),
                    Dia = "20",
                    Next = "插座盘",
                    DownstreamAxis = "D-A" + index,
                    UpstreamAxis = "U-A" + index,
                    DeviceFloor = "2F",
                    PanelFloor = "1F",
                    FacilitySwitch = "1P20A"
                }).ToList();
                store.RefreshRows(source, rows, "test-hash");

                // 1005 IDs exercise the SQLite parameter limit; casing must not
                // change the machine identity used by the CAD picker.
                List<string> selected = rows.Select(row => row.MachineId.ToLowerInvariant()).ToList();
                List<MachineRow> actual = store.ReadRowsForMachines(source, selected);

                Assert.Equal(rows.Count, actual.Count);
                Assert.Equal("M0", actual[0].MachineId);
                Assert.Equal("M1004", actual[1004].MachineId);
                MachineRow sample = actual[537];
                Assert.Equal("C537", sample.CircuitName);
                Assert.Equal("Cable537", sample.Cable);
                Assert.Equal("D-A537", sample.DownstreamAxis);
                Assert.Equal("BATCH-53", sample.Batch);
                Assert.Equal("U-A537", sample.UpstreamAxis);
                Assert.Equal("1P20A", sample.FacilitySwitch);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { }
            }
        }

        [Fact]
        public void SnapshotMetadataIsReplacedEvenWhenRowsAreUnchanged()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_sqlite_meta_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                var rows = new[] { new MachineRow { MachineId = "M1", CircuitName = "C1" } };
                MachineWorkbookSnapshotInfo first = store.RefreshRows(source, rows, "hash-a");
                Thread.Sleep(10);
                MachineWorkbookSnapshotInfo second = store.RefreshRows(source, rows, "hash-a");

                Assert.Equal(1, second.RowCount);
                Assert.Equal("hash-a", second.WorkbookHash);
                Assert.NotEqual(first.RefreshedUtc, second.RefreshedUtc);
                Assert.True(store.TryGetSnapshot(source, out MachineWorkbookSnapshotInfo current));
                Assert.Equal(second.RefreshedUtc, current.RefreshedUtc);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { }
            }
        }

        [Fact]
        public void FindRows_IgnoresInternalWhitespaceInMachineAndCircuitNames()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_sqlite_identity_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "machine.xlsx");
            var store = new MachineWorkbookSnapshotStore(Path.Combine(root, "machine.db"));
            try
            {
                store.RefreshRows(source, new[]
                {
                    new MachineRow { MachineId = "MQ-01", CircuitName = "设备A" }
                }, "identity-test");

                Assert.Single(store.FindRows(source, "M Q-01"));
                Assert.Single(store.FindRows(source, "设备 A"));
                Assert.Single(store.ReadRowsForMachines(source, new[] { "M Q-01" }));
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { }
            }
        }

        [Fact]
        public void RefreshRows_MigratesLegacySnapshotWithoutBatchColumn()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_sqlite_migrate_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "machine.xlsx");
            string database = Path.Combine(root, "machine.db");
            try
            {
                using (var connection = new WindowsSqliteConnection(database))
                {
                    connection.Execute(@"
CREATE TABLE machine_sources (
    source_key TEXT NOT NULL PRIMARY KEY,
    source_display TEXT NOT NULL,
    workbook_hash TEXT NOT NULL,
    refreshed_utc TEXT NOT NULL,
    row_count INTEGER NOT NULL
)" );
                    connection.Execute(@"
CREATE TABLE machine_rows (
    source_key TEXT NOT NULL,
    source_row INTEGER NOT NULL,
    machine_id TEXT NOT NULL,
    circuit_name TEXT NOT NULL,
    region TEXT NOT NULL,
    cable TEXT NOT NULL,
    fr TEXT NOT NULL,
    detail TEXT NOT NULL,
    seq TEXT NOT NULL,
    dia TEXT NOT NULL,
    upstream_type TEXT NOT NULL,
    downstream_axis TEXT NOT NULL,
    upstream_axis TEXT NOT NULL,
    device_floor TEXT NOT NULL,
    panel_floor TEXT NOT NULL,
    facility_switch TEXT NOT NULL,
    PRIMARY KEY (source_key, source_row)
)" );
                }

                var store = new MachineWorkbookSnapshotStore(database);
                store.RefreshRows(source, new[]
                {
                    new MachineRow { MachineId = "M1", CircuitName = "C1",
                        Batch = "B-01", Seq = "7" }
                }, "legacy-migrated");

                MachineRow result = Assert.Single(store.ReadAllRows(source));
                Assert.Equal("B-01", result.Batch);
                Assert.Equal("7", result.Seq);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}

using System;
using System.IO;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    public class SubmissionWorkbookWriterTests
    {
        [Fact]
        public void Upsert_PreservesSubmissionHistoryAndReplacesLatestDetails()
        {
            string folder = Path.Combine(Path.GetTempPath(), "uncad_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                var first = Record("M01", "设备A", "旧详情");
                SubmissionWriteResult created = SubmissionWorkbookWriter.Upsert(path, first,
                    new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
                Assert.False(created.ReplacedExisting);

                var latest = Record("M01", "设备A", "新详情");
                latest.PanelType = "插座盘";
                SubmissionWriteResult updated = SubmissionWorkbookWriter.Upsert(path, latest,
                    new DateTimeOffset(2026, 8, 2, 11, 30, 0, TimeSpan.Zero));
                Assert.True(updated.ReplacedExisting);
                Assert.Equal(1, updated.RemovedDetailRows);
                Assert.NotEqual(created.SubmittedAt, updated.SubmittedAt);
                Assert.Equal(updated.SubmittedAt, updated.UpdatedAt);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        var sheet = workbook.GetSheet(SubmissionWorkbookWriter.SheetName);
                        Assert.Equal(2, sheet.LastRowNum);
                        var oldRow = sheet.GetRow(1);
                        var latestRow = sheet.GetRow(2);
                        Assert.Equal("旧详情", oldRow.GetCell(Column(sheet, "配电详情")).StringCellValue);
                        Assert.Equal(created.SubmittedAt, oldRow.GetCell(Column(sheet, "提交时间")).StringCellValue);
                        Assert.Equal("M01", latestRow.GetCell(Column(sheet, "机台ID")).StringCellValue);
                        Assert.Equal("设备A", latestRow.GetCell(Column(sheet, "设备名称")).StringCellValue);
                        Assert.Equal("插座盘", latestRow.GetCell(Column(sheet, "盘柜类型")).StringCellValue);
                        Assert.Equal("新详情", latestRow.GetCell(Column(sheet, "配电详情")).StringCellValue);
                        Assert.Equal("16.4", latestRow.GetCell(Column(sheet, "电缆米数")).StringCellValue);
                        Assert.Equal("2", latestRow.GetCell(Column(sheet, "软管米数")).StringCellValue);
                        Assert.Equal("4", latestRow.GetCell(Column(sheet, "桥架米数")).StringCellValue);
                        Assert.Equal("5", latestRow.GetCell(Column(sheet, "线管米数")).StringCellValue);
                        Assert.Equal(updated.SubmittedAt, latestRow.GetCell(Column(sheet, "提交时间")).StringCellValue);
                        Assert.Equal(updated.UpdatedAt, latestRow.GetCell(Column(sheet, "更新时间")).StringCellValue);

                        var details = workbook.GetSheet(SubmissionWorkbookWriter.DetailSheetName);
                        Assert.Equal(1, details.LastRowNum);
                        Assert.Equal("电缆\n包塑金属软管", details.GetRow(1)
                            .GetCell(Column(details, "材料名称")).StringCellValue);
                        Assert.Equal("16.4\n2", details.GetRow(1)
                            .GetCell(Column(details, "数量")).StringCellValue);
                        Assert.Equal("1.1\n3.1", details.GetRow(1)
                            .GetCell(Column(details, "项目编码")).StringCellValue);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_KeepsDifferentDeviceAsSeparateRecord()
        {
            string folder = Path.Combine(Path.GetTempPath(), "uncad_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                DateTimeOffset now = DateTimeOffset.Now;
                SubmissionWorkbookWriter.Upsert(path, Record("M01", "设备A", "A"), now);
                SubmissionWorkbookWriter.Upsert(path, Record("M01", "设备B", "B"), now);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try { Assert.Equal(2, workbook.GetSheet(SubmissionWorkbookWriter.SheetName).LastRowNum); }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_AppendsNewColumnsToLegacyWorkbook_WithoutLosingRows()
        {
            string folder = Path.Combine(Path.GetTempPath(), "uncad_submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                var legacyWorkbook = new XSSFWorkbook();
                try
                {
                    var sheet = legacyWorkbook.CreateSheet(SubmissionWorkbookWriter.SheetName);
                    string[] oldHeaders =
                    {
                        "机台ID", "设备名称", "盘柜类型", "电缆型号", "FR", "配电详情",
                        "软管直径", "下游轴位", "上游轴位", "提交时间", "更新时间"
                    };
                    var header = sheet.CreateRow(0);
                    for (int i = 0; i < oldHeaders.Length; i++) header.CreateCell(i).SetCellValue(oldHeaders[i]);
                    var legacy = sheet.CreateRow(1);
                    legacy.CreateCell(0).SetCellValue("OLD");
                    legacy.CreateCell(1).SetCellValue("旧设备");
                    legacy.CreateCell(3).SetCellValue("LEGACY-CABLE");
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        legacyWorkbook.Write(stream);
                }
                finally { legacyWorkbook.Close(); }

                SubmissionWorkbookWriter.Upsert(path, Record("NEW", "新设备", "详情"), DateTimeOffset.Now);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        var sheet = workbook.GetSheet(SubmissionWorkbookWriter.SheetName);
                        Assert.Equal(2, sheet.LastRowNum);
                        NPOI.SS.UserModel.IRow oldRow = FindRow(sheet, "OLD");
                        NPOI.SS.UserModel.IRow newRow = FindRow(sheet, "NEW");
                        Assert.Equal("旧设备", oldRow.GetCell(Column(sheet, "设备名称")).StringCellValue);
                        foreach (string header in SubmissionWorkbookWriter.Headers)
                            Assert.True(Column(sheet, header) >= 0);
                        Assert.True(Column(sheet, "设备原电缆型号") >= 0);
                        Assert.True(Column(sheet, "清单电缆型号") >= 0);
                        Assert.Equal("LEGACY-CABLE", oldRow
                            .GetCell(Column(sheet, "设备原电缆型号")).StringCellValue);
                        Assert.Equal("LEGACY-CABLE", oldRow
                            .GetCell(Column(sheet, "清单电缆型号")).StringCellValue);
                        Assert.Equal("ZB-YJVR-3*2.5", newRow
                            .GetCell(Column(sheet, "电缆型号")).StringCellValue);
                        Assert.Equal("ORIGINAL-CABLE", newRow
                            .GetCell(Column(sheet, "设备原电缆型号")).StringCellValue);
                        Assert.Equal("16.4", newRow.GetCell(Column(sheet, "电缆米数")).StringCellValue);
                        var details = workbook.GetSheet(SubmissionWorkbookWriter.DetailSheetName);
                        Assert.Equal(1, details.LastRowNum);
                        foreach (string header in SubmissionWorkbookWriter.DetailHeaders)
                            Assert.True(Column(details, header) >= 0);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void UpsertMany_InsertsSeveralFramesInOneWorkbookWrite()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_batch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                SubmissionBatchWriteResult result = SubmissionWorkbookWriter.UpsertMany(path,
                    new[] { Record("M01", "设备A", "A"), Record("M02", "设备B", "B") },
                    new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));

                Assert.Equal(2, result.AddedCount);
                Assert.Equal(0, result.ReplacedCount);
                Assert.Equal(2, result.Records.Count);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        Assert.Equal(2, workbook.GetSheet(
                            SubmissionWorkbookWriter.SheetName).LastRowNum);
                        Assert.Equal(2, workbook.GetSheet(
                            SubmissionWorkbookWriter.DetailSheetName).LastRowNum);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void UpsertMany_AddsHistoryAndRefreshesExistingLatestDetails()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_batch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                SubmissionWriteResult original = SubmissionWorkbookWriter.Upsert(path,
                    Record("M01", "设备A", "旧"),
                    new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero));
                SubmissionBatchWriteResult result = SubmissionWorkbookWriter.UpsertMany(path,
                    new[] { Record("M01", "设备A", "新"), Record("M02", "设备B", "新增") },
                    new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero));

                Assert.Equal(2, result.AddedCount);
                Assert.Equal(1, result.ReplacedCount);
                Assert.NotEqual(original.SubmittedAt, result.Records[0].SubmittedAt);
                Assert.Equal(result.Records[0].SubmittedAt, result.UpdatedAt);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        Assert.Equal(3, workbook.GetSheet(
                            SubmissionWorkbookWriter.SheetName).LastRowNum);
                        Assert.Equal(2, workbook.GetSheet(
                            SubmissionWorkbookWriter.DetailSheetName).LastRowNum);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void UpsertMany_RejectsDuplicateInputBeforeChangingWorkbook()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_batch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                SubmissionWorkbookWriter.Upsert(path, Record("BASE", "设备", "基线"),
                    DateTimeOffset.UtcNow);
                byte[] before = File.ReadAllBytes(path);

                // Normalized duplicate keys must fail before acquiring or mutating the target.
                Assert.Throws<InvalidDataException>(() => SubmissionWorkbookWriter.UpsertMany(path,
                    new[] { Record("M01", "设备A", "A"), Record(" m01 ", "设备A", "B") },
                    DateTimeOffset.UtcNow));
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_GroupsSameMachineRowsAndKeepsOnlyLatestDeviceDetails()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_group_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                DateTimeOffset time = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
                SubmissionWorkbookWriter.Upsert(path, Record("M02", "设备A", "旧A"), time);
                SubmissionWorkbookWriter.Upsert(path, Record("M01", "设备X", "X"), time.AddMinutes(1));
                SubmissionWorkbookWriter.Upsert(path, Record("M02", "设备B", "B"), time.AddMinutes(2));
                SubmissionWorkbookWriter.Upsert(path, Record("M02", "设备A", "新A"), time.AddMinutes(3));

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        NPOI.SS.UserModel.ISheet history = workbook.GetSheet(
                            SubmissionWorkbookWriter.SheetName);
                        int historyMachine = Column(history, "机台ID");
                        Assert.Equal(new[] { "M01", "M02", "M02", "M02" }, new[]
                        {
                            history.GetRow(1).GetCell(historyMachine).StringCellValue,
                            history.GetRow(2).GetCell(historyMachine).StringCellValue,
                            history.GetRow(3).GetCell(historyMachine).StringCellValue,
                            history.GetRow(4).GetCell(historyMachine).StringCellValue
                        });
                        Assert.Equal("旧A", history.GetRow(2)
                            .GetCell(Column(history, "配电详情")).StringCellValue);
                        Assert.Equal("新A", history.GetRow(4)
                            .GetCell(Column(history, "配电详情")).StringCellValue);

                        NPOI.SS.UserModel.ISheet details = workbook.GetSheet(
                            SubmissionWorkbookWriter.DetailSheetName);
                        int detailMachine = Column(details, "机台ID");
                        Assert.Equal(3, details.LastRowNum);
                        Assert.Equal(new[] { "M01", "M02", "M02" }, new[]
                        {
                            details.GetRow(1).GetCell(detailMachine).StringCellValue,
                            details.GetRow(2).GetCell(detailMachine).StringCellValue,
                            details.GetRow(3).GetCell(detailMachine).StringCellValue
                        });
                        int deviceColumn = Column(details, "设备名称");
                        Assert.Equal(new[] { "设备B", "设备A" }, new[]
                        {
                            details.GetRow(2).GetCell(deviceColumn).StringCellValue,
                            details.GetRow(3).GetCell(deviceColumn).StringCellValue
                        });
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_CompactsLegacyMaterialRowsIntoOneDeviceRow()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_compact_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                var legacy = new XSSFWorkbook();
                try
                {
                    var details = legacy.CreateSheet(SubmissionWorkbookWriter.DetailSheetName);
                    var header = details.CreateRow(0);
                    for (int column = 0; column < SubmissionWorkbookWriter.DetailHeaders.Length; column++)
                        header.CreateCell(column).SetCellValue(
                            SubmissionWorkbookWriter.DetailHeaders[column]);
                    for (int rowIndex = 1; rowIndex <= 2; rowIndex++)
                    {
                        var row = details.CreateRow(rowIndex);
                        row.CreateCell(0).SetCellValue("OLD");
                        row.CreateCell(1).SetCellValue("旧设备");
                        row.CreateCell(2).SetCellValue(rowIndex.ToString());
                        row.CreateCell(3).SetCellValue(rowIndex == 1 ? "电缆" : "软管");
                        row.CreateCell(6).SetCellValue(rowIndex == 1 ? "10" : "2");
                    }
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        legacy.Write(stream);
                }
                finally { legacy.Close(); }

                SubmissionWorkbookWriter.UpsertMany(path,
                    new[] { Record("NEW", "新设备", "详情") }, DateTimeOffset.UtcNow);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        var details = workbook.GetSheet(SubmissionWorkbookWriter.DetailSheetName);
                        Assert.Equal(2, details.LastRowNum);
                        NPOI.SS.UserModel.IRow oldRow = FindRow(details, "OLD");
                        Assert.Equal("电缆\n软管", oldRow
                            .GetCell(Column(details, "材料名称")).StringCellValue);
                        Assert.Equal("10\n2", oldRow
                            .GetCell(Column(details, "数量")).StringCellValue);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_DuplicateCompactRowsKeepOnlyLatestVersion()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_latest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                var source = new XSSFWorkbook();
                try
                {
                    var details = source.CreateSheet(SubmissionWorkbookWriter.DetailSheetName);
                    var header = details.CreateRow(0);
                    for (int column = 0; column < SubmissionWorkbookWriter.DetailHeaders.Length; column++)
                        header.CreateCell(column).SetCellValue(
                            SubmissionWorkbookWriter.DetailHeaders[column]);
                    string[] names = { "旧电缆\n旧软管", "新断路器\n新插座" };
                    string[] times = { "2026-08-01 10:00:00", "2026-08-02 10:00:00" };
                    for (int rowIndex = 1; rowIndex <= 2; rowIndex++)
                    {
                        var row = details.CreateRow(rowIndex);
                        row.CreateCell(0).SetCellValue("OLD");
                        row.CreateCell(1).SetCellValue("旧设备");
                        row.CreateCell(2).SetCellValue("1\n2");
                        row.CreateCell(3).SetCellValue(names[rowIndex - 1]);
                        row.CreateCell(8).SetCellValue(times[rowIndex - 1]);
                        row.CreateCell(9).SetCellValue(times[rowIndex - 1]);
                    }
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        source.Write(stream);
                }
                finally { source.Close(); }

                SubmissionWorkbookWriter.Upsert(path, Record("NEW", "新设备", "详情"),
                    DateTimeOffset.UtcNow);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var workbook = new XSSFWorkbook(stream);
                    try
                    {
                        var details = workbook.GetSheet(SubmissionWorkbookWriter.DetailSheetName);
                        Assert.Equal(2, details.LastRowNum);
                        NPOI.SS.UserModel.IRow retained = FindRow(details, "OLD");
                        Assert.Equal("新断路器\n新插座", retained
                            .GetCell(Column(details, "材料名称")).StringCellValue);
                        Assert.Equal("2026-08-02 10:00:00", retained
                            .GetCell(Column(details, "更新时间")).StringCellValue);
                    }
                    finally { workbook.Close(); }
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void ValidateTargetForUpdate_AcceptsWritableNewTargetWithoutCreatingWorkbook()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_preflight_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                SubmissionWorkbookWriter.ValidateTargetForUpdate(path);
                Assert.False(File.Exists(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void ValidateTargetForUpdate_RejectsCorruptExistingWorkbook()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_preflight_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                File.WriteAllText(path, "not an xlsx workbook");
                Assert.ThrowsAny<Exception>(() =>
                    SubmissionWorkbookWriter.ValidateTargetForUpdate(path));
                Assert.Equal("not an xlsx workbook", File.ReadAllText(path));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void Upsert_WhenWorkbookIsWriteLocked_ReleasesInternalLockForRetry()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "uncad_submit_lock_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, SubmissionWorkbookWriter.DefaultFileName);
            try
            {
                SubmissionRecord record = Record("LOCK01", "设备A", "初始");
                SubmissionWorkbookWriter.Upsert(path, record, DateTimeOffset.UtcNow);

                using (var external = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.Read))
                {
                    Assert.Throws<IOException>(() => SubmissionWorkbookWriter.Upsert(
                        path, Record("LOCK01", "设备A", "被锁定"), DateTimeOffset.UtcNow));
                }

                // 第一次失败必须释放 .uncad.lock，否则本次重试会等待并再次失败。
                SubmissionWriteResult retry = SubmissionWorkbookWriter.Upsert(
                    path, Record("LOCK01", "设备A", "重试成功"), DateTimeOffset.UtcNow);
                Assert.True(retry.ReplacedExisting);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        private static NPOI.SS.UserModel.IRow FindRow(
            NPOI.SS.UserModel.ISheet sheet, string machineId)
        {
            int machineColumn = Column(sheet, "机台ID");
            for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                NPOI.SS.UserModel.IRow row = sheet.GetRow(rowIndex);
                if (string.Equals(row?.GetCell(machineColumn)?.StringCellValue, machineId,
                    StringComparison.OrdinalIgnoreCase)) return row;
            }
            throw new InvalidDataException("Missing machine row: " + machineId);
        }

        private static int Column(NPOI.SS.UserModel.ISheet sheet, string header)
        {
            NPOI.SS.UserModel.IRow row = sheet.GetRow(0);
            for (int column = 0; column < row.LastCellNum; column++)
                if (string.Equals(row.GetCell(column)?.StringCellValue, header,
                    StringComparison.Ordinal)) return column;
            throw new InvalidDataException("Missing header: " + header);
        }

        private static SubmissionRecord Record(string machine, string device, string detail)
            => new SubmissionRecord
            {
                MachineId = machine,
                DeviceName = device,
                PanelType = "I-Line盘",
                OriginalCable = "ORIGINAL-CABLE",
                Cable = "ZB-YJVR-3*2.5",
                CableMeters = "16.4",
                Fr = "FR-01",
                Detail = detail,
                Diameter = "20",
                FlexibleConduitMeters = "2",
                BridgeInfo = "桥架200*100 4M",
                BridgeMeters = "4",
                ConduitInfo = "⌀20线管 5M",
                ConduitMeters = "5",
                DownstreamAxis = "2/T",
                UpstreamAxis = "LAB",
                Materials = new System.Collections.Generic.List<SubmissionMaterial>
                {
                    new SubmissionMaterial
                    {
                        Number = "1", Name = "电缆", Description = "电缆描述",
                        Unit = "M", Quantity = "16.4", Code = "1.1"
                    },
                    new SubmissionMaterial
                    {
                        Number = "2", Name = "包塑金属软管", Description = "软管描述",
                        Unit = "M", Quantity = "2", Code = "3.1"
                    }
                }
            };
    }
}

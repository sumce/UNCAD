using System;
using System.Collections.Generic;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Submission;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class BoqWorkbookWriterTests
    {
        [Fact]
        public void BuildTargetPath_UsesMachineFolderAndBoqFileName()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_path_"
                + Guid.NewGuid().ToString("N"));
            try
            {
                string path = BoqWorkbookWriter.BuildTargetPath(root, "MACHINE01");
                Assert.Equal(Path.Combine(root, "MACHINE01", "[BOQ]MACHINE01.xlsx"), path);
                Assert.True(Directory.Exists(Path.Combine(root, "MACHINE01")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Theory]
        [InlineData("BOQ_Template.xlsx")]
        [InlineData("BOQ模板.xlsx")]
        public void ResolveTemplatePath_UsesExplicitPluginDirectory(string fileName)
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_template_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string expected = Path.Combine(root, fileName);
                File.WriteAllBytes(expected, new byte[] { 1 });

                string actual = BoqWorkbookWriter.ResolveTemplatePath(root);

                Assert.Equal(Path.GetFullPath(expected), actual);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void ResolveTemplatePath_MissingTemplateReportsSearchedPluginDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_missing_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                FileNotFoundException error = Assert.Throws<FileNotFoundException>(() =>
                    BoqWorkbookWriter.ResolveTemplatePath(root));
                Assert.Contains(Path.GetFullPath(root), error.Message);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_UpdatesOnlyMatchingTemplateQuantityRows()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_write_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "machine", "[BOQ]MACHINE01.xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                {
                    ISheet sheet = workbook.CreateSheet("Sheet1");
                    IRow first = sheet.CreateRow(0);
                    first.CreateCell(0).SetCellValue("1.1");
                    first.CreateCell(4).SetCellValue(99d);
                    IRow second = sheet.CreateRow(1);
                    second.CreateCell(0).SetCellValue("3.3");
                    second.CreateCell(4).SetCellValue(88d);
                    using (var stream = new FileStream(template, FileMode.CreateNew))
                        workbook.Write(stream);
                }

                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01",
                        DeviceName = "设备",
                        Materials = new List<SubmissionMaterial>
                        {
                            new SubmissionMaterial { Code = "1.1", Quantity = "12" },
                            new SubmissionMaterial { Code = "3.3", Quantity = "4.5" }
                        }
                    }
                });
                Assert.False(File.Exists(target + ".uncad.lock"));

                using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    var resultWorkbook = new XSSFWorkbook(stream);
                    ISheet sheet = resultWorkbook.GetSheet("Sheet1");
                    Assert.Equal(12d, sheet.GetRow(0).GetCell(11).NumericCellValue);
                    Assert.Equal(4.5d, sheet.GetRow(1).GetCell(11).NumericCellValue);
                    Assert.Equal("SUM(L1:L1)", sheet.GetRow(0).GetCell(4).CellFormula);
                    resultWorkbook.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_UpdatesOneDeviceColumnAndPreservesOtherDevices()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_devices_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Sheet1");
                sheet.CreateRow(0).CreateCell(0).SetCellValue("1.1");
                sheet.GetRow(0).CreateCell(4).SetCellValue(0d);
                using (var stream = new FileStream(template, FileMode.CreateNew))
                    workbook.Write(stream);
                workbook.Close();

                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01", DeviceName = "设备A",
                        Materials = new List<SubmissionMaterial>
                        { new SubmissionMaterial { Code = "1.1", Quantity = "12" } }
                    }
                });
                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01", DeviceName = "设备B",
                        Materials = new List<SubmissionMaterial>
                        { new SubmissionMaterial { Code = "1.1", Quantity = "3" } }
                    }
                });

                using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    var resultWorkbook = new XSSFWorkbook(stream);
                    ISheet result = resultWorkbook.GetSheet("Sheet1");
                    Assert.Equal("设备A", result.GetRow(4).GetCell(11).StringCellValue);
                    Assert.Equal("设备B", result.GetRow(4).GetCell(12).StringCellValue);
                    Assert.Equal(12d, result.GetRow(0).GetCell(11).NumericCellValue);
                    Assert.Equal(3d, result.GetRow(0).GetCell(12).NumericCellValue);
                    ICell total = result.GetRow(0).GetCell(4);
                    Assert.Equal("SUM(L1:M1)", total.CellFormula);
                    Assert.Equal(CellType.Formula, total.CellType);
                    Assert.Equal(15d, total.NumericCellValue);
                    resultWorkbook.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_Supports31CircuitsAndUpdatesOneCircuit()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_31_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Sheet1");
                sheet.CreateRow(0).CreateCell(0).SetCellValue("1.1");
                sheet.GetRow(0).CreateCell(4).SetCellValue(0d);
                using (var stream = new FileStream(template, FileMode.CreateNew))
                    workbook.Write(stream);
                workbook.Close();

                var records = new List<SubmissionRecord>();
                for (int index = 1; index <= 31; index++)
                {
                    records.Add(new SubmissionRecord
                    {
                        MachineId = "MACHINE01", DeviceName = "回路" + index,
                        Materials = new List<SubmissionMaterial>
                        {
                            new SubmissionMaterial { Code = "1.1", Quantity = index.ToString() }
                        }
                    });
                }
                BoqWorkbookWriter.Write(target, template, records);
                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01", DeviceName = "回路17",
                        Materials = new List<SubmissionMaterial>
                        { new SubmissionMaterial { Code = "1.1", Quantity = "99" } }
                    }
                });

                using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    var resultWorkbook = new XSSFWorkbook(stream);
                    ISheet result = resultWorkbook.GetSheet("Sheet1");
                    Assert.Equal("回路1", result.GetRow(4).GetCell(11).StringCellValue);
                    Assert.Equal("回路31", result.GetRow(4).GetCell(41).StringCellValue);
                    Assert.Equal(1d, result.GetRow(0).GetCell(11).NumericCellValue);
                    Assert.Equal(99d, result.GetRow(0).GetCell(27).NumericCellValue);
                    Assert.Equal(31d, result.GetRow(0).GetCell(41).NumericCellValue);
                    Assert.Equal("SUM(L1:AP1)", result.GetRow(0).GetCell(4).CellFormula);
                    resultWorkbook.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_RejectsMaterialWithoutFixedCatalogCode()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_code_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                {
                    workbook.CreateSheet("Sheet1").CreateRow(0).CreateCell(0).SetCellValue("1.1");
                    using (var stream = new FileStream(template, FileMode.CreateNew))
                        workbook.Write(stream);
                }
                Assert.Throws<InvalidDataException>(() => BoqWorkbookWriter.Write(
                    Path.Combine(root, "out.xlsx"), template, new[]
                    {
                        new SubmissionRecord
                        {
                            MachineId = "MACHINE01", DeviceName = "设备",
                            Materials = new List<SubmissionMaterial>
                            { new SubmissionMaterial { Quantity = "1" } }
                        }
                    }));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_FallsBackToMaterialNumberWhenCodeColumnIsBlank()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_number_fallback_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "machine", "[BOQ]MACHINE01.xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Sheet1");
                sheet.CreateRow(0).CreateCell(0).SetCellValue("4.11");
                using (var stream = new FileStream(template, FileMode.CreateNew))
                    workbook.Write(stream);
                workbook.Close();

                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01", DeviceName = "设备",
                        Materials = new List<SubmissionMaterial>
                        {
                            new SubmissionMaterial { Number = "4.11", Code = "", Quantity = "1" }
                        }
                    }
                });

                using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    var result = new XSSFWorkbook(stream);
                    Assert.Equal(1d, result.GetSheet("Sheet1").GetRow(0)
                        .GetCell(11).NumericCellValue);
                    result.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_AllowEmptyMaterialsClearsExistingDeviceColumn()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_empty_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "machine", "[BOQ]MACHINE01.xlsx");
            try
            {
                var workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Sheet1");
                sheet.CreateRow(0).CreateCell(0).SetCellValue("1.1");
                sheet.GetRow(0).CreateCell(11).SetCellValue(42d);
                sheet.CreateRow(4).CreateCell(11).SetCellValue("璁惧");
                using (var stream = new FileStream(template, FileMode.CreateNew))
                    workbook.Write(stream);
                workbook.Close();

                BoqWorkbookWriter.Write(target, template, new[]
                {
                    new SubmissionRecord
                    {
                        MachineId = "MACHINE01",
                        DeviceName = "璁惧",
                        Materials = new List<SubmissionMaterial>()
                    }
                }, true);

                using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
                {
                    var result = new XSSFWorkbook(stream);
                    Assert.Equal(0d, result.GetSheet("Sheet1").GetRow(0)
                        .GetCell(11).NumericCellValue);
                    result.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_WhenExistingWorkbookChangesBeforeReplace_DoesNotOverwriteExternalEdit()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_external_change_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);
                CreateWorkbook(target, 7d);

                IOException error = Assert.ThrowsAny<IOException>(() => BoqWorkbookWriter.Write(
                    target, template, Records("12"), false,
                    () => CreateWorkbook(target, 99d)));

                Assert.Contains("外部修改", error.Message);
                Assert.Equal(99d, ReadQuantity(target));
                Assert.False(File.Exists(target + ".uncad.lock"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_WhenMissingWorkbookIsCreatedBeforeReplace_DoesNotOverwriteNewFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_external_create_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);

                IOException error = Assert.ThrowsAny<IOException>(() => BoqWorkbookWriter.Write(
                    target, template, Records("12"), false,
                    () => CreateWorkbook(target, 77d)));

                Assert.Contains("其他程序创建", error.Message);
                Assert.Equal(77d, ReadQuantity(target));
                Assert.False(File.Exists(target + ".uncad.lock"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_WhenExistingWorkbookIsDeletedBeforeReplace_DoesNotRecreateIt()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_external_delete_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);
                CreateWorkbook(target, 7d);

                IOException error = Assert.ThrowsAny<IOException>(() => BoqWorkbookWriter.Write(
                    target, template, Records("12"), false, () => File.Delete(target)));

                Assert.Contains("被删除", error.Message);
                Assert.False(File.Exists(target));
                Assert.False(File.Exists(target + ".uncad.lock"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void ValidateRevision_RejectsChangesMadeDuringUserConfirmation()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_revision_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(target, 7d);
                BoqWorkbookRevision revision = BoqWorkbookWriter.CaptureRevision(target);
                CreateWorkbook(target, 88d);

                IOException error = Assert.ThrowsAny<IOException>(() =>
                    BoqWorkbookWriter.ValidateRevision(revision));

                Assert.Contains("确认期间", error.Message);
                Assert.Equal(88d, ReadQuantity(target));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void ValidateRevision_RejectsFileCreatedDuringUserConfirmation()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_revision_create_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                BoqWorkbookRevision revision = BoqWorkbookWriter.CaptureRevision(target);
                CreateWorkbook(target, 66d);

                IOException error = Assert.ThrowsAny<IOException>(() =>
                    BoqWorkbookWriter.ValidateRevision(revision));

                Assert.Contains("其他程序创建", error.Message);
                Assert.Equal(66d, ReadQuantity(target));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_WhenWorkbookChangesDuringReadback_DoesNotRestoreOverExternalEdit()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_readback_change_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);
                CreateWorkbook(target, 7d);

                BoqExternalModificationException error =
                    Assert.Throws<BoqExternalModificationException>(() =>
                        BoqWorkbookWriter.Write(target, template, Records("12"), false,
                            null, () => CreateWorkbook(target, 55d)));

                Assert.Contains("保留外部最新版", error.Message);
                Assert.Equal(55d, ReadQuantity(target));
                Assert.False(File.Exists(target + ".uncad.lock"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void WriteTracked_WhenFailureOccursBeforeReplace_ReportsUntouched()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_untouched_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);
                CreateWorkbook(target, 7d);
                var progress = new BoqWorkbookWriteProgress();

                Assert.Throws<InvalidOperationException>(() => BoqWorkbookWriter.WriteTracked(
                    target, template, Records("12"), false, progress,
                    () => throw new InvalidOperationException("before replace")));

                Assert.Equal(BoqWorkbookWriteState.Untouched, progress.State);
                Assert.Null(progress.WrittenFingerprint);
                Assert.Equal(7d, ReadQuantity(target));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void WriteTracked_WhenReadbackFailsAndRestoreSucceeds_ReportsRestored()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_restored_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);
                CreateWorkbook(target, 7d);
                var progress = new BoqWorkbookWriteProgress();

                Assert.Throws<InvalidDataException>(() => BoqWorkbookWriter.WriteTracked(
                    target, template, Records("9007199254740993"), false, progress));

                Assert.Equal(BoqWorkbookWriteState.Restored, progress.State);
                Assert.Null(progress.WrittenFingerprint);
                Assert.Equal(7d, ReadQuantity(target));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void WriteTracked_WhenUnrelatedCellChangesDuringReadback_PreservesExternalEdit()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_readback_note_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            string target = Path.Combine(root, "[BOQ]MACHINE01.xlsx");
            try
            {
                CreateWorkbook(template, 0d);
                CreateWorkbook(target, 7d);
                var progress = new BoqWorkbookWriteProgress();

                BoqExternalModificationException error =
                    Assert.Throws<BoqExternalModificationException>(() =>
                        BoqWorkbookWriter.WriteTracked(target, template, Records("12"), false,
                            progress, afterReplace: () => WriteNote(target, "external")));

                Assert.Contains("保留外部最新版", error.Message);
                Assert.Equal(BoqWorkbookWriteState.Replaced, progress.State);
                Assert.NotNull(progress.WrittenFingerprint);
                Assert.Equal("external", ReadNote(target));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static List<SubmissionRecord> Records(string quantity)
            => new List<SubmissionRecord>
            {
                new SubmissionRecord
                {
                    MachineId = "MACHINE01",
                    DeviceName = "设备",
                    Materials = new List<SubmissionMaterial>
                    {
                        new SubmissionMaterial { Code = "1.1", Quantity = quantity }
                    }
                }
            };

        private static void CreateWorkbook(string path, double quantity)
        {
            var workbook = new XSSFWorkbook();
            try
            {
                ISheet sheet = workbook.CreateSheet("Sheet1");
                sheet.CreateRow(0).CreateCell(0).SetCellValue("1.1");
                sheet.GetRow(0).CreateCell(11).SetCellValue(quantity);
                sheet.CreateRow(4).CreateCell(11).SetCellValue("设备");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                    FileShare.None))
                    workbook.Write(stream);
            }
            finally { workbook.Close(); }
        }

        private static double ReadQuantity(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite))
            {
                var workbook = new XSSFWorkbook(stream);
                try { return workbook.GetSheet("Sheet1").GetRow(0).GetCell(11).NumericCellValue; }
                finally { workbook.Close(); }
            }
        }

        private static void WriteNote(string path, string note)
        {
            XSSFWorkbook workbook;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite))
                workbook = new XSSFWorkbook(stream);
            try
            {
                IRow row = workbook.GetSheet("Sheet1").GetRow(10)
                    ?? workbook.GetSheet("Sheet1").CreateRow(10);
                row.CreateCell(2).SetCellValue(note);
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                    FileShare.None))
                    workbook.Write(stream);
            }
            finally { workbook.Close(); }
        }

        private static string ReadNote(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite))
            {
                var workbook = new XSSFWorkbook(stream);
                try { return workbook.GetSheet("Sheet1").GetRow(10).GetCell(2).StringCellValue; }
                finally { workbook.Close(); }
            }
        }
    }
}

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
                Assert.False(File.Exists(target + ".boq.lock"));

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
                    Assert.Equal("SUM(L1:M1)", result.GetRow(0).GetCell(4).CellFormula);
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
    }
}

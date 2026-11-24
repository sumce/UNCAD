using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Features.PdfExport;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-057：清单（BOQ）/ PDF / DWG 导出对「一个机台两条同名回路（上游编号不同）」
    /// 的口径。3.5.0 之前 BOQ 设备列按回路名定位——同名回路并成一列（数量相加）、
    /// DATA 页只留最后一行；PDF 靠 -2 后缀按选择顺序区分，重出一条会顶掉另一条。
    /// </summary>
    public sealed class BoqSameNameCircuitTests
    {
        private const string Machine = "MEDPC01";
        private const string Circuit = "AC Rack";
        private const string UpA = "3N-11B1A-PP-02";
        private const string UpB = "3N-31D2A-PP-03";
        private const string LabelA = Circuit + "（上游 " + UpA + "）";
        private const string LabelB = Circuit + "（上游 " + UpB + "）";

        private static readonly MachineRow[] Rows =
        {
            new MachineRow { MachineId = Machine, CircuitName = Circuit, Fr = UpA },
            new MachineRow { MachineId = Machine, CircuitName = Circuit, Fr = UpB },
            new MachineRow { MachineId = Machine, CircuitName = "LP", Fr = "3N-LP-01" }
        };

        private static string Key(string circuit, string upstream)
            => CircuitIdentityKey.Of(new MachineRow
                { MachineId = Machine, CircuitName = circuit, Fr = upstream });

        private static SubmissionRecord Record(string circuit, string upstream,
            string quantity, bool label = true)
        {
            var record = new SubmissionRecord
            {
                MachineId = Machine,
                DeviceName = circuit,
                CircuitKey = upstream == null ? "" : Key(circuit, upstream),
                // DATA 行按电缆长度(m) 列核对是哪一条回路的数据。
                CableMeters = quantity,
                Materials = { new SubmissionMaterial { Code = "1.1", Quantity = quantity } }
            };
            if (label) BoqDeviceLabels.Apply(new[] { record }, Rows);
            return record;
        }

        [Fact]
        public void Labels_SameNameCircuitsGetUpstreamSuffixUniqueNamesStayPlain()
        {
            SubmissionRecord a = Record(Circuit, UpA, "12");
            SubmissionRecord b = Record(Circuit, UpB, "3");
            SubmissionRecord lp = Record("LP", "3N-LP-01", "8");

            Assert.Equal(LabelA, a.DeviceLabel);
            Assert.Equal(LabelB, b.DeviceLabel);
            Assert.Equal("LP", lp.DeviceLabel);
            Assert.Equal(UpA, a.UpstreamId);
            // 分组键：有身份键用键——两条同名回路不是一个组。
            Assert.NotEqual(BoqDeviceLabels.ColumnKey(a), BoqDeviceLabels.ColumnKey(b));

            // 台账不可用：退回回路名（旧口径），不拦清单输出。
            SubmissionRecord plain = Record(Circuit, UpA, "12", label: false);
            BoqDeviceLabels.Apply(new[] { plain }, null);
            Assert.Equal(Circuit, plain.DeviceLabel);

            // 台账里的上游带槽位/轴位标注：列名用去掉标注的编号。
            BoqDeviceLabels.Apply(new[] { plain }, new[]
            {
                new MachineRow { MachineId = Machine, CircuitName = Circuit, Fr = UpA + " 36(43/AC)" },
                new MachineRow { MachineId = Machine, CircuitName = Circuit, Fr = UpB }
            });
            Assert.Equal(LabelA, plain.DeviceLabel);
        }

        private static string CreateTemplate(string root)
        {
            string template = Path.Combine(root, "template.xlsx");
            var workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("Sheet1");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("1.1");
            sheet.GetRow(0).CreateCell(4).SetCellValue(0d);
            using (var stream = new FileStream(template, FileMode.CreateNew))
                workbook.Write(stream);
            workbook.Close();
            return template;
        }

        private static string[] DeviceColumns(string target)
        {
            using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
            {
                var workbook = new XSSFWorkbook(stream);
                try
                {
                    NPOI.SS.UserModel.IRow header = workbook.GetSheet("Sheet1").GetRow(4);
                    var names = new List<string>();
                    for (int column = 11; column < header.LastCellNum; column++)
                        names.Add(header.GetCell(column)?.ToString() ?? "");
                    return names.ToArray();
                }
                finally { workbook.Close(); }
            }
        }

        private static double Quantity(string target, string device)
        {
            using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read))
            {
                var workbook = new XSSFWorkbook(stream);
                try
                {
                    ISheet sheet = workbook.GetSheet("Sheet1");
                    NPOI.SS.UserModel.IRow header = sheet.GetRow(4);
                    for (int column = 11; column < header.LastCellNum; column++)
                        if ((header.GetCell(column)?.ToString() ?? "") == device)
                            return sheet.GetRow(0).GetCell(column).NumericCellValue;
                    throw new InvalidOperationException("设备列不存在：" + device);
                }
                finally { workbook.Close(); }
            }
        }

        [Fact]
        public void Write_SameNameCircuitsGetSeparateColumnsNotOneSummedColumn()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_same_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = CreateTemplate(root);
            string target = Path.Combine(root, "[BOQ]MEDPC01.xlsx");
            try
            {
                BoqWorkbookWriter.Write(target, template, new[]
                {
                    Record(Circuit, UpA, "12"), Record(Circuit, UpB, "3")
                });
                Assert.Equal(new[] { LabelA, LabelB }, DeviceColumns(target));
                Assert.Equal(12d, Quantity(target, LabelA));
                Assert.Equal(3d, Quantity(target, LabelB));

                // 旧口径：只重出其中一条时，“AC Rack”那一列被清成这一条的量。
                BoqWorkbookWriter.Write(target, template, new[] { Record(Circuit, UpB, "7") });
                Assert.Equal(12d, Quantity(target, LabelA));
                Assert.Equal(7d, Quantity(target, LabelB));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Write_LegacyMergedColumnIsTakenOverNotLeftBehind()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_legacy_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = CreateTemplate(root);
            string target = Path.Combine(root, "[BOQ]MEDPC01.xlsx");
            try
            {
                // 旧版留下的合并列：回路名命名、数量是两条回路的和。
                BoqWorkbookWriter.Write(target, template, new[]
                    { Record(Circuit, null, "15", label: false) });
                Assert.Equal(new[] { Circuit }, DeviceColumns(target));

                BoqWorkbookWriter.Write(target, template, new[]
                {
                    Record(Circuit, UpA, "12"), Record(Circuit, UpB, "3")
                });

                // 合并列被第一条同名回路接管改名，不留一列重复数量进合计。
                Assert.Equal(new[] { LabelA, LabelB }, DeviceColumns(target));
                Assert.Equal(12d, Quantity(target, LabelA));
                Assert.Equal(3d, Quantity(target, LabelB));
                Assert.Equal(15d, Quantity(target, LabelA) + Quantity(target, LabelB));
            }
            finally { Directory.Delete(root, true); }
        }

        private static string Cell(ISheet sheet, int row, int column)
            => sheet.GetRow(row)?.GetCell(column)?.ToString() ?? "";

        [Fact]
        public void DataSheet_SameNameCircuitsKeepOneRowEachWithUpstreamColumn()
        {
            var workbook = new XSSFWorkbook();
            try
            {
                BoqDataSheet.Write(workbook, new[]
                    { Record(Circuit, UpA, "12"), Record(Circuit, UpB, "3") },
                    new DateTime(2026, 11, 24, 9, 0, 0));
                ISheet sheet = workbook.GetSheet("DATA");
                Assert.Equal(2, sheet.LastRowNum);
                Assert.Equal("上游编号", Cell(sheet, 0, BoqDataSheet.UpstreamIdIndex));
                Assert.Equal(UpA, Cell(sheet, 1, BoqDataSheet.UpstreamIdIndex));
                Assert.Equal(UpB, Cell(sheet, 2, BoqDataSheet.UpstreamIdIndex));
                Assert.Equal("12", Cell(sheet, 1, 5));

                // 重跑其中一条：只更新它那一行，另一条不变（旧版互相覆盖）。
                BoqDataSheet.Write(workbook, new[] { Record(Circuit, UpB, "7") },
                    new DateTime(2026, 11, 24, 10, 0, 0));
                Assert.Equal(2, sheet.LastRowNum);
                Assert.Equal("7", Cell(sheet, 2, 5));
                Assert.Equal("12", Cell(sheet, 1, 5));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void DataSheet_LegacyRowWithoutUpstreamIsReusedAndStamped()
        {
            var workbook = new XSSFWorkbook();
            try
            {
                // 旧版 DATA 行：没有上游编号。
                BoqDataSheet.Write(workbook,
                    new[] { Record(Circuit, null, "15", label: false) },
                    new DateTime(2026, 11, 24, 9, 0, 0));
                ISheet sheet = workbook.GetSheet("DATA");
                Assert.Equal(1, sheet.LastRowNum);

                // 这个回路名在表里只有一行且上游为空：沿用并补上上游，不留两行。
                BoqDataSheet.Write(workbook, new[] { Record(Circuit, UpA, "12") },
                    new DateTime(2026, 11, 24, 10, 0, 0));
                Assert.Equal(1, sheet.LastRowNum);
                Assert.Equal(UpA, Cell(sheet, 1, BoqDataSheet.UpstreamIdIndex));
                Assert.Equal("12", Cell(sheet, 1, 5));

                // 第二条同名回路：另起一行。
                BoqDataSheet.Write(workbook, new[] { Record(Circuit, UpB, "3") },
                    new DateTime(2026, 11, 24, 11, 0, 0));
                Assert.Equal(2, sheet.LastRowNum);
                Assert.Equal(UpB, Cell(sheet, 2, BoqDataSheet.UpstreamIdIndex));
            }
            finally { workbook.Close(); }
        }

        [Fact]
        public void PdfNaming_UpstreamSuffixOnlyForSameNameCircuits()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 名字唯一：文件名不变。
            Assert.Equal("MEDPC01_LP.pdf", PdfExportNaming.BuildFileName(Machine, "LP"));
            // 同名回路：带上上游编号，两条互不相同、不再靠 -2 后缀按选择顺序区分。
            string first = PdfExportNaming.AllocateFileName(Machine, Circuit, used, UpA);
            string second = PdfExportNaming.AllocateFileName(Machine, Circuit, used, UpB);
            Assert.Equal("MEDPC01_AC Rack_3N-11B1A-PP-02.pdf", first);
            Assert.Equal("MEDPC01_AC Rack_3N-31D2A-PP-03.pdf", second);
            // 单独重出其中一条：文件名与上次一致，不会顶掉另一条。
            var again = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(second, PdfExportNaming.AllocateFileName(Machine, Circuit, again, UpB));
        }

        [Fact]
        public void DwgExport_CountsCircuitsByIdentityNotByName()
        {
            string source = File.ReadAllText(RepoFile("src", "UNCAD", "Features",
                "DwgExport", "DwgExportFeature.cs"));
            Assert.Contains("\"k:\" + frame.CircuitKey", source);
            Assert.DoesNotContain(".Select(frame => (frame.DeviceName ?? \"\").Trim())\r\n"
                + "                            .Distinct(StringComparer.OrdinalIgnoreCase).Count()",
                source);
        }

        private static string RepoFile(params string[] parts)
            => Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..")), Path.Combine(parts));
    }
}

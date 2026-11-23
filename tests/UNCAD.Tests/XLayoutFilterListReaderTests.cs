using System;
using System.IO;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XLayoutFilterListReaderTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "uncad_xlayout_filter_" + Guid.NewGuid().ToString("N"));

        public XLayoutFilterListReaderTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private string TempPath(string name) => Path.Combine(_directory, name);

        [Fact]
        public void Xlsx_ReadsMachineAndCircuitColumnsFromFirstSheet()
        {
            string path = TempPath("list.xlsx");
            WriteXlsx(path, new[] { "机台ID", "回路名称" }, new[]
            {
                new[] { "MTPWM04", "Scrubber - 2" },
                new[] { "MTPWM04", "LP" },
                new[] { "MTPWM05", "" }
            });

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Equal(3, list.Entries.Count);
            Assert.Equal("MTPWM04", list.Entries[0].MachineId);
            Assert.Equal("Scrubber - 2", list.Entries[0].CircuitName);
            Assert.True(list.Entries[2].IsMachineLevel);
            Assert.Equal(path, list.SourcePath);
        }

        [Theory]
        [InlineData("U_机台ID", "U_回路名称")]
        [InlineData("机台", "设备名称")]
        [InlineData("MachineID", "回路")]
        public void Xlsx_AcceptsHeaderAliases(string machineHeader, string circuitHeader)
        {
            string path = TempPath("aliases.xlsx");
            WriteXlsx(path, new[] { circuitHeader, machineHeader }, new[]
            {
                new[] { "泵1", "M01" }
            });

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Equal("M01", list.Entries[0].MachineId);
            Assert.Equal("泵1", list.Entries[0].CircuitName);
        }

        [Fact]
        public void Xlsx_CircuitColumnOptional_YieldsMachineLevelEntries()
        {
            string path = TempPath("machine-only.xlsx");
            WriteXlsx(path, new[] { "机台ID" }, new[]
            {
                new[] { "M01" }, new[] { "M02" }
            });

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Equal(2, list.Entries.Count);
            Assert.All(list.Entries, entry => Assert.True(entry.IsMachineLevel));
        }

        [Fact]
        public void Xlsx_MissingMachineColumn_NamesAcceptableHeaders()
        {
            string path = TempPath("bad-header.xlsx");
            WriteXlsx(path, new[] { "设备", "数量" }, new[]
            {
                new[] { "M01", "2" }
            });

            bool ok = XLayoutFilterListReader.TryRead(path,
                out _, out string error);

            Assert.False(ok);
            Assert.Contains("认不出机台ID列", error);
            Assert.Contains("机台ID", error);
        }

        [Fact]
        public void Xlsx_SkipsEmptyMachineRowsAndDeduplicates()
        {
            string path = TempPath("dedup.xlsx");
            WriteXlsx(path, new[] { "机台ID", "回路名称" }, new[]
            {
                new[] { "M01", "A" },
                new[] { "", "ghost" },
                new[] { "M01", "A" },
                new[] { " m 01 ", "a " }
            });

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Single(list.Entries);
            Assert.Equal("M01", list.Entries[0].MachineId);
        }

        [Fact]
        public void Csv_Utf8WithBom_CommaDelimiter()
        {
            string path = TempPath("utf8.csv");
            File.WriteAllText(path, "机台ID,回路名称\r\nMTPWM04,Scrubber - 2\r\nMTPWM05,\r\n",
                new UTF8Encoding(true));

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Equal(2, list.Entries.Count);
            Assert.Equal("Scrubber - 2", list.Entries[0].CircuitName);
            Assert.True(list.Entries[1].IsMachineLevel);
        }

        [Fact]
        public void Csv_GbkWithoutBom_SemicolonDelimiter()
        {
            string path = TempPath("gbk.csv");
            File.WriteAllBytes(path, Encoding.GetEncoding(936).GetBytes(
                "机台ID;回路名称\nM01;泵1\n"));

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Equal("M01", list.Entries[0].MachineId);
            Assert.Equal("泵1", list.Entries[0].CircuitName);
        }

        [Fact]
        public void Csv_TabDelimiter_IsSniffedFromHeader()
        {
            string path = TempPath("tabs.csv");
            File.WriteAllText(path, "U_机台ID\tU_回路名称\nM01\t泵1\n",
                new UTF8Encoding(false));

            bool ok = XLayoutFilterListReader.TryRead(path,
                out var list, out string error);

            Assert.True(ok, error);
            Assert.Equal("泵1", list.Entries[0].CircuitName);
        }

        [Fact]
        public void Csv_MissingMachineColumn_NamesAcceptableHeaders()
        {
            string path = TempPath("bad.csv");
            File.WriteAllText(path, "名称,数量\nM01,1\n", new UTF8Encoding(false));

            bool ok = XLayoutFilterListReader.TryRead(path,
                out _, out string error);

            Assert.False(ok);
            Assert.Contains("认不出机台ID列", error);
        }

        [Fact]
        public void MissingFile_FailsWithNamedPath()
        {
            bool ok = XLayoutFilterListReader.TryRead(
                Path.Combine(_directory, "absent.xlsx"), out _, out string error);

            Assert.False(ok);
            Assert.Contains("清单文件不存在", error);
        }

        private static void WriteXlsx(string path, string[] header, string[][] rows)
        {
            IWorkbook workbook = new XSSFWorkbook();
            try
            {
                ISheet sheet = workbook.CreateSheet("清单");
                IRow headerRow = sheet.CreateRow(0);
                for (int c = 0; c < header.Length; c++)
                    headerRow.CreateCell(c).SetCellValue(header[c]);
                for (int r = 0; r < rows.Length; r++)
                {
                    IRow row = sheet.CreateRow(r + 1);
                    for (int c = 0; c < rows[r].Length; c++)
                        row.CreateCell(c).SetCellValue(rows[r][c]);
                }
                using (var stream = new FileStream(path, FileMode.Create,
                    FileAccess.Write, FileShare.Read)) workbook.Write(stream);
            }
            finally { workbook.Close(); }
        }
    }
}

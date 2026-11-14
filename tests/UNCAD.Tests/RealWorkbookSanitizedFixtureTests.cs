using System;
using System.IO;
using System.Text.RegularExpressions;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 真实客户台账的脱敏夹具冒烟测试（夹具由 tools/FixtureSanitizer 从
    /// 真实台账生成，字符串经确定性映射替换为 S001、S002… 占位符，
    /// 生成时已通过关键词与最长 CJK 串泄漏检查）。
    /// 工具对原表的统计输出：数据行 4999（回路名非空）、删除线行 163、
    /// 可解析行 4836、去重机台 832；原表本含删除线行，未追加 S-STRIKE 样本。
    /// </summary>
    public class RealWorkbookSanitizedFixtureTests
    {
        private const int OriginalDataRows = 4999;
        private const int OriginalStrikeoutRows = 163;
        private const int ExpectedParsedRows = OriginalDataRows - OriginalStrikeoutRows; // 4836
        private const int ExpectedMachineCount = 832;

        private static string FixturePath
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures",
                "machine-ledger-sanitized.xlsx");

        [Fact]
        public void ReadRows_BindsUnifiedHeaderOnSanitizedLedger()
        {
            Assert.True(File.Exists(FixturePath), "缺少脱敏夹具: " + FixturePath);

            var rows = ExcelMachineReader.ReadRows(FixturePath);

            // 夹具保留统一 U_ 表头与「回路名称」，能被绑定并解析出数据。
            Assert.NotEmpty(rows);
            Assert.True(rows.Count > 0, "解析行数应大于 0");
            Assert.True(ExcelMachineReader.DistinctMachineIds(FixturePath).Count >= 1,
                "机台数应至少为 1");
        }

        [Fact]
        public void MachineIds_AllMatchSanitizerPlaceholderPattern()
        {
            var ids = ExcelMachineReader.DistinctMachineIds(FixturePath);

            // 脱敏映射为保序编号占位符：S + 至少 3 位数字。
            Assert.All(ids, id => Assert.Matches(new Regex(@"^S\d{3,}$"), id));
            Assert.Equal(ExpectedMachineCount, ids.Count);
        }

        [Fact]
        public void ReadRows_StrikeoutRowsAreIgnored()
        {
            var rows = ExcelMachineReader.ReadRows(FixturePath);

            // 原表 4999 个数据行中 163 行回路名带删除线（D-002 双通道识别），
            // 这些行必须被忽略：解析行数 == 非删除线数据行数。
            Assert.Equal(ExpectedParsedRows, rows.Count);
            // 原表本含删除线行，工具未补样本；该字面量不应出现在任何解析结果里。
            Assert.DoesNotContain(rows, row => row.CircuitName == "S-STRIKE");
        }
    }
}

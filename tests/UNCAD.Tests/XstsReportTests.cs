using System.Collections.Generic;
using System.Linq;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XstsReportTests
    {
        [Fact]
        public void Build_FrameDetailsPreserveEveryDrawnMaterialAndText()
        {
            var frame = new XstsCircuitRecord("M Q-01", "设备 A") { FrameHandle = "0012AB" };
            frame.TableRows.Add(new List<string> { "材料清单", "", "", "", "", "" });
            frame.TableRows.Add(new List<string> { "序号", "项目名称", "项目特征", "单位", "数量", "编码" });
            frame.TableRows.Add(new List<string> { "1", "多芯电缆 XLPE", @"{\C1;1.名称：YJV-3*25mm²}\P敷设", "m", "12.50", "1.1" });
            frame.TableRows.Add(new List<string> { "2", "桥架", "200*100", "m", "8", "2.1" });
            frame.TableRows.Add(new List<string> { "3", "线管", "DN25", "m", "4", "3.1" });
            frame.TableRows.Add(new List<string> { "4", "软管", "DN20", "m", "2", "3.5" });
            frame.TableRows.Add(new List<string> { "5", "插座盘", "现场型号", "台", "1", "4.11" });
            frame.TableRows.Add(new List<string> { "6", "断路器", "3P63A", "个", "0", "5.2" });
            frame.TableRows.Add(new List<string> { "7", "设备", "保留空数量", "台", "", "" });
            frame.TableRows.Add(new List<string> { "自填", "其他配件", "非固定清单", "套", "待确认", "CUSTOM-01" });
            frame.TableRows.Add(new List<string> { "", "未编号项目", "仍须保留", "", "", "" });
            frame.TableRows.Add(new List<string> { "10", "", "", "", "", "" });
            frame.TableRows.Add(new List<string> { "", "", "", "", "", "" });

            XstsReport report = XstsReportBuilder.Build(new[] { frame },
                new[] { new XstsCircuitRecord("MQ-01", "设备A") });

            Assert.Equal(1, report.FrameCount);
            Assert.Equal(1, report.SelectedCircuitCount);
            XstsFrameDetail detail = Assert.Single(report.Frames);
            Assert.Equal("0012AB", detail.FrameHandle);
            Assert.Equal("M Q-01", detail.MachineId);
            Assert.Equal("设备 A", detail.CircuitName);
            Assert.Equal(9, detail.Items.Count);
            Assert.Equal(9, report.MaterialColumns.Count);
            Assert.Equal("1.名称：YJV-3*25mm²\n敷设", detail.Items[0].Description);
            Assert.Equal("12.50", detail.Items[0].Quantity);
            Assert.Equal("0", detail.Items[5].Quantity);
            Assert.Equal("", detail.Items[6].Quantity);
            Assert.Equal("CUSTOM-01", detail.Items[7].Code);
            Assert.Equal("待确认", detail.Items[7].Quantity);
            Assert.Equal("未编号项目", detail.Items[8].Name);
            Assert.Equal("YJV-3*25mm²", detail.Cells[0].Model);
            Assert.Equal("12.50", detail.Cells[0].Quantity);
            Assert.Equal("DN20", detail.Cells[2].Model);
            Assert.Equal("DN25", detail.Cells[3].Model);
            Assert.Equal("0", detail.Cells[5].Quantity);
            Assert.Equal("", detail.Cells[6].Quantity);
            Assert.Contains("CUSTOM-01", detail.Cells[7].DetailText);
            Assert.Contains(@"\P", frame.TableRows[2][2]);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void Build_DuplicateEmptyAndFailedFramesKeepTheirOwnIds()
        {
            var first = new XstsCircuitRecord("M1", "A") { FrameHandle = "A1" };
            first.TableRows.Add(new List<string> { "1", "桥架", "200*100", "m", "2", "2.1" });
            var duplicate = new XstsCircuitRecord("M1", "A") { FrameHandle = "A2" };
            duplicate.TableRows.AddRange(first.TableRows);
            var failed = new XstsCircuitRecord("", "", "图框 A3 身份读取失败") { FrameHandle = "A3" };
            failed.TableRows.Add(new List<string> { "1", "软管", "DN20", "m", "3", "" });
            var empty = new XstsCircuitRecord("M2", "B") { FrameHandle = "A4" };

            XstsReport report = XstsReportBuilder.Build(new[] { first, duplicate, failed, empty },
                new[] { new XstsCircuitRecord("M1", "A"), new XstsCircuitRecord("M2", "B") });

            Assert.Equal(4, report.FrameCount);
            Assert.Equal(2, report.SelectedCircuitCount);
            Assert.Equal(new[] { "A1", "A2", "A3", "A4" },
                report.Frames.Select(row => row.FrameHandle));
            Assert.Equal("软管", Assert.Single(report.Frames[2].Items).Name);
            Assert.Empty(report.Frames[3].Items);
            Assert.All(report.Frames[3].Cells, cell => Assert.Null(cell));
            Assert.Contains(report.Issues, issue => issue.FrameIdText == "A2"
                && issue.Description.Contains("重复"));
            Assert.Contains(report.Issues, issue => issue.FrameIdText == "A3"
                && issue.Description.Contains("身份读取失败"));
        }

        [Fact]
        public void Build_AlignsModelQuantityPairsAndLeavesMissingMaterialsBlank()
        {
            var first = new XstsCircuitRecord("M1", "A") { FrameHandle = "001" };
            first.TableRows.Add(new List<string> { "1", "电缆", "YJV-3*25", "m", "12.50", "1.1" });
            first.TableRows.Add(new List<string> { "2", "桥架", "300*100", "m", "2", "2.5" });
            first.TableRows.Add(new List<string> { "3", "桥架", "400*100", "m", "0", "2.4" });
            first.TableRows.Add(new List<string> { "", "", "无项目名称仍保留", "", "待确认", "00123" });
            var second = new XstsCircuitRecord("M1", "A") { FrameHandle = "002" };
            second.TableRows.Add(new List<string> { "1", "桥架", "200*100", "m", "8", "2.1" });
            second.TableRows.Add(new List<string> { "2", "电缆", "YJV-3*10", "m", "6", "1.3" });
            second.TableRows.Add(new List<string> { "3", "配件[自填].A", "原样保留", "套", "", "X-01" });
            var empty = new XstsCircuitRecord("M2", "B") { FrameHandle = "003" };

            XstsReport report = XstsReportBuilder.Build(new[] { first, second, empty },
                new[] { new XstsCircuitRecord("M1", "A"), new XstsCircuitRecord("M2", "B") });

            Assert.Equal(3, report.Frames.Count);
            Assert.Equal(new[] { "电缆型号", "桥架型号", "软管型号", "线管型号",
                "桥架2型号", "未命名项目型号", "配件[自填].A型号" },
                report.MaterialColumns.Select(column => column.ModelHeader));
            Assert.Equal(new[] { "电缆米数", "桥架米数", "软管米数", "线管米数",
                "桥架2米数", "未命名项目数量", "配件[自填].A数量（套）" },
                report.MaterialColumns.Select(column => column.QuantityHeader));
            Assert.Equal(new[] { "001", "002", "003" },
                report.Frames.Select(frame => frame.FrameHandle));
            Assert.Equal("YJV-3*25", report.Frames[0].Cells[0].Model);
            Assert.Equal("12.50", report.Frames[0].Cells[0].Quantity);
            Assert.Equal("300*100", report.Frames[0].Cells[1].Model);
            Assert.Equal("2", report.Frames[0].Cells[1].Quantity);
            Assert.Equal("400*100", report.Frames[0].Cells[4].Model);
            Assert.Equal("0", report.Frames[0].Cells[4].Quantity);
            Assert.Equal("无项目名称仍保留", report.Frames[0].Cells[5].Model);
            Assert.Equal("待确认", report.Frames[0].Cells[5].Quantity);
            Assert.Contains("编码：00123", report.Frames[0].Cells[5].DetailText);
            Assert.Null(report.Frames[0].Cells[2]);
            Assert.Null(report.Frames[0].Cells[3]);
            Assert.Equal("YJV-3*10", report.Frames[1].Cells[0].Model);
            Assert.Equal("200*100", report.Frames[1].Cells[1].Model);
            Assert.Null(report.Frames[1].Cells[4]);
            Assert.Equal("X-01", report.Frames[1].Cells[6].Code);
            Assert.All(report.Frames[2].Cells, cell => Assert.Null(cell));
            Assert.Contains(report.Issues, issue => issue.FrameHandle == "002"
                && issue.Description.Contains("重复"));
        }

        [Fact]
        public void Build_UsesCatalogNamesForStandardColumnsWithoutClassifyingRemovalItemsAsCable()
        {
            var frame = new XstsCircuitRecord("M1", "A") { FrameHandle = "01" };
            string[] categories = { "电缆", "桥架", "软管", "线管" };
            var catalog = ListItemReader.ReadEmbedded();
            foreach (string category in categories)
            {
                ListItem item = catalog.First(entry => entry.Category == category);
                frame.TableRows.Add(new List<string>
                {
                    "1", item.Name, item.Feature, item.Unit, "6.50", item.Code
                });
            }
            frame.TableRows.Add(new List<string> { "5", "电缆拆除", "拆除现有电缆", "m", "3", "" });

            XstsReport report = XstsReportBuilder.Build(new[] { frame }, new XstsCircuitRecord[0]);

            Assert.Equal(5, report.MaterialColumns.Count);
            Assert.Equal("电缆拆除型号", report.MaterialColumns[4].ModelHeader);
            for (int index = 0; index < categories.Length; index++)
            {
                Assert.Equal(categories[index] + "型号", report.MaterialColumns[index].ModelHeader);
                Assert.Equal(categories[index] + "米数", report.MaterialColumns[index].QuantityHeader);
                Assert.Equal(BoqFeatureName.Extract(frame.TableRows[index][2]),
                    report.Frames[0].Cells[index].Model);
                Assert.Equal("6.50", report.Frames[0].Cells[index].Quantity);
            }
            Assert.Equal("拆除现有电缆", report.Frames[0].Cells[4].Model);
        }

        [Fact]
        public void Build_RejectedFrameRetainsIdAndBoundaryFailure()
        {
            var regions = new FrameRegionCollection { SelectedFrameCount = 1 };
            regions.RejectFrame("F0", "图框 F0 已旋转，不能使用轴对齐边界批量处理。");
            XstsReport report = XstsReportBuilder.Build(regions.RejectedFrames
                .Select(pair => new XstsCircuitRecord("", "", pair.Value) { FrameHandle = pair.Key }),
                new XstsCircuitRecord[0]);

            Assert.Equal(regions.SelectedFrameCount, report.FrameCount);
            Assert.Equal("F0", Assert.Single(report.Frames).FrameHandle);
            Assert.Equal("F0", Assert.Single(report.Issues).FrameIdText);
            Assert.Equal(regions.Errors[0], report.Issues[0].Description);
        }

        [Fact]
        public void Build_UsesOnlyExactSelectedMachineIdsWithoutNearNameHints()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("MSRTL05", "A") },
                new[] { new XstsCircuitRecord("MSRTL04", "A") });

            Assert.Equal("MSRTL05", Assert.Single(report.Machines).MachineId);
            Assert.Equal("机台ID在Excel基准中不存在", Assert.Single(report.Issues).Description);
            Assert.False(report.Machines[0].ExpectedDataAvailable);
        }

        [Fact]
        public void Build_DeduplicatesSelectedCircuitsAndReportsMissingExpected()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B")
                },
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B"),
                    new XstsCircuitRecord("M1", "C")
                });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(2, machine.SelectedCircuitCount);
            Assert.Equal(3, machine.ExpectedCircuitCount);
            Assert.Equal("C", Assert.Single(machine.MissingCircuits));
            Assert.Equal(1, report.MissingCircuitCount);
        }

        [Fact]
        public void Build_ReportsOneBatchAndSequencePerMachine()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B")
                },
                new[]
                {
                    new XstsCircuitRecord("M1", "A", "B-2026-09", "17"),
                    new XstsCircuitRecord("M1", "B", "B-2026-09", "17")
                });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal("B-2026-09", machine.Batch);
            Assert.Equal("17", machine.Seq);
        }

        [Fact]
        public void Build_IsCaseInsensitiveAndIgnoresIncompleteRows()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("m1", " a "), new XstsCircuitRecord("", "B") },
                new[] { new XstsCircuitRecord("M1", "A") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal("m1", machine.MachineId);
            Assert.Equal(1, machine.SelectedCircuitCount);
            Assert.Empty(machine.MissingCircuits);
        }

        [Fact]
        public void Build_IgnoresInternalWhitespaceInMachineAndCircuitNames()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M Q-01", "设备 A") },
                new[] { new XstsCircuitRecord("MQ-01", "设备A") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal("M Q-01", machine.MachineId);
            Assert.Equal(1, machine.SelectedCircuitCount);
            Assert.Equal(1, machine.ExpectedCircuitCount);
            Assert.Empty(machine.MissingCircuits);
            Assert.Empty(machine.UnexpectedCircuits);
        }

        [Fact]
        public void Build_DoesNotAddMachinesThatWereNotSelected()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M2", "B")
                });

            Assert.Single(report.Machines);
            Assert.Equal("M1", report.Machines[0].MachineId);
        }

        [Fact]
        public void Build_WithoutExpectedDataDoesNotClaimZeroMissing()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new XstsCircuitRecord[0], XstsExpectedDataStatus.NotConfigured,
                "未配置机台 Excel");

            Assert.False(report.ExpectedDataAvailable);
            Assert.Equal("无法判断", report.ExpectedCircuitText);
            Assert.Equal("无法判断", report.MissingCircuitText);
            Assert.Equal("无法判断", report.Machines[0].MissingText);
        }

        [Fact]
        public void Build_ReportsIncompleteDuplicateAndUnknownMachineFrames()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("m1", "a"),
                    new XstsCircuitRecord("", "B")
                },
                new[] { new XstsCircuitRecord("M2", "C") });

            Assert.Equal(3, report.FrameCount);
            Assert.Contains(report.Issues, issue => issue.Description.Contains("重复"));
            Assert.Contains(report.Issues, issue => issue.Description == "缺少机台ID");
            Assert.Contains(report.Issues, issue => issue.Description.Contains("Excel基准"));
            Assert.Empty(report.Machines[0].UnexpectedCircuits);
            Assert.Equal("部分无法判断", report.ExpectedCircuitText);
            Assert.Equal("部分无法判断", report.MissingCircuitText);
        }

        [Fact]
        public void Build_MachineAbsentFromReadableWorkbookIsUnknownPerRow()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("MISSING", "A") },
                new[] { new XstsCircuitRecord("OTHER", "B") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.True(report.ExpectedDataAvailable);
            Assert.False(machine.ExpectedDataAvailable);
            Assert.Equal("无法判断", machine.ExpectedCircuitText);
            Assert.Equal("无法判断", machine.MissingText);
            Assert.Empty(machine.UnexpectedCircuits);
            Assert.Equal(0, report.UnexpectedCircuitCount);
            Assert.Contains(report.Issues, issue => issue.Description.Contains("Excel基准"));
        }

        [Fact]
        public void Build_PreservesFrameReadErrorsAsIssues()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("", "", "图框 42 身份读取失败") },
                new XstsCircuitRecord[0]);

            XstsFrameIssue issue = Assert.Single(report.Issues);
            Assert.Equal(1, issue.FrameNumber);
            Assert.Equal("图框 42 身份读取失败", issue.Description);
            Assert.Empty(report.Machines);
        }
    }
}

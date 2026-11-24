using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-057：制图信息表（图框内小表：专业/楼层/制图/审核/日期/版本）内容模板、
    /// 创建时间持久化、清单标题模板、XLAYOUT 排版参数。
    /// </summary>
    public sealed class DrawingInfoTemplateTests
    {
        private static readonly DateTime RunUtc = new DateTime(2026, 11, 25, 6, 30, 0,
            DateTimeKind.Utc);

        private static MachineRow Machine() => new MachineRow
        {
            Region = "AMHS", MachineId = "MEDPC01", CircuitName = "AC Rack",
            Batch = "B3", Seq = "07", Fr = "3U-UPS10D1-PP-03 36(43/AC)",
            DeviceFloor = "3F", PanelFloor = "2F", DownstreamAxis = "C-05",
            UpstreamAxis = "A-01", FacilitySwitch = "FS-12", Next = "I-Line盘",
            Cable = "ZB-YJV-3*25", Detail = "N480 3P4W 3P100A"
        };

        private static string Local(DateTime utc, string format = "yyyy-MM-dd")
            => utc.ToLocalTime().ToString(format, System.Globalization.CultureInfo.InvariantCulture);

        private static DrawingInfoContext U1F() => DrawingInfoContext.ForRun(
            Machine(), null, "U1F", RunUtc, "tester");

        private static DrawingInfoContext U1U(FrameInfoJsonRecord previous)
            => DrawingInfoContext.ForRun(Machine(), previous, "U1U", RunUtc, "tester");

        [Fact]
        public void DefaultTemplates_ReproduceTheOldBehaviourExactly()
        {
            // 3.5.0：楼层 = 设备楼层/上游楼层、日期 = 运行当天，其余四格从不写。
            IReadOnlyList<string> values = DrawingInfoTemplateRenderer.Render(
                DrawingInfoTemplates.Default, U1F());

            Assert.Equal("", values[0]);
            Assert.Equal("3F/2F", values[1]);
            Assert.Equal("", values[2]);
            Assert.Equal("", values[3]);
            Assert.Equal(Local(RunUtc), values[4]);
            Assert.Equal("", values[5]);
            // 只有楼层与日期两列有模板 → 其余列不动图框里的内容。
            Assert.Equal(new[] { false, true, false, false, true, false },
                Enumerable.Range(0, 6).Select(DrawingInfoTemplates.Default.IsSet).ToArray());
            Assert.True(DrawingInfoTemplates.Default.IsDefault);
        }

        [Fact]
        public void FixedTextAndPlaceholders_CanBeMixedInEveryColumn()
        {
            var templates = DrawingInfoTemplates.Create(
                "电气-(区域)", "(设备楼层)F/(上游楼层)F", "张三", "李四",
                "(U1F创建时间:yyyy/MM/dd)", "V(序号)");

            IReadOnlyList<string> values = DrawingInfoTemplateRenderer.Render(templates, U1F());

            Assert.Equal("电气-AMHS", values[0]);
            Assert.Equal("3FF/2FF", values[1]);
            Assert.Equal("张三", values[2]);
            Assert.Equal("李四", values[3]);
            Assert.Equal(Local(RunUtc, "yyyy/MM/dd"), values[4]);
            Assert.Equal("V07", values[5]);
        }

        [Fact]
        public void FullWidthBracketsAndColon_AreAccepted()
        {
            // 用户输入法默认全角：括号与冒号都认。
            string value = DrawingInfoTemplateRenderer.Resolve(
                "（机台ID）－（日期：yyyy年MM月dd日）", U1F());

            Assert.Equal("MEDPC01－" + Local(RunUtc, "yyyy年MM月dd日"), value);
        }

        [Fact]
        public void CreationAndUpdateTimes_FollowTheCommandAndThePreviousRecord()
        {
            const string filled = "2026-11-24T01:05:00.0000000Z";
            // 占位符的括号是语法的一部分，渲染后不保留。
            const string template = "创建=(U1F创建时间)｜更新=(U1U更新时间)｜最近=(更新时间)";

            // U1F：创建时间 = 本次；还没被 U1U 更新过 → (U1U更新时间) 为空。
            Assert.Equal("创建=" + Local(RunUtc) + "｜更新=｜最近=" + Local(RunUtc),
                DrawingInfoTemplateRenderer.Resolve(template, U1F()));

            // U1U：创建时间沿用上一条记录；(U1U更新时间) = 本次。
            string updated = DrawingInfoTemplateRenderer.Resolve(template, U1U(
                new FrameInfoJsonRecord { FilledUtc = filled, LastCommand = "U1F" }));
            Assert.Equal("创建=" + Local(new DateTime(2026, 11, 24, 1, 5, 0, DateTimeKind.Utc))
                + "｜更新=" + Local(RunUtc) + "｜最近=" + Local(RunUtc), updated);
        }

        [Fact]
        public void Update_OnALegacyRecordKeepsItsLastWriteTimeAsCreationTime()
        {
            // 3.5.0 及之前的 JSON 没有创建时间：U1U 接手时把它的最后写入时间当创建时间，
            // 不丢。
            var legacy = new FrameInfoJsonRecord { LastModifiedUtc = "2026-11-20T08:00:00.0000000Z" };

            string value = DrawingInfoTemplateRenderer.Resolve("(U1F创建时间)", U1U(legacy));

            Assert.Equal(Local(new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc)), value);
        }

        [Fact]
        public void UnknownPlaceholder_IsKeptLiteralAndReported()
        {
            // 普通括号文字（专业：电气(动力)）不受影响；拼错的占位符在设置页点名。
            string value = DrawingInfoTemplateRenderer.Resolve("电气(动力) (机台ＩＤ)", U1F());

            Assert.Equal("电气(动力) (机台ＩＤ)", value);
            Assert.Equal(new[] { "动力", "机台ＩＤ" },
                DrawingInfoTemplateRenderer.UnknownPlaceholders("电气(动力) (机台ＩＤ)"));
            Assert.Empty(DrawingInfoTemplateRenderer.UnknownPlaceholders("(机台ID)/(日期)"));
        }

        [Fact]
        public void UpstreamNumber_DropsSlotAndAxisAnnotations()
        {
            Assert.Equal("3U-UPS10D1-PP-03",
                DrawingInfoTemplateRenderer.Resolve("(上游编号)", U1F()));
        }

        [Fact]
        public void InvalidTimeFormat_FallsBackToTheDefaultDateFormat()
        {
            // 非法的 .NET 日期格式（孤立的 %）不能让写入抛异常：退回默认 yyyy-MM-dd。
            Assert.Equal(Local(RunUtc), DrawingInfoTemplateRenderer.Resolve(
                "(日期:%)", U1F()));
        }

        [Fact]
        public void Templates_AreClampedToTheCellLimit()
        {
            DrawingInfoTemplates templates = DrawingInfoTemplates.Create(
                new string('x', 500), "", "", "", "", "");

            Assert.Equal(DrawingInfoTemplates.MaximumTemplateLength, templates.Profession.Length);
            Assert.False(templates.IsDefault);
        }

        // ---- 创建时间持久化 ----

        [Fact]
        public void Update_U1F_StampsCreationTimeAndCommand()
        {
            FrameInfoJsonRecord record = FrameInfoJsonRecordUpdater.Update(null,
                Machine(), null, "U1F", RunUtc);

            Assert.Equal(RunUtc.ToString("o"), record.FilledUtc);
            Assert.Equal("U1F", record.LastCommand);
            Assert.Equal(record.LastModifiedUtc, record.FilledUtc);
        }

        [Fact]
        public void Update_U1U_PreservesCreationTimeAndRecordsTheUpdate()
        {
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "MEDPC01", DeviceName = "AC Rack",
                FilledUtc = "2026-11-24T01:05:00.0000000Z", LastCommand = "U1F"
            };

            FrameInfoJsonRecord record = FrameInfoJsonRecordUpdater.Update(previous,
                Machine(), null, "U1U", RunUtc);

            Assert.Equal("2026-11-24T01:05:00.0000000Z", record.FilledUtc);
            Assert.Equal("U1U", record.LastCommand);
            Assert.Equal(RunUtc.ToString("o"), record.LastModifiedUtc);
        }

        [Fact]
        public void Codec_RoundTripsCreationTimeAndCommand_AndOldJsonParsesEmpty()
        {
            var record = new FrameInfoJsonRecord
            {
                MachineId = "MEDPC01", DeviceName = "AC Rack", Fr = "3N-A",
                FilledUtc = "2026-11-24T01:05:00.0000000Z", LastCommand = "U1U"
            };

            string payload = FrameInfoJsonCodec.Serialize(record);
            Assert.Contains("\"filledUtc\":\"2026-11-24T01:05:00.0000000Z\"", payload);
            Assert.Contains("\"lastCommand\":\"U1U\"", payload);
            FrameInfoJsonRecord parsed = FrameInfoJsonCodec.Parse(payload);
            Assert.Equal(record.FilledUtc, parsed.FilledUtc);
            Assert.Equal("U1U", parsed.LastCommand);

            // 老图：没有这两个键 → 空串，读取不抛。
            string legacy = payload.Replace(",\"filledUtc\":\"2026-11-24T01:05:00.0000000Z\"", "")
                .Replace(",\"lastCommand\":\"U1U\"", "");
            FrameInfoJsonRecord old = FrameInfoJsonCodec.Parse(legacy);
            Assert.Equal("", old.FilledUtc);
            Assert.Equal("", old.LastCommand);
        }

        // ---- 清单标题模板 ----

        [Fact]
        public void BoqTitles_RenderPlaceholdersAndStayEmptyByDefault()
        {
            Assert.False(BoqTitleTemplates.Default.IsSet);
            var templates = BoqTitleTemplates.Create("(机台ID) 二次配清单", "Power", "(日期)");

            Assert.Equal("MEDPC01 二次配清单",
                templates.Render(0, "MEDPC01", RunUtc.ToLocalTime(), "tester"));
            Assert.Equal("Power", templates.Render(1, "MEDPC01", RunUtc.ToLocalTime(), "tester"));
            Assert.Equal(Local(RunUtc), templates.Render(2, "MEDPC01", RunUtc.ToLocalTime(), "tester"));
            // 未知占位符原样保留。
            Assert.Equal("(区域)", BoqTitleTemplates.Create("(区域)", "", "").Render(0,
                "MEDPC01", RunUtc.ToLocalTime(), "tester"));
        }

        [Fact]
        public void BoqWorkbook_AppliesConfiguredTitlesAndLeavesThemAloneWhenBlank()
        {
            string root = Path.Combine(Path.GetTempPath(), "uncad_boq_titles_"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = Path.Combine(root, "template.xlsx");
            var source = new XSSFWorkbook();
            ISheet sheet = source.CreateSheet("Sheet1");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("新项目项目");
            sheet.CreateRow(1).CreateCell(0).SetCellValue("二次配Power工程");
            sheet.CreateRow(2).CreateCell(0).SetCellValue("详细价目表 Breakdown Sheet");
            sheet.CreateRow(3).CreateCell(0).SetCellValue("1.1");
            sheet.GetRow(3).CreateCell(4).SetCellValue(0d);
            using (var stream = new FileStream(template, FileMode.CreateNew))
                source.Write(stream);
            source.Close();
            var records = new[]
            {
                new SubmissionRecord
                {
                    MachineId = "MEDPC01", DeviceName = "AC Rack",
                    Materials = { new SubmissionMaterial { Code = "1.1", Quantity = "12" } }
                }
            };
            try
            {
                string plain = Path.Combine(root, "plain.xlsx");
                BoqWorkbookWriter.Write(plain, template, records);
                Assert.Equal("新项目项目", ReadCell(plain, 0));

                string titled = Path.Combine(root, "titled.xlsx");
                BoqWorkbookWriter.WriteTracked(titled, template, records, false,
                    new BoqWorkbookWriteProgress(), null, null,
                    BoqTitleTemplates.Create("(机台ID) 清单", "", ""));
                Assert.Equal("MEDPC01 清单", ReadCell(titled, 0));
                // 留空的行保持模板里的文字。
                Assert.Equal("二次配Power工程", ReadCell(titled, 1));
                Assert.Equal("详细价目表 Breakdown Sheet", ReadCell(titled, 2));
            }
            finally { Directory.Delete(root, true); }
        }

        private static string ReadCell(string path, int row)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var workbook = new XSSFWorkbook(stream);
                try { return workbook.GetSheet("Sheet1").GetRow(row).GetCell(0).ToString(); }
                finally { workbook.Close(); }
            }
        }

        // ---- XLAYOUT 排版参数 ----

        [Fact]
        public void LayoutOptions_DefaultsMatchTheOldConstants_AndBadValuesFallBack()
        {
            XLayoutRuntimeOptions defaults = XLayoutRuntimeOptions.Default;
            Assert.Equal(10000d, defaults.Spacing);
            Assert.Equal(25000d, defaults.LabelTextHeight);
            Assert.Equal(300000d, defaults.LabelLeftDistance);
            Assert.True(defaults.WarningMarker);

            XLayoutRuntimeOptions options = XLayoutRuntimeOptions.Create(20000d, 30000d, 0d, false);
            Assert.Equal(20000d, options.Spacing);
            Assert.Equal(30000d, options.LabelTextHeight);
            Assert.Equal(0d, options.LabelLeftDistance);   // 0 合法：标签贴着图框
            Assert.False(options.WarningMarker);

            // 手误/越界回落默认，不让图框叠在一起。
            XLayoutRuntimeOptions bad = XLayoutRuntimeOptions.Create(-5d, 0d, double.NaN, true);
            Assert.Equal(10000d, bad.Spacing);
            Assert.Equal(25000d, bad.LabelTextHeight);
            Assert.Equal(300000d, bad.LabelLeftDistance);
        }

        [Fact]
        public void ConfigKeys_ForTheFramePage_UseTheirOwnPersistentNames()
        {
            Assert.Equal("UNC_FILL_INFO_PROFESSION", ConfigKeys.FillInfoProfession);
            Assert.Equal("UNC_FILL_INFO_FLOOR", ConfigKeys.FillInfoFloor);
            Assert.Equal("UNC_FILL_INFO_DRAFTER", ConfigKeys.FillInfoDrafter);
            Assert.Equal("UNC_FILL_INFO_REVIEWER", ConfigKeys.FillInfoReviewer);
            Assert.Equal("UNC_FILL_INFO_DATE", ConfigKeys.FillInfoDate);
            Assert.Equal("UNC_FILL_INFO_VERSION", ConfigKeys.FillInfoVersion);
            Assert.Equal("UNC_BOQ_TITLE_1", ConfigKeys.BoqTitleLine1);
            Assert.Equal("UNC_BOQ_TITLE_2", ConfigKeys.BoqTitleLine2);
            Assert.Equal("UNC_BOQ_TITLE_3", ConfigKeys.BoqTitleLine3);
            Assert.Equal("UNC_XLAYOUT_SPACING", ConfigKeys.XLayoutSpacing);
            Assert.Equal("UNC_XLAYOUT_LABEL_HEIGHT", ConfigKeys.XLayoutLabelTextHeight);
            Assert.Equal("UNC_XLAYOUT_LABEL_DISTANCE", ConfigKeys.XLayoutLabelDistance);
            Assert.Equal("UNC_XLAYOUT_WARNING_MARKER", ConfigKeys.XLayoutWarningMarker);
        }
    }
}

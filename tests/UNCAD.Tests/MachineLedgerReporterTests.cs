using System;
using System.Collections.Generic;
using UNCAD.Core.Excel;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 机台台账上报契约（服务端 POST /api/client/reports/machines）：
    /// 工作簿 U_ 字段映射、凭证/空快照跳过、错误码处理，以及"只上报台账输入字段、
    /// 绝不上报 CAD 产物（电缆长度、BOQ、frameinfo_json）"这条业务约束。
    /// </summary>
    public sealed class MachineLedgerReporterTests
    {
        private const string Account = "devuser";
        private const string Device = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a";
        private const string Token = "qN3fZk9Wx2LpRt7YvBc4MdE6GhJ1Ks8UoA5TiPnQrZe";
        private const string Source = @"C:\数据\北京永芯项目台账2026年3月25日.xlsx";

        [Fact]
        public void BuildRequestUri_UsesTrustedHostAccountAndDevice()
        {
            Uri uri = MachineLedgerReporter.BuildRequestUri(Account, Device);

            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.Equal("uncad.aosa.me", uri.Host);
            Assert.Equal(443, uri.Port);
            Assert.Equal("/api/client/reports/machines", uri.AbsolutePath);
            Assert.Equal("?account=" + Account + "&device=" + Device, uri.Query);
            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(uri));
            Assert.Equal("uncad.aosa.me", MachineLedgerReporter.ExpectedHost);
        }

        [Fact]
        public void BuildRequestUri_RejectsMissingDevice()
            => Assert.Throws<ArgumentException>(
                () => MachineLedgerReporter.BuildRequestUri(Account, ""));

        [Fact]
        public void BuildPayload_MapsEveryUnifiedWorkbookField()
        {
            // 映射依据 ExcelMachineReader 的严格表头绑定：
            // Region=U_区域、Fr=U_上游编号、PanelFloor=U_上游楼层、Detail=U_配电信息、
            // Next=U_上游类型、DownstreamAxis=U_设备轴位、Seq=U_序号、Dia=U_软管直径。
            var rows = new List<MachineRow>
            {
                new MachineRow
                {
                    MachineId = " MDIDA03 ",
                    CircuitName = "MDIDA03-1",
                    Region = "A区",
                    DeviceFloor = "3F",
                    DownstreamAxis = "X12/Y8",
                    Fr = "MPOVL01",
                    PanelFloor = "1F",
                    UpstreamAxis = "X3/Y2",
                    Detail = "配电箱AP-3",
                    Cable = "YJV-4x25+1x16",
                    Next = "盘柜",
                    FacilitySwitch = "有",
                    Batch = "第一批",
                    Seq = "12",
                    Dia = "32"
                }
            };

            MachineLedgerPayload payload = MachineLedgerReporter.BuildPayload(Source, rows);

            Assert.Equal("北京永芯项目台账2026年3月25日.xlsx", payload.source);
            Assert.Equal("第一批", payload.batch);
            MachineLedgerRow row = Assert.Single(payload.rows);
            Assert.Equal("MDIDA03", row.machineId);
            Assert.Equal("MDIDA03-1", row.circuitName);
            Assert.Equal("A区", row.area);
            Assert.Equal("3F", row.deviceFloor);
            Assert.Equal("X12/Y8", row.deviceAxis);
            Assert.Equal("MPOVL01", row.upstreamCode);
            Assert.Equal("1F", row.upstreamFloor);
            Assert.Equal("X3/Y2", row.upstreamAxis);
            Assert.Equal("配电箱AP-3", row.powerInfo);
            Assert.Equal("YJV-4x25+1x16", row.cableModel);
            Assert.Equal("盘柜", row.upstreamType);
            Assert.Equal("有", row.facilitySwitch);
            Assert.Equal("第一批", row.batch);
            Assert.Equal("12", row.sequence);
            Assert.Equal("32", row.flexibleConduitDiameter);
        }

        [Fact]
        public void SerializedPayload_UsesProtocolFieldNamesAndNoCadProducts()
        {
            var rows = new List<MachineRow>
            {
                new MachineRow { MachineId = "MDIDA03", CircuitName = "MDIDA03-1", Cable = "YJV" }
            };

            string json = MachineLedgerReporter.Serialize(
                MachineLedgerReporter.BuildPayload(Source, rows));

            foreach (string field in new[]
            {
                "machineId", "circuitName", "area", "deviceFloor", "deviceAxis",
                "upstreamCode", "upstreamFloor", "upstreamAxis", "powerInfo",
                "cableModel", "upstreamType", "facilitySwitch", "batch", "sequence",
                "flexibleConduitDiameter", "source", "rows"
            })
                Assert.Contains("\"" + field + "\"", json);

            // 电缆长度 / BOQ / frameinfo_json 属于 CAD 产物，协议明确不收。
            foreach (string forbidden in new[]
            {
                "frameInfoJson", "cableLength", "cableMeters", "CableMeters",
                "cableLengthMm", "boq", "frameinfo"
            })
                Assert.DoesNotContain(forbidden, json);
        }

        [Fact]
        public void BuildPayload_DropsRowsWithoutMachineId()
        {
            var rows = new List<MachineRow>
            {
                new MachineRow { MachineId = "", CircuitName = "空机台" },
                new MachineRow { MachineId = "   ", CircuitName = "空白机台" },
                new MachineRow { MachineId = "MDIDA03", CircuitName = "有效" }
            };

            MachineLedgerPayload payload = MachineLedgerReporter.BuildPayload(Source, rows);

            MachineLedgerRow row = Assert.Single(payload.rows);
            Assert.Equal("MDIDA03", row.machineId);
        }

        [Theory]
        [InlineData(@"C:\数据\台账.xlsx", "台账.xlsx")]
        [InlineData("/srv/data/ledger.xlsx", "ledger.xlsx")]
        [InlineData("https://example.test/a/b/ledger.xlsx?token=secret", "ledger.xlsx")]
        [InlineData("https://example.test/a/ledger.xlsx#sheet1", "ledger.xlsx")]
        [InlineData("ledger.xlsx", "ledger.xlsx")]
        [InlineData("  ", "")]
        public void ResolveSourceName_KeepsOnlyWorkbookFileName(string source, string expected)
            => Assert.Equal(expected, MachineLedgerReporter.ResolveSourceName(source));

        [Fact]
        public void ResolveBatch_OnlyAcceptsOneUnanimousBatch()
        {
            Assert.Equal("第一批", MachineLedgerReporter.ResolveBatch(new List<MachineRow>
            {
                new MachineRow { Batch = "第一批" },
                new MachineRow { Batch = " 第一批 " }
            }));
            Assert.Equal("", MachineLedgerReporter.ResolveBatch(new List<MachineRow>
            {
                new MachineRow { Batch = "第一批" },
                new MachineRow { Batch = "第二批" }
            }));
            Assert.Equal("", MachineLedgerReporter.ResolveBatch(new List<MachineRow>
            {
                new MachineRow { Batch = "" }
            }));
            Assert.Equal("", MachineLedgerReporter.ResolveBatch(null));
        }

        [Fact]
        public void Report_SkipsSilentlyWhenNotSignedIn()
        {
            foreach (MachineLedgerCredentials credentials in new[]
            {
                new MachineLedgerCredentials { Token = Token },
                new MachineLedgerCredentials { Account = Account },
                new MachineLedgerCredentials()
            })
            {
                bool posted = false;
                MachineLedgerReporter reporter = Reporter(credentials,
                    _ => SampleRows(), (_, __) => { posted = true; return Ok(); });

                MachineLedgerReportOutcome outcome = reporter.Report(Source);

                Assert.Equal(MachineLedgerReportStatus.SkippedNotSignedIn, outcome.Status);
                Assert.Equal("", outcome.Message);
                Assert.False(posted);
            }
        }

        [Fact]
        public void Report_SkipsEmptySnapshotToAvoidWipingServerLedger()
        {
            bool posted = false;
            MachineLedgerReporter reporter = Reporter(Credentials(),
                _ => new List<MachineRow>(), (_, __) => { posted = true; return Ok(); });

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.SkippedEmptySnapshot, outcome.Status);
            Assert.Contains("空", outcome.Message);
            Assert.False(outcome.RequiresRelogin);
            Assert.False(posted);
        }

        [Fact]
        public void Report_RejectsOversizedSnapshotWithoutCallingServer()
        {
            var rows = new List<MachineRow>();
            for (int index = 0; index <= MachineLedgerReporter.MaximumRows; index++)
                rows.Add(new MachineRow { MachineId = "M" + index });
            bool posted = false;
            MachineLedgerReporter reporter = Reporter(Credentials(), _ => rows,
                (_, __) => { posted = true; return Ok(); });

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Rejected, outcome.Status);
            Assert.Contains("上限", outcome.Message);
            Assert.False(posted);
        }

        [Fact]
        public void Report_PostsFullSnapshotAndReportsServerCounts()
        {
            string body = null;
            MachineLedgerReporter reporter = Reporter(Credentials(), _ => SampleRows(),
                (_, json) =>
                {
                    body = json;
                    return new MachineLedgerHttpResult(200,
                        "{\"ok\":true,\"reportId\":12,\"reported\":3,\"inserted\":1,"
                        + "\"updated\":1,\"removed\":2}");
                });

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Reported, outcome.Status);
            Assert.Equal(3, outcome.RowCount);
            Assert.Equal(1, outcome.Inserted);
            Assert.Equal(1, outcome.Updated);
            Assert.Equal(2, outcome.Removed);
            Assert.Equal(12, outcome.ReportId);
            Assert.Contains("3 行", outcome.Message);
            Assert.Contains("新增 1", outcome.Message);
            Assert.False(outcome.RequiresRelogin);
            Assert.NotNull(body);
            Assert.Contains("\"machineId\":\"MDIDA03\"", body);
            Assert.Contains("\"circuitName\":\"MDIDA03-1\"", body);
        }

        [Fact]
        public void Report_Conflict409RequiresRelogin()
        {
            MachineLedgerReporter reporter = Reporter(Credentials(), _ => SampleRows(),
                (_, __) => new MachineLedgerHttpResult(409,
                    "{\"error\":\"本机授权已停止（账号已在其他设备登录），请重新登录后再上报。\"}"));

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Rejected, outcome.Status);
            Assert.True(outcome.RequiresRelogin);
            Assert.Contains("UNC_LOGIN", outcome.Message);
            Assert.Contains("重新登录", outcome.Message);
        }

        [Theory]
        [InlineData(401, true)]
        [InlineData(403, true)]
        [InlineData(404, false)]
        [InlineData(400, false)]
        public void Report_ServerRejectionsSurfaceChineseReason(int statusCode, bool relogin)
        {
            MachineLedgerReporter reporter = Reporter(Credentials(), _ => SampleRows(),
                (_, __) => new MachineLedgerHttpResult(statusCode,
                    "{\"error\":\"服务端中文原因\"}"));

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Rejected, outcome.Status);
            Assert.Contains("服务端中文原因", outcome.Message);
            Assert.Equal(relogin, outcome.RequiresRelogin);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(302)]
        [InlineData(0)]
        public void Report_TransportAndServerFailuresAreRetryable(int statusCode)
        {
            MachineLedgerReporter reporter = Reporter(Credentials(), _ => SampleRows(),
                (_, __) => new MachineLedgerHttpResult(statusCode, ""));

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Failed, outcome.Status);
            Assert.Contains("HTTP " + statusCode, outcome.Message);
            Assert.False(outcome.RequiresRelogin);
        }

        [Fact]
        public void Report_NetworkExceptionNeverEscapes()
        {
            MachineLedgerReporter reporter = Reporter(Credentials(), _ => SampleRows(),
                (_, __) => throw new System.Net.WebException("无法解析远程名称"));

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Failed, outcome.Status);
            Assert.Contains("网络", outcome.Message);
        }

        [Fact]
        public void Report_SnapshotReadFailureNeverEscapes()
        {
            MachineLedgerReporter reporter = Reporter(Credentials(),
                _ => throw new InvalidOperationException("sqlite 损坏"),
                (_, __) => Ok());

            MachineLedgerReportOutcome outcome = reporter.Report(Source);

            Assert.Equal(MachineLedgerReportStatus.Failed, outcome.Status);
            Assert.Contains("快照", outcome.Message);
        }

        [Fact]
        public void Coordinator_ReportsLedgerOnlyAfterSuccessfulRefresh()
        {
            var queued = new List<Action>();
            var reported = new List<string>();
            var reporter = new MachineLedgerReporter(
                source => { reported.Add(source); return SampleRows(); },
                (_, __) => Ok(),
                Credentials);
            var coordinator = new MachineDataRefreshCoordinator(_ =>
                new MachineWorkbookSourceResult
                {
                    Updated = true,
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 3 }
                }, queued.Add, reporter);

            Assert.Equal(MachineDataRefreshStartStatus.Started,
                coordinator.TryStartManualRefresh(Source));
            Assert.Single(queued)();

            Assert.Equal(new[] { Source }, reported);
            var notices = new List<string>();
            while (coordinator.TryTakeNotice(out string message)) notices.Add(message);
            Assert.Contains(notices, item => item.Contains("共 3 行"));
            Assert.Contains(notices, item => item.Contains("台账已上报"));
        }

        [Fact]
        public void Coordinator_KeepsRefreshResultWhenReporterFails()
        {
            var queued = new List<Action>();
            var reporter = new MachineLedgerReporter(
                _ => throw new InvalidOperationException("reporter 炸了"),
                (_, __) => Ok(), Credentials);
            var coordinator = new MachineDataRefreshCoordinator(_ =>
                new MachineWorkbookSourceResult
                {
                    Updated = true,
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 5 }
                }, queued.Add, reporter);

            coordinator.TryStartManualRefresh(Source);
            Assert.Single(queued)();   // 不得抛出：上报异常只影响台账，不影响刷新

            // 先出刷新结论，再出上报失败提示；两者互不影响。
            Assert.True(coordinator.TryTakeNotice(out string refreshNotice));
            Assert.Contains("共 5 行", refreshNotice);
            Assert.True(coordinator.TryTakeNotice(out string reportNotice));
            Assert.Contains("快照", reportNotice);
            Assert.False(coordinator.TryTakeNotice(out _));
        }

        [Fact]
        public void Coordinator_DoesNotReportWhenRefreshFellBackToCache()
        {
            var queued = new List<Action>();
            bool reported = false;
            var reporter = new MachineLedgerReporter(
                _ => { reported = true; return SampleRows(); },
                (_, __) => Ok(), Credentials);
            var coordinator = new MachineDataRefreshCoordinator(_ =>
                new MachineWorkbookSourceResult
                {
                    UsedCachedFallback = true,
                    Warning = "download failed",
                    Snapshot = new MachineWorkbookSnapshotInfo { RowCount = 3 }
                }, queued.Add, reporter);

            coordinator.TryStartManualRefresh(Source);
            Assert.Single(queued)();

            Assert.False(reported);
        }

        private static MachineLedgerReporter Reporter(MachineLedgerCredentials credentials,
            Func<string, List<MachineRow>> readRows,
            Func<MachineLedgerCredentials, string, MachineLedgerHttpResult> post)
            => new MachineLedgerReporter(readRows, post, () => credentials);

        private static MachineLedgerCredentials Credentials()
            => new MachineLedgerCredentials
            {
                Account = Account,
                DeviceId = Device,
                Token = Token
            };

        private static MachineLedgerHttpResult Ok()
            => new MachineLedgerHttpResult(200, "{\"ok\":true,\"reported\":3}");

        private static List<MachineRow> SampleRows() => new List<MachineRow>
        {
            new MachineRow
            {
                MachineId = "MDIDA03",
                CircuitName = "MDIDA03-1",
                Region = "A区",
                DeviceFloor = "3F",
                DownstreamAxis = "X12/Y8",
                Fr = "MPOVL01",
                PanelFloor = "1F",
                UpstreamAxis = "X3/Y2",
                Detail = "配电箱AP-3",
                Cable = "YJV-4x25+1x16",
                Next = "盘柜",
                FacilitySwitch = "有",
                Batch = "第一批",
                Seq = "1",
                Dia = "32"
            },
            new MachineRow { MachineId = "MDIDA03", CircuitName = "MDIDA03-2", Batch = "第一批" },
            new MachineRow { MachineId = "MDIDA04", CircuitName = "MDIDA04-1", Batch = "第一批" }
        };
    }
}

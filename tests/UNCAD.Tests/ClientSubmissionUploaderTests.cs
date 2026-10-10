using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UNCAD.Core.Fill;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1F/U1U/U1DWG 数据上传契约（服务端 /api/client/submissions/*）：
    /// 最小单位是**回路**（= 一个图框），只上报清单编号/名称/数量/单位，
    /// 不上报 frameinfo_json / BOQ 全量 / 电缆长度等图纸大对象；
    /// 未登录静默跳过，409/401/403 要求重新登录，任何失败都不得抛异常。
    /// </summary>
    public sealed class ClientSubmissionUploaderTests
    {
        private const string Account = "devuser";
        private const string Device = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a";
        private const string Token = "qN3fZk9Wx2LpRt7YvBc4MdE6GhJ1Ks8UoA5TiPnQrZe";
        private const string Machine = "MDIDA03";
        private const string Circuit = "MDIDA03-1";

        // ---- 地址与查询参数 ----

        [Theory]
        [InlineData("/api/client/submissions/circuit")]
        [InlineData("/api/client/submissions/dwg-export")]
        [InlineData("/api/client/submissions/file")]
        public void BuildRequestUri_UsesTrustedHostAndHttps443(string path)
        {
            Uri uri = ClientSubmissionUploader.BuildRequestUri(path,
                ClientSubmissionUploader.BuildQuery(Credentials()));

            Assert.Equal("https", uri.Scheme);
            Assert.Equal("uncad.aosa.me", uri.Host);
            Assert.Equal(443, uri.Port);
            Assert.Equal(path, uri.AbsolutePath);
            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(uri));
            Assert.Equal("uncad.aosa.me", ClientSubmissionUploader.ExpectedHost);
        }

        [Fact]
        public void BuildUploadQuery_CarriesMachineFileKindAndCircuits()
        {
            string query = ClientSubmissionUploader.BuildUploadQuery(Credentials(), Machine,
                "MDIDA03.dwg", new List<string> { " MDIDA03-1 ", "MDIDA03-2", "MDIDA03-1" });

            Assert.Contains("account=" + Account, query);
            Assert.Contains("device=" + Device, query);
            Assert.Contains("machineId=" + Machine, query);
            Assert.Contains("fileName=MDIDA03.dwg", query);
            Assert.Contains("kind=dwg", query);
            Assert.Contains("circuits=MDIDA03-1,MDIDA03-2", query);
        }

        [Fact]
        public void NormalizeCircuits_TrimsDropsEmptyAndDeduplicates()
            => Assert.Equal(new[] { "A", "B" },
                ClientSubmissionUploader.NormalizeCircuits(
                    new List<string> { " A ", "", "A", null, "B" }));

        // ---- 清单映射：只上报必要字段 ----

        [Fact]
        public void BuildInventory_MapsCodeNameQuantityUnitOnly()
        {
            List<CircuitInventoryRow> inventory = ClientSubmissionUploader.BuildInventory(
                new List<TableFillRow>
                {
                    new TableFillRow
                    {
                        Code = " 2.1 ",
                        Name = "电缆 YJV-4x25",
                        Quantity = "12",
                        Unit = "m",
                        Description = "不应上传的大描述",
                        Category = TableFillCategory.Cable,
                        SortOrder = 7
                    }
                });

            CircuitInventoryRow row = Assert.Single(inventory);
            Assert.Equal("2.1", row.code);
            Assert.Equal("电缆 YJV-4x25", row.name);
            Assert.Equal("12", row.quantity);
            Assert.Equal("m", row.unit);
        }

        [Fact]
        public void CircuitPayload_UsesProtocolFieldNamesAndNoCadProducts()
        {
            var payload = new CircuitSubmissionPayload
            {
                machineId = Machine,
                circuitName = Circuit,
                command = "U1U",
                frameHandle = "1A2F",
                inventory = ClientSubmissionUploader.BuildInventory(new List<TableFillRow>
                {
                    new TableFillRow { Code = "1", Name = "桥架 200", Quantity = "4", Unit = "m" }
                })
            };

            string json = ClientSubmissionUploader.Serialize(payload);

            foreach (string field in new[]
                { "machineId", "circuitName", "command", "frameHandle", "inventory",
                  "code", "name", "quantity", "unit" })
                Assert.Contains("\"" + field + "\"", json);

            foreach (string forbidden in new[]
                { "frameInfoJson", "frameinfo", "boq", "cableMeters", "CableMeters",
                  "description", "sortOrder", "category" })
                Assert.DoesNotContain(forbidden, json);
        }

        // ---- 回路清单提交 ----

        [Fact]
        public void SubmitCircuit_SkipsSilentlyWhenNotSignedIn()
        {
            bool sent = false;
            ClientSubmissionUploader uploader = Uploader(new ClientSubmissionCredentials(),
                _ => { sent = true; return Ok(); });

            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "U1U",
                "1A2F", Rows());

            Assert.Equal(ClientSubmissionStatus.SkippedNotSignedIn, outcome.Status);
            Assert.Equal("", outcome.Message);
            Assert.False(sent);
        }

        [Fact]
        public void SubmitCircuit_RejectsMissingMachineOrCircuitWithoutCallingServer()
        {
            bool sent = false;
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => { sent = true; return Ok(); });

            Assert.Equal(ClientSubmissionStatus.Rejected,
                uploader.SubmitCircuit("", Circuit, "U1F", "", Rows()).Status);
            Assert.Equal(ClientSubmissionStatus.Rejected,
                uploader.SubmitCircuit(Machine, "  ", "U1F", "", Rows()).Status);
            Assert.False(sent);
        }

        [Fact]
        public void SubmitCircuit_PostsPerCircuitPayloadWithToken()
        {
            ClientSubmissionRequest captured = null;
            ClientSubmissionUploader uploader = Uploader(Credentials(), request =>
            {
                captured = request;
                return new ClientSubmissionHttpResult(200, "{\"ok\":true,\"circuitId\":7}");
            });

            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "u1f",
                "1A2F", Rows());

            Assert.Equal(ClientSubmissionStatus.Accepted, outcome.Status);
            Assert.Equal("", outcome.Message);   // 成功只在日志留痕，避免刷屏
            Assert.Equal("/api/client/submissions/circuit", captured.Path);
            Assert.Equal(Token, captured.Token);
            Assert.Contains("account=" + Account, captured.Query);
            Assert.Null(captured.FilePath);
            Assert.Contains("\"command\":\"U1F\"", captured.JsonBody);
            Assert.Contains("\"machineId\":\"" + Machine + "\"", captured.JsonBody);
            Assert.Contains("\"circuitName\":\"" + Circuit + "\"", captured.JsonBody);
            Assert.Contains("\"frameHandle\":\"1A2F\"", captured.JsonBody);
        }

        [Fact]
        public void SubmitCircuit_Conflict409RequiresRelogin()
        {
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => new ClientSubmissionHttpResult(409,
                    "{\"error\":\"本机授权已停止（账号已在其他设备登录），请重新登录后再上报。\"}"));

            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "U1U",
                "", Rows());

            Assert.Equal(ClientSubmissionStatus.Rejected, outcome.Status);
            Assert.True(outcome.RequiresRelogin);
            Assert.Contains("UNC_LOGIN", outcome.Message);
            Assert.Contains("重新登录", outcome.Message);
        }

        [Theory]
        [InlineData(401, true)]
        [InlineData(403, true)]
        [InlineData(404, false)]
        [InlineData(400, false)]
        [InlineData(413, false)]
        public void SubmitCircuit_ServerRejectionsSurfaceChineseReason(int statusCode,
            bool relogin)
        {
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => new ClientSubmissionHttpResult(statusCode, "{\"error\":\"服务端中文原因\"}"));

            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "U1U",
                "", Rows());

            Assert.Equal(ClientSubmissionStatus.Rejected, outcome.Status);
            Assert.Equal(statusCode, outcome.StatusCode);
            Assert.Contains("服务端中文原因", outcome.Message);
            Assert.Equal(relogin, outcome.RequiresRelogin);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(302)]
        [InlineData(0)]
        public void SubmitCircuit_TransportFailuresAreRetryable(int statusCode)
        {
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => new ClientSubmissionHttpResult(statusCode, ""));

            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "U1U",
                "", Rows());

            Assert.Equal(ClientSubmissionStatus.Failed, outcome.Status);
            Assert.False(outcome.RequiresRelogin);
        }

        [Fact]
        public void SubmitCircuit_NetworkExceptionNeverEscapes()
        {
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => throw new System.Net.WebException("无法解析远程名称"));

            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "U1F",
                "", Rows());

            Assert.Equal(ClientSubmissionStatus.Failed, outcome.Status);
            Assert.Contains("网络错误", outcome.Message);
        }

        // ---- U1DWG 导出登记 ----

        [Fact]
        public void ReportDwgExport_PostsMachineAndCircuitList()
        {
            ClientSubmissionRequest captured = null;
            ClientSubmissionUploader uploader = Uploader(Credentials(), request =>
            {
                captured = request;
                return Ok();
            });

            ClientSubmissionOutcome outcome = uploader.ReportDwgExport(Machine,
                new List<string> { Circuit, "MDIDA03-2", Circuit });

            Assert.Equal(ClientSubmissionStatus.Accepted, outcome.Status);
            Assert.Equal("/api/client/submissions/dwg-export", captured.Path);
            Assert.Contains("\"machineId\":\"" + Machine + "\"", captured.JsonBody);
            Assert.Contains("\"circuits\":[\"" + Circuit + "\",\"MDIDA03-2\"]",
                captured.JsonBody);
        }

        [Fact]
        public void ReportDwgExport_RejectsEmptyOrOversizedCircuitListLocally()
        {
            bool sent = false;
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => { sent = true; return Ok(); });

            Assert.Equal(ClientSubmissionStatus.Rejected,
                uploader.ReportDwgExport(Machine, new List<string>()).Status);
            Assert.Equal(ClientSubmissionStatus.Rejected,
                uploader.ReportDwgExport(Machine, new List<string> { "  " }).Status);

            var tooMany = new List<string>();
            for (int index = 0; index <= ClientSubmissionUploader.MaximumCircuits; index++)
                tooMany.Add("C" + index);
            ClientSubmissionOutcome outcome = uploader.ReportDwgExport(Machine, tooMany);
            Assert.Equal(ClientSubmissionStatus.Rejected, outcome.Status);
            Assert.Contains("上限", outcome.Message);
            Assert.False(sent);
        }

        // ---- DWG 文件本体上传 ----

        [Fact]
        public void UploadDwgFile_SendsRawStreamWithQueryParameters()
        {
            string path = WriteTempFile(new byte[] { 0x41, 0x43, 0x31, 0x30 });
            try
            {
                ClientSubmissionRequest captured = null;
                ClientSubmissionUploader uploader = Uploader(Credentials(), request =>
                {
                    captured = request;
                    return Ok();
                });

                ClientSubmissionOutcome outcome = uploader.UploadDwgFile(Machine, path,
                    new List<string> { Circuit });

                Assert.Equal(ClientSubmissionStatus.Accepted, outcome.Status);
                Assert.Equal("/api/client/submissions/file", captured.Path);
                Assert.Equal(path, captured.FilePath);      // 原始字节流，不是 JSON
                Assert.Null(captured.JsonBody);
                Assert.Contains("kind=dwg", captured.Query);
                Assert.Contains("fileName=" + Path.GetFileName(path), captured.Query);
                Assert.Contains("circuits=" + Circuit, captured.Query);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void UploadDwgFile_RejectsMissingEmptyAndOversizedFilesLocally()
        {
            bool sent = false;
            ClientSubmissionUploader uploader = Uploader(Credentials(),
                _ => { sent = true; return Ok(); });

            ClientSubmissionOutcome missing = uploader.UploadDwgFile(Machine,
                Path.Combine(Path.GetTempPath(), "uncad-missing-" + Guid.NewGuid() + ".dwg"),
                new List<string> { Circuit });
            Assert.Equal(ClientSubmissionStatus.Failed, missing.Status);

            string empty = WriteTempFile(new byte[0]);
            try
            {
                ClientSubmissionOutcome blank = uploader.UploadDwgFile(Machine, empty,
                    new List<string> { Circuit });
                Assert.Equal(ClientSubmissionStatus.Rejected, blank.Status);
                Assert.Contains("空", blank.Message);
            }
            finally { File.Delete(empty); }

            Assert.False(ClientSubmissionUploader.IsOverFileLimit(
                ClientSubmissionUploader.MaximumFileBytes));
            Assert.True(ClientSubmissionUploader.IsOverFileLimit(
                ClientSubmissionUploader.MaximumFileBytes + 1));
            Assert.False(sent);
        }

        [Fact]
        public void UploadDwgFile_Surfaces413()
        {
            string path = WriteTempFile(new byte[] { 1, 2, 3 });
            try
            {
                ClientSubmissionUploader uploader = Uploader(Credentials(),
                    _ => new ClientSubmissionHttpResult(413,
                        "{\"error\":\"文件超过上限（200 MB）。\"}"));

                ClientSubmissionOutcome outcome = uploader.UploadDwgFile(Machine, path,
                    new List<string> { Circuit });

                Assert.Equal(ClientSubmissionStatus.Rejected, outcome.Status);
                Assert.Contains("200 MB", outcome.Message);
                Assert.False(outcome.RequiresRelogin);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void DisabledUploader_NeverTouchesTheNetwork()
        {
            // 诊断开关：Default 之外的显式禁用实现必须不发任何请求。
            Assert.Equal(ClientSubmissionStatus.SkippedNotSignedIn,
                ClientSubmissionUploader.None.SubmitCircuit(Machine, Circuit, "U1U", "",
                    Rows()).Status);
            Assert.Equal(ClientSubmissionStatus.SkippedNotSignedIn,
                ClientSubmissionUploader.None.ReportDwgExport(Machine,
                    new List<string> { Circuit }).Status);
        }

        // ---- 命令挂钩：位置与顺序（源码契约）----

        [Fact]
        public void FillFeature_UploadsOnlyAfterTheWriteTransactionCommits()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));

            int commit = source.IndexOf("transaction.Commit();", StringComparison.Ordinal);
            int batchComplete = source.IndexOf("outputBatch.Complete();",
                StringComparison.Ordinal);
            int lastRollback = source.LastIndexOf("outputBatch.Rollback();",
                StringComparison.Ordinal);
            int upload = source.IndexOf("UploadCircuitSubmission(picked, tableRows",
                StringComparison.Ordinal);

            Assert.True(commit >= 0 && batchComplete > commit);
            Assert.True(upload > batchComplete,
                "U1F/U1U 的数据上传必须在提交事务与 BOQ 输出完成之后才排队");
            Assert.True(upload > lastRollback,
                "上传只能排在成功路径上：位于所有回滚分支之后");
            Assert.Contains("ThreadPool.QueueUserWorkItem", source);
            Assert.Contains("ClientSubmissionUploader.Default.SubmitCircuit(", source);
        }

        [Fact]
        public void BatchUpdate_UploadsOneCircuitPerFrameAfterCommit()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));

            int commit = source.IndexOf("transaction.Commit();", StringComparison.Ordinal);
            int lastRollback = source.LastIndexOf("outputBatch.Rollback();",
                StringComparison.Ordinal);
            int upload = source.IndexOf("FillFeature.UploadCircuitSubmission(", StringComparison.Ordinal);

            Assert.True(commit >= 0 && upload > commit);
            Assert.True(upload > lastRollback,
                "批量 U1U 的上传只能排在成功路径上（所有回滚分支之后）");
            Assert.Contains("foreach (Plan plan in plans)", source);
        }

        [Fact]
        public void DwgExportFeature_RegistersCircuitsBeforeUploadingTheFile()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Features", "DwgExport",
                "DwgExportFeature.cs"));

            int commit = source.IndexOf("transaction.Commit();", StringComparison.Ordinal);
            int report = source.IndexOf("ReportDwgExport(", StringComparison.Ordinal);
            int file = source.IndexOf("UploadDwgFile(", StringComparison.Ordinal);

            Assert.True(commit >= 0 && report > commit);
            Assert.True(file > report, "必须先登记覆盖回路，再上传 DWG 本体");
            Assert.Contains("ThreadPool.QueueUserWorkItem", source);
            Assert.Contains("ClientSubmissionUploader.Default", source);
        }

        [Fact]
        public void DwgExportResult_ExposesPerMachineFileAndCircuits()
        {
            string service = File.ReadAllText(Repo("src", "UNCAD", "Features", "DwgExport",
                "DwgFrameExportService.cs"));

            Assert.Contains("class DwgExportMachine", service);
            Assert.Contains("Machines = exported", service);
            Assert.Contains("Circuits = machine.Select(frame => (frame.DeviceName ?? \"\").Trim())",
                service);
        }

        // ---- 辅助 ----

        private static ClientSubmissionUploader Uploader(
            ClientSubmissionCredentials credentials,
            Func<ClientSubmissionRequest, ClientSubmissionHttpResult> send)
            => new ClientSubmissionUploader(() => credentials, send);

        private static ClientSubmissionCredentials Credentials()
            => new ClientSubmissionCredentials
            {
                Account = Account,
                DeviceId = Device,
                Token = Token
            };

        private static ClientSubmissionHttpResult Ok()
            => new ClientSubmissionHttpResult(200, "{\"ok\":true}");

        private static List<TableFillRow> Rows() => new List<TableFillRow>
        {
            new TableFillRow { Code = "1", Name = "电缆", Quantity = "3", Unit = "m" }
        };

        private static string WriteTempFile(byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad-upload-" + Guid.NewGuid().ToString("N") + ".dwg");
            File.WriteAllBytes(path, content);
            return path;
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}

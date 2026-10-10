using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Web.Script.Serialization;
using UNCAD.Core.Fill;

namespace UNCAD.Infra
{
    /// <summary>提交结果分类（决定日志级别与命令行提示）。</summary>
    internal enum ClientSubmissionStatus
    {
        /// <summary>未登录：静默跳过，不报错、不刷屏。</summary>
        SkippedNotSignedIn,
        /// <summary>服务端接受（HTTP 200 + ok）。</summary>
        Accepted,
        /// <summary>服务端明确拒绝（400/401/403/404/409/413 或客户端前置校验）。</summary>
        Rejected,
        /// <summary>网络/IO/HTTP 层面的失败（可重新执行命令重试）。</summary>
        Failed
    }

    internal sealed class ClientSubmissionOutcome
    {
        public ClientSubmissionStatus Status { get; set; }
        /// <summary>可直接展示的中文提示；空串表示不需要提示用户。</summary>
        public string Message { get; set; } = "";
        /// <summary>401/403/409：本机会话已失效/被顶掉，必须重新登录。</summary>
        public bool RequiresRelogin { get; set; }
        public int StatusCode { get; set; }
    }

    internal sealed class ClientSubmissionCredentials
    {
        public string Account { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Token { get; set; } = "";
    }

    /// <summary>一次提交请求（JSON 端点用 JsonBody，文件端点用 FilePath 原始字节流）。</summary>
    internal sealed class ClientSubmissionRequest
    {
        public string Path { get; set; }
        public string Query { get; set; }
        public string Token { get; set; }
        public string JsonBody { get; set; }
        public string FilePath { get; set; }
    }

    internal sealed class ClientSubmissionHttpResult
    {
        public ClientSubmissionHttpResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? "";
        }

        public int StatusCode { get; }
        public string Body { get; }
    }

    /// <summary>清单行：只上报编号/名称/数量/单位，不带任何图纸大对象。</summary>
    internal sealed class CircuitInventoryRow
    {
        public string code { get; set; }
        public string name { get; set; }
        public string quantity { get; set; }
        public string unit { get; set; }
    }

    internal sealed class CircuitSubmissionPayload
    {
        public string machineId { get; set; }
        public string circuitName { get; set; }
        public string command { get; set; }
        public string frameHandle { get; set; }
        public List<CircuitInventoryRow> inventory { get; set; }
    }

    internal sealed class DwgExportPayload
    {
        public string machineId { get; set; }
        public List<string> circuits { get; set; }
    }

    internal sealed class ClientSubmissionResponse
    {
        public bool ok { get; set; }
        public string error { get; set; }
    }

    /// <summary>
    /// U1F/U1U/U1DWG 的数据上传（服务端 <c>/api/client/submissions/*</c>）。
    ///
    /// 业务语义：**最小单位 = 回路 = 客户端的一个图框**（一个机台有多个回路/图框）。
    /// - U1F/U1U 成功后提交该回路的清单内容；
    /// - U1DWG 成功后先登记"该机台覆盖了哪些回路"，再上传该机台的 DWG 文件本体。
    ///
    /// 约束：
    /// - 只上报命令产物里必要的字段，**不发送 frameinfo_json、BOQ 全量、统计原始数据**等大对象；
    /// - 方法**不抛异常**（结果统一走 <see cref="ClientSubmissionOutcome"/>），后台线程调用，
    ///   上传失败绝不能回滚图纸或影响用户可用性；
    /// - 未登录（无账号/无令牌）直接静默跳过；409/401/403 明确要求重新登录。
    /// </summary>
    internal sealed class ClientSubmissionUploader
    {
        internal const string CircuitPath = "/api/client/submissions/circuit";
        internal const string DwgExportPath = "/api/client/submissions/dwg-export";
        internal const string UploadPath = "/api/client/submissions/file";
        /// <summary>上报主机；由 OnlineLicenseMonitor.IsExpectedEndpoint 最终校验。</summary>
        internal const string ExpectedHost = "uncad.aosa.me";
        /// <summary>与服务端 SubmissionService.MaximumFileBytes 一致。</summary>
        internal const long MaximumFileBytes = 200L * 1024L * 1024L;
        /// <summary>与服务端单机台回路数上限一致。</summary>
        internal const int MaximumCircuits = 500;
        private const int TimeoutMilliseconds = 20000;
        private const int UploadReadWriteTimeoutMilliseconds = 600000;

        private readonly Func<ClientSubmissionCredentials> _credentials;
        private readonly Func<ClientSubmissionRequest, ClientSubmissionHttpResult> _send;

        internal static ClientSubmissionUploader Default { get; } =
            new ClientSubmissionUploader(ReadCredentials, Send);

        /// <summary>禁用上传（测试与诊断用；不产生任何网络调用）。</summary>
        internal static ClientSubmissionUploader None { get; } =
            new ClientSubmissionUploader(() => new ClientSubmissionCredentials(),
                _ => new ClientSubmissionHttpResult(0, ""));

        internal ClientSubmissionUploader(Func<ClientSubmissionCredentials> credentials,
            Func<ClientSubmissionRequest, ClientSubmissionHttpResult> send)
        {
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>U1F/U1U 成功后提交**该回路（=该图框）**的清单内容。</summary>
        internal ClientSubmissionOutcome SubmitCircuit(string machineId, string circuitName,
            string command, string frameHandle, IReadOnlyList<TableFillRow> rows)
        {
            string machine = (machineId ?? "").Trim();
            string circuit = (circuitName ?? "").Trim();
            if (machine.Length == 0)
                return Reject("缺少机台 ID，回路清单未上传。", false, 0);
            if (circuit.Length == 0)
                return Reject("缺少回路名称，回路清单未上传。", false, 0);
            if (!TryCredentials(out ClientSubmissionCredentials credentials, out _))
                return Skipped();

            var payload = new CircuitSubmissionPayload
            {
                machineId = machine,
                circuitName = circuit,
                command = NormalizeCommand(command),
                frameHandle = (frameHandle ?? "").Trim(),
                inventory = BuildInventory(rows)
            };
            ClientSubmissionOutcome outcome = Post(credentials, CircuitPath,
                BuildQuery(credentials), Serialize(payload), null);
            if (outcome.Status == ClientSubmissionStatus.Accepted)
            {
                outcome.Message = "回路清单已上传：" + payload.command + " " + machine + " "
                    + circuit + "（" + payload.inventory.Count + " 项清单）。";
                Log.Info(outcome.Message);
                outcome.Message = "";   // 成功只在日志里留痕，避免多机台刷屏
            }
            else
            {
                outcome.Message = "回路清单上传未完成（" + machine + " " + circuit + "）："
                    + outcome.Message;
            }
            return outcome;
        }

        /// <summary>U1DWG 成功后登记"该机台覆盖了哪些回路"。</summary>
        internal ClientSubmissionOutcome ReportDwgExport(string machineId,
            IReadOnlyList<string> circuits)
        {
            string machine = (machineId ?? "").Trim();
            if (machine.Length == 0)
                return Reject("缺少机台 ID，U1DWG 导出登记未完成。", false, 0);
            List<string> list = NormalizeCircuits(circuits);
            if (list.Count == 0)
                return Reject("缺少回路列表，U1DWG 导出登记未完成。", false, 0);
            if (list.Count > MaximumCircuits)
                return Reject("机台 " + machine + " 的回路数 " + list.Count
                    + " 超过服务端上限 " + MaximumCircuits + "，导出登记未完成。", false, 0);
            if (!TryCredentials(out ClientSubmissionCredentials credentials, out _))
                return Skipped();

            var payload = new DwgExportPayload { machineId = machine, circuits = list };
            ClientSubmissionOutcome outcome = Post(credentials, DwgExportPath,
                BuildQuery(credentials), Serialize(payload), null);
            if (outcome.Status == ClientSubmissionStatus.Accepted)
            {
                Log.Info("U1DWG 导出已登记: " + machine + "（" + list.Count + " 个回路）");
                outcome.Message = "";
            }
            else
            {
                outcome.Message = "U1DWG 导出登记未完成（" + machine + "）：" + outcome.Message;
            }
            return outcome;
        }

        /// <summary>U1DWG 导出成功后上传该机台的 DWG 文件本体（原始字节流）。</summary>
        internal ClientSubmissionOutcome UploadDwgFile(string machineId, string filePath,
            IReadOnlyList<string> circuits)
        {
            string machine = (machineId ?? "").Trim();
            string path = (filePath ?? "").Trim();
            if (machine.Length == 0)
                return Reject("缺少机台 ID，DWG 未上传。", false, 0);
            if (path.Length == 0)
                return Reject("缺少 DWG 文件路径，未上传。", false, 0);
            if (!File.Exists(path))
                return Failure("DWG 文件不存在，未上传：" + path);
            long length;
            try { length = new FileInfo(path).Length; }
            catch (Exception ex) { return Failure("无法读取 DWG 文件信息：" + ex.Message); }
            if (length <= 0)
                return Reject("DWG 文件为空，未上传：" + path, false, 0);
            if (IsOverFileLimit(length))
                return Reject("DWG 文件 " + (length / 1024 / 1024)
                    + " MB 超过服务端上限 200 MB，未上传。", false, 0);
            List<string> list = NormalizeCircuits(circuits);
            if (!TryCredentials(out ClientSubmissionCredentials credentials, out _))
                return Skipped();

            string fileName = Path.GetFileName(path);
            ClientSubmissionOutcome outcome = Post(credentials, UploadPath,
                BuildUploadQuery(credentials, machine, fileName, list), null, path);
            if (outcome.Status == ClientSubmissionStatus.Accepted)
            {
                Log.Info("DWG 已上传: " + machine + " " + fileName
                    + "（" + list.Count + " 个回路，" + (length / 1024) + " KB）");
                outcome.Message = "";
            }
            else
            {
                outcome.Message = "DWG 上传未完成（" + machine + " " + fileName + "）："
                    + outcome.Message;
            }
            return outcome;
        }

        // ---- 纯逻辑：可被 tests/UNCAD.Tests/ClientSubmissionUploaderTests.cs 直接验证 ----

        /// <summary>本机会话凭证：账号名来自注册表，设备与令牌来自 DeviceIdentity（与心跳/台账同一套）。</summary>
        internal static ClientSubmissionCredentials ReadCredentials()
        {
            MachineLedgerCredentials session = MachineLedgerReporter.ReadCredentials();
            return new ClientSubmissionCredentials
            {
                Account = session.Account,
                DeviceId = session.DeviceId,
                Token = session.Token
            };
        }

        internal static Uri BuildRequestUri(string path, string query)
        {
            string relative = (path ?? "").Trim();
            if (relative.Length == 0 || relative[0] != '/')
                throw new ArgumentException("提交路径必须以 / 开头。", nameof(path));
            var builder = new UriBuilder(new Uri("https://" + ExpectedHost + relative,
                UriKind.Absolute));
            if (!string.IsNullOrWhiteSpace(query)) builder.Query = query;
            if (!OnlineLicenseMonitor.IsExpectedEndpoint(builder.Uri))
                throw new InvalidDataException("数据上传地址不受信任。");
            return builder.Uri;
        }

        internal static string BuildQuery(ClientSubmissionCredentials credentials)
            => "account=" + Uri.EscapeDataString((credentials?.Account ?? "").Trim())
                + "&device=" + Uri.EscapeDataString((credentials?.DeviceId ?? "").Trim());

        internal static string BuildUploadQuery(ClientSubmissionCredentials credentials,
            string machineId, string fileName, IReadOnlyList<string> circuits)
        {
            var builder = new StringBuilder(BuildQuery(credentials));
            builder.Append("&machineId=").Append(Uri.EscapeDataString((machineId ?? "").Trim()));
            builder.Append("&fileName=").Append(Uri.EscapeDataString((fileName ?? "").Trim()));
            builder.Append("&kind=dwg");
            List<string> list = NormalizeCircuits(circuits);
            if (list.Count > 0)
            {
                // 服务端按 ',' 拆分并 Trim；回路名里的逗号不被支持（图框设备名不含逗号）。
                builder.Append("&circuits=");
                for (int index = 0; index < list.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    builder.Append(Uri.EscapeDataString(list[index]));
                }
            }
            return builder.ToString();
        }

        /// <summary>只上报编号/名称/数量/单位——不上报任何图纸大对象。</summary>
        internal static List<CircuitInventoryRow> BuildInventory(IReadOnlyList<TableFillRow> rows)
        {
            var inventory = new List<CircuitInventoryRow>();
            foreach (TableFillRow row in rows ?? new List<TableFillRow>())
            {
                if (row == null) continue;
                inventory.Add(new CircuitInventoryRow
                {
                    code = Text(row.Code),
                    name = Text(row.Name),
                    quantity = Text(row.Quantity),
                    unit = Text(row.Unit)
                });
            }
            return inventory;
        }

        internal static List<string> NormalizeCircuits(IReadOnlyList<string> circuits)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string circuit in circuits ?? new List<string>())
            {
                string value = (circuit ?? "").Trim();
                if (value.Length == 0) continue;
                if (seen.Add(value)) list.Add(value);
            }
            return list;
        }

        internal static string NormalizeCommand(string command)
        {
            string value = (command ?? "").Trim().ToUpperInvariant();
            return value.Length == 0 ? "U1U" : value;
        }

        /// <summary>与服务端同一上限：超过 200 MB 直接本地拒绝，不做无谓上传。</summary>
        internal static bool IsOverFileLimit(long bytes) => bytes > MaximumFileBytes;

        internal static string Serialize(object payload)
            => new JavaScriptSerializer().Serialize(payload);

        internal static ClientSubmissionResponse ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return new JavaScriptSerializer()
                    .Deserialize<ClientSubmissionResponse>(json.TrimStart('\uFEFF'));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- 内部实现 ----

        private ClientSubmissionOutcome Post(ClientSubmissionCredentials credentials,
            string path, string query, string jsonBody, string filePath)
        {
            ClientSubmissionHttpResult result;
            try
            {
                result = _send(new ClientSubmissionRequest
                {
                    Path = path,
                    Query = query,
                    Token = credentials.Token,
                    JsonBody = jsonBody,
                    FilePath = filePath
                });
            }
            catch (Exception ex)
            {
                Log.Warn("数据上传失败: " + ex.Message);
                return Failure("网络错误：" + ex.Message);
            }
            ClientSubmissionResponse response = ParseResponse(result.Body);
            if (result.StatusCode == (int)HttpStatusCode.OK
                && (response == null || response.ok))
                return new ClientSubmissionOutcome { Status = ClientSubmissionStatus.Accepted };
            string serverError = (response?.error ?? "").Trim();
            switch (result.StatusCode)
            {
                case 409:
                    return Reject("本机授权已停止（账号已在其它设备登录）；请运行 UNC_LOGIN "
                        + "重新登录后再执行本命令。", true, result.StatusCode);
                case 401:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "设备登录凭证已失效，请运行 UNC_LOGIN 重新登录。"
                        : serverError + "（请重新登录）", true, result.StatusCode);
                case 403:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "该设备尚未绑定到本账号，请运行 UNC_LOGIN 重新登录。"
                        : serverError, true, result.StatusCode);
                case 404:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "账号不存在，上传未完成。" : serverError, false, result.StatusCode);
                case 413:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "文件超过服务端上限（200 MB）。" : serverError, false, result.StatusCode);
                case 400:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "上传内容被服务端拒绝。" : serverError, false, result.StatusCode);
                default:
                    return Failure("HTTP " + result.StatusCode
                        + (string.IsNullOrEmpty(serverError) ? "" : "：" + serverError));
            }
        }

        private bool TryCredentials(out ClientSubmissionCredentials credentials,
            out string error)
        {
            error = "";
            try
            {
                credentials = _credentials() ?? new ClientSubmissionCredentials();
            }
            catch (Exception ex)
            {
                credentials = new ClientSubmissionCredentials();
                error = ex.Message;
                Log.Warn("读取本机登录凭证失败: " + ex.Message);
                return false;
            }
            return !string.IsNullOrWhiteSpace(credentials.Account)
                && !string.IsNullOrWhiteSpace(credentials.Token);
        }

        /// <summary>后台任务结果统一从这里提示：未登录静默，其余记日志 + 命令行提示。</summary>
        private static ClientSubmissionOutcome Reject(string message, bool requiresRelogin,
            int statusCode)
        {
            Log.Warn("数据上传被拒绝: " + message);
            MachineDataRefreshCoordinator.EnqueueNotice(message);
            return new ClientSubmissionOutcome
            {
                Status = ClientSubmissionStatus.Rejected,
                Message = message,
                RequiresRelogin = requiresRelogin,
                StatusCode = statusCode
            };
        }

        private static ClientSubmissionOutcome Failure(string message)
        {
            Log.Warn("数据上传失败: " + message);
            MachineDataRefreshCoordinator.EnqueueNotice(message);
            return new ClientSubmissionOutcome
            {
                Status = ClientSubmissionStatus.Failed,
                Message = message
            };
        }

        private static ClientSubmissionOutcome Skipped()
            => new ClientSubmissionOutcome { Status = ClientSubmissionStatus.SkippedNotSignedIn };

        private static ClientSubmissionHttpResult Send(ClientSubmissionRequest request)
        {
            Uri uri = BuildRequestUri(request.Path, request.Query);
            HttpWebRequest http = WebRequest.CreateHttp(uri);
            http.Method = "POST";
            http.UserAgent = "UNCAD/" + ProductMetadata.VersionLabel;
            http.Accept = "application/json,text/plain,*/*";
            http.AllowAutoRedirect = false;
            http.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            http.Timeout = TimeoutMilliseconds;
            http.ReadWriteTimeout = UploadReadWriteTimeoutMilliseconds;
            http.Headers[OnlineLicenseMonitor.TokenHeaderName] = request.Token ?? "";
            if (!string.IsNullOrEmpty(request.FilePath))
            {
                http.ContentType = "application/octet-stream";
                WriteFileBody(http, request.FilePath);
            }
            else
            {
                http.ContentType = "application/json";
                WriteBody(http, Encoding.UTF8.GetBytes(request.JsonBody ?? ""));
            }
            try
            {
                using (var response = (HttpWebResponse)http.GetResponse())
                    return new ClientSubmissionHttpResult((int)response.StatusCode,
                        ReadBody(response));
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse error)
            {
                // 400/401/403/404/409/413 是服务端的明确结论，转成结果而不是异常。
                using (error) return new ClientSubmissionHttpResult((int)error.StatusCode,
                    ReadBody(error));
            }
        }

        /// <summary>文件按 128KB 分块流式写出，避免把 DWG 整个读进内存。</summary>
        private static void WriteFileBody(HttpWebRequest http, string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumFileBytes)
                throw new IOException("文件超过 200 MB 上限。");
            http.ContentLength = info.Length;
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 128 * 1024))
            using (Stream target = http.GetRequestStream())
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    target.Write(buffer, 0, read);
            }
        }

        private static void WriteBody(HttpWebRequest http, byte[] body)
        {
            http.ContentLength = body.Length;
            using (Stream target = http.GetRequestStream())
                target.Write(body, 0, body.Length);
        }

        private static string ReadBody(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null) return "";
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static string Text(string value) => (value ?? "").Trim();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Web.Script.Serialization;
using UNCAD.Core.Excel;

namespace UNCAD.Infra
{
    /// <summary>上报结果分类（决定日志级别与命令行提示的措辞）。</summary>
    internal enum MachineLedgerReportStatus
    {
        /// <summary>未登录（无账号或无令牌）：静默跳过，不报错。</summary>
        SkippedNotSignedIn,
        /// <summary>本机快照为空：跳过，避免清空服务端台账。</summary>
        SkippedEmptySnapshot,
        /// <summary>服务端接受（HTTP 200 + ok）。</summary>
        Reported,
        /// <summary>服务端明确拒绝（400/401/403/404/409 或行数超限）。</summary>
        Rejected,
        /// <summary>网络/HTTP/解析层面的失败（可下次刷新重试）。</summary>
        Failed
    }

    internal sealed class MachineLedgerReportOutcome
    {
        public MachineLedgerReportStatus Status { get; set; }
        /// <summary>可直接展示的中文提示；空串表示不需要提示用户。</summary>
        public string Message { get; set; } = "";
        /// <summary>409：本机授权已被其它设备接管，必须重新登录。</summary>
        public bool RequiresRelogin { get; set; }
        public int RowCount { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
        public long ReportId { get; set; }
    }

    internal sealed class MachineLedgerCredentials
    {
        public string Account { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Token { get; set; } = "";
    }

    internal sealed class MachineLedgerHttpResult
    {
        public MachineLedgerHttpResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? "";
        }

        public int StatusCode { get; }
        public string Body { get; }
    }

    /// <summary>上报请求体（字段名必须与服务端 CLIENT-REPORTING.md 一致）。</summary>
    internal sealed class MachineLedgerRow
    {
        public string machineId { get; set; }
        public string circuitName { get; set; }
        public string area { get; set; }
        public string deviceFloor { get; set; }
        public string deviceAxis { get; set; }
        public string upstreamCode { get; set; }
        public string upstreamFloor { get; set; }
        public string upstreamAxis { get; set; }
        public string powerInfo { get; set; }
        public string cableModel { get; set; }
        public string upstreamType { get; set; }
        public string facilitySwitch { get; set; }
        public string batch { get; set; }
        public string sequence { get; set; }
        public string flexibleConduitDiameter { get; set; }
    }

    internal sealed class MachineLedgerPayload
    {
        public string source { get; set; }
        public string batch { get; set; }
        public List<MachineLedgerRow> rows { get; set; }
    }

    internal sealed class MachineLedgerResponse
    {
        public bool ok { get; set; }
        public long reportId { get; set; }
        public int reported { get; set; }
        public int inserted { get; set; }
        public int updated { get; set; }
        public int removed { get; set; }
        public string error { get; set; }
    }

    /// <summary>
    /// 机台台账上报（服务端 <c>POST /api/client/reports/machines</c>，**机台数据的唯一写入口**）。
    ///
    /// 业务约束：U1F/U1U 产生的数据属于 CAD，Web 端一律只读；台账的权威来源是
    /// U1SET/U1DATA 刷新后的本机 SQLite 快照，因此只有插件能写台账，并且：
    /// - **只上报台账输入字段**（工作簿 U_ 契约），不上报 BOQ 行、块属性、
    ///   frameinfo_json 或电缆长度等 CAD 产物；
    /// - 每次上报都是该客户的**完整快照**（服务端会删除本次未出现的行）；
    /// - 失败绝不影响本地刷新结果与用户可用性：只记日志 + 命令行提示，下次刷新自动重试；
    ///   只有 409（本机授权已被其它设备接管）必须明确要求用户重新登录。
    /// </summary>
    internal sealed class MachineLedgerReporter
    {
        internal const string ReportPath = "/api/client/reports/machines";
        /// <summary>与服务端 MachineReportService.MaximumRows 保持一致。</summary>
        internal const int MaximumRows = 20000;
        /// <summary>上报主机；由 OnlineLicenseMonitor.IsExpectedEndpoint 做最终校验，
        /// 两者不一致时上报会以"地址不受信任"失败（测试已锁死一致）。</summary>
        internal const string ExpectedHost = "uncad.aosa.me";
        private const int TimeoutMilliseconds = 20000;
        private const int ReadWriteTimeoutMilliseconds = 120000;

        private readonly Func<string, List<MachineRow>> _readRows;
        private readonly Func<MachineLedgerCredentials, string, MachineLedgerHttpResult> _post;
        private readonly Func<MachineLedgerCredentials> _credentials;

        internal static MachineLedgerReporter Default { get; } = new MachineLedgerReporter(
            source => MachineWorkbookSnapshotStore.Default.ReadAllRows(source),
            Post,
            ReadCredentials);

        /// <summary>禁用上报（测试与诊断用；不产生任何网络调用）。</summary>
        internal static MachineLedgerReporter None { get; } = new MachineLedgerReporter(
            _ => new List<MachineRow>(),
            (credentials, body) => new MachineLedgerHttpResult(0, ""),
            () => new MachineLedgerCredentials());

        internal MachineLedgerReporter(
            Func<string, List<MachineRow>> readRows,
            Func<MachineLedgerCredentials, string, MachineLedgerHttpResult> post,
            Func<MachineLedgerCredentials> credentials)
        {
            _readRows = readRows ?? throw new ArgumentNullException(nameof(readRows));
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        /// <summary>
        /// 上报指定数据源的完整台账快照。**不抛异常**：所有失败都转成
        /// <see cref="MachineLedgerReportOutcome"/>，由调用方决定日志与提示。
        /// </summary>
        internal MachineLedgerReportOutcome Report(string source)
        {
            MachineLedgerCredentials credentials;
            try
            {
                credentials = _credentials() ?? new MachineLedgerCredentials();
            }
            catch (Exception ex)
            {
                Log.Warn("台账上报读取本机凭证失败: " + ex.Message);
                return Failure("读取本机登录凭证失败：" + ex.Message);
            }
            if (string.IsNullOrWhiteSpace(credentials.Account))
                return Skipped(MachineLedgerReportStatus.SkippedNotSignedIn, "");
            if (string.IsNullOrWhiteSpace(credentials.Token))
                return Skipped(MachineLedgerReportStatus.SkippedNotSignedIn, "");

            List<MachineRow> rows;
            try
            {
                rows = _readRows(source) ?? new List<MachineRow>();
            }
            catch (Exception ex)
            {
                Log.Warn("台账上报读取本机快照失败: " + ex.Message);
                return Failure("读取本机台账快照失败：" + ex.Message);
            }
            if (rows.Count == 0)
            {
                return Skipped(MachineLedgerReportStatus.SkippedEmptySnapshot,
                    "本机台账快照为空，已跳过上报（避免清空服务端台账）。");
            }
            if (rows.Count > MaximumRows)
            {
                return Reject("本机台账 " + rows.Count + " 行，超过服务端单次上限 "
                    + MaximumRows + " 行，未上报。", false);
            }

            MachineLedgerPayload payload = BuildPayload(source, rows);
            string body = Serialize(payload);
            MachineLedgerHttpResult result;
            try
            {
                result = _post(credentials, body);
            }
            catch (Exception ex)
            {
                Log.Warn("台账上报失败: " + ex.Message);
                return Failure("台账上报失败（网络）：" + ex.Message);
            }

            MachineLedgerResponse response = ParseResponse(result.Body);
            if (result.StatusCode == (int)HttpStatusCode.OK
                && (response == null || response.ok))
            {
                var reported = new MachineLedgerReportOutcome
                {
                    Status = MachineLedgerReportStatus.Reported,
                    RowCount = response?.reported ?? rows.Count,
                    Inserted = response?.inserted ?? 0,
                    Updated = response?.updated ?? 0,
                    Removed = response?.removed ?? 0,
                    ReportId = response?.reportId ?? 0,
                    Message = "台账已上报：共 " + rows.Count + " 行（新增 "
                        + (response?.inserted ?? 0) + " / 更新 " + (response?.updated ?? 0)
                        + " / 删除 " + (response?.removed ?? 0) + "）。"
                };
                Log.Info("台账上报成功: " + reported.Message);
                return reported;
            }

            string serverError = (response?.error ?? "").Trim();
            switch (result.StatusCode)
            {
                case 409:
                    // 与心跳的"被顶掉"是同一件事：必须让用户看见并要求重新登录。
                    return Reject("本机授权已停止（账号已在其它设备登录），台账未上报；"
                        + "请运行 UNC_LOGIN 重新登录后再刷新。", true);
                case 401:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "设备登录凭证已失效，台账未上报；请运行 UNC_LOGIN 重新登录后再刷新。"
                        : serverError + "（请重新登录）", true);
                case 403:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "该设备尚未绑定到本账号，台账未上报；请运行 UNC_LOGIN 重新登录。"
                        : serverError, true);
                case 404:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "账号不存在，台账未上报。" : serverError, false);
                case 400:
                    return Reject(string.IsNullOrEmpty(serverError)
                        ? "上报数据被服务端拒绝。" : serverError, false);
                default:
                    return Failure("台账上报失败（HTTP " + result.StatusCode
                        + (string.IsNullOrEmpty(serverError) ? "" : "：" + serverError) + "）。");
            }
        }

        // ---- 纯逻辑：可被 tests/UNCAD.Tests/MachineLedgerReporterTests.cs 直接验证 ----

        /// <summary>服务端凭证读取：账号（v2 语义）来自注册表，设备与令牌来自 DeviceIdentity。</summary>
        internal static MachineLedgerCredentials ReadCredentials() => new MachineLedgerCredentials
        {
            Account = (Settings.Get(ConfigKeys.OnlineAuthorizationCode, "") ?? "").Trim(),
            DeviceId = DeviceIdentity.GetOrCreateDeviceId(),
            Token = DeviceIdentity.GetToken()
        };

        /// <summary>上报地址：账号与设备走查询参数，令牌走请求头（与心跳同一套凭证）。</summary>
        internal static Uri BuildRequestUri(string account, string deviceId)
        {
            string name = OnlineLicenseMonitor.NormalizeAccount(account);
            string device = (deviceId ?? "").Trim();
            if (device.Length == 0)
                throw new ArgumentException("设备标识不能为空。", nameof(deviceId));
            var builder = new UriBuilder(new Uri("https://" + ExpectedHost + ReportPath,
                UriKind.Absolute))
            {
                Query = "account=" + Uri.EscapeDataString(name)
                    + "&device=" + Uri.EscapeDataString(device)
            };
            if (!OnlineLicenseMonitor.IsExpectedEndpoint(builder.Uri))
                throw new InvalidDataException("台账上报地址不受信任。");
            return builder.Uri;
        }

        /// <summary>
        /// 工作簿 U_ 契约 → 上报字段。映射依据 ExcelMachineReader 的严格表头绑定：
        /// Region=U_区域、MachineId=U_机台ID、CircuitName=回路名称、
        /// DeviceFloor=U_设备楼层、DownstreamAxis=U_设备轴位、Fr=U_上游编号、
        /// PanelFloor=U_上游楼层、UpstreamAxis=U_上游轴位、Detail=U_配电信息、
        /// Cable=U_电缆型号、Next=U_上游类型、FacilitySwitch=U_厂务开关、
        /// Batch=U_批次、Seq=U_序号、Dia=U_软管直径。
        /// </summary>
        internal static MachineLedgerPayload BuildPayload(string source,
            IReadOnlyList<MachineRow> rows)
        {
            var list = new List<MachineLedgerRow>();
            foreach (MachineRow row in rows ?? new List<MachineRow>())
            {
                if (row == null) continue;
                string machineId = Text(row.MachineId);
                if (machineId.Length == 0) continue;   // 服务端按 machineId 过滤
                list.Add(new MachineLedgerRow
                {
                    machineId = machineId,
                    circuitName = Text(row.CircuitName),
                    area = Text(row.Region),
                    deviceFloor = Text(row.DeviceFloor),
                    deviceAxis = Text(row.DownstreamAxis),
                    upstreamCode = Text(row.Fr),
                    upstreamFloor = Text(row.PanelFloor),
                    upstreamAxis = Text(row.UpstreamAxis),
                    powerInfo = Text(row.Detail),
                    cableModel = Text(row.Cable),
                    upstreamType = Text(row.Next),
                    facilitySwitch = Text(row.FacilitySwitch),
                    batch = Text(row.Batch),
                    sequence = Text(row.Seq),
                    flexibleConduitDiameter = Text(row.Dia)
                });
            }
            return new MachineLedgerPayload
            {
                source = ResolveSourceName(source),
                batch = ResolveBatch(rows),
                rows = list
            };
        }

        /// <summary>上报用的来源名：去掉目录与 URL 查询串，只保留工作簿文件名（与协议示例一致）。</summary>
        internal static string ResolveSourceName(string source)
        {
            string value = (source ?? "").Trim();
            if (value.Length == 0) return "";
            int cut = value.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) value = value.Substring(0, cut);
            int separator = value.LastIndexOfAny(new[] { '/', '\\' });
            if (separator >= 0 && separator < value.Length - 1)
                value = value.Substring(separator + 1);
            return value.Trim();
        }

        /// <summary>批次：快照内 U_批次 完全一致才作为本次批次，多批次不猜（返回空串）。</summary>
        internal static string ResolveBatch(IReadOnlyList<MachineRow> rows)
        {
            string batch = "";
            foreach (MachineRow row in rows ?? new List<MachineRow>())
            {
                string value = Text(row?.Batch);
                if (value.Length == 0) continue;
                if (batch.Length == 0)
                {
                    batch = value;
                    continue;
                }
                if (!string.Equals(batch, value, StringComparison.Ordinal)) return "";
            }
            return batch;
        }

        internal static string Serialize(MachineLedgerPayload payload)
            => new JavaScriptSerializer().Serialize(payload);

        internal static MachineLedgerResponse ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return new JavaScriptSerializer()
                    .Deserialize<MachineLedgerResponse>(json.TrimStart('\uFEFF'));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static MachineLedgerHttpResult Post(MachineLedgerCredentials credentials,
            string body)
        {
            Uri uri = BuildRequestUri(credentials.Account, credentials.DeviceId);
            HttpWebRequest request = WebRequest.CreateHttp(uri);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.UserAgent = "UNCAD/" + ProductMetadata.VersionLabel;
            request.Accept = "application/json,text/plain,*/*";
            request.AllowAutoRedirect = false;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = ReadWriteTimeoutMilliseconds;
            request.Headers[OnlineLicenseMonitor.TokenHeaderName] = credentials.Token;
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            request.ContentLength = payload.Length;
            try
            {
                using (Stream stream = request.GetRequestStream())
                    stream.Write(payload, 0, payload.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                    return new MachineLedgerHttpResult((int)response.StatusCode,
                        ReadBody(response));
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse error)
            {
                // 400/401/403/404/409 是服务端的明确结论，转成结果而不是异常。
                using (error) return new MachineLedgerHttpResult((int)error.StatusCode,
                    ReadBody(error));
            }
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

        private static MachineLedgerReportOutcome Skipped(MachineLedgerReportStatus status,
            string message) => new MachineLedgerReportOutcome
            {
                Status = status,
                Message = message ?? ""
            };

        private static MachineLedgerReportOutcome Reject(string message, bool requiresRelogin)
        {
            Log.Warn("台账上报被拒绝: " + message);
            return new MachineLedgerReportOutcome
            {
                Status = MachineLedgerReportStatus.Rejected,
                Message = message ?? "",
                RequiresRelogin = requiresRelogin
            };
        }

        private static MachineLedgerReportOutcome Failure(string message)
            => new MachineLedgerReportOutcome
            {
                Status = MachineLedgerReportStatus.Failed,
                Message = message ?? ""
            };
    }
}

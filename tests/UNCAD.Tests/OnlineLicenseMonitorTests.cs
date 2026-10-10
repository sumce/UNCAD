using System;
using System.Globalization;
using System.IO;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// v2 心跳与状态语义：账号 + 单设备在线，**且不支持离线使用**。
    /// 核心不变量：只有服务器明确返回 active 才放行；pending / unavailable
    /// （未验证、断网、服务器不可达）以及 device-kicked / token-invalid / unbound /
    /// device-missing / unknown / expired / suspended / revoked 一律**立即停用**。
    /// </summary>
    public sealed class OnlineLicenseMonitorTests
    {
        private const string Account = "devuser";
        private const string Device = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a";
        private static readonly DateTimeOffset ServerNow =
            new DateTimeOffset(2026, 10, 11, 9, 30, 0, TimeSpan.FromHours(8));
        private static readonly DateTimeOffset Expiry =
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
        private const string KickedMessage =
            "本账号已在设备「WORKSTATION-B」于 2026-10-11T01:26:52+08:00 登录，本机授权已停止。"
            + "如需在本机使用，请在登录窗口重新登录以在此设备上线。";

        [Fact]
        public void RefreshInterval_IsThreeMinutes()
            => Assert.Equal(TimeSpan.FromMinutes(3), OnlineLicenseMonitor.RefreshInterval);

        [Fact]
        public void Endpoint_AllowsOnlyExpectedHttpsDomain()
        {
            Uri allowed = OnlineLicenseMonitor.BuildEndpoint(Account);
            Uri http = new UriBuilder(allowed) { Scheme = "http", Port = 80 }.Uri;
            Uri otherHost = new UriBuilder(allowed)
                { Host = allowed.Host + ".invalid" }.Uri;

            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(allowed));
            Assert.False(OnlineLicenseMonitor.IsExpectedEndpoint(http));
            Assert.False(OnlineLicenseMonitor.IsExpectedEndpoint(otherHost));
        }

        [Fact]
        public void Request_UsesAccountPathDeviceQueryAndTokenHeader()
        {
            Uri request = OnlineLicenseMonitor.BuildRequestUri("ABC_123-x", Device);

            Assert.Equal("/d/key/ABC_123-x/pro.key", request.AbsolutePath);
            Assert.Equal("?device=" + Device, request.Query);
            Assert.Equal("X-Uncad-Token", OnlineLicenseMonitor.TokenHeaderName);
        }

        [Fact]
        public void Response_ParsesActiveLicenseWithDeviceFieldsAndNotification()
        {
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                ActiveJson(), ServerNow, Account, Device);

            Assert.True(state.HasResponse);
            Assert.True(state.IsActive);
            Assert.Equal("active", state.Status);
            Assert.Equal(LicenseMode.Project, state.LicenseMode);
            Assert.Equal("UNCAD-JSWY", state.CustomerCode);
            Assert.Equal("江苏文炎建设工程有限公司", state.Company);
            Assert.Equal("李小亮", state.Licensee);
            Assert.Equal("请尽快支付订阅费用，以免造成使用影响。", state.Message);
            Assert.Equal(Device, state.DeviceId);
            Assert.Equal("WORKSTATION-A", state.ActiveDeviceName);
            Assert.Equal(1, state.DeviceLimit);
            Assert.Equal("", state.FailureReason);
        }

        [Fact]
        public void Response_AcceptsPerpetualActiveLicenseWithoutExpiry()
        {
            string json = ActiveJson()
                .Replace("\"Project\"", "\"Perpetual\"")
                .Replace("\"2026-11-01T00:00:00+08:00\"", "null");
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                json, ServerNow, Account, Device);

            Assert.True(state.IsActive);
            Assert.Equal(LicenseMode.Perpetual, state.LicenseMode);
            Assert.Null(state.ExpiresAt);
        }

        [Fact]
        public void Response_RejectsWrongAccount()
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                OnlineLicenseMonitor.ParseResponse(ActiveJson(), ServerNow, "someone-else", Device));

            Assert.Contains("账号与请求不一致", error.Message);
        }

        [Fact]
        public void Response_RejectsDeviceIdFromAnotherDevice()
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                OnlineLicenseMonitor.ParseResponse(ActiveJson(), ServerNow, Account,
                    "11111111-2222-3333-4444-555555555555"));

            Assert.Contains("设备标识", error.Message);
        }

        [Fact]
        public void Response_ToleratesMissingDeviceIdForOlderServer()
        {
            string json = ActiveJson().Replace("\"" + Device + "\"", "\"\"");
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                json, ServerNow, Account, Device);

            Assert.True(state.IsActive);
            Assert.Equal("", state.DeviceId);
        }

        [Fact]
        public void Response_KeepsRealExpiryWhenExpiredAndBlocksDisabledAccounts()
        {
            OnlineLicenseState expired = OnlineLicenseMonitor.ParseResponse(
                ActiveJson(), Expiry.AddMinutes(1), Account, Device);
            OnlineLicenseState revoked = OnlineLicenseMonitor.ParseResponse(
                SessionJson("revoked", "授权已被吊销，请联系 UNSIAO.Ltd。"),
                ServerNow, Account, Device);

            Assert.False(expired.IsActive);
            Assert.NotNull(expired.ExpiresAt);
            Assert.Contains("到期", expired.FailureReason);

            Assert.False(revoked.IsActive);
            Assert.Equal("revoked", revoked.Status);
            Assert.Contains("吊销", revoked.FailureReason);
        }

        [Fact]
        public void Response_DeviceKickedUsesServerMessageAndParsesActiveDevice()
        {
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                SessionJson("device-kicked", KickedMessage), ServerNow, Account, Device);

            Assert.False(state.IsActive);
            Assert.Equal("device-kicked", state.Status);
            Assert.Equal(LicenseMode.Perpetual, state.LicenseMode);
            Assert.Null(state.ExpiresAt);
            Assert.Equal(KickedMessage, state.FailureReason);
            Assert.Equal("WORKSTATION-B", state.ActiveDeviceName);
            Assert.Equal("2026-10-11T01:26:52+08:00", state.SignedInAt);
            Assert.Equal(1, state.DeviceLimit);
        }

        [Fact]
        public void Response_DeviceKickedFallsBackToChineseReasonWithDeviceAndTime()
        {
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                SessionJson("device-kicked", ""), ServerNow, Account, Device);

            Assert.False(state.IsActive);
            Assert.Contains("WORKSTATION-B", state.FailureReason);
            Assert.Contains("2026-10-11T01:26:52+08:00", state.FailureReason);
            Assert.Contains("重新登录", state.FailureReason);
        }

        [Theory]
        [InlineData("token-invalid", "凭证")]
        [InlineData("unbound", "绑定")]
        [InlineData("device-missing", "设备")]
        [InlineData("unknown", "账号")]
        [InlineData("suspended", "暂停")]
        public void Response_NewRejectionStatusesCarryChineseReasons(string status, string keyword)
        {
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                SessionJson(status, ""), ServerNow, Account, Device);

            Assert.False(state.IsActive);
            Assert.Equal(status, state.Status);
            Assert.Contains(keyword, state.FailureReason);
        }

        // ---- 取消离线支持：拿不到服务器结论一律立即停用（本轮最重要的回归锁） ----

        [Fact]
        public void DeviceKicked_IsStoppedImmediately()
        {
            OnlineLicenseState kicked = Kicked();

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(
                kicked, ServerNow.UtcDateTime, false);

            Assert.True(license.IsExpired);
            Assert.False(string.IsNullOrWhiteSpace(license.BlockReason));
            Assert.Contains("WORKSTATION-B", license.BlockReason);
            Assert.DoesNotContain("宽限", license.StatusText);
        }

        [Theory]
        [InlineData("pending")]
        [InlineData("unavailable")]
        public void PendingAndUnavailable_AreStoppedImmediately(string status)
        {
            // 用户明确要求"不要再支持离线使用"：没有服务器结论就立即停用。
            var online = new OnlineLicenseState
            {
                HasResponse = false,
                IsActive = false,
                Status = status,
                FailureReason = "暂时无法连接授权服务器。"
            };

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(
                online, ServerNow.UtcDateTime, false);

            Assert.True(license.IsExpired);
            Assert.Contains("不支持离线使用", license.BlockReason);
            Assert.DoesNotContain("宽限", license.StatusText);
        }

        [Fact]
        public void OnlyActiveServerVerdict_AllowsCommands()
        {
            OnlineLicenseState active = OnlineLicenseMonitor.ParseResponse(
                ActiveJson(), ServerNow, Account, Device);

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(
                active, ServerNow.UtcDateTime, false);

            Assert.True(active.IsActive);
            Assert.False(license.IsExpired);
            Assert.Contains("在线有效", license.StatusText);
        }

        [Fact]
        public void FreshWatermark_DoesNotEnableOfflineUse()
        {
            // 有"刚验证成功"的水位线也不再宽限：CurrentLicense() 走的是真实路径，
            // 此时监控状态是初始 pending（还没拿到服务器结论）→ 必须停用。
            string original = Settings.Get(ConfigKeys.LicenseLastValidatedUtc, "");
            try
            {
                Settings.Set(ConfigKeys.LicenseLastValidatedUtc,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                LicenseSnapshot license = ProductMetadata.CurrentLicense();

                Assert.True(license.IsExpired);
                Assert.Contains("不支持离线使用", license.BlockReason);
            }
            finally
            {
                Settings.Set(ConfigKeys.LicenseLastValidatedUtc, original);
            }
        }

        [Theory]
        [InlineData("device-kicked")]
        [InlineData("token-invalid")]
        [InlineData("unbound")]
        [InlineData("device-missing")]
        [InlineData("unknown")]
        [InlineData("expired")]
        [InlineData("suspended")]
        [InlineData("revoked")]
        public void ServerVerdicts_AreAlwaysStopped(string status)
        {
            string json = status == "expired"
                ? ActiveJson()
                : SessionJson(status, "服务器拒绝本次请求，请重新登录。");
            DateTimeOffset now = status == "expired" ? Expiry.AddMinutes(1) : ServerNow;
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(
                json, now, Account, Device);

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(
                state, now.UtcDateTime, false);

            Assert.True(license.IsExpired, status + " 必须立即停用");
            Assert.False(string.IsNullOrWhiteSpace(license.BlockReason),
                status + " 必须有可展示的中文原因");
            Assert.DoesNotContain("宽限", license.StatusText);
        }

        [Fact]
        public void ClockTamper_IsReportedAsBlockReason()
        {
            // 没有宽限后，回拨不再"延长授权"，只影响提示文案；
            // 到期判定仍以服务器 Date 头为准（见 OnlyActiveServerVerdict_*）。
            var online = new OnlineLicenseState
            {
                HasResponse = false,
                IsActive = false,
                Status = "unavailable",
                FailureReason = "暂时无法连接授权服务器。"
            };

            LicenseSnapshot license = ProductMetadata.EvaluateOnlineLicense(
                online, ServerNow.UtcDateTime, true);

            Assert.True(license.IsExpired);
            Assert.Contains("回拨", license.BlockReason);
        }

        [Fact]
        public void NoStoredAccount_IsStoppedImmediately()
        {
            string original = Settings.Get(ConfigKeys.OnlineAuthorizationCode, "");
            try
            {
                Settings.Set(ConfigKeys.OnlineAuthorizationCode, "");
                Assert.Equal("", OnlineLicenseMonitor.StoredAccount);
                Assert.True(ProductMetadata.CurrentLicense().IsExpired);
            }
            finally
            {
                Settings.Set(ConfigKeys.OnlineAuthorizationCode, original);
            }
        }

        private static OnlineLicenseState Kicked()
            => OnlineLicenseMonitor.ParseResponse(
                SessionJson("device-kicked", KickedMessage), ServerNow, Account, Device);

        /// <summary>拒绝类文档必须 Perpetual + ExpiresAt=null（服务端硬约束）。</summary>
        private static string SessionJson(string status, string message) => @"{
  ""SchemaVersion"": 1,
  ""Product"": ""UNCAD Pro"",
  ""LicenseMode"": ""Perpetual"",
  ""AuthorizationCode"": """ + Account + @""",
  ""CustomerCode"": ""UNKNOWN"",
  ""Company"": """",
  ""Licensee"": """",
  ""Status"": """ + status + @""",
  ""ExpiresAt"": null,
  ""ExpectedAuthorizationYears"": null,
  ""MessageTitle"": ""UNSIAO 授权提示"",
  ""MessageLevel"": ""warning"",
  ""Message"": """ + message + @""",
  ""DeviceId"": """ + Device + @""",
  ""ActiveDeviceName"": ""WORKSTATION-B"",
  ""SignedInAt"": ""2026-10-11T01:26:52+08:00"",
  ""DeviceLimit"": 1
}";

        private static string ActiveJson() => @"{
  ""SchemaVersion"": 1,
  ""Product"": ""UNCAD Pro"",
  ""LicenseMode"": ""Project"",
  ""AuthorizationCode"": """ + Account + @""",
  ""CustomerCode"": ""UNCAD-JSWY"",
  ""Company"": ""江苏文炎建设工程有限公司"",
  ""Licensee"": ""李小亮"",
  ""Status"": ""active"",
  ""ExpiresAt"": ""2026-11-01T00:00:00+08:00"",
  ""ExpectedAuthorizationYears"": 10,
  ""MessageTitle"": ""订阅费用提醒"",
  ""MessageLevel"": ""warning"",
  ""Message"": ""请尽快支付订阅费用，以免造成使用影响。"",
  ""DeviceId"": """ + Device + @""",
  ""ActiveDeviceName"": ""WORKSTATION-A"",
  ""SignedInAt"": ""2026-09-03T08:00:00+08:00"",
  ""DeviceLimit"": 1
}";
    }
}

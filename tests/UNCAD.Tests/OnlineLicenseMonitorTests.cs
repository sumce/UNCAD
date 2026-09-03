using System;
using System.IO;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class OnlineLicenseMonitorTests
    {
        private const string AuthorizationCode = "test_code_123";

        [Fact]
        public void RefreshInterval_IsFiveMinutes()
            => Assert.Equal(TimeSpan.FromMinutes(5), OnlineLicenseMonitor.RefreshInterval);

        [Fact]
        public void Endpoint_AllowsOnlyExpectedHttpsDomain()
        {
            Uri allowed = OnlineLicenseMonitor.BuildEndpoint("code");
            Uri http = new UriBuilder(allowed) { Scheme = "http", Port = 80 }.Uri;
            Uri otherHost = new UriBuilder(allowed)
                { Host = allowed.Host + ".invalid" }.Uri;

            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(allowed));
            Assert.False(OnlineLicenseMonitor.IsExpectedEndpoint(http));
            Assert.False(OnlineLicenseMonitor.IsExpectedEndpoint(otherHost));
        }

        [Fact]
        public void AuthorizationCode_IsRuntimeInputAndBuildsExpectedUrl()
        {
            Assert.Equal("ABC_123-x", OnlineLicenseMonitor.NormalizeCode("  ABC_123-x  "));
            Assert.Equal("/d/key/ABC_123-x/pro.key",
                OnlineLicenseMonitor.BuildEndpoint("ABC_123-x").AbsolutePath);
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeCode("bad/code"));
        }

        [Fact]
        public void Response_ParsesActiveLicenseAndNotification()
        {
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(ValidJson(),
                new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.FromHours(8)),
                AuthorizationCode);

            Assert.True(state.HasResponse);
            Assert.True(state.IsActive);
            Assert.Equal("active", state.Status);
            Assert.Equal(LicenseMode.Project, state.LicenseMode);
            Assert.Equal("UNCAD-JSWY", state.CustomerCode);
            Assert.Equal("江苏文炎建设工程有限公司", state.Company);
            Assert.Equal("李小亮", state.Licensee);
            Assert.Equal("请尽快支付订阅费用，以免造成使用影响。", state.Message);
        }

        [Fact]
        public void Response_RejectsWrongAuthorizationCode()
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                OnlineLicenseMonitor.ParseResponse(ValidJson(), DateTimeOffset.UtcNow,
                    "wrong"));

            Assert.Contains("授权码与输入不一致", error.Message);
        }

        [Fact]
        public void Response_AcceptsCustomerIdentityFromKeyWithoutVersionCheck()
        {
            string json = ValidJson().Replace("UNCAD-JSWY", "CUSTOMER-ANY");
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(json,
                DateTimeOffset.UtcNow, AuthorizationCode);

            Assert.True(state.IsActive);
            Assert.Equal("CUSTOMER-ANY", state.CustomerCode);
        }

        [Fact]
        public void Response_AllowsControllablePerpetualLicenseWithoutExpiry()
        {
            string json = ValidJson()
                .Replace("\"Project\"", "\"Perpetual\"")
                .Replace("\"UNCAD-JSWY\"", "\"UNCAD-JSHY\"")
                .Replace("\"2026-10-01T00:00:00+08:00\"", "\"\"");
            OnlineLicenseState state = OnlineLicenseMonitor.ParseResponse(json,
                DateTimeOffset.UtcNow, AuthorizationCode);

            Assert.True(state.IsActive);
            Assert.Equal(LicenseMode.Perpetual, state.LicenseMode);
            Assert.Null(state.ExpiresAt);
        }

        [Fact]
        public void Response_BlocksExpiredOrDisabledLicense()
        {
            OnlineLicenseState expired = OnlineLicenseMonitor.ParseResponse(ValidJson(),
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(8)),
                AuthorizationCode);
            OnlineLicenseState disabled = OnlineLicenseMonitor.ParseResponse(
                ValidJson().Replace("\"active\"", "\"suspended\""),
                new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.FromHours(8)),
                AuthorizationCode);

            Assert.False(expired.IsActive);
            Assert.Contains("到期", expired.FailureReason);
            Assert.False(disabled.IsActive);
            Assert.Contains("suspended", disabled.FailureReason);
        }

        private static string ValidJson() => @"{
  ""schemaVersion"": 1,
  ""product"": ""UNCAD Pro"",
  ""licenseMode"": ""Project"",
  ""authorizationCode"": """ + AuthorizationCode + @""",
  ""customerCode"": ""UNCAD-JSWY"",
  ""company"": ""江苏文炎建设工程有限公司"",
  ""licensee"": ""李小亮"",
  ""status"": ""active"",
  ""expiresAt"": ""2026-10-01T00:00:00+08:00"",
  ""expectedAuthorizationYears"": 10,
  ""messageTitle"": ""订阅费用提醒"",
  ""messageLevel"": ""warning"",
  ""message"": ""请尽快支付订阅费用，以免造成使用影响。""
}";
    }
}

using System;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// v2 客户端登录/设备身份契约：授权主机 uncad.aosa.me、心跳携带
    /// <c>?device=</c> 与 <c>X-Uncad-Token</c>、回调必须同时校验 state 与 device。
    /// 这三条是"账号 + 单设备在线"的安全边界，只可收紧不可放宽。
    /// </summary>
    public sealed class ClientLoginContractTests
    {
        private const string Account = "devuser";
        private const string Device = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a";
        /// <summary>服务端 DeviceToken 的形状：base64url(32B) = 43 字符。</summary>
        private const string Token = "qN3fZk9Wx2LpRt7YvBc4MdE6GhJ1Ks8UoA5TiPnQrZe";
        private const string Nonce = "3f7a1c9e5b2d8064a1f3c7e9d5b20846";

        [Fact]
        public void BuildEndpoint_UsesHttps443OnUncadHost()
        {
            Uri endpoint = OnlineLicenseMonitor.BuildEndpoint(Account);

            Assert.Equal(Uri.UriSchemeHttps, endpoint.Scheme);
            Assert.Equal("uncad.aosa.me", endpoint.Host);
            Assert.Equal(443, endpoint.Port);
            Assert.Equal("/d/key/" + Account + "/pro.key", endpoint.AbsolutePath);
            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(endpoint));
        }

        [Fact]
        public void BuildRequestUri_CarriesDeviceQueryParameter()
        {
            Uri request = OnlineLicenseMonitor.BuildRequestUri(Account, Device);

            Assert.Equal("/d/key/" + Account + "/pro.key", request.AbsolutePath);
            Assert.Equal("?device=" + Device, request.Query);
            Assert.Equal("X-Uncad-Token", OnlineLicenseMonitor.TokenHeaderName);
            Assert.Equal("device", OnlineLicenseMonitor.DeviceQueryName);
            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(request));
        }

        [Fact]
        public void BuildEndpoint_SupportsServerSideAccountCharsetSuchAsChinese()
        {
            // 服务端账号 3-64 字符且不限制字符集；中文账号必须能安全进入路径。
            Uri endpoint = OnlineLicenseMonitor.BuildEndpoint("张三");
            string path = Uri.UnescapeDataString(endpoint.AbsolutePath);

            Assert.Equal("/d/key/张三/pro.key", path);
            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(endpoint));
        }

        [Fact]
        public void LoginUrl_UsesSameTrustedHostAsLicenseEndpoint()
        {
            Uri login = ClientLoginForm.BuildLoginUrl(Nonce, Device, "WORKSTATION-A");

            Assert.Equal(OnlineLicenseMonitor.BuildEndpoint(Account).Host, login.Host);
            Assert.Equal(ClientLoginForm.LicenseHost, login.Host);
            Assert.Equal(Uri.UriSchemeHttps, login.Scheme);
            Assert.Equal(443, login.Port);
            Assert.Equal("/client/login", login.AbsolutePath);
            Assert.Contains("state=" + Nonce, login.Query);
            Assert.Contains("device=" + Device, login.Query);
            Assert.Contains("name=WORKSTATION-A", login.Query);
            Assert.True(OnlineLicenseMonitor.IsExpectedEndpoint(login));
        }

        [Fact]
        public void LoginUrl_RequiresStateAndDevice()
        {
            Assert.Throws<ArgumentException>(
                () => ClientLoginForm.BuildLoginUrl("", Device, "PC"));
            Assert.Throws<ArgumentException>(
                () => ClientLoginForm.BuildLoginUrl(Nonce, "", "PC"));
        }

        [Fact]
        public void StateNonce_IsRandomHexAndSingleUse()
        {
            string first = ClientLoginForm.CreateStateNonce();
            string second = ClientLoginForm.CreateStateNonce();

            Assert.Equal(32, first.Length);
            Assert.Matches("^[0-9a-f]{32}$", first);
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void DeviceId_IsStableOnceCreated()
        {
            string first = DeviceIdentity.GetOrCreateDeviceId();
            string second = DeviceIdentity.GetOrCreateDeviceId();

            Assert.True(Guid.TryParse(first, out _));
            Assert.Equal(first, second);
            Assert.Equal(first, DeviceIdentity.GetOrCreateDeviceId());
        }

        [Fact]
        public void EnsureDeviceId_KeepsValidValueAndReplacesInvalidOne()
        {
            Assert.Equal(Device, DeviceIdentity.EnsureDeviceId(Device));
            Assert.Equal(Device, DeviceIdentity.EnsureDeviceId("  " + Device + "  "));

            string generated = DeviceIdentity.EnsureDeviceId("");
            Assert.True(Guid.TryParse(generated, out _));
            Assert.NotEqual("", generated);

            string healed = DeviceIdentity.EnsureDeviceId("not-a-guid");
            Assert.True(Guid.TryParse(healed, out _));
            Assert.NotEqual("not-a-guid", healed);
        }

        [Fact]
        public void Token_ValidationAcceptsServerShapeAndRejectsUnsafeValues()
        {
            Assert.True(DeviceIdentity.IsPlausibleToken(Token));
            Assert.True(DeviceIdentity.IsPlausibleToken("  " + Token + "  "));
            Assert.False(DeviceIdentity.IsPlausibleToken(""));
            Assert.False(DeviceIdentity.IsPlausibleToken(null));
            Assert.False(DeviceIdentity.IsPlausibleToken("bad token"));
            Assert.False(DeviceIdentity.IsPlausibleToken("line\r\nbreak"));
            Assert.False(DeviceIdentity.IsPlausibleToken("凭证"));
            Assert.False(DeviceIdentity.IsPlausibleToken(new string('a', 513)));
        }

        [Theory]
        [InlineData("http://uncad.aosa.me/d/key/devuser/pro.key")]
        [InlineData("https://cloud.aosa.me/d/key/devuser/pro.key")]
        [InlineData("https://uncad.aosa.me.invalid/d/key/devuser/pro.key")]
        [InlineData("https://www.aosa.me/d/key/devuser/pro.key")]
        [InlineData("https://evil.example/d/key/devuser/pro.key")]
        [InlineData("https://uncad.aosa.me:8443/d/key/devuser/pro.key")]
        [InlineData("https://uncad.aosa.me:80/d/key/devuser/pro.key")]
        [InlineData("http://uncad.aosa.me:443/d/key/devuser/pro.key")]
        public void IsExpectedEndpoint_RejectsAnythingButHttps443OnUncadHost(string url)
            => Assert.False(OnlineLicenseMonitor.IsExpectedEndpoint(new Uri(url)));

        [Fact]
        public void IsExpectedEndpoint_RejectsNull()
            => Assert.False(OnlineLicenseMonitor.IsExpectedEndpoint(null));

        [Fact]
        public void Callback_ParsesTokenDeviceAccountAndState()
        {
            Uri uri = CallbackUri(Token, Device, Nonce);

            Assert.True(ClientLoginForm.TryParseCallback(uri, Nonce, Device,
                out string token, out string device, out string state, out string account,
                out string error));

            Assert.Equal(Token, token);
            Assert.Equal(Device, device);
            Assert.Equal(Nonce, state);
            Assert.Equal(Account, account);
            Assert.Equal("", error);
        }

        [Fact]
        public void Callback_WithoutAccountStillParsesForFallbackPath()
        {
            // 服务端新增 account= 之前/缺失时：仍必须解析成功，把账号留给
            // 输入框预填与同源识别兜底，而不是报错或崩溃。
            Uri uri = new Uri("https://uncad.aosa.me/client/callback?token="
                + Uri.EscapeDataString(Token) + "&device=" + Uri.EscapeDataString(Device)
                + "&state=" + Uri.EscapeDataString(Nonce));

            Assert.True(ClientLoginForm.TryParseCallback(uri, Nonce, Device,
                out string token, out string device, out _, out string account, out string error));

            Assert.Equal(Token, token);
            Assert.Equal(Device, device);
            Assert.Equal("", account);
            Assert.Equal("", error);
        }

        [Fact]
        public void Callback_RejectsMismatchedState()
        {
            string forged = ClientLoginForm.CreateStateNonce();
            Uri uri = CallbackUri(Token, Device, forged);

            Assert.False(ClientLoginForm.TryParseCallback(uri, Nonce, Device,
                out string token, out _, out string state, out _, out string error));

            Assert.Equal("", token);
            Assert.Equal(forged, state);
            Assert.Contains("state", error);
            Assert.Contains("拒绝", error);
        }

        [Fact]
        public void Callback_RejectsMismatchedDevice()
        {
            // 凭证是发给别的设备的：即使 state 正确也必须拒绝。
            const string otherDevice = "11111111-2222-3333-4444-555555555555";
            Uri uri = CallbackUri(Token, otherDevice, Nonce);

            Assert.False(ClientLoginForm.TryParseCallback(uri, Nonce, Device,
                out string token, out string device, out _, out _, out string error));

            Assert.Equal("", token);
            Assert.Equal(otherDevice, device);
            Assert.Contains("设备", error);
            Assert.Contains("拒绝", error);
        }

        [Fact]
        public void Callback_RejectsMissingTokenDeviceOrState()
        {
            Assert.False(ClientLoginForm.TryParseCallback(
                new Uri("https://uncad.aosa.me/client/callback?device=" + Device
                    + "&account=" + Account + "&state=" + Nonce), Nonce, Device,
                out _, out _, out _, out _, out string noToken));
            Assert.Contains("凭证", noToken);

            Assert.False(ClientLoginForm.TryParseCallback(
                new Uri("https://uncad.aosa.me/client/callback?token=" + Token
                    + "&account=" + Account + "&state=" + Nonce), Nonce, Device,
                out _, out _, out _, out _, out string noDevice));
            Assert.Contains("设备", noDevice);

            Assert.False(ClientLoginForm.TryParseCallback(
                new Uri("https://uncad.aosa.me/client/callback?token=" + Token
                    + "&device=" + Device + "&account=" + Account), Nonce, Device,
                out _, out _, out _, out _, out string noState));
            Assert.Contains("state", noState);
        }

        [Fact]
        public void Callback_RejectsMissingLocalNonceOrDevice()
        {
            Uri uri = CallbackUri(Token, Device, Nonce);

            Assert.False(ClientLoginForm.TryParseCallback(uri, "", Device,
                out _, out _, out _, out _, out string noNonce));
            Assert.Contains("state", noNonce);

            Assert.False(ClientLoginForm.TryParseCallback(uri, Nonce, "",
                out _, out _, out _, out _, out string noLocalDevice));
            Assert.Contains("设备", noLocalDevice);
        }

        [Fact]
        public void Callback_RejectsUntrustedHost()
        {
            Uri uri = new Uri("https://evil.example/client/callback?token=" + Token
                + "&device=" + Device + "&account=" + Account + "&state=" + Nonce);

            Assert.False(ClientLoginForm.TryParseCallback(uri, Nonce, Device,
                out _, out _, out _, out _, out string error));
            Assert.Contains("受信任", error);
        }

        [Fact]
        public void Callback_RejectsWrongPath()
        {
            Uri uri = new Uri("https://uncad.aosa.me/client/callback/extra?token=" + Token
                + "&device=" + Device + "&account=" + Account + "&state=" + Nonce);

            Assert.False(ClientLoginForm.TryParseCallback(uri, Nonce, Device,
                out _, out _, out _, out _, out string error));
            Assert.Contains("路径", error);
        }

        [Fact]
        public void CallbackPayload_ParsesWindowLicenseObject()
        {
            string json = "{\"token\":\"" + Token + "\",\"device\":\"" + Device
                + "\",\"account\":\"" + Account + "\",\"state\":\"" + Nonce + "\"}";

            Assert.True(ClientLoginForm.TryParseCallbackPayload(json, Nonce, Device,
                out string token, out string device, out string account, out string error));

            Assert.Equal(Token, token);
            Assert.Equal(Device, device);
            Assert.Equal(Account, account);
            Assert.Equal("", error);
        }

        [Fact]
        public void CallbackPayload_WithoutAccountStillParsesForFallbackPath()
        {
            string json = "{\"token\":\"" + Token + "\",\"device\":\"" + Device
                + "\",\"state\":\"" + Nonce + "\"}";

            Assert.True(ClientLoginForm.TryParseCallbackPayload(json, Nonce, Device,
                out _, out _, out string account, out string error));

            Assert.Equal("", account);
            Assert.Equal("", error);
        }

        [Fact]
        public void CallbackPayload_RejectsMismatchedStateOrDevice()
        {
            string wrongState = "{\"token\":\"" + Token + "\",\"device\":\"" + Device
                + "\",\"account\":\"" + Account + "\",\"state\":\""
                + ClientLoginForm.CreateStateNonce() + "\"}";
            Assert.False(ClientLoginForm.TryParseCallbackPayload(wrongState, Nonce, Device,
                out _, out _, out _, out string stateError));
            Assert.Contains("state", stateError);

            string wrongDevice = "{\"token\":\"" + Token
                + "\",\"device\":\"11111111-2222-3333-4444-555555555555\",\"account\":\""
                + Account + "\",\"state\":\"" + Nonce + "\"}";
            Assert.False(ClientLoginForm.TryParseCallbackPayload(wrongDevice, Nonce, Device,
                out _, out _, out _, out string deviceError));
            Assert.Contains("设备", deviceError);
        }

        [Fact]
        public void CallbackPayload_RejectsMalformedJson()
            => Assert.False(ClientLoginForm.TryParseCallbackPayload("not-json", Nonce, Device,
                out _, out _, out _, out string _));

        [Fact]
        public void Message_ParsesAccountEnvelopeFromSameOriginProfile()
        {
            string json = "{\"kind\":\"account\",\"account\":\"devuser\"}";

            Assert.True(ClientLoginForm.TryParseMessage(json,
                out LicenseCallbackPayload payload, out string error));

            Assert.Equal("account", payload.kind);
            Assert.Equal("devuser", payload.account);
            Assert.Equal("", error);
        }

        [Fact]
        public void NormalizeAccount_AllowsServerCharsetAndRejectsPathBreakingInput()
        {
            Assert.Equal("devuser", OnlineLicenseMonitor.NormalizeAccount("  devuser  "));
            Assert.Equal("张三", OnlineLicenseMonitor.NormalizeAccount("张三"));
            Assert.Equal("user.name@example.com",
                OnlineLicenseMonitor.NormalizeAccount("user.name@example.com"));

            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount(""));
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount("   "));
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount("a/b"));
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount("a?b"));
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount("a&b=1"));
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount("a#b"));
            Assert.Throws<ArgumentException>(() => OnlineLicenseMonitor.NormalizeAccount("a b"));
            Assert.Throws<ArgumentException>(
                () => OnlineLicenseMonitor.NormalizeAccount(new string('a', 129)));
        }

        [Fact]
        public void ResolveAccount_PrefersServerCallbackAccountForHeartbeat()
        {
            // 心跳端点 /d/key/{account}/pro.key 必须有账号：回调带 account 时以它为准。
            Assert.Equal("server-user",
                ClientLoginForm.ResolveAccount("server-user", "stale-typed-user"));
            Assert.Equal("server-user",
                ClientLoginForm.ResolveAccount("  server-user  ", ""));
            // 缺失时退回输入框/同源识别兜底，且不抛异常。
            Assert.Equal("typed-user", ClientLoginForm.ResolveAccount("", "typed-user"));
            Assert.Equal("typed-user", ClientLoginForm.ResolveAccount(null, " typed-user "));
            Assert.Equal("", ClientLoginForm.ResolveAccount(null, null));
        }

        private static Uri CallbackUri(string token, string device, string state)
            => new Uri("https://uncad.aosa.me/client/callback?token="
                + Uri.EscapeDataString(token)
                + "&device=" + Uri.EscapeDataString(device)
                + "&account=" + Uri.EscapeDataString(Account)
                + "&state=" + Uri.EscapeDataString(state));
    }
}

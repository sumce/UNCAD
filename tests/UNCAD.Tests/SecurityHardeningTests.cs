using System;
using System.IO;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-037 安全加固的回归测试：登录凭证 DPAPI 加密、日志账户脱敏、
    /// 登录页弹窗拦截。WebView 行为无法无头验证，用源码契约锁住订阅。
    /// </summary>
    public sealed class SecurityHardeningTests
    {
        [Theory]
        [InlineData("abc123.-_")]
        [InlineData("一个很长的凭证字符串超过普通长度用来确认编码稳定")]
        [InlineData("!")]
        public void TokenCipher_RoundTripsThroughDpapi(string plain)
        {
            string encrypted = TokenCipher.Encrypt(plain);

            Assert.StartsWith(TokenCipher.Prefix, encrypted);
            Assert.True(TokenCipher.LooksEncrypted(encrypted));
            Assert.NotEqual(plain, encrypted);
            Assert.Equal(plain, TokenCipher.TryDecrypt(encrypted));
        }

        [Fact]
        public void TokenCipher_PlaintextTokenNeverStored()
        {
            string encrypted = TokenCipher.Encrypt("secret-token-value");

            Assert.DoesNotContain("secret-token-value", encrypted);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("legacy-plaintext-token")]
        [InlineData("dpapi:not-valid-base64!!!")]
        [InlineData("dpapi:AAAA")]
        public void TokenCipher_BadInput_ReturnsNull(string stored)
        {
            Assert.Null(TokenCipher.TryDecrypt(stored));
        }

        [Theory]
        [InlineData("zhang.wei@corp.cn", "z***@corp.cn")]
        [InlineData("a@b.cn", "*@b.cn")]
        [InlineData("worker", "w***")]
        [InlineData("w", "*")]
        [InlineData("  padded@x.cn  ", "p***@x.cn")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void AccountMask_HidesLocalPart(string account, string expected)
        {
            Assert.Equal(expected, AccountMask.Mask(account));
        }

        [Fact]
        public void ClientLoginWindow_BlocksNewWindowsAndMasksAccountInLogs()
        {
            string source = File.ReadAllText(RepoFile(
                "src", "UNCAD", "UI", "Wpf", "ClientLoginWindow.xaml.cs"));

            // 弹窗拦截：订阅 + Handled 必须同时存在，只订不拦等于没做。
            Assert.Contains("NewWindowRequested +=", source);
            Assert.Contains("e.Handled = true", source);
            // 账户脱敏：识别日志必须走 Mask，且不得残留明文拼接。
            Assert.Contains("AccountMask.Mask(account)", source);
            Assert.DoesNotContain("识别账号: \" + account", source);
            Assert.DoesNotContain("识别结果: \" + account", source);
            // 非授权服务器导航必须在 NavigationStarting 里取消（拦截不放行）。
            Assert.Contains("args.Cancel = true", source);
            Assert.Contains("IsExpectedEndpoint(uri)", source);
        }

        [Fact]
        public void DeviceIdentity_TokenPath_UsesCipherNotPlaintext()
        {
            string source = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Infra", "DeviceIdentity.cs"));

            // 保存必须走加密；明文只允许出现在旧值升级分支的注释说明里。
            Assert.Contains("TokenCipher.Encrypt", source);
            Assert.Contains("TokenCipher.TryDecrypt", source);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}

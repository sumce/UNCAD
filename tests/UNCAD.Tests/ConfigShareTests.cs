using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UNCAD.Core.ConfigShare;
using UNCAD.Core.Contracts;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class ConfigShareTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            public readonly Dictionary<string, string> Values =
                new Dictionary<string, string>(StringComparer.Ordinal);
            public int SetManyCalls;

            public string Get(string name, string defaultValue)
                => Values.TryGetValue(name, out string value) ? value : defaultValue;

            public void Set(string name, string value) => Values[name] = value;

            public void SetMany(IReadOnlyDictionary<string, string> values)
            {
                SetManyCalls++;
                foreach (KeyValuePair<string, string> item in values)
                    Values[item.Key] = item.Value;
            }
        }

        private static ConfigShareService Service(MemoryStore store)
            => new ConfigShareService(store);

        [Fact]
        public void Export_ContainsOnlyExplicitlySetShareableKeys()
        {
            var source = new MemoryStore();
            source.Values[ConfigKeys.UnlHeight] = "180";
            source.Values[ConfigKeys.SubmitFolder] = @"D:\UNCAD输出";
            source.Values[ConfigKeys.OnlineAuthorizationCode] = "account@example.com";
            source.Values[ConfigKeys.ClockWatermarkUtc] = "20261101120000";
            source.Values[ConfigKeys.UpdateNotesSeenVersion] = "2.9.3";
            source.Values[ConfigKeys.LicenseLastValidatedUtc] = "20261101120000";
            source.Values[ConfigKeys.FillRecentCatalogItems] = "A,B";

            ConfigSharePayload payload = ConfigShareCodec.Decode(
                Service(source).ExportCode());

            Assert.Equal(ConfigSharePayload.CurrentVersion, payload.Version);
            Assert.Equal(ConfigSharePayload.ProductName, payload.Product);
            Assert.Equal(ProductMetadata.VersionLabel, payload.AppVersion);
            Assert.Contains(ConfigKeys.UnlHeight, payload.Items.Keys);
            Assert.Contains(ConfigKeys.SubmitFolder, payload.Items.Keys);
            Assert.Equal("180", payload.Items[ConfigKeys.UnlHeight]);
            Assert.DoesNotContain(ConfigKeys.UnlText, payload.Items.Keys);
            // 导出必须和导入一样应用排除表：授权账号/时钟水位线/更新提示/UI 记忆永不进分享码。
            foreach (string sensitive in new[]
            {
                ConfigKeys.OnlineAuthorizationCode,
                ConfigKeys.ClockWatermarkUtc,
                ConfigKeys.UpdateNotesSeenVersion,
                ConfigKeys.LicenseLastValidatedUtc,
                ConfigKeys.FillRecentCatalogItems
            })
            {
                Assert.DoesNotContain(sensitive, payload.Items.Keys);
                Assert.DoesNotContain("account@example.com", payload.Items.Values);
            }
            Assert.Matches(new Regex("^[0-9a-f]{64}$"), payload.Checksum);
        }

        [Fact]
        public void Preview_MarksOnlyValuesThatDifferFromThisMachine()
        {
            var target = new MemoryStore();
            target.Values[ConfigKeys.UnlHeight] = "180";
            target.Values[ConfigKeys.UnqHeight] = "180";

            ConfigSharePayload payload = new ConfigSharePayload
            {
                AppVersion = "2.10.0",
                CreatedUtc = DateTimeOffset.Now,
                Items = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    [ConfigKeys.UnlHeight] = "180",
                    [ConfigKeys.UnqHeight] = "200"
                }
            };
            ConfigSharePreview preview = Service(target).PreviewCode(
                ConfigShareCodec.Encode(payload));

            Assert.Equal("2.10.0", preview.Version);
            Assert.Equal(2, preview.Items.Count);
            ConfigShareChange change = Assert.Single(preview.Changes);
            Assert.Equal(ConfigKeys.UnqHeight, change.Key);
            Assert.Equal("180", change.CurrentValue);
            Assert.Equal("200", change.SharedValue);
            Assert.False(change.IsNew);
        }

        [Fact]
        public void Roundtrip_ImportAppliesEveryItemInOneAtomicSetMany()
        {
            var source = new MemoryStore();
            source.Values[ConfigKeys.UnlHeight] = "180";
            source.Values[ConfigKeys.UnqHeight] = "200";
            string code = Service(source).ExportCode();

            var target = new MemoryStore();
            ConfigSharePreview preview = Service(target).PreviewCode(code);
            Assert.Equal(2, preview.Changes.Count);
            Assert.All(preview.Changes, change => Assert.True(change.IsNew));

            ConfigShareImportResult result = Service(target).ImportCode(code);

            Assert.Equal(1, target.SetManyCalls);
            Assert.Equal(new[] { ConfigKeys.UnlHeight, ConfigKeys.UnqHeight },
                result.Applied);
            Assert.Empty(result.Skipped);
            Assert.Equal("180", target.Values[ConfigKeys.UnlHeight]);
            Assert.Equal("200", target.Values[ConfigKeys.UnqHeight]);
        }

        [Fact]
        public void Import_SkipsUnknownAndSensitiveKeys()
        {
            var payload = new ConfigSharePayload
            {
                CreatedUtc = DateTimeOffset.Now,
                Items = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["NOT_A_REAL_KEY"] = "1",
                    [ConfigKeys.OnlineAuthorizationCode] = "attacker@example.com",
                    [ConfigKeys.UnlHeight] = "200"
                }
            };
            var target = new MemoryStore();

            ConfigShareImportResult result = Service(target)
                .ImportCode(ConfigShareCodec.Encode(payload));

            Assert.Equal(new[] { ConfigKeys.UnlHeight }, result.Applied);
            Assert.Equal(2, result.SkippedCount);
            Assert.Contains("NOT_A_REAL_KEY", result.Skipped);
            Assert.Contains(ConfigKeys.OnlineAuthorizationCode, result.Skipped);
            Assert.False(target.Values.ContainsKey(ConfigKeys.OnlineAuthorizationCode));
            Assert.Equal("200", target.Values[ConfigKeys.UnlHeight]);
        }

        [Fact]
        public void Import_WithNoApplicableItemsThrowsChineseError()
        {
            var payload = new ConfigSharePayload
            {
                CreatedUtc = DateTimeOffset.Now,
                Items = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["NOT_A_REAL_KEY"] = "1"
                }
            };
            string code = ConfigShareCodec.Encode(payload);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => Service(new MemoryStore()).ImportCode(code));
            Assert.Contains("没有可应用的 UNCAD 配置项", error.Message);
        }

        [Fact]
        public void Decode_RejectsEmptyAndNonBase64Text()
        {
            Assert.Contains("为空", Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode("   ")).Message);
            Assert.Contains("Base64", Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode("不是base64!!")).Message);
        }

        [Fact]
        public void Decode_RejectsTamperedPayloadBecauseChecksumMismatches()
        {
            var payload = new ConfigSharePayload
            {
                CreatedUtc = DateTimeOffset.Now,
                Items = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    [ConfigKeys.UnlHeight] = "180"
                }
            };
            string code = ConfigShareCodec.Encode(payload);
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(code));
            Assert.Contains("\"UNL_HEIGHT\":\"180\"", json);
            string tampered = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                json.Replace("\"UNL_HEIGHT\":\"180\"", "\"UNL_HEIGHT\":\"240\"")));

            ConfigShareFormatException error = Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode(tampered));
            Assert.Contains("校验失败", error.Message);
        }

        [Fact]
        public void Decode_RejectsUnsupportedVersionAndForeignProduct()
        {
            string unsupportedVersion = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "{\"v\":99,\"product\":\"UNCAD Pro\",\"items\":{}}"));
            Assert.Contains("版本不支持", Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode(unsupportedVersion)).Message);

            string foreignProduct = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "{\"v\":1,\"product\":\"Other CAD\",\"items\":{}}"));
            Assert.Contains("仅支持", Assert.Throws<ConfigShareFormatException>(
                () => ConfigShareCodec.Decode(foreignProduct)).Message);
        }

        [Fact]
        public void Decode_ToleratesWhitespaceInsideCopiedCode()
        {
            var payload = new ConfigSharePayload
            {
                CreatedUtc = DateTimeOffset.Now,
                Items = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    [ConfigKeys.UnlHeight] = "180"
                }
            };
            string code = ConfigShareCodec.Encode(payload);
            string wrapped = code.Substring(0, 20) + Environment.NewLine
                + "   " + code.Substring(20);

            Assert.Equal("180", ConfigShareCodec.Decode(wrapped).Items[ConfigKeys.UnlHeight]);
        }

        [Fact]
        public void PublicServiceApi_MatchesTheFixedContract()
        {
            Assert.Equal(typeof(string), typeof(ConfigShareService)
                .GetMethod("Export", Type.EmptyTypes).ReturnType);
            Assert.Equal(typeof(ConfigSharePreview), typeof(ConfigShareService)
                .GetMethod("Preview", new[] { typeof(string) }).ReturnType);
            Assert.Equal(typeof(ConfigShareImportResult), typeof(ConfigShareService)
                .GetMethod("Import", new[] { typeof(string) }).ReturnType);
            Assert.True(typeof(ConfigSharePreview)
                .GetProperty("Changes").PropertyType.IsGenericType);
        }

        [Fact]
        public void OperationLogApi_MatchesTheFixedContract()
        {
            Assert.Equal(typeof(OperationLogScope),
                typeof(OperationLog).GetMethod("Begin",
                    new[] { typeof(string), typeof(string) }).ReturnType);
            Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(OperationLogScope)));
            Assert.Equal(typeof(IReadOnlyList<OperationLogEntry>),
                typeof(OperationLog).GetMethod("ReadRecent",
                    new[] { typeof(int) }).ReturnType);
            Assert.Equal(typeof(string), typeof(OperationLog)
                .GetProperty("Directory").PropertyType);
            Assert.NotNull(typeof(OperationLog).GetMethod("OpenFolder",
                Type.EmptyTypes));
        }
    }
}

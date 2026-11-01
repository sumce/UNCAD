using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace UNCAD.Core.ConfigShare
{
    /// <summary>配置分享码解析/校验失败；消息可直接展示给用户（中文、说明下一步动作）。</summary>
    public sealed class ConfigShareFormatException : Exception
    {
        public ConfigShareFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// 配置分享码载荷：UTF8 JSON 的 Base64。Items 只包含导出方显式设置过的配置键；
    /// CreatedUtc 与 Checksum 用于校验分享码是否被修改或复制不完整。
    /// </summary>
    public sealed class ConfigSharePayload
    {
        public const int CurrentVersion = 1;
        public const string ProductName = "UNCAD Pro";

        public int Version { get; set; } = CurrentVersion;
        public string Product { get; set; } = ProductName;
        /// <summary>导出方插件版本标签，例如 2.10.0；仅用于显示，不参与兼容判断。</summary>
        public string AppVersion { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }
        public IDictionary<string, string> Items { get; set; } =
            new SortedDictionary<string, string>(StringComparer.Ordinal);
        public string Checksum { get; set; } = "";
    }

    /// <summary>
    /// 配置分享码编解码（纯 Core，无配置存储/AutoCAD 依赖，可直接单测）：
    /// JSON {v,product,version,createdUtc,items,checksum} → UTF8 Base64，SHA-256 校验内容完整性。
    /// 键白名单与导入落盘由 Infra.ConfigShareService 负责，本类只保证格式与校验。
    /// </summary>
    public static class ConfigShareCodec
    {
        private const int MaxCodeLength = 1_000_000;
        private const int MaxItems = 2000;
        private const int MaxKeyLength = 128;
        private const int MaxValueLength = 8192;
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>把载荷编码成 Base64 分享码；Items 按键名排序，checksum 由内容重新计算。</summary>
        public static string Encode(ConfigSharePayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            SortedDictionary<string, string> items = NormalizeItems(payload.Items);
            ValidateItems(items);
            payload.Version = ConfigSharePayload.CurrentVersion;
            payload.Product = ConfigSharePayload.ProductName;
            payload.AppVersion = (payload.AppVersion ?? "").Trim();
            payload.Checksum = ComputeChecksum(payload.Version, payload.Product,
                payload.AppVersion, payload.CreatedUtc, items);
            var body = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["v"] = payload.Version,
                ["product"] = payload.Product,
                ["version"] = payload.AppVersion,
                ["createdUtc"] = payload.CreatedUtc.ToString("o",
                    CultureInfo.InvariantCulture),
                ["items"] = items,
                ["checksum"] = payload.Checksum
            };
            return Convert.ToBase64String(Utf8.GetBytes(Json.Serialize(body)));
        }

        /// <summary>
        /// 解析并校验分享码；任何格式/版本/产品/校验失败都抛出带中文说明的
        /// <see cref="ConfigShareFormatException"/>，调用方直接显示 Message 即可。
        /// </summary>
        public static ConfigSharePayload Decode(string code)
        {
            string normalized = NormalizeCode(code);
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(normalized);
            }
            catch (FormatException)
            {
                throw new ConfigShareFormatException(
                    "配置分享码不是有效的 Base64 文本，请重新复制完整分享码。");
            }

            Dictionary<string, object> body;
            try
            {
                body = Json.DeserializeObject(Utf8.GetString(bytes))
                    as Dictionary<string, object>;
            }
            catch (Exception)
            {
                body = null;
            }
            if (body == null)
                throw new ConfigShareFormatException(
                    "配置分享码内容损坏（JSON 无法解析），请重新复制完整分享码。");

            int version = ReadInt(body, "v");
            if (version != ConfigSharePayload.CurrentVersion)
                throw new ConfigShareFormatException("配置分享码版本不支持（v=" + version
                    + "），请升级 UNCAD 后重试。");

            string product = ReadString(body, "product");
            if (!string.Equals(product, ConfigSharePayload.ProductName,
                StringComparison.OrdinalIgnoreCase))
                throw new ConfigShareFormatException("配置分享码来自其它产品（"
                    + (product.Length == 0 ? "未知" : product)
                    + "），仅支持 " + ConfigSharePayload.ProductName + " 配置分享码。");

            string appVersion = ReadString(body, "version");
            string createdText = ReadString(body, "createdUtc");
            if (!DateTimeOffset.TryParse(createdText, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset createdUtc))
                throw new ConfigShareFormatException(
                    "配置分享码缺少或包含非法的创建时间，请重新复制完整分享码。");

            SortedDictionary<string, string> items = ReadItems(body);
            ValidateItems(items);

            string expected = ComputeChecksum(version, product, appVersion, createdUtc, items);
            if (!FixedTimeEquals(ReadString(body, "checksum"), expected))
                throw new ConfigShareFormatException(
                    "配置分享码校验失败：内容可能被修改或复制不完整，请重新获取分享码。");

            return new ConfigSharePayload
            {
                Version = version,
                Product = ConfigSharePayload.ProductName,
                AppVersion = appVersion,
                CreatedUtc = createdUtc,
                Items = items,
                Checksum = expected
            };
        }

        private static string NormalizeCode(string code)
        {
            string trimmed = (code ?? "").Trim();
            if (trimmed.Length == 0)
                throw new ConfigShareFormatException(
                    "配置分享码为空，请先复制完整的分享码。");
            var builder = new StringBuilder(trimmed.Length);
            foreach (char character in trimmed)
                if (!char.IsWhiteSpace(character)) builder.Append(character);
            string normalized = builder.ToString();
            if (normalized.Length > MaxCodeLength)
                throw new ConfigShareFormatException("配置分享码过长，请确认没有粘贴到其它内容。");
            return normalized;
        }

        private static SortedDictionary<string, string> NormalizeItems(
            IDictionary<string, string> items)
        {
            var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (items == null) return normalized;
            foreach (KeyValuePair<string, string> item in items)
            {
                string key = (item.Key ?? "").Trim();
                if (key.Length == 0) continue;
                normalized[key] = item.Value ?? "";
            }
            return normalized;
        }

        private static SortedDictionary<string, string> ReadItems(
            IDictionary<string, object> body)
        {
            var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (!body.TryGetValue("items", out object raw) || raw == null)
                throw new ConfigShareFormatException(
                    "配置分享码缺少配置项，请重新复制完整分享码。");
            var dictionary = raw as Dictionary<string, object>;
            if (dictionary == null)
                throw new ConfigShareFormatException(
                    "配置分享码的配置项格式非法，请重新复制完整分享码。");
            foreach (KeyValuePair<string, object> item in dictionary)
            {
                string key = (item.Key ?? "").Trim();
                if (key.Length == 0) continue;
                items[key] = Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? "";
            }
            return items;
        }

        private static void ValidateItems(IDictionary<string, string> items)
        {
            if (items.Count > MaxItems)
                throw new ConfigShareFormatException("配置分享码包含的配置项过多（超过 "
                    + MaxItems + " 项），已拒绝导入。");
            foreach (KeyValuePair<string, string> item in items)
            {
                if (item.Key.Length > MaxKeyLength)
                    throw new ConfigShareFormatException("配置分享码包含非法配置键："
                        + item.Key.Substring(0, Math.Min(item.Key.Length, 32)) + "…");
                if ((item.Value ?? "").Length > MaxValueLength)
                    throw new ConfigShareFormatException("配置 " + item.Key
                        + " 的值过长（超过 " + MaxValueLength + " 字符），已拒绝导入。");
            }
        }

        private static string ComputeChecksum(int version, string product,
            string appVersion, DateTimeOffset createdUtc, IDictionary<string, string> items)
        {
            var canonicalItems = new SortedDictionary<string, string>(
                items ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            var canonical = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["v"] = version,
                ["product"] = product ?? "",
                ["version"] = appVersion ?? "",
                ["createdUtc"] = createdUtc.ToString("o", CultureInfo.InvariantCulture),
                ["items"] = canonicalItems
            };
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Utf8.GetBytes(Json.Serialize(canonical)));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            byte[] a = Utf8.GetBytes((left ?? "").Trim().ToLowerInvariant());
            byte[] b = Utf8.GetBytes((right ?? "").Trim().ToLowerInvariant());
            if (a.Length != b.Length) return false;
            int difference = 0;
            for (int index = 0; index < a.Length; index++) difference |= a[index] ^ b[index];
            return difference == 0;
        }

        private static string ReadString(IDictionary<string, object> body, string key)
        {
            if (body == null || !body.TryGetValue(key, out object raw) || raw == null)
                return "";
            return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        }

        private static int ReadInt(IDictionary<string, object> body, string key)
        {
            if (body == null || !body.TryGetValue(key, out object raw) || raw == null)
                return 0;
            try { return Convert.ToInt32(raw, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }
}

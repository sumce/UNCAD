using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UNCAD.Core.ConfigShare;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>单个配置键在导入前后的差异；UI 用它展示“将变更”列表。</summary>
    public sealed class ConfigShareChange
    {
        public ConfigShareChange(string key, string currentValue, string sharedValue)
        {
            Key = key ?? "";
            CurrentValue = currentValue ?? "";
            SharedValue = sharedValue ?? "";
        }

        /// <summary>注册表键名，例如 UNL_HEIGHT。</summary>
        public string Key { get; }
        /// <summary>本机当前值；未设置时为空字符串。</summary>
        public string CurrentValue { get; }
        /// <summary>分享码中的值。</summary>
        public string SharedValue { get; }
        /// <summary>本机尚未设置该键（导入后等于新增）。</summary>
        public bool IsNew => CurrentValue.Length == 0;
    }

    /// <summary>分享码预览：来源信息、可应用的配置项、将发生的变更和被跳过的键。</summary>
    public sealed class ConfigSharePreview
    {
        internal ConfigSharePreview(string product, string version, int formatVersion,
            DateTimeOffset createdUtc, IReadOnlyDictionary<string, string> items,
            IReadOnlyList<ConfigShareChange> changes, IReadOnlyList<string> skippedKeys)
        {
            Product = product ?? "";
            Version = version ?? "";
            FormatVersion = formatVersion;
            CreatedUtc = createdUtc;
            Items = items;
            Changes = changes;
            SkippedKeys = skippedKeys;
        }

        /// <summary>导出方产品名，固定为 UNCAD Pro。</summary>
        public string Product { get; }
        /// <summary>导出方插件版本标签，例如 2.10.0。</summary>
        public string Version { get; }
        /// <summary>分享码格式版本（当前 1）。</summary>
        public int FormatVersion { get; }
        /// <summary>分享码创建时间（导出方本地时间带偏移）。</summary>
        public DateTimeOffset CreatedUtc { get; }
        /// <summary>分享码中可应用的配置项（已按白名单过滤）。</summary>
        public IReadOnlyDictionary<string, string> Items { get; }
        /// <summary>与本机当前值不同的配置项，按 Key 排序；相同值不会出现在这里。</summary>
        public IReadOnlyList<ConfigShareChange> Changes { get; }
        /// <summary>被跳过的键：未知键或禁止分享的敏感键。</summary>
        public IReadOnlyList<string> SkippedKeys { get; }
    }

    /// <summary>导入结果：成功写入的键与被跳过的键（未知/敏感/不支持的键）。</summary>
    public sealed class ConfigShareImportResult
    {
        internal ConfigShareImportResult(IReadOnlyList<string> applied,
            IReadOnlyList<string> skipped)
        {
            Applied = applied;
            Skipped = skipped;
        }

        /// <summary>已写入的配置键（按名称排序）。</summary>
        public IReadOnlyList<string> Applied { get; }
        /// <summary>被跳过的键（未知或禁止分享）。</summary>
        public IReadOnlyList<string> Skipped { get; }
        public int AppliedCount => Applied.Count;
        public int SkippedCount => Skipped.Count;
    }

    /// <summary>
    /// 配置分享服务：把本机 U1SET 配置导出为 Base64 分享码，或从分享码预览/导入配置。
    /// 只接受 ConfigKeys 白名单内的键，并排除授权、时钟水位线、更新提示和 UI 记忆等敏感/机器相关键；
    /// 导入通过 Settings.SetMany 一次原子写入（注册表实现自带快照回滚）。
    /// </summary>
    public sealed class ConfigShareService
    {
        private static readonly HashSet<string> ShareableKeys = BuildShareableKeys();
        private static readonly HashSet<string> ExcludedKeys = new HashSet<string>(
            StringComparer.Ordinal)
        {
            ConfigKeys.OnlineAuthorizationCode,
            ConfigKeys.ClockWatermarkUtc,
            ConfigKeys.UpdateNotesSeenVersion,
            ConfigKeys.LicenseLastValidatedUtc,
            ConfigKeys.FillRecentCatalogItems
        };

        /// <summary>生产默认实例：读写注册表当前配置档。</summary>
        public static ConfigShareService Default { get; } =
            new ConfigShareService(RegistrySettingsStore.Instance);

        private readonly ISettingsStore _store;

        internal ConfigShareService(ISettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>导出当前本机配置为 Base64 分享码（只导出显式设置过的白名单键）。</summary>
        public static string Export() => Default.ExportCode();

        /// <summary>解析并预览分享码，不写配置；格式/校验错误抛出中文异常。</summary>
        public static ConfigSharePreview Preview(string code) => Default.PreviewCode(code);

        /// <summary>解析校验后原子导入分享码；未知/敏感键跳过，全部不可用时抛出中文异常。</summary>
        public static ConfigShareImportResult Import(string code) => Default.ImportCode(code);

        internal string ExportCode()
        {
            var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in ShareableKeys.OrderBy(value => value,
                StringComparer.Ordinal))
            {
                // 白名单含 ConfigKeys 全部键；导出必须与导入一样排除授权/时钟/更新提示等敏感键。
                if (!IsShareable(key)) continue;
                string value = _store.Get(key, null);
                if (value != null) items[key] = value;
            }
            var payload = new ConfigSharePayload
            {
                Version = ConfigSharePayload.CurrentVersion,
                Product = ConfigSharePayload.ProductName,
                AppVersion = ProductMetadata.VersionLabel,
                CreatedUtc = DateTimeOffset.Now,
                Items = items
            };
            return ConfigShareCodec.Encode(payload);
        }

        internal ConfigSharePreview PreviewCode(string code)
        {
            ConfigSharePayload payload = ConfigShareCodec.Decode(code);
            var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var changes = new List<ConfigShareChange>();
            var skipped = new List<string>();
            foreach (KeyValuePair<string, string> item in payload.Items)
            {
                if (!IsShareable(item.Key))
                {
                    skipped.Add(item.Key);
                    continue;
                }
                items[item.Key] = item.Value ?? "";
                string current = _store.Get(item.Key, null) ?? "";
                if (!string.Equals(current, item.Value ?? "", StringComparison.Ordinal))
                    changes.Add(new ConfigShareChange(item.Key, current, item.Value));
            }
            return new ConfigSharePreview(payload.Product, payload.AppVersion,
                payload.Version, payload.CreatedUtc, items, changes,
                skipped.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }

        internal ConfigShareImportResult ImportCode(string code)
        {
            ConfigSharePreview preview = PreviewCode(code);
            if (preview.Items.Count == 0)
                throw new InvalidOperationException(
                    "分享码中没有可应用的 UNCAD 配置项（未知键或敏感键已全部跳过）。");

            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> item in preview.Items)
                values[item.Key] = item.Value ?? "";
            try
            {
                // 一次 SetMany：注册表实现会先整体校验，失败时按快照回滚，不会留下半套配置。
                _store.SetMany(values);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "配置导入失败，原配置已尽力恢复：" + ex.Message, ex);
            }
            return new ConfigShareImportResult(values.Keys.ToArray(),
                preview.SkippedKeys);
        }

        /// <summary>键是否在 ConfigKeys 白名单内且不在禁止分享清单中。</summary>
        internal static bool IsShareable(string key)
            => !string.IsNullOrWhiteSpace(key)
                && ShareableKeys.Contains(key.Trim())
                && !ExcludedKeys.Contains(key.Trim());

        private static HashSet<string> BuildShareableKeys()
        {
            // ConfigKeys 是配置键唯一来源；反射白名单让新增键自动可分享，新增敏感键只需加入排除表。
            return new HashSet<string>(typeof(ConfigKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && !field.IsInitOnly
                    && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue())
                .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
        }
    }
}

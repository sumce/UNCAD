using System.Globalization;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>
    /// 配置门面：静态访问入口，委托给 ISettingsStore 实现（注册表，键名兼容旧 LISP 插件）。
    /// </summary>
    public static class Settings
    {
        private static readonly ISettingsStore Store = RegistrySettingsStore.Instance;

        public static string Get(string name, string defaultValue) => Store.Get(name, defaultValue);
        public static void Set(string name, string value) => Store.Set(name, value);

        public static bool GetBool(string name, bool defaultValue)
        {
            string value = Store.Get(name, null);
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            string normalized = value.Trim();
            if (normalized == "1" || normalized.Equals("true",
                System.StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized == "0" || normalized.Equals("false",
                System.StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue;
        }

        public static void SetBool(string name, bool value)
            => Store.Set(name, value ? "1" : "0");

        /// <summary>数值读取：解析失败或 &lt;= 0 时返回默认值（与 LISP 版行为一致）。</summary>
        public static double GetDouble(string name, double defaultValue)
        {
            string s = Store.Get(name, null);
            if (s != null &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) &&
                v > 0)
                return v;
            return defaultValue;
        }
    }
}

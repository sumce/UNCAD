using System;
using System.Reflection;
using Microsoft.Win32;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>
    /// 注册表配置存储（ISettingsStore 实现）。
    /// 键名与旧 AutoLISP 插件 (setenv)/(getenv) 一致，可读取 LISP 版已保存的配置。
    /// AutoCAD 主机 API 通过反射访问：在无 AutoCAD 的测试/工具环境（例如 xUnit 宿主）下
    /// 不会在 JIT 阶段因缺少 accoremgd/AcMgd 抛 FileNotFoundException，而是降级到固定根键。
    /// </summary>
    public sealed class RegistrySettingsStore : ISettingsStore
    {
        public static readonly RegistrySettingsStore Instance = new RegistrySettingsStore();

        private const string VariablesKey = "Variables";
        private const string FallbackRoot = @"Software\Autodesk\AutoCAD";

        private static string ProductRootKey
        {
            get
            {
                try
                {
                    Type hostType = Type.GetType(
                        "Autodesk.AutoCAD.DatabaseServices.HostApplicationServices, AcDbMgd",
                        throwOnError: false);
                    object host = hostType?.GetProperty("Current",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                    string root = Convert.ToString(host?.GetType().GetProperty(
                        "UserRegistryProductRootKey")?.GetValue(host, null) ?? "");
                    return string.IsNullOrWhiteSpace(root) ? FallbackRoot : root;
                }
                catch (Exception ex)
                {
                    Log.Warn("AutoCAD 主机注册表根不可用: " + ex.Message);
                    return FallbackRoot;
                }
            }
        }

        private static string ProfileVariablesPath
        {
            get
            {
                try
                {
                    string profile = CurrentProfileName();
                    if (profile.Length > 0)
                        return ProductRootKey + @"\Profiles\" + profile + @"\" + VariablesKey;
                }
                catch (Exception ex)
                {
                    Log.Warn("配置档名读取失败: " + ex.Message);
                }
                return ProductRootKey + @"\" + VariablesKey;
            }
        }

        private static string CurrentProfileName()
        {
            try
            {
                // 当前配置档名已保存在产品根键；直接读取可避免配置读取期间再次回调 AutoCAD 系统变量。
                using (var key = Registry.CurrentUser.OpenSubKey(ProductRootKey))
                    return Convert.ToString(key?.GetValue("CurProfile")) ?? "";
            }
            catch (Exception ex)
            {
                Log.Warn("当前配置档名读取失败: " + ex.Message);
                return "";
            }
        }

        public string Get(string name, string defaultValue)
        {
            try
            {
                var v = ReadKey(ProfileVariablesPath, name);
                if (v != null) return v;
                v = ReadKey(ProductRootKey + @"\" + VariablesKey, name);
                if (v != null) return v;
            }
            catch (System.Exception ex)
            {
                Log.Warn("配置读取失败: " + name + " → " + ex.Message);
            }
            return defaultValue;
        }

        public void Set(string name, string value)
        {
            WriteKey(ProfileVariablesPath, name, value);
            WriteKey(ProductRootKey + @"\" + VariablesKey, name, value);
        }

        private static string ReadKey(string path, string name)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(path))
                    return key?.GetValue(name) as string;
            }
            catch (System.Exception ex)
            {
                Log.Warn("注册表读取异常: " + path + "\\" + name + " → " + ex.Message);
                return null;
            }
        }

        private static void WriteKey(string path, string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(path))
                    key?.SetValue(name, value, RegistryValueKind.String);
            }
            catch (System.Exception ex)
            {
                Log.Warn("注册表写入异常: " + path + "\\" + name + " → " + ex.Message);
            }
        }
    }
}

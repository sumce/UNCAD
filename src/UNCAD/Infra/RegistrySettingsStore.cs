using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    /// <summary>
    /// 注册表配置存储（ISettingsStore 实现）。
    /// 键名与旧 AutoLISP 插件 (setenv)/(getenv) 一致，可读取 LISP 版已保存的配置。
    /// </summary>
    public sealed class RegistrySettingsStore : ISettingsStore
    {
        public static readonly RegistrySettingsStore Instance = new RegistrySettingsStore();

        private const string VariablesKey = "Variables";

        private static string ProductRootKey
        {
            get
            {
                try { return HostApplicationServices.Current.UserRegistryProductRootKey; }
                catch { return @"Software\Autodesk\AutoCAD"; }
            }
        }

        private static string ProfileVariablesPath
        {
            get
            {
                try
                {
                    var profile = Convert.ToString(Application.GetSystemVariable("CPROFILE"));
                    if (!string.IsNullOrEmpty(profile))
                        return ProductRootKey + @"\Profiles\" + profile + @"\" + VariablesKey;
                }
                catch { }
                return ProductRootKey + @"\" + VariablesKey;
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path))
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path))
                    key?.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
            }
            catch (System.Exception ex)
            {
                Log.Warn("注册表写入异常: " + path + "\\" + name + " → " + ex.Message);
            }
        }
    }
}

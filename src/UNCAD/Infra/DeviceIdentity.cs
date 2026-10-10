using System;
using Microsoft.Win32;

namespace UNCAD.Infra
{
    /// <summary>
    /// 本机设备身份与会话凭证（v2 授权模型：账号 + 单设备在线）。
    ///
    /// 设备 ID 一旦生成永不改变：重装系统或删除注册表会被服务端视为新设备
    /// （这是预期语义，不是缺陷）。
    ///
    /// 存储位置刻意**不复用** <see cref="Settings"/> / ISettingsStore：
    /// 后者写入 AutoCAD 的 per-profile <c>Variables</c> 键，配置档被重置或切换
    /// 时会丢失；设备 ID 一变，服务端就把本机当成新设备并顶掉自己的会话。
    /// 因此这里固定使用与配置档无关的用户级键。
    ///
    /// 维护提示：键名常量请留在本文件，**不要**为了"统一配置键"把它们搬进
    /// <c>ConfigKeys</c> / <see cref="Settings"/>——搬回去就等于把设备身份绑到
    /// AutoCAD 配置档上，会重新引入"用户把自己顶掉"的问题。
    ///   HKCU\SOFTWARE\UNSIAO\UNCAD\UNC_DEVICE_ID
    ///   HKCU\SOFTWARE\UNSIAO\UNCAD\UNC_LICENSE_TOKEN
    /// </summary>
    internal static class DeviceIdentity
    {
        internal const string RegistryPath = @"Software\UNSIAO\UNCAD";
        internal const string DeviceIdValueName = "UNC_DEVICE_ID";
        internal const string TokenValueName = "UNC_LICENSE_TOKEN";
        /// <summary>设备凭证是 base64url(32B)≈43 字符；上限留足余量。</summary>
        private const int MaximumTokenLength = 512;

        private static readonly object Sync = new object();
        private static string _cachedDeviceId;

        /// <summary>设备名：上报给服务端用于"已被 XXX 顶掉"的提示。</summary>
        internal static string DeviceName
        {
            get
            {
                try { return (Environment.MachineName ?? "").Trim(); }
                catch (Exception ex)
                {
                    Log.Warn("读取机器名失败: " + ex.Message);
                    return "";
                }
            }
        }

        /// <summary>读取本机设备 ID；不存在或不合法时生成并持久化。</summary>
        internal static string GetOrCreateDeviceId()
        {
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(_cachedDeviceId)) return _cachedDeviceId;
                string stored = ReadValue(DeviceIdValueName);
                string deviceId = EnsureDeviceId(stored);
                if (!string.Equals(stored, deviceId, StringComparison.Ordinal)
                    && !TryWriteValue(DeviceIdValueName, deviceId))
                    Log.Warn("设备标识无法写入注册表，本次会话继续使用内存中的值。");
                _cachedDeviceId = deviceId;
                return deviceId;
            }
        }

        /// <summary>
        /// 纯逻辑（可单测）：已是合法 GUID 就原样保留——"一旦生成永不改变"；
        /// 缺失或损坏才生成新的。
        /// </summary>
        internal static string EnsureDeviceId(string stored)
        {
            string value = (stored ?? "").Trim();
            return Guid.TryParse(value, out Guid parsed)
                ? parsed.ToString() : Guid.NewGuid().ToString();
        }

        /// <summary>本机登录凭证；未登录或写入失败时为空串（fail-closed）。</summary>
        internal static string GetToken()
        {
            string token = ReadValue(TokenValueName) ?? "";
            return token.Trim().Length > MaximumTokenLength ? "" : token.Trim();
        }

        /// <summary>登录成功后由 ClientLoginForm 写入回调返回的 token。</summary>
        internal static void SaveToken(string token)
        {
            string value = (token ?? "").Trim();
            if (value.Length == 0)
                throw new InvalidOperationException("登录凭证为空，未写入本机。");
            if (value.Length > MaximumTokenLength)
                throw new InvalidOperationException("登录凭证过长，已拒绝写入。");
            if (!TryWriteValue(TokenValueName, value))
                throw new InvalidOperationException(
                    "登录凭证无法写入注册表，请检查当前用户的注册表权限后重试。");
        }

        internal static void ClearToken() => TryWriteValue(TokenValueName, "");

        /// <summary>
        /// 凭证字符合法性：服务端生成的是 base64url，但客户端只做"能安全放进
        /// HTTP 头"的校验，避免服务端换编码后客户端跟着改。
        /// </summary>
        internal static bool IsPlausibleToken(string token)
        {
            string value = (token ?? "").Trim();
            if (value.Length == 0 || value.Length > MaximumTokenLength) return false;
            foreach (char character in value)
                if (character < 0x21 || character > 0x7E) return false;
            return true;
        }

        private static string ReadValue(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    return key?.GetValue(name) as string;
            }
            catch (Exception ex)
            {
                Log.Warn("设备身份读取失败（" + name + "）: " + ex.Message);
                return null;
            }
        }

        private static bool TryWriteValue(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null) return false;
                    key.SetValue(name, value ?? "", RegistryValueKind.String);
                    return string.Equals(key.GetValue(name) as string, value ?? "",
                        StringComparison.Ordinal);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("设备身份写入失败（" + name + "）: " + ex.Message);
                return false;
            }
        }
    }
}

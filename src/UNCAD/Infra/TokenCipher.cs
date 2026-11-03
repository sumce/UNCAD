using System;
using System.Security.Cryptography;
using System.Text;

namespace UNCAD.Infra
{
    /// <summary>
    /// 登录凭证的 DPAPI 加解密（CurrentUser 作用域，D-037 安全加固）。
    ///
    /// 存储格式：dpapi:Base64(ProtectedData)。解不开（换用户解密、篡改、损坏）
    /// 一律返回 null —— fail-closed，用户重新登录即可，绝不把可疑值当凭证用。
    /// 旧版明文凭证不在此处理：DeviceIdentity 负责识别并在读取时顺手升级。
    /// </summary>
    internal static class TokenCipher
    {
        internal const string Prefix = "dpapi:";

        // 附加熵：挡住"同机其他程序也用 DPAPI 就能顺手解开"的边角情况，
        // 不是密钥；改动它等于淘汰所有已存凭证（用户需重新登录一次）。
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UNCAD.DeviceIdentity.v2");

        internal static bool LooksEncrypted(string value)
            => value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

        internal static string Encrypt(string plain)
        {
            if (plain == null) throw new ArgumentNullException(nameof(plain));
            byte[] bytes = Encoding.UTF8.GetBytes(plain);
            byte[] protectedBytes =
                ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }

        /// <summary>解密失败返回 null；入参不是密文格式也返回 null（不承担明文回退）。</summary>
        internal static string TryDecrypt(string stored)
        {
            if (!LooksEncrypted(stored)) return null;
            try
            {
                byte[] protectedBytes =
                    Convert.FromBase64String(stored.Substring(Prefix.Length));
                byte[] bytes = ProtectedData.Unprotect(
                    protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}

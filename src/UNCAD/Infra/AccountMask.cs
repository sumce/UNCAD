using System;

namespace UNCAD.Infra
{
    /// <summary>
    /// 日志脱敏（D-037 安全加固）：账户名（通常是邮箱）进入任何日志前必须先经过这里。
    /// 纯字符串变换，无状态，可单测。
    /// </summary>
    internal static class AccountMask
    {
        /// <summary>zhang.wei@corp.cn → z***@corp.cn；无 @ 保留首字符 + ***；空值原样返回。</summary>
        internal static string Mask(string account)
        {
            string value = (account ?? "").Trim();
            if (value.Length == 0) return "";
            int at = value.IndexOf('@');
            if (at <= 0)
                return value.Length == 1 ? "*" : value[0] + "***";
            string local = value.Substring(0, at);
            return (local.Length == 1 ? "*" : local[0] + "***") + value.Substring(at);
        }
    }
}

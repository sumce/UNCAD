using System;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// 从固定清单「项目特征」中取出 1.名称: 段，即 BOQ 行的型号文本
    /// （如 梯形桥架200Wx100H）。
    ///
    /// 固定清单没有独立的型号字段，型号只存在于项目特征的首段，因此需要统一解析。
    /// 项目特征在内存中是真实换行分段（ListItemReader 已把源文件的 \n 还原）。
    /// </summary>
    public static class BoqFeatureName
    {
        private const string AsciiPrefix = "1.名称:";
        private const string WidePrefix = "1.名称：";

        /// <summary>取出 1.名称: 的值；无该段或为空时返回空字符串。</summary>
        public static string Extract(string feature)
        {
            string text = (feature ?? "").Trim();
            if (text.Length == 0) return "";
            if (text.StartsWith(AsciiPrefix, StringComparison.Ordinal))
                text = text.Substring(AsciiPrefix.Length);
            else if (text.StartsWith(WidePrefix, StringComparison.Ordinal))
                text = text.Substring(WidePrefix.Length);
            else
                return "";
            return Cut(text).Trim();
        }

        private static string Cut(string text)
        {
            int cut = text.Length;
            foreach (char separator in new[] { '\n', '\r' })
            {
                int index = text.IndexOf(separator);
                if (index >= 0 && index < cut) cut = index;
            }
            // CAD 段落符也可能出现在特征文本里。
            int paragraph = text.IndexOf("\\P", StringComparison.Ordinal);
            if (paragraph >= 0 && paragraph < cut) cut = paragraph;
            return text.Substring(0, cut);
        }
    }
}

using System.Text;

namespace UNCAD.Cad
{
    /// <summary>统一配置打印：命令启动时输出当前生效配置，便于排查与验收。</summary>
    public static class ConfigPrinter
    {
        public static void Print(CadContext ctx, string tag, params (string Label, object Value)[] items)
        {
            var sb = new StringBuilder("\n[" + tag + "] 配置: ");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(items[i].Label).Append('=').Append(items[i].Value);
            }
            ctx.Write(sb.ToString());
        }
    }
}

using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.About
{
    /// <summary>
    /// 在线登录入口（v2：账号 + 单设备在线）：WebView2 打开 UNCAD 账号登录页，
    /// 取回本机登录凭证后由 <see cref="OnlineLicenseMonitor.ValidateAndSave"/> 心跳
    /// 校验，active 才算在本机上线。
    ///
    /// 重要：本命令**不得**调用 <see cref="ProductMetadata.EnsureCommandAllowed"/>。
    /// 授权缺失、过期或被其它设备顶掉时，它必须仍然可用，否则用户无法用它重新登录
    /// 抢回本机（服务端要求抢回必须重新走一次登录）。
    /// </summary>
    [Feature("client-login", "在线登录", Commands = CommandIds.ClientLoginFeatureCommands,
        Description = "使用 UNCAD 账号登录，在本机上线并取回授权")]
    public sealed class ClientLoginFeature : CommandBase
    {
        [CommandMethod(CommandIds.ClientLogin)]
        public void UncadLogin() => Run();

        protected override void Execute(CadContext ctx)
        {
            using (var form = new ClientLoginForm())
            {
                DialogResult result =
                    Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                if (result == DialogResult.OK)
                    ctx.Write("\n[UNC_LOGIN] 本机已上线，账号授权验证成功。");
                else
                    ctx.Write("\n[UNC_LOGIN] 未完成登录，授权状态未改变。");
            }
        }
    }
}

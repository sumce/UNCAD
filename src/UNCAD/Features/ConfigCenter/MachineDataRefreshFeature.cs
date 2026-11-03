using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Infra;

namespace UNCAD.Features.ConfigCenter
{
    /// <summary>Starts a non-blocking refresh of the configured machine workbook.</summary>
    [Feature("machine-data-refresh", "刷新机台数据",
        Commands = CommandIds.DataRefreshFeatureCommands,
        Description = "后台刷新机台数据并保留上一次成功快照")]
    public sealed class MachineDataRefreshFeature : CommandBase
    {
        [CommandMethod(CommandIds.DataRefresh)]
        public void RefreshMachineData() => Run();

        protected override void Execute(CadContext ctx)
        {
            string source = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
            // D-041：本地/UNC 文件与 HTTP/HTTPS 网络地址都支持显式刷新；
            // 网络地址只在这里或 U1SET 的“刷新”按钮处下载，命令本身不联网。
            MachineDataRefreshStartStatus status = MachineDataRefreshCoordinator.Default
                .TryStartManualRefresh(source);
            if (status == MachineDataRefreshStartStatus.NoSource)
            {
                ctx.Write("\n[U1DATA] 尚未配置机台数据源，请先在 U1SET 中选择工作簿。");
                return;
            }
            if (status == MachineDataRefreshStartStatus.AlreadyRunning)
            {
                ctx.Write("\n[U1DATA] 机台数据正在后台刷新，无需重复执行。");
                return;
            }

            ctx.Write("\n[U1DATA] 已开始后台刷新"
                + (MachineWorkbookSource.IsRemote(source) ? "（网络数据源，正在下载）" : "")
                + "；完成前 U1F/U1U 等命令继续使用当前 SQLite 快照。");
        }
    }
}

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
            // 2.10.0：远程机台数据源已移除，U1DATA 只刷新本地/共享文件夹工作簿。
            if (MachineWorkbookSource.IsRemote(source))
            {
                ctx.Write("\n[U1DATA] 不再支持网络机台数据源（HTTP/HTTPS 下载已移除）："
                    + source);
                ctx.Write("\n[U1DATA] 请把机台 Excel 保存到本地或共享文件夹，"
                    + "并在 U1SET 中重新选择后再刷新。");
                return;
            }
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

            ctx.Write("\n[U1DATA] 已开始后台刷新；完成前 U1F/U1U 等命令继续使用当前 SQLite 快照。");
        }
    }
}

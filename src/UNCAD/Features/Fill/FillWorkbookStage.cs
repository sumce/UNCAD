using System;
using System.Diagnostics;
using System.Windows.Forms;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段3「加载数据」：加载机台/盘柜 SQLite 快照（用户唯一的外部 Excel 来源）
    /// 和内嵌固定清单，并守住两个致命空态——无机台 ID 与固定清单缺失。
    /// 从 FillFeature 拆出：U1F/U1U 与批量 U1U 的数据加载、计时日志和
    /// 固定清单异常文案必须保持同一份。
    /// </summary>
    internal static class FillWorkbookStage
    {
        /// <summary>
        /// 加载机台 SQLite 快照并做空态校验；任何失败（快照读失败、无机台 ID、
        /// 固定清单缺失）都向用户说明并返回 null，调用方直接结束命令。
        /// </summary>
        internal static FillWorkbookSnapshot Load(CadContext ctx, string path,
            bool updateMode)
        {
            FillWorkbookSnapshot workbook;
            var stageClock = Stopwatch.StartNew();
            try
            {
                workbook = FillWorkbookSnapshot.Load(path);
                stageClock.Stop();
                Log.Info((updateMode ? "U1U" : "U1F") + " 机台SQLite快照加载"
                    + (workbook.MachineCacheHit ? "(已刷新快照)" : "(未知状态)") + ": "
                    + stageClock.ElapsedMilliseconds + " ms");
            }
            catch (System.Exception ex)
            {
                // 3.2.0.6（审计修复）：单框 U1U 也走本加载器，前缀此前硬编码
                // [U1F]——用户跑 U1U 却被提示去查 U1F，误导排查方向。
                ctx.Write("\n[" + (updateMode ? "U1U" : "U1F")
                    + "] 读取机台 SQLite 快照失败: " + ex.Message);
                Log.Error((updateMode ? "U1U" : "U1F")
                    + " read machine SQLite snapshot failed", ex);
                return null;
            }
            ctx.Write("\n[" + (updateMode ? "U1U" : "U1F") + "] 机台数据 "
                + (workbook.MachineCacheHit ? "已使用SQLite快照" : "状态未知")
                + "：" + workbook.MachineSourcePath
                + "；内嵌固定清单 " + workbook.CatalogItemCount + " 项。");

            if (workbook.MachineIds.Count == 0)
            {
                ctx.Write("\n[" + (updateMode ? "U1U" : "U1F") + "] Excel 中无机台ID数据。");
                return null;
            }
            if (workbook.ListItems.Count == 0)
            {
                // 固定清单为空代表插件资源或发布包损坏，不能让用户确认生成无编码项目。
                MessageBox.Show(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle),
                    "插件内没有固定清单数据，已停止本次填充。请重新安装完整版本。",
                    "清单固定清单异常", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return null;
            }
            return workbook;
        }
    }
}

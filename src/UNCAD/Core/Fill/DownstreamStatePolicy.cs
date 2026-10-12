using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// Downstream 块状态对清单的影响。
    ///
    /// 用户规则（2026-10-11 确认）：
    /// - <c>socket box</c> → 在上游规则之外**额外加一行 4.x 插座箱/插座盘**，
    ///   额定电流取 U1SET「下游插座箱额定电流」（默认 20A），受「插座盘」开关控制。
    /// - <c>I-line_Panel</c> → **额外加一行电盘**，额定电流取 U1SET「下游电盘额定电流」
    ///   （默认 1000A），受「电盘」开关控制。
    /// - <c>busbar connector</c> → 只上色，不产生物料。
    /// - **永不添加断路器**：断路器只由上游 U_上游类型=I-Line盘 产生。这条是用户明确
    ///   要求，别因为"看起来该成对出现"而补上（已用测试锁死）。
    /// - 不添加 8.x 插座行：那条由 Device_Build20260716 的状态负责。
    /// - 两行的额定电流一律取自设置，**不从 U_配电信息 提取**：那是盘柜主开关电流，
    ///   现场用 100A 去匹配 4.x 直接整行未匹配（BOQ-CATALOG-MISSING）。
    /// </summary>
    public static class DownstreamStatePolicy
    {
        public static List<TableFillRow> Apply(IEnumerable<TableFillRow> rows,
            BoqCatalogIndex catalog, FillAutoFillOptions autoFill, DownstreamState state,
            double socketAmps, double panelAmps)
        {
            List<TableFillRow> result = (rows ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null).ToList();
            autoFill = autoFill ?? FillAutoFillOptions.Default;

            TableFillRow extra = null;
            if (state == DownstreamState.SocketBox && autoFill.OutletPanel)
                extra = TableFillPlanner.BuildOutletPanelRow(socketAmps, catalog);
            else if (state == DownstreamState.ILinePanel && autoFill.Panel)
                extra = TableFillPlanner.BuildPanelRow(panelAmps, catalog);
            if (extra == null) return result;

            result.Add(extra);
            // 稳定排序：与上游规则产生的行同 SortOrder 时保持原顺序，
            // 新的一套落在设备行之后、插座行之前。
            result = result.OrderBy(row => row.SortOrder).ToList();
            for (int index = 0; index < result.Count; index++)
                result[index].SortOrder = index + 1;
            return result;
        }
    }
}

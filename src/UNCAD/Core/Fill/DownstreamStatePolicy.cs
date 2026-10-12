using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// Downstream 块状态对清单的影响。
    ///
    /// 用户规则（2026-10-11 确认）：
    /// - Downstream 的三态与 upstream 相同，但**只有 <c>socket box</c> 产生物料**；
    ///   <c>I-line_Panel</c> / <c>busbar connector</c> 只用于上色，不产生任何清单项。
    /// - 命中时"在上游规则之外再加一套"：一行 4.x 插座箱/插座盘，受 <c>插座盘</c>
    ///   自动填充开关控制（默认关闭）。
    /// - **不添加 8.x 插座行**：那条由 Device_Build20260716 的状态负责。
    /// - **永不添加断路器**：断路器只由上游 U_上游类型=I-Line盘 产生。这条是用户明确
    ///   要求，别因为"看起来该成对出现"而补上。
    /// </summary>
    public static class DownstreamStatePolicy
    {
        public static List<TableFillRow> ApplySocketBoxPanel(IEnumerable<TableFillRow> rows,
            MachineRow machine, BoqCatalogIndex catalog, FillAutoFillOptions autoFill,
            bool downstreamSocketBox)
        {
            List<TableFillRow> result = (rows ?? Enumerable.Empty<TableFillRow>())
                .Where(row => row != null).ToList();
            if (!downstreamSocketBox) return result;
            autoFill = autoFill ?? FillAutoFillOptions.Default;
            if (!autoFill.OutletPanel) return result;

            TableFillRow panel = TableFillPlanner.BuildOutletPanelRow(machine?.Detail, catalog);
            if (panel == null) return result;
            result.Add(panel);
            // 稳定排序：与上游规则产生的行同 SortOrder 时保持原顺序，
            // 新的一套落在设备行之后、插座行之前。
            result = result.OrderBy(row => row.SortOrder).ToList();
            for (int index = 0; index < result.Count; index++)
                result[index].SortOrder = index + 1;
            return result;
        }
    }
}

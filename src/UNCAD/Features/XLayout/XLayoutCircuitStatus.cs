using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.XLayout
{
    /// <summary>
    /// D-057：XLAYOUT 的台账对照数据——一次命令只读一次 SQLite 快照，机台
    /// 基准（回路清单）、Excel 行序、上游编号、区域三处共用。旧实现
    /// ReadExpectedCircuits / BuildArrangeContext / LoadRegionByMachine 各读一遍。
    /// 快照不可用（未配置/未刷新/读取失败）时 <see cref="Available"/> = false，
    /// 所有机台按“无基准”处理，行为与旧版一致。
    /// </summary>
    internal sealed class XLayoutWorkbookContext
    {
        internal XLayoutWorkbookContext(bool available,
            IDictionary<string, List<MachineRow>> rowsByMachine,
            IReadOnlyList<MachineRow> rowsInOrder,
            IReadOnlyDictionary<string, string> regionByMachine)
        {
            Available = available;
            RowsByMachine = rowsByMachine ?? new Dictionary<string, List<MachineRow>>();
            RowsInOrder = rowsInOrder ?? new List<MachineRow>();
            RegionByMachine = regionByMachine
                ?? new Dictionary<string, string>();
        }

        public bool Available { get; }
        /// <summary>归一化机台号 → 该机台的台账行（保持 Excel 行序）。</summary>
        public IDictionary<string, List<MachineRow>> RowsByMachine { get; }
        /// <summary>全部已读取的台账行（Excel 行序）——排列行序用。</summary>
        public IReadOnlyList<MachineRow> RowsInOrder { get; }
        /// <summary>归一化机台号 → 台账 U_区域（选择窗区域筛选用）。</summary>
        public IReadOnlyDictionary<string, string> RegionByMachine { get; }

        public bool HasBaseline(string machineId)
            => Available && RowsByMachine.ContainsKey(IdentityTextNormalizer.Key(machineId ?? ""));

        /// <summary>该机台的台账回路（带身份键）；无基准返回空表。</summary>
        public List<CircuitSelection> Expected(string machineId)
        {
            if (!HasBaseline(machineId)
                || !RowsByMachine.TryGetValue(IdentityTextNormalizer.Key(machineId ?? ""),
                    out List<MachineRow> rows))
                return new List<CircuitSelection>();
            return rows.Select(CircuitSelection.Of).ToList();
        }

        internal static XLayoutWorkbookContext Load(IEnumerable<string> machineIds)
        {
            try
            {
                string configured = Settings.Get(ConfigKeys.FillExcelPath, "").Trim();
                if (configured.Length == 0) return new XLayoutWorkbookContext(false, null, null, null);
                if (!MachineWorkbookSource.TryGetSnapshot(configured, out _))
                    return new XLayoutWorkbookContext(false, null, null, null);
                var byMachine = new Dictionary<string, List<MachineRow>>(
                    IdentityTextNormalizer.Comparer);
                var region = new Dictionary<string, string>(IdentityTextNormalizer.Comparer);
                var order = new List<MachineRow>();
                foreach (MachineRow row in MachineWorkbookSnapshotStore.Default
                    .ReadRowsForMachines(configured, machineIds ?? new string[0]))
                {
                    string key = IdentityTextNormalizer.Key(row.MachineId ?? "");
                    if (key.Length == 0) continue;
                    if (!byMachine.TryGetValue(key, out List<MachineRow> rows))
                    {
                        rows = new List<MachineRow>();
                        byMachine[key] = rows;
                    }
                    rows.Add(row);
                    order.Add(row);
                    if (!region.ContainsKey(key)) region[key] = (row.Region ?? "").Trim();
                }
                return new XLayoutWorkbookContext(true, byMachine, order, region);
            }
            catch (System.Exception ex)
            {
                Log.Error("[XLAYOUT] SQLite 机台快照读取失败，机台状态/行序/上游/区域暂不可用。", ex);
                return new XLayoutWorkbookContext(false, null, null, null);
            }
        }
    }

    /// <summary>
    /// D-057：XLAYOUT 的回路状态判定——图框 ↔ 台账行走统一配对内核
    /// (<see cref="CircuitMatcher"/>)。旧实现按回路名分组：同机台同名回路
    /// （上游不同）被当成“重复”画黄框，应有个数塌缩，只画一条也判完整。
    /// </summary>
    internal static class XLayoutCircuitStatus
    {
        /// <summary>
        /// 逐机台配对。warningHandles 非空时收下需要画黄框的图框句柄：
        /// 重复图框 + 同名回路待确认的图框。
        /// </summary>
        internal static Dictionary<string, XLayoutMachineSummary> Evaluate(
            IReadOnlyList<XLayoutFrameItem> items, XLayoutWorkbookContext workbook,
            ISet<string> warningHandles)
        {
            var result = new Dictionary<string, XLayoutMachineSummary>(
                IdentityTextNormalizer.Comparer);
            if (items == null) return result;
            foreach (IGrouping<string, XLayoutFrameItem> machine in items
                .Where(item => item != null)
                .GroupBy(item => (item.MachineId ?? "").Trim(),
                    IdentityTextNormalizer.Comparer))
            {
                List<XLayoutFrameItem> machineItems = machine.ToList();
                CircuitMatchResult match = CircuitMatcher.Pair(machine.Key,
                    workbook?.Expected(machine.Key),
                    machineItems.Select(item => item.ToSelection()),
                    workbook != null && workbook.HasBaseline(machine.Key));
                var summary = new XLayoutMachineSummary(machine.Key);
                summary.Apply(match, machineItems.Select(item => new XLayoutFrameRow
                {
                    Handle = item.Handle ?? "",
                    CircuitName = item.DeviceName ?? "",
                    // 图框自己没记上游时，用配对上的台账行的上游（唯一回路名配对）。
                    UpstreamId = (item.UpstreamId ?? "").Length > 0 ? item.UpstreamId
                        : match.Pairs.Where(pair => pair.Drawn.Handle == item.Handle)
                            .Select(pair => pair.Expected.UpstreamId ?? "")
                            .FirstOrDefault() ?? ""
                }));
                result[machine.Key] = summary;
                if (warningHandles == null) continue;
                foreach (CircuitSelection item in match.Unresolved)
                    warningHandles.Add(item.Handle ?? "");
                foreach (CircuitSelection item in match.Duplicates.Select(duplicate => duplicate.Drawn))
                    warningHandles.Add(item.Handle ?? "");
            }
            return result;
        }

        /// <summary>排列对照数据：就绪等级来自给定机台统计；行序/上游来自台账。</summary>
        internal static XLayoutArrangeContext BuildArrangeContext(
            XLayoutWorkbookContext workbook,
            IDictionary<string, XLayoutMachineSummary> summaries)
        {
            var context = new XLayoutArrangeContext();
            if (summaries != null)
                foreach (XLayoutMachineSummary summary in summaries.Values)
                    context.ReadinessRank[summary.MachineId] = summary.ReadinessRank;
            if (workbook == null || !workbook.Available) return context;
            int order = 0;
            foreach (MachineRow row in workbook.RowsInOrder)
                context.AddRow(row, order++);
            return context;
        }
    }
}

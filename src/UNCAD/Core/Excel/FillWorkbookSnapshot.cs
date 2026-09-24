using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Core.Text;

namespace UNCAD.Core.Excel
{
    /// <summary>
    /// U1F / U1U 单次命令使用的 SQLite 快照和按需查询入口。
    /// 这里不持有整个工作簿或全部机台行；刷新时才解析 XLSX，命令只读取
    /// 当前需要的机台数据。
    /// </summary>
    public sealed class FillWorkbookSnapshot
    {
        private readonly MachineWorkbookSnapshotStore _store;
        private readonly string _source;
        private readonly List<string> _machineIds;
        private readonly HashSet<string> _machineIdSet;
        private readonly Dictionary<string, List<MachineRow>> _machineRows =
            new Dictionary<string, List<MachineRow>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _queryGate = new object();

        private FillWorkbookSnapshot(MachineWorkbookSnapshotStore store, string source,
            MachineWorkbookSnapshotInfo snapshot, List<ListItem> listItems)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Catalog = new BoqCatalogIndex(listItems);
            ListItems = Catalog.Items;
            MachineSourcePath = snapshot.SourceDisplay;
            LastRefreshUtc = snapshot.RefreshedUtc;
            SnapshotRowCount = snapshot.RowCount;
            // Machine IDs are a small picker list; circuit rows remain query-on-demand.
            _machineIds = _store.ReadMachineIds(_source);
            _machineIdSet = new HashSet<string>(_machineIds
                .Select(IdentityTextNormalizer.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        public List<string> MachineIds => new List<string>(_machineIds);
        public List<ListItem> ListItems { get; }
        public BoqCatalogIndex Catalog { get; }
        public string MachineSourcePath { get; }
        /// <summary>保留旧调用方的语义名称；现在表示已从 SQLite 快照读取。</summary>
        public bool MachineCacheHit => true;
        public string LastRefreshUtc { get; }
        public int SnapshotRowCount { get; }

        /// <summary>内嵌固定清单的项目数（数据随插件版本固化，不再有外部清单文件）。</summary>
        public int CatalogItemCount => ListItems.Count;

        /// <summary>
        /// 加载上次成功写入的 SQLite 快照并合并内嵌固定清单。
        /// 命令不检查源 Excel 当前内容；远程源由启动刷新或 U1DATA 更新，
        /// 本地源由 U1DATA/U1SET 手动刷新。
        /// </summary>
        public static FillWorkbookSnapshot Load(string machineSource)
            => Load(machineSource, MachineWorkbookSnapshotStore.Default);

        internal static FillWorkbookSnapshot Load(string machineSource,
            MachineWorkbookSnapshotStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (!store.TryGetSnapshot(machineSource,
                out MachineWorkbookSnapshotInfo snapshot))
                throw new InvalidDataException("机台数据尚未刷新到 SQLite。请执行 U1DATA，或在 U1SET 中选择数据源并点击“刷新”。");
            List<ListItem> listItems = ListItemReader.ReadEmbedded();
            return new FillWorkbookSnapshot(store,
                MachineWorkbookSnapshotStore.NormalizeSource(machineSource), snapshot,
                listItems);
        }

        public List<MachineRow> FindRows(string keyword)
        {
            string key = (keyword ?? "").Trim();
            if (key.Length == 0 || !_machineIdSet.Contains(
                IdentityTextNormalizer.Key(key)))
                return _store.FindRows(_source, key);

            lock (_queryGate)
            {
                if (!_machineRows.TryGetValue(key, out List<MachineRow> rows))
                {
                    rows = _store.FindRows(_source, key)
                        .Select(Clone).ToList();
                    _machineRows[key] = rows;
                }
                return rows.Select(Clone).ToList();
            }
        }

        private static MachineRow Clone(MachineRow source)
            => new MachineRow
            {
                Region = source?.Region ?? "",
                MachineId = source?.MachineId ?? "",
                CircuitName = source?.CircuitName ?? "",
                Batch = source?.Batch ?? "",
                Cable = source?.Cable ?? "",
                Fr = source?.Fr ?? "",
                Detail = source?.Detail ?? "",
                Seq = source?.Seq ?? "",
                Dia = source?.Dia ?? "",
                Next = source?.Next ?? "",
                DownstreamAxis = source?.DownstreamAxis ?? "",
                UpstreamAxis = source?.UpstreamAxis ?? "",
                DeviceFloor = source?.DeviceFloor ?? "",
                PanelFloor = source?.PanelFloor ?? "",
                FacilitySwitch = source?.FacilitySwitch ?? ""
            };
    }
}

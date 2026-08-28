using System;
using System.Collections.Generic;
using System.IO;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>UNC_FILL 单次命令使用的只读 Excel 快照和机台索引。</summary>
    public sealed class FillWorkbookSnapshot
    {
        private static readonly StableFileCache<List<MachineRow>> MachineCache =
            new StableFileCache<List<MachineRow>>("机台数据 Excel",
                LoadMachineRows, CloneMachineRows);
        private readonly List<MachineRow> _machineRows;
        private readonly Dictionary<string, List<MachineRow>> _machineIndex;

        private FillWorkbookSnapshot(List<MachineRow> machineRows, List<ListItem> listItems,
            string machineSourcePath, bool machineCacheHit)
        {
            _machineRows = machineRows;
            Catalog = new BoqCatalogIndex(listItems);
            ListItems = Catalog.Items;
            MachineSourcePath = machineSourcePath;
            MachineCacheHit = machineCacheHit;
            MachineIds = ExcelMachineReader.DistinctMachineIds(machineRows);
            _machineIndex = new Dictionary<string, List<MachineRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (MachineRow row in machineRows)
            {
                string id = (row.MachineId ?? "").Trim();
                if (id.Length == 0) continue;
                if (!_machineIndex.TryGetValue(id, out var group))
                {
                    group = new List<MachineRow>();
                    _machineIndex[id] = group;
                }
                group.Add(row);
            }
        }

        public List<string> MachineIds { get; }
        public List<ListItem> ListItems { get; }
        public BoqCatalogIndex Catalog { get; }
        public string MachineSourcePath { get; }
        public bool MachineCacheHit { get; }

        /// <summary>内嵌固定清单的项目数（数据随插件版本固化，不再有外部清单文件）。</summary>
        public int CatalogItemCount => ListItems.Count;

        /// <summary>
        /// 加载唯一的外部 Excel（机台/设备表）并合并内嵌固定清单。
        /// 固定清单来自程序集资源，用户不需要也无法提供清单文件。
        /// </summary>
        public static FillWorkbookSnapshot Load(string machineFilePath)
        {
            string machinePath = Path.GetFullPath(machineFilePath ?? "");

            List<MachineRow> machineRows = MachineCache.Load(
                machinePath, out bool machineCacheHit);
            List<ListItem> listItems = ListItemReader.ReadEmbedded();
            return new FillWorkbookSnapshot(machineRows, listItems, machinePath,
                machineCacheHit);
        }

        private static List<MachineRow> LoadMachineRows(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                var workbook = new XSSFWorkbook(stream);
                try { return ExcelMachineReader.ReadAll(workbook); }
                finally { workbook.Close(); }
            }
        }

        private static List<MachineRow> CloneMachineRows(List<MachineRow> source)
        {
            var result = new List<MachineRow>();
            foreach (MachineRow row in source ?? new List<MachineRow>())
            {
                result.Add(new MachineRow
                {
                    Region = row.Region,
                    MachineId = row.MachineId,
                    CircuitName = row.CircuitName,
                    Cable = row.Cable,
                    Fr = row.Fr,
                    Detail = row.Detail,
                    Seq = row.Seq,
                    Dia = row.Dia,
                    Next = row.Next,
                    DownstreamAxis = row.DownstreamAxis,
                    UpstreamAxis = row.UpstreamAxis
                });
            }
            return result;
        }

        public List<MachineRow> FindRows(string keyword)
        {
            string key = (keyword ?? "").Trim();
            if (_machineIndex.TryGetValue(key, out var exact)) return new List<MachineRow>(exact);
            return ExcelMachineReader.FindRows(_machineRows, key);
        }
    }
}

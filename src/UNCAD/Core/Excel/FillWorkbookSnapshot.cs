using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NPOI.XSSF.UserModel;

namespace UNCAD.Core.Excel
{
    /// <summary>UNC_FILL 单次命令使用的只读 Excel 快照和机台索引。</summary>
    public sealed class FillWorkbookSnapshot
    {
        private readonly List<MachineRow> _machineRows;
        private readonly Dictionary<string, List<MachineRow>> _machineIndex;

        private FillWorkbookSnapshot(List<MachineRow> machineRows, List<ListItem> listItems,
            string machineSourcePath, string catalogSourcePath, bool catalogCacheHit)
        {
            _machineRows = machineRows;
            ListItems = listItems;
            MachineSourcePath = machineSourcePath;
            CatalogSourcePath = catalogSourcePath;
            CatalogCacheHit = catalogCacheHit;
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
        public string MachineSourcePath { get; }
        public string CatalogSourcePath { get; }
        public bool CatalogCacheHit { get; }

        public static FillWorkbookSnapshot Load(string filePath)
            => Load(filePath, null);

        public static FillWorkbookSnapshot Load(string machineFilePath, string catalogFilePath)
        {
            string machinePath = Path.GetFullPath(machineFilePath ?? "");
            string catalogPath = string.IsNullOrWhiteSpace(catalogFilePath)
                ? machinePath
                : Path.GetFullPath(catalogFilePath.Trim());

            List<MachineRow> machineRows = LoadMachineRowsStable(machinePath);
            List<ListItem> listItems = BoqCatalogCache.Load(catalogPath, out bool cacheHit);
            return new FillWorkbookSnapshot(machineRows, listItems, machinePath,
                catalogPath, cacheHit);
        }

        private static List<MachineRow> LoadMachineRowsStable(string path)
        {
            System.Exception lastError = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var before = new FileInfo(path);
                if (!before.Exists) throw new FileNotFoundException("机台数据 Excel 不存在。", path);
                long length = before.Length;
                DateTime writeTime = before.LastWriteTimeUtc;
                try
                {
                    List<MachineRow> rows;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    {
                        var workbook = new XSSFWorkbook(stream);
                        try { rows = ExcelMachineReader.ReadAll(workbook); }
                        finally { workbook.Close(); }
                    }
                    before.Refresh();
                    if (before.Exists && before.Length == length
                        && before.LastWriteTimeUtc == writeTime) return rows;
                }
                catch (InvalidDataException) { throw; }
                catch (System.Exception ex) { lastError = ex; }
                if (attempt < 2) Thread.Sleep(120);
            }
            throw new IOException("机台数据 Excel 正在更新或无法稳定读取，请保存完成后重试。",
                lastError);
        }

        public List<MachineRow> FindRows(string keyword)
        {
            string key = (keyword ?? "").Trim();
            if (_machineIndex.TryGetValue(key, out var exact)) return new List<MachineRow>(exact);
            return ExcelMachineReader.FindRows(_machineRows, key);
        }
    }
}

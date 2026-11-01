using System;
using System.IO;

namespace UNCAD.Core.Excel
{
    internal sealed class MachineWorkbookSourceResult
    {
        public string LocalPath { get; set; }
        public bool Updated { get; set; }
        /// <summary>
        /// 保留字段（历史版本用于"远程下载失败回退到已下载缓存"）。
        /// 2.10.0 起远程机台数据源已移除，本地刷新不会产生回退，恒为 false。
        /// </summary>
        public bool UsedCachedFallback { get; set; }
        public string Warning { get; set; }
        public MachineWorkbookSnapshotInfo Snapshot { get; set; }
    }

    /// <summary>
    /// Refreshes a **local/UNC** workbook for U1SET, U1DATA, or explicit user refresh.
    ///
    /// 2.10.0 起不再下载远程工作簿：机台数据源只能是本地或共享文件夹中的 Excel。
    /// Drawing commands do not call this class; they read the SQLite snapshot.
    /// </summary>
    internal static class MachineWorkbookSource
    {
        /// <summary>
        /// 是否为 HTTP/HTTPS 地址。保留该判断只为在 U1SET/刷新入口给出"网络数据源已移除"
        /// 的明确中文提示——任何刷新路径都不再发起网络请求。
        /// </summary>
        internal static bool IsRemote(string source)
        {
            return Uri.TryCreate((source ?? "").Trim(), UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        internal static bool HasSnapshot(string source)
            => MachineWorkbookSnapshotStore.Default.HasSnapshot(source);

        internal static bool TryGetSnapshot(string source,
            out MachineWorkbookSnapshotInfo snapshot)
            => MachineWorkbookSnapshotStore.Default.TryGetSnapshot(source, out snapshot);

        /// <summary>
        /// Imports a local (or UNC) workbook for an explicit refresh.
        /// A successful refresh always replaces the SQLite snapshot, even when
        /// the XLSX bytes are unchanged.
        /// </summary>
        internal static MachineWorkbookSourceResult Refresh(string source)
            => Refresh(source, MachineWorkbookSnapshotStore.Default);

        internal static MachineWorkbookSourceResult Refresh(string source,
            MachineWorkbookSnapshotStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            string value = (source ?? "").Trim();
            if (value.Length == 0)
                throw new ArgumentException("机台数据源不能为空。", nameof(source));
            if (IsRemote(value))
                throw new InvalidDataException(
                    "不再支持网络机台数据源（HTTP/HTTPS 下载已移除）：" + value
                    + "。请把机台 Excel 保存到本地或共享文件夹，并在 U1SET 中重新选择后再刷新。");
            if (!File.Exists(value))
                throw new FileNotFoundException("机台数据 Excel 不存在：" + value, value);

            MachineWorkbookSnapshotInfo snapshot = store.RefreshFromFile(value, value);
            return new MachineWorkbookSourceResult
            {
                LocalPath = Path.GetFullPath(value),
                Updated = true,
                Snapshot = snapshot
            };
        }

        internal static void ValidateWorkbook(string path)
        {
            try
            {
                ExcelMachineReader.ReadRows(path);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("机台 Excel 无法解析。", ex);
            }
        }
    }
}

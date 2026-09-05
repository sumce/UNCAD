using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;

namespace UNCAD.Core.Excel
{
    internal sealed class MachineWorkbookSourceResult
    {
        public string LocalPath { get; set; }
        public bool Updated { get; set; }
        public bool UsedCachedFallback { get; set; }
        public string Warning { get; set; }
        public MachineWorkbookSnapshotInfo Snapshot { get; set; }
    }

    /// <summary>
    /// Handles the explicit U1SET refresh operation. Commands do not call this
    /// class; they read the SQLite snapshot through MachineWorkbookSnapshotStore.
    /// </summary>
    internal static class MachineWorkbookSource
    {
        private const long MaximumBytes = 100L * 1024L * 1024L;

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
        /// Retained for older diagnostics that need the downloaded XLSX path.
        /// Production command paths must use TryGetSnapshot instead.
        /// </summary>
        internal static bool TryGetCachedPath(string source, out string path)
            => TryGetCachedPath(source, null, out path);

        internal static bool TryGetCachedPath(string source, string cacheDirectory,
            out string path)
        {
            path = null;
            if (!Uri.TryCreate((source ?? "").Trim(), UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;

            path = CachePath(uri, cacheDirectory);
            if (!File.Exists(path))
            {
                path = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Imports a local or remote workbook only when U1SET explicitly invokes
        /// refresh. A successful refresh always replaces the SQLite snapshot,
        /// even when the XLSX bytes are unchanged.
        /// </summary>
        internal static MachineWorkbookSourceResult Refresh(string source)
            => Refresh(source, MachineWorkbookSnapshotStore.Default, null);

        internal static MachineWorkbookSourceResult Refresh(string source,
            MachineWorkbookSnapshotStore store, string cacheDirectory = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            string value = (source ?? "").Trim();
            if (value.Length == 0)
                throw new ArgumentException("机台数据源不能为空。", nameof(source));

            if (!IsRemote(value))
            {
                if (!File.Exists(value))
                    throw new FileNotFoundException("机台数据 Excel 不存在。", value);
                MachineWorkbookSnapshotInfo snapshot =
                    store.RefreshFromFile(value, value);
                return new MachineWorkbookSourceResult
                {
                    LocalPath = Path.GetFullPath(value),
                    Updated = true,
                    Snapshot = snapshot
                };
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                throw new ArgumentException("机台数据源不是有效的 HTTP/HTTPS 地址。",
                    nameof(source));

            string cachePath = CachePath(uri, cacheDirectory);
            string downloadPath = cachePath + ".download-" + Guid.NewGuid().ToString("N");
            try
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                    Download(uri, downloadPath);
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A network outage after a successful prior refresh is non-fatal;
                    // the command can continue using the last explicit SQLite snapshot.
                    if (store.TryGetSnapshot(value,
                        out MachineWorkbookSnapshotInfo previous))
                        return new MachineWorkbookSourceResult
                        {
                            LocalPath = File.Exists(cachePath) ? cachePath : null,
                            UsedCachedFallback = true,
                            Warning = ex.Message,
                            Snapshot = previous
                        };
                    throw new IOException("无法下载网络机台 Excel，且没有可用 SQLite 快照。", ex);
                }

                // Parsing and SQLite replacement happen before the optional XLSX
                // compatibility cache is swapped, so a parse failure leaves the
                // old database snapshot and old downloaded file intact.
                MachineWorkbookSnapshotInfo snapshot =
                    store.RefreshFromFile(value, downloadPath);
                string cacheWarning = null;
                try
                {
                    if (File.Exists(cachePath)) File.Replace(downloadPath, cachePath, null);
                    else File.Move(downloadPath, cachePath);
                }
                catch (Exception ex)
                {
                    // The XLSX file is only a compatibility diagnostic cache;
                    // a successful SQLite refresh must not be reported as failed
                    // merely because that optional copy cannot be replaced.
                    cacheWarning = "下载文件缓存未更新: " + ex.Message;
                }
                return new MachineWorkbookSourceResult
                {
                    LocalPath = File.Exists(cachePath) ? cachePath : null,
                    Updated = true,
                    Snapshot = snapshot,
                    Warning = cacheWarning
                };
            }
            finally
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); }
                catch { }
            }
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
                throw new InvalidDataException("下载的机台 Excel 无法解析。", ex);
            }
        }

        internal static string CacheKey(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            string normalized = new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                return BitConverter.ToString(hash, 0, 12).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void Download(Uri uri, string destination)
        {
            HttpWebRequest request = WebRequest.CreateHttp(uri);
            if (uri.IsLoopback) request.Proxy = null;
            request.Method = "GET";
            request.UserAgent = "UNCAD";
            request.Accept = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,*/*";
            request.AutomaticDecompression = DecompressionMethods.GZip
                | DecompressionMethods.Deflate;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new IOException("网络机台 Excel 返回 HTTP "
                        + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "。");
                if (response.ContentLength > MaximumBytes)
                    throw new InvalidDataException("网络机台 Excel 超过 100 MB 限制。");
                using (Stream input = response.GetResponseStream())
                using (var output = new FileStream(destination, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > MaximumBytes)
                            throw new InvalidDataException("网络机台 Excel 超过 100 MB 限制。");
                        output.Write(buffer, 0, read);
                    }
                }
            }
        }

        private static string CachePath(Uri uri, string cacheDirectory)
        {
            string directory = cacheDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                string root = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
                directory = Path.Combine(root, "UNCAD", "cache");
            }
            return Path.Combine(directory, "machine-" + CacheKey(uri) + ".xlsx");
        }
    }
}

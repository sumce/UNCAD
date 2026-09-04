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
    }

    internal static class MachineWorkbookSource
    {
        private const long MaximumBytes = 100L * 1024L * 1024L;

        internal static bool IsRemote(string source)
        {
            return Uri.TryCreate((source ?? "").Trim(), UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Resolves the cache created by <see cref="Refresh"/> without touching the network.
        /// A cache is only considered available when the file still exists.
        /// </summary>
        internal static bool TryGetCachedPath(string source, out string path)
        {
            path = null;
            if (!Uri.TryCreate((source ?? "").Trim(), UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;

            path = CachePath(uri);
            if (!File.Exists(path))
            {
                path = null;
                return false;
            }
            return true;
        }

        internal static MachineWorkbookSourceResult Refresh(string source)
        {
            if (!Uri.TryCreate((source ?? "").Trim(), UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("机台数据源不是有效的 HTTP/HTTPS 地址。",
                    nameof(source));

            string cachePath = CachePath(uri);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            string downloadPath = cachePath + ".download-" + Guid.NewGuid().ToString("N");
            try
            {
                Download(uri, downloadPath);
                ValidateWorkbook(downloadPath);
                if (File.Exists(cachePath) && SameContent(downloadPath, cachePath))
                    return new MachineWorkbookSourceResult { LocalPath = cachePath };

                if (File.Exists(cachePath)) File.Replace(downloadPath, cachePath, null);
                else File.Move(downloadPath, cachePath);
                return new MachineWorkbookSourceResult
                {
                    LocalPath = cachePath,
                    Updated = true
                };
            }
            catch (InvalidDataException)
            {
                // A downloaded workbook with an unsupported schema must never
                // fall back to an older cached workbook: that would re-enable
                // the very legacy fields the reader is required to reject.
                throw;
            }
            catch (Exception ex)
            {
                if (!File.Exists(cachePath))
                    throw new IOException("无法下载或验证网络机台 Excel，且没有可用缓存。", ex);
                return new MachineWorkbookSourceResult
                {
                    LocalPath = cachePath,
                    UsedCachedFallback = true,
                    Warning = ex.Message
                };
            }
            finally
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); }
                catch { }
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
            var normalized = new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    normalized));
                return BitConverter.ToString(hash, 0, 12).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string CachePath(Uri uri)
        {
            string directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "UNCAD", "cache");
            return Path.Combine(directory, "machine-" + CacheKey(uri) + ".xlsx");
        }

        private static bool SameContent(string left, string right)
        {
            var leftInfo = new FileInfo(left);
            var rightInfo = new FileInfo(right);
            if (leftInfo.Length != rightInfo.Length) return false;
            using (SHA256 sha = SHA256.Create())
            using (FileStream leftStream = File.OpenRead(left))
            using (FileStream rightStream = File.OpenRead(right))
            {
                byte[] leftHash = sha.ComputeHash(leftStream);
                byte[] rightHash = sha.ComputeHash(rightStream);
                for (int index = 0; index < leftHash.Length; index++)
                    if (leftHash[index] != rightHash[index]) return false;
                return true;
            }
        }
    }
}

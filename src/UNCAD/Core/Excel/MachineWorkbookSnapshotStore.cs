using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UNCAD.Core.Text;

namespace UNCAD.Core.Excel
{
    /// <summary>Metadata for the last explicit machine-workbook refresh.</summary>
    internal sealed class MachineWorkbookSnapshotInfo
    {
        public string SourceKey { get; set; }
        public string SourceDisplay { get; set; }
        public string WorkbookHash { get; set; }
        public string RefreshedUtc { get; set; }
        public int RowCount { get; set; }
    }

    /// <summary>
    /// Persists the parsed machine workbook in SQLite.  The XLSX is an input to
    /// an explicit refresh only; command paths query this store and never inspect
    /// the source file or its current timestamp.
    /// </summary>
    internal sealed class MachineWorkbookSnapshotStore
    {
        private const string CreateSourcesSql = @"
CREATE TABLE IF NOT EXISTS machine_sources (
    source_key TEXT NOT NULL PRIMARY KEY,
    source_display TEXT NOT NULL,
    workbook_hash TEXT NOT NULL,
    refreshed_utc TEXT NOT NULL,
    row_count INTEGER NOT NULL
)";

        private const string CreateRowsSql = @"
CREATE TABLE IF NOT EXISTS machine_rows (
    source_key TEXT NOT NULL,
    source_row INTEGER NOT NULL,
    machine_id TEXT NOT NULL,
    circuit_name TEXT NOT NULL,
    region TEXT NOT NULL,
    cable TEXT NOT NULL,
    fr TEXT NOT NULL,
    detail TEXT NOT NULL,
    batch TEXT NOT NULL,
    seq TEXT NOT NULL,
    dia TEXT NOT NULL,
    upstream_type TEXT NOT NULL,
    downstream_axis TEXT NOT NULL,
    upstream_axis TEXT NOT NULL,
    device_floor TEXT NOT NULL,
    panel_floor TEXT NOT NULL,
    facility_switch TEXT NOT NULL,
    PRIMARY KEY (source_key, source_row),
    FOREIGN KEY (source_key) REFERENCES machine_sources(source_key)
)";

        private const string CreateIdentityIndexSql = @"
CREATE INDEX IF NOT EXISTS idx_machine_rows_identity
ON machine_rows(source_key, machine_id COLLATE NOCASE, circuit_name)";

        private const string SelectColumns =
            "source_row, machine_id, circuit_name, region, cable, fr, detail, batch, seq, dia, "
            + "upstream_type, downstream_axis, upstream_axis, device_floor, panel_floor, facility_switch";

        private readonly string _databasePath;
        private readonly object _writeGate = new object();
        private readonly object _schemaGate = new object();
        private bool _schemaReady;

        // SQLite limits a prepared statement to a finite number of bound
        // parameters (999 on the Windows build shipped with AutoCAD 2022).
        // Keep room for the source key so large XSTS/XLAYOUT selections are
        // split into predictable, read-only batches.
        private const int MaxMachineIdsPerQuery = 900;

        internal static MachineWorkbookSnapshotStore Default { get; } =
            new MachineWorkbookSnapshotStore(DefaultPath());

        internal MachineWorkbookSnapshotStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("SQLite 数据库路径不能为空。", nameof(databasePath));
            _databasePath = Path.GetFullPath(databasePath);
        }

        internal string DatabasePath => _databasePath;

        internal MachineWorkbookSnapshotInfo RefreshFromFile(string source,
            string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath))
                throw new ArgumentException("机台数据 Excel 路径不能为空。", nameof(workbookPath));
            if (!File.Exists(workbookPath))
                throw new FileNotFoundException("机台数据 Excel 不存在。", workbookPath);

            // Parse completely before opening the database.  Hashing both sides
            // prevents a workbook being saved halfway through the parse from
            // producing a snapshot whose rows and metadata describe different
            // versions. An invalid or unstable workbook cannot remove the last
            // valid snapshot.
            string beforeHash = HashFile(workbookPath);
            List<MachineRow> rows = ExcelMachineReader.ReadRows(workbookPath);
            string afterHash = HashFile(workbookPath);
            if (!string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("机台数据 Excel 在读取期间发生变化，请保存完成后重新点击“刷新”。");
            return Replace(source, rows, afterHash);
        }

        internal MachineWorkbookSnapshotInfo RefreshRows(string source,
            IEnumerable<MachineRow> rows, string workbookHash = "")
        {
            List<MachineRow> snapshot = (rows ?? Enumerable.Empty<MachineRow>())
                .Where(row => row != null).Select(Clone).ToList();
            return Replace(source, snapshot, workbookHash ?? "");
        }

        internal bool HasSnapshot(string source)
            => TryGetSnapshot(source, out _);

        internal bool TryGetSnapshot(string source,
            out MachineWorkbookSnapshotInfo info)
        {
            info = null;
            if (!File.Exists(_databasePath)) return false;
            string key = SourceKey(source);
            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                using (WindowsSqliteStatement statement = connection.Prepare(
                    "SELECT source_key, source_display, workbook_hash, refreshed_utc, row_count "
                    + "FROM machine_sources WHERE source_key = ?"))
                {
                    statement.Bind(1, key);
                    int resultCode = statement.Step();
                    if (resultCode == WindowsSqliteConnection.SqliteDone)
                        return false;
                    statement.CheckStep(resultCode);
                    info = ReadInfo(statement);
                    return true;
                }
            }
        }

        internal List<string> ReadMachineIds(string source)
        {
            string key = SourceKey(source);
            var result = new List<string>();
            if (!File.Exists(_databasePath)) return result;
            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                using (WindowsSqliteStatement statement = connection.Prepare(
                    "SELECT DISTINCT machine_id COLLATE NOCASE FROM machine_rows WHERE source_key = ? "
                    + "ORDER BY machine_id COLLATE NOCASE"))
                {
                    statement.Bind(1, key);
                    while (true)
                    {
                        int resultCode = statement.Step();
                        if (resultCode == WindowsSqliteConnection.SqliteDone) break;
                        statement.CheckStep(resultCode);
                        string value = statement.Text(0).Trim();
                        if (value.Length > 0) result.Add(value);
                    }
                }
            }
            return result
                .GroupBy(IdentityTextNormalizer.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal List<MachineRow> FindRows(string source, string keyword)
        {
            string key = SourceKey(source);
            string value = (keyword ?? "").Trim();
            if (!File.Exists(_databasePath) || value.Length == 0)
                return new List<MachineRow>();

            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                List<MachineRow> exact = QueryRows(connection,
                    "SELECT " + SelectColumns + " FROM machine_rows "
                    + "WHERE source_key = ? AND machine_id COLLATE NOCASE = ? "
                    + "ORDER BY source_row",
                    key, value);
                if (exact.Count > 0) return exact;

                // SQLite's NOCASE collation does not ignore whitespace inside an
                // identity. Read this source only on an exact miss, then apply the
                // shared Core key so M Q-01 and MQ-01 address the same rows.
                // ponytail: keep the fallback scan on rare normalized-key misses;
                // add a persisted compact identity column only if snapshots grow large.
                List<MachineRow> normalized = QueryRows(connection,
                    "SELECT " + SelectColumns + " FROM machine_rows "
                    + "WHERE source_key = ? ORDER BY source_row", key)
                    .Where(row => IdentityTextNormalizer.Equals(row.MachineId, value))
                    .ToList();
                if (normalized.Count > 0) return normalized;

                // Preserve the old picker fallback: when no machine ID matches,
                // search the circuit-name column.  Escape wildcard characters so
                // user input is a literal substring, not a SQL pattern.
                string pattern = "%" + EscapeLike(value) + "%";
                List<MachineRow> byCircuit = QueryRows(connection,
                    "SELECT " + SelectColumns + " FROM machine_rows "
                    + "WHERE source_key = ? AND circuit_name LIKE ? ESCAPE '\\' "
                    + "ORDER BY source_row", key, pattern);
                if (byCircuit.Count > 0) return byCircuit;
                return QueryRows(connection,
                    "SELECT " + SelectColumns + " FROM machine_rows "
                    + "WHERE source_key = ? ORDER BY source_row", key)
                    .Where(row => IdentityTextNormalizer.Contains(row.CircuitName, value))
                    .ToList();
            }
        }

        internal List<MachineRow> ReadRowsForMachines(string source,
            IEnumerable<string> machineIds)
        {
            string key = SourceKey(source);
            List<string> ids = (machineIds ?? Enumerable.Empty<string>())
                .Select(id => (id ?? "").Trim())
                .Where(id => id.Length > 0)
                .GroupBy(IdentityTextNormalizer.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
            if (!File.Exists(_databasePath) || ids.Count == 0)
                return new List<MachineRow>();

            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                var result = new List<MachineRow>();
                for (int offset = 0; offset < ids.Count; offset += MaxMachineIdsPerQuery)
                {
                    List<string> batch = ids.Skip(offset).Take(MaxMachineIdsPerQuery).ToList();
                    string placeholders = string.Join(",", batch.Select(_ => "?"));
                    using (WindowsSqliteStatement statement = connection.Prepare(
                        "SELECT " + SelectColumns + " FROM machine_rows WHERE source_key = ? "
                        + "AND machine_id COLLATE NOCASE IN (" + placeholders
                        + ") ORDER BY source_row"))
                    {
                        statement.Bind(1, key);
                        for (int index = 0; index < batch.Count; index++)
                            statement.Bind(index + 2, batch[index]);
                        result.AddRange(ReadRows(statement));
                    }
                }
                HashSet<string> found = new HashSet<string>(
                    result.Select(row => IdentityTextNormalizer.Key(row.MachineId)),
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> missing = new HashSet<string>(
                    ids.Select(IdentityTextNormalizer.Key), StringComparer.OrdinalIgnoreCase);
                missing.ExceptWith(found);
                if (missing.Count > 0)
                {
                    result.AddRange(QueryRows(connection,
                        "SELECT " + SelectColumns + " FROM machine_rows "
                        + "WHERE source_key = ? ORDER BY source_row", key)
                        .Where(row => missing.Contains(
                            IdentityTextNormalizer.Key(row.MachineId))));
                }
                return result.OrderBy(row => row.SourceRow).ToList();
            }
        }

        internal List<MachineRow> ReadAllRows(string source)
        {
            string key = SourceKey(source);
            if (!File.Exists(_databasePath)) return new List<MachineRow>();
            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                using (WindowsSqliteStatement statement = connection.Prepare(
                    "SELECT " + SelectColumns + " FROM machine_rows "
                    + "WHERE source_key = ? ORDER BY source_row"))
                {
                    statement.Bind(1, key);
                    return ReadRows(statement);
                }
            }
        }

        internal void Remove(string source)
        {
            if (!File.Exists(_databasePath)) return;
            string key = SourceKey(source);
            lock (_writeGate)
            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                connection.Execute("BEGIN IMMEDIATE");
                try
                {
                    using (WindowsSqliteStatement statement = connection.Prepare(
                        "DELETE FROM machine_rows WHERE source_key = ?"))
                    {
                        statement.Bind(1, key);
                        StepDone(statement);
                    }
                    using (WindowsSqliteStatement statement = connection.Prepare(
                        "DELETE FROM machine_sources WHERE source_key = ?"))
                    {
                        statement.Bind(1, key);
                        StepDone(statement);
                    }
                    connection.Execute("COMMIT");
                }
                catch
                {
                    TryRollback(connection);
                    throw;
                }
            }
        }

        internal static string NormalizeSource(string source)
        {
            string value = (source ?? "").Trim();
            if (value.Length == 0)
                throw new ArgumentException("机台数据源不能为空。", nameof(source));
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
            }
            string path = Path.GetFullPath(value).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path.ToUpperInvariant();
        }

        internal static string SourceKey(string source)
        {
            string normalized = NormalizeSource(source);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(normalized);
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", "").ToLowerInvariant();
            }
        }

        private MachineWorkbookSnapshotInfo Replace(string source,
            IList<MachineRow> rows, string workbookHash)
        {
            string normalized = NormalizeSource(source);
            string key = SourceKey(normalized);
            string refreshed = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string display = (source ?? "").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath));

            lock (_writeGate)
            using (WindowsSqliteConnection connection = Open())
            {
                EnsureSchema(connection);
                connection.Execute("BEGIN IMMEDIATE");
                try
                {
                    using (WindowsSqliteStatement deleteRows = connection.Prepare(
                        "DELETE FROM machine_rows WHERE source_key = ?"))
                    {
                        deleteRows.Bind(1, key);
                        StepDone(deleteRows);
                    }
                    using (WindowsSqliteStatement deleteSource = connection.Prepare(
                        "DELETE FROM machine_sources WHERE source_key = ?"))
                    {
                        deleteSource.Bind(1, key);
                        StepDone(deleteSource);
                    }
                    using (WindowsSqliteStatement insertSource = connection.Prepare(
                        "INSERT INTO machine_sources(source_key, source_display, workbook_hash, "
                        + "refreshed_utc, row_count) VALUES (?, ?, ?, ?, ?)"))
                    {
                        insertSource.Bind(1, key);
                        insertSource.Bind(2, display);
                        insertSource.Bind(3, workbookHash ?? "");
                        insertSource.Bind(4, refreshed);
                        insertSource.Bind(5, rows?.Count ?? 0);
                        StepDone(insertSource);
                    }

                    using (WindowsSqliteStatement insert = connection.Prepare(
                        "INSERT INTO machine_rows(source_key, source_row, machine_id, "
                        + "circuit_name, region, cable, fr, detail, batch, seq, dia, upstream_type, "
                        + "downstream_axis, upstream_axis, device_floor, panel_floor, facility_switch) "
                        + "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)"))
                    {
                        for (int index = 0; index < (rows?.Count ?? 0); index++)
                        {
                            MachineRow row = rows[index] ?? new MachineRow();
                            insert.Bind(1, key);
                            insert.Bind(2, index);
                            insert.Bind(3, row.MachineId);
                            insert.Bind(4, row.CircuitName);
                            insert.Bind(5, row.Region);
                            insert.Bind(6, row.Cable);
                            insert.Bind(7, row.Fr);
                            insert.Bind(8, row.Detail);
                            insert.Bind(9, row.Batch);
                            insert.Bind(10, row.Seq);
                            insert.Bind(11, row.Dia);
                            insert.Bind(12, row.Next);
                            insert.Bind(13, row.DownstreamAxis);
                            insert.Bind(14, row.UpstreamAxis);
                            insert.Bind(15, row.DeviceFloor);
                            insert.Bind(16, row.PanelFloor);
                            insert.Bind(17, row.FacilitySwitch);
                            StepDone(insert);
                            insert.Reset();
                        }
                    }
                    connection.Execute("COMMIT");
                }
                catch
                {
                    TryRollback(connection);
                    throw;
                }
            }

            return new MachineWorkbookSnapshotInfo
            {
                SourceKey = key,
                SourceDisplay = display,
                WorkbookHash = workbookHash ?? "",
                RefreshedUtc = refreshed,
                RowCount = rows?.Count ?? 0
            };
        }

        private WindowsSqliteConnection Open()
        {
            try { return new WindowsSqliteConnection(_databasePath); }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                throw new IOException("无法打开机台数据 SQLite 缓存。", ex);
            }
        }

        private void EnsureSchema(WindowsSqliteConnection connection)
        {
            if (_schemaReady) return;
            lock (_schemaGate)
            {
                if (_schemaReady) return;
                connection.Execute(CreateSourcesSql);
                connection.Execute(CreateRowsSql);
                // Existing users may have a SQLite snapshot from an earlier
                // release. CREATE TABLE IF NOT EXISTS does not alter that table,
                // so add the new optional metadata column in place and preserve
                // all previously refreshed rows.
                EnsureColumn(connection, "machine_rows", "batch",
                    "TEXT NOT NULL DEFAULT ''");
                connection.Execute(CreateIdentityIndexSql);
                _schemaReady = true;
            }
        }

        private static void EnsureColumn(WindowsSqliteConnection connection,
            string table, string column, string definition)
        {
            bool found = false;
            using (WindowsSqliteStatement statement = connection.Prepare(
                "PRAGMA table_info(" + table + ")"))
            {
                while (true)
                {
                    int result = statement.Step();
                    if (result == WindowsSqliteConnection.SqliteDone) break;
                    statement.CheckStep(result);
                    if (string.Equals(statement.Text(1), column,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
                connection.Execute("ALTER TABLE " + table + " ADD COLUMN "
                    + column + " " + definition);
        }

        private static List<MachineRow> QueryRows(WindowsSqliteConnection connection,
            string sql, params string[] values)
        {
            using (WindowsSqliteStatement statement = connection.Prepare(sql))
            {
                for (int index = 0; index < values.Length; index++)
                    statement.Bind(index + 1, values[index]);
                return ReadRows(statement);
            }
        }

        private static List<MachineRow> ReadRows(WindowsSqliteStatement statement)
        {
            var result = new List<MachineRow>();
            while (true)
            {
                int resultCode = statement.Step();
                if (resultCode == WindowsSqliteConnection.SqliteDone) break;
                statement.CheckStep(resultCode);
                result.Add(new MachineRow
                {
                    SourceRow = (int)statement.Int64(0),
                    MachineId = statement.Text(1),
                    CircuitName = statement.Text(2),
                    Region = statement.Text(3),
                    Cable = statement.Text(4),
                    Fr = statement.Text(5),
                    Detail = statement.Text(6),
                    Batch = statement.Text(7),
                    Seq = statement.Text(8),
                    Dia = statement.Text(9),
                    Next = statement.Text(10),
                    DownstreamAxis = statement.Text(11),
                    UpstreamAxis = statement.Text(12),
                    DeviceFloor = statement.Text(13),
                    PanelFloor = statement.Text(14),
                    FacilitySwitch = statement.Text(15)
                });
            }
            return result;
        }

        private static MachineWorkbookSnapshotInfo ReadInfo(WindowsSqliteStatement statement)
            => new MachineWorkbookSnapshotInfo
            {
                SourceKey = statement.Text(0),
                SourceDisplay = statement.Text(1),
                WorkbookHash = statement.Text(2),
                RefreshedUtc = statement.Text(3),
                RowCount = (int)statement.Int64(4)
            };

        private static void StepDone(WindowsSqliteStatement statement)
        {
            int result = statement.Step();
            if (result == WindowsSqliteConnection.SqliteDone) return;
            statement.CheckStep(result);
            throw new InvalidOperationException("SQLite 写入未完成。");
        }

        private static void TryRollback(WindowsSqliteConnection connection)
        {
            try { connection.Execute("ROLLBACK"); }
            catch { }
        }

        private static string EscapeLike(string value)
            => (value ?? "").Replace("\\", "\\\\")
                .Replace("%", "\\%").Replace("_", "\\_");

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", "").ToLowerInvariant();
            }
        }

        private static MachineRow Clone(MachineRow source)
            => new MachineRow
            {
                SourceRow = source?.SourceRow ?? 0,
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

        private static string DefaultPath()
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();
            return Path.Combine(root, "UNCAD", "cache", "machine-data.db");
        }
    }
}

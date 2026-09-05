using System;
using System.Runtime.InteropServices;

namespace UNCAD.Core.Excel
{
    /// <summary>
    /// Minimal ADO.NET-shaped wrapper over the Windows SQLite C API.
    /// AutoCAD 2022 runs on supported Windows versions that ship winsqlite3.dll,
    /// so the bundle does not need another native SQLite payload.
    /// </summary>
    internal sealed class WindowsSqliteConnection : IDisposable
    {
        internal const int SqliteRow = 100;
        internal const int SqliteDone = 101;
        private const int SqliteOk = 0;
        private IntPtr _handle;

        public WindowsSqliteConnection(string path)
        {
            try
            {
                // open16 is the Unicode entry point.  sqlite3_open_v2 expects
                // UTF-8 and would corrupt non-ASCII Windows paths when called
                // through the .NET Unicode marshaler.
                int result = Native.sqlite3_open16(path, out _handle);
                if (result != SqliteOk)
                {
                    string message = ErrorMessage(result);
                    Close();
                    throw new InvalidOperationException("SQLite 打开数据库失败: " + message);
                }
                Native.sqlite3_busy_timeout(_handle, 5000);
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "当前 Windows 缺少 winsqlite3.dll，无法使用机台数据缓存。", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "当前 Windows 的 winsqlite3.dll 不支持所需 SQLite 接口。", ex);
            }
        }

        public WindowsSqliteStatement Prepare(string sql)
        {
            if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(WindowsSqliteConnection));
            IntPtr statement;
            int result = Native.sqlite3_prepare16_v2(_handle, sql ?? "", -1,
                out statement, IntPtr.Zero);
            Check(result);
            return new WindowsSqliteStatement(this, statement);
        }

        public void Execute(string sql)
        {
            using (WindowsSqliteStatement statement = Prepare(sql))
            {
                while (true)
                {
                    int result = statement.Step();
                    if (result == SqliteDone) return;
                    if (result != SqliteRow) Check(result);
                }
            }
        }

        internal string ErrorMessage(int result)
        {
            if (_handle == IntPtr.Zero) return "SQLite 错误码 " + result;
            IntPtr value = Native.sqlite3_errmsg16(_handle);
            string text = value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(value);
            return string.IsNullOrWhiteSpace(text)
                ? "SQLite 错误码 " + result : text;
        }

        internal void Check(int result)
        {
            if (result != SqliteOk)
                throw new InvalidOperationException("SQLite 操作失败: " + ErrorMessage(result));
        }

        internal void FinalizeStatement(IntPtr statement)
        {
            if (statement == IntPtr.Zero) return;
            int result = Native.sqlite3_finalize(statement);
            if (result != SqliteOk)
                throw new InvalidOperationException("SQLite 关闭语句失败: " + ErrorMessage(result));
        }

        private void Close()
        {
            if (_handle == IntPtr.Zero) return;
            Native.sqlite3_close_v2(_handle);
            _handle = IntPtr.Zero;
        }

        public void Dispose() => Close();

        private static class Native
        {
            private const string Library = "winsqlite3.dll";

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
                CharSet = CharSet.Unicode)]
            internal static extern int sqlite3_open16(string filename,
                out IntPtr database);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
                CharSet = CharSet.Unicode)]
            internal static extern int sqlite3_prepare16_v2(IntPtr database,
                string sql, int byteCount, out IntPtr statement, IntPtr tail);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_busy_timeout(IntPtr database,
                int milliseconds);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
                CharSet = CharSet.Unicode)]
            internal static extern IntPtr sqlite3_errmsg16(IntPtr database);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close_v2(IntPtr database);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_finalize(IntPtr statement);
        }
    }

    internal sealed class WindowsSqliteStatement : IDisposable
    {
        private static readonly IntPtr SqliteTransient = new IntPtr(-1);
        private readonly WindowsSqliteConnection _connection;
        private IntPtr _handle;

        internal WindowsSqliteStatement(WindowsSqliteConnection connection,
            IntPtr handle)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _handle = handle;
        }

        public int Step()
        {
            if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(WindowsSqliteStatement));
            return Native.sqlite3_step(_handle);
        }

        internal void CheckStep(int result)
        {
            if (result != WindowsSqliteConnection.SqliteRow
                && result != WindowsSqliteConnection.SqliteDone)
                throw new InvalidOperationException("SQLite 查询失败: "
                    + _connection.ErrorMessage(result));
        }

        public void Bind(int index, string value)
        {
            int result = Native.sqlite3_bind_text16(_handle, index, value ?? "", -1,
                SqliteTransient);
            _connection.Check(result);
        }

        public void Bind(int index, int value)
        {
            int result = Native.sqlite3_bind_int(_handle, index, value);
            _connection.Check(result);
        }

        public string Text(int index)
        {
            if (Native.sqlite3_column_type(_handle, index) == 5) return "";
            IntPtr value = Native.sqlite3_column_text16(_handle, index);
            return value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(value) ?? "";
        }

        public long Int64(int index) => Native.sqlite3_column_int64(_handle, index);

        public void Reset()
        {
            int result = Native.sqlite3_reset(_handle);
            _connection.Check(result);
            result = Native.sqlite3_clear_bindings(_handle);
            _connection.Check(result);
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            _connection.FinalizeStatement(handle);
        }

        private static class Native
        {
            private const string Library = "winsqlite3.dll";

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_step(IntPtr statement);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
                CharSet = CharSet.Unicode)]
            internal static extern int sqlite3_bind_text16(IntPtr statement,
                int index, string value, int byteCount, IntPtr destructor);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_int(IntPtr statement,
                int index, int value);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_type(IntPtr statement, int index);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_text16(IntPtr statement,
                int index);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern long sqlite3_column_int64(IntPtr statement,
                int index);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_reset(IntPtr statement);

            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_clear_bindings(IntPtr statement);
        }
    }
}

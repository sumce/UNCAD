using System;
using System.IO;

namespace UNCAD.Core.IO
{
    /// <summary>
    /// Shared atomic replacement for a file that has already been written to a
    /// temporary path.  The helper owns only the mechanical part: unique backup,
    /// File.Replace, move fallback, restore on failure.  Callers keep their own
    /// fingerprint checks and exception types.
    /// </summary>
    public static class AtomicFileWriter
    {
        /// <summary>
        /// Replaces <paramref name="target"/> with <paramref name="temporary"/>.
        /// If the target disappeared, <paramref name="onTargetMissing"/> may throw
        /// the caller's conflict exception; otherwise the temporary is moved into
        /// place.  <paramref name="verifyBeforeFallback"/> runs only after
        /// File.Replace reports an unsupported/locked filesystem, immediately
        /// before the non-atomic move fallback.
        /// </summary>
        public static void Replace(string target, string temporary,
            Action onTargetMissing = null, Action verifyBeforeFallback = null)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("目标文件路径为空。", nameof(target));
            if (string.IsNullOrWhiteSpace(temporary))
                throw new ArgumentException("临时文件路径为空。", nameof(temporary));

            if (!File.Exists(target))
            {
                onTargetMissing?.Invoke();
                File.Move(temporary, target);
                return;
            }

            // Unique backup: never overwrite a user's own .bak, and never make the
            // move fallback fail just because a fixed backup name already exists.
            string backup = target + ".uncad-replace-"
                + Guid.NewGuid().ToString("N") + ".bak";
            try
            {
                File.Replace(temporary, target, backup, true);
                // Best-effort cleanup: antivirus/indexers may briefly hold the
                // backup.  A successful replace must not be turned into a batch
                // rollback just because the backup could not be deleted.
                FileCleanup.TryDelete(backup);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }

            verifyBeforeFallback?.Invoke();

            bool movedOriginal = false;
            try
            {
                File.Move(target, backup);
                movedOriginal = true;
                File.Move(temporary, target);
                FileCleanup.TryDelete(backup);
            }
            catch
            {
                if (movedOriginal && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target);
                throw;
            }
        }
    }
}

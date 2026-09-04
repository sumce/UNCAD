using System.IO;

namespace UNCAD.Core.IO
{
    /// <summary>Best-effort file cleanup shared by the workbook writers.</summary>
    public static class FileCleanup
    {
        /// <summary>
        /// Deletes a file if it exists, swallowing failure by design: callers use
        /// this for temporary/stale artifacts whose removal must never break the
        /// main output path.
        /// </summary>
        public static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}

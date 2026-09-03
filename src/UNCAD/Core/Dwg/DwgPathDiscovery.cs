using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UNCAD.Core.Dwg
{
    public sealed class DwgPathDiscoveryResult
    {
        public List<string> Files { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    public static class DwgPathDiscovery
    {
        public static DwgPathDiscoveryResult Find(IEnumerable<string> paths)
        {
            var result = new DwgPathDiscoveryResult();
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            foreach (string raw in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string path;
                try { path = Path.GetFullPath(raw); }
                catch (Exception ex)
                {
                    result.Errors.Add(raw + ": " + ex.Message);
                    continue;
                }
                if (Directory.Exists(path)) pending.Push(path);
                else if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".dwg",
                    StringComparison.OrdinalIgnoreCase)) files.Add(path);
            }

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                if (!visited.Add(directory)) continue;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.dwg",
                        SearchOption.TopDirectoryOnly)) files.Add(Path.GetFullPath(file));
                }
                catch (Exception ex)
                {
                    result.Errors.Add(directory + ": " + ex.Message);
                }

                try
                {
                    foreach (string child in Directory.EnumerateDirectories(directory))
                    {
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                                continue;
                            pending.Push(Path.GetFullPath(child));
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(child + ": " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(directory + ": " + ex.Message);
                }
            }

            result.Files.AddRange(files.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase));
            return result;
        }
    }
}

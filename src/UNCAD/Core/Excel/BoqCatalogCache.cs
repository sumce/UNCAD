using System.Collections.Generic;

namespace UNCAD.Core.Excel
{
    internal static class BoqCatalogCache
    {
        private static readonly StableFileCache<List<ListItem>> Cache =
            new StableFileCache<List<ListItem>>("固定清单 Excel",
                ListItemReader.ReadList, Clone);

        public static List<ListItem> Load(string filePath, out bool cacheHit)
            => Cache.Load(filePath, out cacheHit);

        internal static void Clear() => Cache.Clear();

        private static List<ListItem> Clone(List<ListItem> source)
        {
            var result = new List<ListItem>();
            foreach (ListItem item in source ?? new List<ListItem>())
            {
                result.Add(new ListItem
                {
                    Code = item.Code,
                    Name = item.Name,
                    Feature = item.Feature,
                    Unit = item.Unit,
                    Spec = item.Spec
                });
            }
            return result;
        }
    }
}

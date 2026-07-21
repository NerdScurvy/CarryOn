using System;
using System.Collections.Generic;

namespace CarryOn.Utility
{
    internal static class CollectionHelper
    {
        public static Dictionary<TKey, (int FirstIndex, List<T> Rows)> GroupByFirstIndex<T, TKey>(
            IEnumerable<T> source,
            Func<T, TKey> keySelector) where TKey : notnull
        {
            var groups = new Dictionary<TKey, (int FirstIndex, List<T> Rows)>();
            var idx = 0;
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (groups.TryGetValue(key, out var existing))
                {
                    // Rows is a reference type, so Add mutates in-place;
                    // reassign the tuple to be defensive against future struct changes.
                    existing.Rows.Add(item);
                    groups[key] = existing;
                }
                else
                {
                    groups[key] = (idx, new List<T> { item });
                }
                idx++;
            }
            return groups;
        }
    }
}

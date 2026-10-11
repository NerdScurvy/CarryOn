using System;
using System.Collections.Generic;

namespace CarryOn.Client.Logic.CarryRenderer
{
    /// <summary>
    /// Small keyed LRU cache. The most-recently-touched entries are kept up to
    /// <paramref name="capacity"/>; evicted values are passed to the optional
    /// <paramref name="onEvict"/> hook so callers can dispose native resources
    /// (textures, meshes).
    /// </summary>
    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int capacity;
        private readonly Action<TValue>? onEvict;
        private readonly Dictionary<TKey, TValue> entries;
        private readonly List<TKey> lru;

        public LruCache(int capacity, Action<TValue>? onEvict = null)
        {
            this.capacity = capacity;
            this.onEvict = onEvict;
            this.entries = new Dictionary<TKey, TValue>();
            this.lru = new List<TKey>();
        }

        /// <summary>
        /// Returns the value for <paramref name="key"/> and marks it most-recently-used,
        /// or false when absent.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (entries.TryGetValue(key, out value!))
            {
                Touch(key);
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Adds or replaces the value for <paramref name="key"/>, marking it
        /// most-recently-used and evicting the oldest entry past capacity.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            if (entries.ContainsKey(key))
                lru.Remove(key);
            entries[key] = value;
            lru.Add(key);
            EnforceLimit();
        }

        /// <summary>
        /// Marks <paramref name="key"/> most-recently-used without changing its value.
        /// </summary>
        public void Touch(TKey key)
        {
            lru.Remove(key);
            lru.Add(key);
        }

        /// <summary>
        /// Disposes every entry via the eviction hook and empties the cache.
        /// </summary>
        public void Clear()
        {
            foreach (var value in entries.Values) onEvict?.Invoke(value);
            entries.Clear();
            lru.Clear();
        }

        private void EnforceLimit()
        {
            while (lru.Count > capacity)
            {
                var oldest = lru[0];
                lru.RemoveAt(0);
                if (entries.Remove(oldest, out var value)) onEvict?.Invoke(value);
            }
        }
    }
}

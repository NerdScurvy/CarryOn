using System;
using System.Collections.Generic;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Case-insensitive string-keyed registry of typed instances. Lookup tries the code as-is
    /// first, then falls back to <c>"{prefix}:&lt;code&gt;"</c>, so codes may be written with
    /// or without the domain prefix.
    /// </summary>
    /// <remarks>
    /// Single-thread contract: registration happens during <c>AssetsFinalize</c> (single-threaded)
    /// and lookups happen during gameplay (VintageStory's tick loop, also single-threaded).
    /// No locking is required.
    /// </remarks>
    internal sealed class CodeRegistry<T> where T : class
    {
        private readonly string prefix;
        private readonly Dictionary<string, T> entries = new(StringComparer.OrdinalIgnoreCase);

        public CodeRegistry(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("Prefix cannot be null or empty.", nameof(prefix));
            this.prefix = prefix;
        }

        /// <summary>
        /// Registers <paramref name="value"/> under <paramref name="code"/> as-is, overwriting any
        /// previous registration for the same code.
        /// </summary>
        public void Register(string code, T value)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code cannot be null or empty.", nameof(code));
            ArgumentNullException.ThrowIfNull(value);
            entries[code] = value;
        }

        /// <summary>
        /// Registers <paramref name="value"/> under the canonical (already-prefixed) <paramref name="code"/>.
        /// Throws if a different instance is already registered for the same code; re-registering the
        /// same instance is a no-op.
        /// </summary>
        public void TryAddCanonical(string code, T value, string duplicateMessageFormat)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code cannot be null or empty.", nameof(code));
            ArgumentNullException.ThrowIfNull(value);
            if (entries.TryGetValue(code, out var existing) && !ReferenceEquals(existing, value))
                throw new InvalidOperationException(string.Format(duplicateMessageFormat, code));
            entries.TryAdd(code, value);
        }

        /// <summary>
        /// Attempts to resolve <paramref name="code"/>, trying the code as-is first and then falling
        /// back to <c>"{prefix}:{code}"</c>.
        /// </summary>
        public bool TryGet(string code, out T? value)
        {
            if (entries.TryGetValue(code, out value)) return true;
            return entries.TryGetValue($"{prefix}:{code}", out value);
        }

        public bool Contains(string code) => entries.ContainsKey(code);

        public IEnumerable<string> EnumerateCodes() => entries.Keys;

        /// <summary>
        /// Removes <paramref name="code"/> only if the currently registered instance is
        /// <paramref name="value"/> (reference-identical).
        /// </summary>
        public bool Unregister(string code, T value)
            => entries.TryGetValue(code, out var existing)
                && ReferenceEquals(existing, value)
                && entries.Remove(code);
    }
}

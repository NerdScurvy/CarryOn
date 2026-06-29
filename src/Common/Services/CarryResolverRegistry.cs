using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;

namespace CarryOn.Common.Services
{
    internal sealed class CarryResolverRegistry
    {
        private readonly Dictionary<string, IRootTransformGroupResolver> rootResolversByCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IAttachmentTransformGroupResolver> attachmentResolversByCode = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterRoot(string modId, IRootTransformGroupResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(modId))
                throw new ArgumentException("Mod id cannot be null or empty.", nameof(modId));
            ArgumentNullException.ThrowIfNull(resolver);
            if (string.IsNullOrWhiteSpace(resolver.ResolverCode))
                throw new ArgumentException("Resolver code cannot be null or empty.", nameof(resolver));

            var canonicalCode = ToCanonicalResolverCode(modId, resolver.ResolverCode);
            if (canonicalCode == null)
                throw new ArgumentException("Resolver code cannot be null or empty after trimming.", nameof(resolver));

            if (rootResolversByCode.TryGetValue(canonicalCode, out var existing) && !ReferenceEquals(existing, resolver))
                throw new InvalidOperationException($"A root resolver is already registered for code '{canonicalCode}'.");

            // TryAdd is a no-op if already present (same-instance re-registration)
            rootResolversByCode.TryAdd(canonicalCode, resolver);
        }

        public void RegisterAttachment(string modId, IAttachmentTransformGroupResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(modId))
                throw new ArgumentException("Mod id cannot be null or empty.", nameof(modId));
            ArgumentNullException.ThrowIfNull(resolver);
            if (string.IsNullOrWhiteSpace(resolver.ResolverCode))
                throw new ArgumentException("Resolver code cannot be null or empty.", nameof(resolver));

            var canonicalCode = ToCanonicalResolverCode(modId, resolver.ResolverCode);
            if (canonicalCode == null)
                throw new ArgumentException("Resolver code cannot be null or empty after trimming.", nameof(resolver));

            if (attachmentResolversByCode.TryGetValue(canonicalCode, out var existing) && !ReferenceEquals(existing, resolver))
                throw new InvalidOperationException($"An attachment resolver is already registered for code '{canonicalCode}'.");

            // TryAdd is a no-op if already present (same-instance re-registration)
            attachmentResolversByCode.TryAdd(canonicalCode, resolver);
        }

        public bool TryGetRootResolver(string resolverCode, out IRootTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
                return false;

            return rootResolversByCode.TryGetValue(canonicalCode, out resolver);
        }

        public bool TryGetAttachmentResolver(string resolverCode, out IAttachmentTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
                return false;

            return attachmentResolversByCode.TryGetValue(canonicalCode, out resolver);
        }

        public bool UnregisterRoot(IRootTransformGroupResolver resolver)
        {
            if (resolver == null || string.IsNullOrWhiteSpace(resolver.ResolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolver.ResolverCode);
            if (canonicalCode == null)
                return false;

            if (rootResolversByCode.TryGetValue(canonicalCode, out var existing)
                && ReferenceEquals(existing, resolver))
            {
                return rootResolversByCode.Remove(canonicalCode);
            }

            return false;
        }

        public bool UnregisterAttachment(IAttachmentTransformGroupResolver resolver)
        {
            if (resolver == null || string.IsNullOrWhiteSpace(resolver.ResolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolver.ResolverCode);
            if (canonicalCode == null)
                return false;

            if (attachmentResolversByCode.TryGetValue(canonicalCode, out var existing)
                && ReferenceEquals(existing, resolver))
            {
                return attachmentResolversByCode.Remove(canonicalCode);
            }

            return false;
        }

        private static string? ToCanonicalLookupCode(string resolverCode)
        {
            var trimmed = resolverCode?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return null;

            return trimmed.IndexOf(':') >= 0
                ? trimmed
                : $"{CarryCode.ModId}:{trimmed}";
        }

        private static string? ToCanonicalResolverCode(string modId, string resolverCode)
        {
            var normalizedModId = modId?.Trim();
            var normalizedCode = resolverCode?.Trim();

            return normalizedCode?.IndexOf(':') >= 0
                ? normalizedCode
                : $"{normalizedModId}:{normalizedCode}";
        }
    }
}

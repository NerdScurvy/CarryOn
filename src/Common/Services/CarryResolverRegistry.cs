using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;

namespace CarryOn.Common.Services
{
    internal sealed class CarryResolverRegistry
    {
        private readonly List<RegisteredTransformGroupResolver> transformGroupResolvers = new();
        private readonly Dictionary<string, RegisteredTransformGroupResolver> transformGroupResolversByCode = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string modId, ICarriedTransformGroupResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Mod id cannot be null or empty.", nameof(modId));
            }

            ArgumentNullException.ThrowIfNull(resolver);
            if (string.IsNullOrWhiteSpace(resolver.ResolverCode))
            {
                throw new ArgumentException("Resolver code cannot be null or empty.", nameof(resolver));
            }

            var canonicalCode = ToCanonicalResolverCode(modId, resolver.ResolverCode);
            if (canonicalCode == null)
            {
                throw new ArgumentException("Resolver code cannot be null or empty after trimming.", nameof(resolver));
            }

            if (transformGroupResolversByCode.TryGetValue(canonicalCode, out var existing))
            {
                if (!ReferenceEquals(existing.Resolver, resolver))
                {
                    throw new InvalidOperationException(
                        $"A transform group resolver is already registered for code '{canonicalCode}' by mod '{existing.ModId}'.");
                }

                return;
            }

            var registration = new RegisteredTransformGroupResolver
            {
                ModId = modId.Trim(),
                ResolverCode = canonicalCode,
                Resolver = resolver
            };

            transformGroupResolvers.Add(registration);
            transformGroupResolversByCode[canonicalCode] = registration;
        }

        public bool TryGetResolver(string resolverCode, out ICarriedTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
            {
                return false;
            }

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
            {
                return false;
            }

            if (!transformGroupResolversByCode.TryGetValue(canonicalCode, out var registration))
            {
                return false;
            }

            resolver = registration.Resolver;
            return true;
        }

        public bool TryGetRegistration(string resolverCode, out RegisteredTransformGroupResolver? registration)
        {
            registration = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
            {
                return false;
            }

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
            {
                return false;
            }

            var found = transformGroupResolversByCode.TryGetValue(canonicalCode, out var foundReg);
            registration = foundReg;
            return found;
        }

        public bool Unregister(ICarriedTransformGroupResolver resolver)
        {
            if (resolver == null) return false;

            RegisteredTransformGroupResolver? registration = null;
            for (var i = 0; i < transformGroupResolvers.Count; i++)
            {
                if (ReferenceEquals(transformGroupResolvers[i].Resolver, resolver))
                {
                    registration = transformGroupResolvers[i];
                    transformGroupResolvers.RemoveAt(i);
                    break;
                }
            }

            if (registration == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(registration.ResolverCode)
                && transformGroupResolversByCode.TryGetValue(registration.ResolverCode, out var existing)
                && ReferenceEquals(existing.Resolver, resolver))
            {
                transformGroupResolversByCode.Remove(registration.ResolverCode);
            }

            return true;
        }

        public IReadOnlyList<RegisteredTransformGroupResolver> GetAll()
        {
            return transformGroupResolvers;
        }

        private static string? ToCanonicalLookupCode(string resolverCode)
        {
            var trimmed = resolverCode?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

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

using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Models;
namespace CarryOn.Common.Services
{
    internal sealed class CarryResolverRegistry
    {
        private readonly CodeRegistry<IRootTransformGroupResolver> rootResolvers = new(CarryCodes.ModId);
        private readonly CodeRegistry<IAttachmentTransformGroupResolver> attachmentResolvers = new(CarryCodes.ModId);
        private readonly CodeRegistry<IEntityTransformGroupResolver> entityResolvers = new(CarryCodes.ModId);

        // CodeRegistry is keyed lookup only and does not guarantee iteration order, so the
        // entity resolvers keep a parallel list in registration order for the override helper.
        private readonly List<IEntityTransformGroupResolver> entityResolverOrder = new();

        public void RegisterRoot(string modId, IRootTransformGroupResolver resolver)
            => RegisterIn(rootResolvers, modId, resolver, resolver.ResolverCode, "A root resolver is already registered for code '{0}'.");

        public void RegisterAttachment(string modId, IAttachmentTransformGroupResolver resolver)
            => RegisterIn(attachmentResolvers, modId, resolver, resolver.ResolverCode, "An attachment resolver is already registered for code '{0}'.");

        public void RegisterEntity(string modId, IEntityTransformGroupResolver resolver)
        {
            RegisterIn(entityResolvers, modId, resolver, resolver.ResolverCode, "An entity resolver is already registered for code '{0}'.");
            if (!entityResolverOrder.Contains(resolver))
            {
                entityResolverOrder.Add(resolver);
            }
        }

        private static void RegisterIn<T>(CodeRegistry<T> registry, string modId, T resolver, string resolverCode, string duplicateMessage) where T : class
        {
            if (string.IsNullOrWhiteSpace(modId))
                throw new ArgumentException("Mod id cannot be null or empty.", nameof(modId));
            ArgumentNullException.ThrowIfNull(resolver);
            if (string.IsNullOrWhiteSpace(resolverCode))
                throw new ArgumentException("Resolver code cannot be null or empty.", nameof(resolver));

            var canonicalCode = ToCanonicalResolverCode(modId, resolverCode);
            if (canonicalCode == null)
                throw new ArgumentException("Resolver code cannot be null or empty after trimming.", nameof(resolver));

            registry.TryAddCanonical(canonicalCode, resolver, duplicateMessage);
        }

        public bool TryGetRootResolver(string resolverCode, out IRootTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
                return false;

            return rootResolvers.TryGet(canonicalCode, out resolver);
        }

        public bool TryGetAttachmentResolver(string resolverCode, out IAttachmentTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
                return false;

            return attachmentResolvers.TryGet(canonicalCode, out resolver);
        }

        public bool TryGetEntityResolver(string resolverCode, out IEntityTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
                return false;

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
                return false;

            return entityResolvers.TryGet(canonicalCode, out resolver);
        }

        public IReadOnlyList<IEntityTransformGroupResolver> EnumerateEntityResolvers()
            => entityResolverOrder;

        // Removal matches the registered entry by resolver instance: registration
        // canonically qualifies the resolver's code with the *caller's* mod id, so a
        // code-keyed lookup recomputed from the resolver's own (possibly bare) code can
        // target the wrong domain and miss. The instance is registered under exactly one
        // canonical code and is the only identity the public unregister API has.

        public bool UnregisterRoot(IRootTransformGroupResolver resolver)
            => resolver != null && rootResolvers.UnregisterByValue(resolver);

        public bool UnregisterAttachment(IAttachmentTransformGroupResolver resolver)
            => resolver != null && attachmentResolvers.UnregisterByValue(resolver);

        public bool UnregisterEntity(IEntityTransformGroupResolver resolver)
        {
            if (resolver == null) return false;
            var removed = entityResolvers.UnregisterByValue(resolver);
            if (removed)
            {
                entityResolverOrder.Remove(resolver);
            }
            return removed;
        }

        private static string? ToCanonicalLookupCode(string resolverCode)
        {
            return ToCanonicalCode(resolverCode, CarryCodes.ModId);
        }

        private static string? ToCanonicalResolverCode(string modId, string resolverCode)
        {
            return ToCanonicalCode(resolverCode, modId);
        }

        private static string? ToCanonicalCode(string? resolverCode, string? defaultModId)
        {
            var normalizedCode = resolverCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCode))
                return null;

            return normalizedCode.IndexOf(':') >= 0
                ? normalizedCode
                : $"{defaultModId?.Trim()}:{normalizedCode}";
        }
    }
}

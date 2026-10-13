using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Public resolver-registry rows for the transform group resolver surface: resolvers
    /// other mods register through <see cref="ICarryManager"/> round-trip through register /
    /// lookup / unregister, lookups accept the code with or without its domain prefix,
    /// unknown codes report a miss, and a second resolver claiming an already-used code is
    /// rejected without disturbing the first registration. Each row unregisters in a
    /// <c>finally</c> because the registry lives for the whole boot.
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Public resolver-registry rows: root, attachment and entity transform group resolvers round-trip through register, lookup and unregister, a duplicate code is rejected without replacing the first registration, and the entity override chain consults resolvers in registration order with the first claim winning.")]
    public class TransformResolverRegistryScenarios : CarryOnScenarioBase
    {
        private const string RootCode = "carryon:test-rootresolver";
        private const string AttachmentCode = "carryon:test-attachmentresolver";
        private const string EntityCode = "carryon:test-entityresolver";

        /// <summary>A caller mod id other than CarryOn's own, used to pin the
        /// domain-qualified register/lookup/unregister round-trip.</summary>
        private const string ForeignModId = "othermod-test";

        private const string UnprefixedRootCode = "test-rootresolver-unprefixed";
        private const string UnprefixedAttachmentCode = "test-attachmentresolver-unprefixed";
        private const string UnprefixedEntityCode = "test-entityresolver-unprefixed";

        private sealed class StubRootResolver : IRootTransformGroupResolver
        {
            public StubRootResolver(string resolverCode) => ResolverCode = resolverCode;

            public string ResolverCode { get; }

            public bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup,
                out IReadOnlyList<string>? candidates)
            {
                candidates = null;
                return false;
            }
        }

        private sealed class StubAttachmentResolver : IAttachmentTransformGroupResolver
        {
            public StubAttachmentResolver(string resolverCode) => ResolverCode = resolverCode;

            public string ResolverCode { get; }

            public bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup,
                out AttachmentResolveResult? result)
            {
                result = null;
                return false;
            }
        }

        private sealed class StubEntityResolver : IEntityTransformGroupResolver
        {
            public StubEntityResolver(string resolverCode, string? overrideGroup = null)
            {
                ResolverCode = resolverCode;
                OverrideGroup = overrideGroup;
            }

            public string ResolverCode { get; }

            public string? OverrideGroup { get; }

            public int Calls { get; private set; }

            public bool TryResolve(ICoreAPI api, EntityAgent entity, CarrySlot carrySlot, string baseGroup, out string? group)
            {
                Calls++;
                group = carrySlot == CarrySlot.Back ? OverrideGroup : null;
                return group != null;
            }
        }

        /// <summary>
        /// 7 - a registered root resolver is found by its exact code and by the bare code
        /// (the lookup falls back to the carryon domain prefix), an unknown code misses,
        /// and unregistering removes it - reporting true once and false for a second
        /// unregister of the same instance.
        /// </summary>
        [Trait("summary", "A root resolver is found by its exact code and by the bare code through the domain fallback, an unknown code misses, and unregister removes it once then reports false for a second call.")]
        [Trait("pins", "The public root resolver registry round-trips register, lookup and unregister.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task RootResolver_RegisterLookupUnregister_RoundTrips()
        {
            var resolver = new StubRootResolver(RootCode);
            try
            {
                Carry.RegisterRootTransformGroupResolver("carryon-test", resolver);

                Assert.True(Carry.TryGetRootTransformGroupResolver(RootCode, out var byExactCode));
                Assert.Same(resolver, byExactCode);
                Assert.True(Carry.TryGetRootTransformGroupResolver("test-rootresolver", out var byBareCode),
                    "a bare resolver code must resolve through the carryon domain fallback");
                Assert.Same(resolver, byBareCode);

                Assert.False(Carry.TryGetRootTransformGroupResolver("carryon:test-missing", out var missing));
                Assert.Null(missing);

                Assert.True(Carry.UnregisterRootTransformGroupResolver(resolver));
                Assert.False(Carry.TryGetRootTransformGroupResolver(RootCode, out var afterUnregister));
                Assert.Null(afterUnregister);
                Assert.False(Carry.UnregisterRootTransformGroupResolver(resolver),
                    "unregistering the same instance twice must report false");
            }
            finally
            {
                Carry.UnregisterRootTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 7 - a second resolver claiming an already-registered code is rejected rather than
        /// silently replacing the first one, and the first instance stays reachable until it
        /// is unregistered (after which the code is free again).
        /// </summary>
        [Trait("summary", "A second root resolver claiming an in-use code throws and leaves the first reachable, and the code is registrable again once the first is released.")]
        [Trait("pins", "A duplicate registration never replaces the first instance.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task RootResolver_DuplicateCode_IsRejectedWithoutReplacingFirst()
        {
            var first = new StubRootResolver(RootCode);
            var second = new StubRootResolver(RootCode);
            try
            {
                Carry.RegisterRootTransformGroupResolver("carryon-test", first);

                Assert.Throws<InvalidOperationException>(() =>
                    Carry.RegisterRootTransformGroupResolver("carryon-test", second));

                Assert.True(Carry.TryGetRootTransformGroupResolver(RootCode, out var resolved));
                Assert.True(ReferenceEquals(first, resolved),
                    "the rejected duplicate must not have replaced the first registration");

                Assert.True(Carry.UnregisterRootTransformGroupResolver(first),
                    "the original registration must survive the rejected duplicate");
                Carry.RegisterRootTransformGroupResolver("carryon-test", second);
                Assert.True(Carry.TryGetRootTransformGroupResolver(RootCode, out var afterReclaim));
                Assert.True(ReferenceEquals(second, afterReclaim),
                    "once the code is released it must be registrable again");
            }
            finally
            {
                Carry.UnregisterRootTransformGroupResolver(first);
                Carry.UnregisterRootTransformGroupResolver(second);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 7 - the attachment resolver registry keeps the same contract as the root one:
        /// register, find (with and without the prefix), miss on an unknown code, remove.
        /// </summary>
        [Trait("summary", "The attachment resolver registry keeps the same register, lookup (exact and bare) and unregister contract as the root one.")]
        [Trait("pins", "The attachment registry behaves like the root registry.")]
        [Trait("role", "parity")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachmentResolver_RegisterLookupUnregister_RoundTrips()
        {
            var resolver = new StubAttachmentResolver(AttachmentCode);
            try
            {
                Carry.RegisterAttachmentTransformGroupResolver("carryon-test", resolver);

                Assert.True(Carry.TryGetAttachmentTransformGroupResolver(AttachmentCode, out var byExactCode));
                Assert.Same(resolver, byExactCode);
                Assert.True(Carry.TryGetAttachmentTransformGroupResolver("test-attachmentresolver", out var byBareCode),
                    "a bare attachment resolver code must resolve through the carryon domain fallback");
                Assert.Same(resolver, byBareCode);

                Assert.False(Carry.TryGetAttachmentTransformGroupResolver("carryon:test-missing", out var missing));
                Assert.Null(missing);

                Assert.True(Carry.UnregisterAttachmentTransformGroupResolver(resolver));
                Assert.False(Carry.TryGetAttachmentTransformGroupResolver(AttachmentCode, out var afterUnregister));
                Assert.Null(afterUnregister);
            }
            finally
            {
                Carry.UnregisterAttachmentTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// The entity resolver registry keeps the same contract as the root and attachment
        /// ones: register, find (with and without the prefix), miss on an unknown code,
        /// remove once, and a second removal reports false.
        /// </summary>
        [Trait("summary", "The entity resolver registry keeps the same register, lookup (exact and bare) and unregister contract as the root and attachment ones.")]
        [Trait("pins", "The entity resolver registry behaves like the root and attachment registries.")]
        [Trait("role", "parity")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EntityResolver_RegisterLookupUnregister_RoundTrips()
        {
            var resolver = new StubEntityResolver(EntityCode);
            try
            {
                Carry.RegisterEntityTransformGroupResolver("carryon-test", resolver);

                Assert.True(Carry.TryGetEntityTransformGroupResolver(EntityCode, out var byExactCode));
                Assert.Same(resolver, byExactCode);
                Assert.True(Carry.TryGetEntityTransformGroupResolver("test-entityresolver", out var byBareCode),
                    "a bare entity resolver code must resolve through the carryon domain fallback");
                Assert.Same(resolver, byBareCode);

                Assert.False(Carry.TryGetEntityTransformGroupResolver("carryon:test-missing", out var missing));
                Assert.Null(missing);

                Assert.True(Carry.UnregisterEntityTransformGroupResolver(resolver));
                Assert.False(Carry.TryGetEntityTransformGroupResolver(EntityCode, out var afterUnregister));
                Assert.Null(afterUnregister);
                Assert.False(Carry.UnregisterEntityTransformGroupResolver(resolver),
                    "unregistering the same entity resolver twice must report false");
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// The override chain: registered entity resolvers are consulted in registration
        /// order, the first to claim a group wins and later resolvers are not consulted,
        /// and unregistering the claiming resolver falls through to the next one - and
        /// finally back to the untouched base group.
        /// </summary>
        [Trait("summary", "ResolveEntityTransformGroupOverride consults entity resolvers in registration order, the first claim wins and later resolvers stay uncalled, and unregistering each claim falls through to the next resolver then the base group.")]
        [Trait("pins", "A per-entity resolver override wins over the resolved base group without replacing it permanently.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EntityResolver_OverrideChain_FirstClaimedWins()
        {
            const string baseGroup = "backpack-large";
            var first = new StubEntityResolver(EntityCode, "backpack-none");
            var second = new StubEntityResolver("carryon:test-entityresolver-second", "backpack-none");
            var entity = new EntityPlayer();

            try
            {
                Carry.RegisterEntityTransformGroupResolver("carryon-test", first);
                Carry.RegisterEntityTransformGroupResolver("carryon-test", second);

                Assert.Equal("backpack-none",
                    Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, baseGroup));
                Assert.Equal(1, first.Calls);
                Assert.True(0 == second.Calls,
                    "the first claim wins, so the second resolver must not be consulted");

                Carry.UnregisterEntityTransformGroupResolver(first);
                Assert.Equal("backpack-none",
                    Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, baseGroup));
                Assert.True(1 == second.Calls,
                    "after the first claim is released the second resolver takes over");

                Carry.UnregisterEntityTransformGroupResolver(second);
                Assert.Equal(baseGroup,
                    Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, baseGroup));
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(first);
                Carry.UnregisterEntityTransformGroupResolver(second);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// A resolver registered by a mod other than CarryOn, under an unprefixed
        /// resolver code, is found through the caller's own domain and can be
        /// unregistered again. Before the fix, registration qualified the code with
        /// the caller's mod id, but unregister qualified the resolver's own (bare)
        /// code with the CarryOn domain, so the lookup missed and the registration
        /// could never be released.
        /// </summary>
        [Trait("summary", "A root resolver registered by another mod with an unprefixed resolver code is found through that mod's domain and Unregister reports true and removes it.")]
        [Trait("pins", "The public resolver registry unregisters a resolver registered under a non-CarryOn mod id with an unprefixed code.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where unregister qualified the bare resolver code with the CarryOn domain and so never matched the registration.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task RootResolver_ForeignModIdUnprefixedCode_Unregisters()
        {
            var resolver = new StubRootResolver(UnprefixedRootCode);
            string registeredCode = $"{ForeignModId}:{UnprefixedRootCode}";
            try
            {
                Carry.RegisterRootTransformGroupResolver(ForeignModId, resolver);

                // Premise/control: registration qualified the code with the caller's
                // domain, so the fully-qualified code resolves. This half stays true
                // with or without the fix and proves the resolver really is stored.
                Assert.True(Carry.TryGetRootTransformGroupResolver(registeredCode, out var stored),
                    "premise: the foreign-mod resolver must be retrievable by its domain-qualified code");
                Assert.Same(resolver, stored);

                Assert.True(Carry.UnregisterRootTransformGroupResolver(resolver),
                    "unregister must release a resolver registered under another mod id with an unprefixed code");
                Assert.False(Carry.TryGetRootTransformGroupResolver(registeredCode, out var afterUnregister));
                Assert.Null(afterUnregister);
            }
            finally
            {
                Carry.UnregisterRootTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// The attachment registry keeps the same round-trip for a resolver registered
        /// by another mod under an unprefixed code. Before the fix, the same domain
        /// mismatch between register (caller mod id) and unregister (CarryOn domain)
        /// applied.
        /// </summary>
        [Trait("summary", "An attachment resolver registered by another mod with an unprefixed resolver code is found through that mod's domain and Unregister reports true and removes it.")]
        [Trait("pins", "The attachment resolver registry unregisters a resolver registered under a non-CarryOn mod id with an unprefixed code.")]
        [Trait("role", "parity")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head for the same register/unregister domain mismatch as the root registry.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachmentResolver_ForeignModIdUnprefixedCode_Unregisters()
        {
            var resolver = new StubAttachmentResolver(UnprefixedAttachmentCode);
            string registeredCode = $"{ForeignModId}:{UnprefixedAttachmentCode}";
            try
            {
                Carry.RegisterAttachmentTransformGroupResolver(ForeignModId, resolver);

                Assert.True(Carry.TryGetAttachmentTransformGroupResolver(registeredCode, out var stored),
                    "premise: the foreign-mod attachment resolver must be retrievable by its domain-qualified code");
                Assert.Same(resolver, stored);

                Assert.True(Carry.UnregisterAttachmentTransformGroupResolver(resolver),
                    "unregister must release an attachment resolver registered under another mod id with an unprefixed code");
                Assert.False(Carry.TryGetAttachmentTransformGroupResolver(registeredCode, out var afterUnregister));
                Assert.Null(afterUnregister);
            }
            finally
            {
                Carry.UnregisterAttachmentTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// The entity registry keeps the same round-trip for a resolver registered by
        /// another mod under an unprefixed code, including removing it from the
        /// registration-order list the override helper walks.
        /// </summary>
        [Trait("summary", "An entity resolver registered by another mod with an unprefixed resolver code is found through that mod's domain and Unregister reports true and removes it.")]
        [Trait("pins", "The entity resolver registry unregisters a resolver registered under a non-CarryOn mod id with an unprefixed code.")]
        [Trait("role", "parity")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head for the same register/unregister domain mismatch as the root registry.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EntityResolver_ForeignModIdUnprefixedCode_Unregisters()
        {
            var resolver = new StubEntityResolver(UnprefixedEntityCode);
            string registeredCode = $"{ForeignModId}:{UnprefixedEntityCode}";
            try
            {
                Carry.RegisterEntityTransformGroupResolver(ForeignModId, resolver);

                Assert.True(Carry.TryGetEntityTransformGroupResolver(registeredCode, out var stored),
                    "premise: the foreign-mod entity resolver must be retrievable by its domain-qualified code");
                Assert.Same(resolver, stored);

                Assert.True(Carry.UnregisterEntityTransformGroupResolver(resolver),
                    "unregister must release an entity resolver registered under another mod id with an unprefixed code");
                Assert.False(Carry.TryGetEntityTransformGroupResolver(registeredCode, out var afterUnregister));
                Assert.Null(afterUnregister);
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }
    }
}

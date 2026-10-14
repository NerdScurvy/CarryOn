using System;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Per-wearer hidden-backpack override rows: the shipped compat signals read each
    /// cosmetic mod's client-replicated state (Hide Gear's settings JSON with the
    /// force-visible suppression, Combat Overhaul's backpack flag, and Vanity's client
    /// API through a reflection scaffold that accepts both shipped API generations),
    /// and the registered resolver downgrades the back-slot base group to the
    /// no-backpack group only while the wearer's own state says the pack is hidden.
    /// The cosmetic mods themselves are not staged here, so every row drives the real
    /// signals through their public seams.
    /// </summary>
    [Trait("area", "Smoke")]
    [Trait("summary", "Hidden-backpack signal rows: Hide Gear's settings JSON with force-visible suppression, Combat Overhaul's backpack flag, and Vanity's client API via a dual-generation reflection scaffold each downgrade the back-slot group per wearer, with the hidden decision memoized by the signal change token.")]
    public class HiddenBackpackSignalScenarios : CarryOnScenarioBase
    {
        // The shipped resolver's code is bare, so registering under the default lookup
        // prefix keeps register/unregister canonicalization symmetric.
        private const string TestModId = "carryon";

        /// <summary>
        /// Hide Gear: a <c>hidegear:hidden</c> JSON with <c>HideBackpack</c> downgrades the
        /// wearer's back group to the no-backpack group; a server-set force-visible flag
        /// restores compensation; settings that say not hidden, or malformed JSON, stay on
        /// the base group; and the hands slot never consults the signal.
        /// </summary>
        [Trait("summary", "Hide Gear's hidden settings JSON downgrades the back group to backpack-none, force-visible restores the base group, malformed JSON degrades to visible, and the hands slot never consults the signal.")]
        [Trait("pins", "The carried block's back position tracks the wearer's own Hide Gear state, including the PvP force-visible suppression.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HideGearSettings_OverrideBackGroup_ForceVisibleSuppresses()
        {
            ITestPlayer player = await JoinPlayer("HideGearTester");
            var attributes = player.Entity.WatchedAttributes;
            var resolver = new HiddenBackpackEntityResolver(
                new IHiddenBackpackSignal[] { new HideGearHiddenBackpackSignal() });

            Carry.RegisterEntityTransformGroupResolver(TestModId, resolver);
            try
            {
                attributes.SetString(HideGearHiddenBackpackSignal.HiddenSettingsKey,
                    "{\"HideBackpack\":true,\"HiddenGear\":{}}");
                Assert.Equal(BackpackGroupNone,
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Back, BackpackGroupLarge));

                Assert.True(BackpackGroupLarge ==
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Hands, BackpackGroupLarge),
                    "the hands slot never consults the hidden-backpack signals");

                attributes.SetBool(HideGearHiddenBackpackSignal.ForceVisibleKey, true);
                Assert.True(BackpackGroupLarge ==
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Back, BackpackGroupLarge),
                    "a force-visible wearer renders with compensation");

                attributes.SetBool(HideGearHiddenBackpackSignal.ForceVisibleKey, false);
                attributes.SetString(HideGearHiddenBackpackSignal.HiddenSettingsKey,
                    "{\"HideBackpack\":false}");
                Assert.Equal(BackpackGroupLarge,
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Back, BackpackGroupLarge));

                attributes.SetString(HideGearHiddenBackpackSignal.HiddenSettingsKey, "not-json");
                Assert.True(BackpackGroupLarge ==
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Back, BackpackGroupLarge),
                    "malformed settings JSON must degrade to visible, never break rendering");
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Combat Overhaul: a true <c>combatoverhaul:hide-backpack</c> watched attribute
        /// downgrades the wearer's back group, and clearing it restores the base group. The
        /// attribute name is stable regardless of which mod writes it, so the same read
        /// covers the legacy-compat stack vanity-slot forks depend on.
        /// </summary>
        [Trait("summary", "A combatoverhaul:hide-backpack=true watched attribute downgrades the back group to backpack-none and clearing it restores the base group.")]
        [Trait("pins", "One stable attribute read covers real Combat Overhaul and the legacy-compat stack that vanity-slot forks depend on.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CombatOverhaulFlag_OverridesBackGroup()
        {
            ITestPlayer player = await JoinPlayer("COHideTester");
            var attributes = player.Entity.WatchedAttributes;
            var resolver = new HiddenBackpackEntityResolver(
                new IHiddenBackpackSignal[] { new CombatOverhaulHiddenBackpackSignal() });

            Carry.RegisterEntityTransformGroupResolver(TestModId, resolver);
            try
            {
                attributes.SetBool(CombatOverhaulHiddenBackpackSignal.HideBackpackKey, true);
                Assert.Equal(BackpackGroupNone,
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Back, BackpackGroupLarge));

                attributes.SetBool(CombatOverhaulHiddenBackpackSignal.HideBackpackKey, false);
                Assert.Equal(BackpackGroupLarge,
                    Carry.ResolveEntityTransformGroupOverride(player.Entity, CarrySlot.Back, BackpackGroupLarge));
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// The Vanity reflection scaffold resolves both shipped API generations: the 2.4.8
        /// shape (<c>ShouldRenderGear</c> over a gear enum, with an integer-index decoy
        /// overload) and the 2.5.1 shape (<c>ShouldRenderSlot</c> over an equipment-slot
        /// enum, with a non-enum slot decoy overload). The enum-typed overload wins, the
        /// Back member resolves by name, and unrecognized shapes report no scaffold.
        /// </summary>
        [Trait("summary", "The Vanity reflection scaffold resolves both shipped API generations (ShouldRenderGear with an int decoy, ShouldRenderSlot with a non-enum decoy), selects the enum-typed overload, resolves Back by name, and rejects unrecognized shapes.")]
        [Trait("pins", "The scaffold matches Vanity's own render decision without referencing the mod assembly, whichever API generation is installed.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task VanityScaffold_ResolvesBothApiGenerations()
        {
            var scaffold248 = VanityHiddenBackpackSignal.TryBuildScaffold(typeof(FakeVanitySystem248));
            Assert.NotNull(scaffold248);
            Assert.Equal("ShouldRenderGear", scaffold248!.ShouldRenderMethod.Name);
            Assert.Equal((object)FakeGearType248.Back, scaffold248.BackSlotValue);

            var scaffold251 = VanityHiddenBackpackSignal.TryBuildScaffold(typeof(FakeVanitySystem251));
            Assert.NotNull(scaffold251);
            Assert.Equal("ShouldRenderSlot", scaffold251!.ShouldRenderMethod.Name);
            Assert.Equal((object)FakeEquipmentSlot251.Back, scaffold251.BackSlotValue);

            Assert.True(VanityHiddenBackpackSignal.TryBuildScaffold(typeof(FakeVanitySystemNoClient)) == null,
                "a type without the static Client property reports no scaffold");
            Assert.True(VanityHiddenBackpackSignal.TryBuildScaffold(typeof(FakeVanitySystemNoMatch)) == null,
                "a type with no enum-typed ShouldRender method reports no scaffold");

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// End to end through the registered resolver, on both Vanity API generations: the
        /// wearer whose back slot the fake client would not render is downgraded to the
        /// no-backpack group while another wearer with a visible pack keeps the base group -
        /// per wearer, with no global mode change. The decoy overload always says render, so
        /// a scaffold that picked it would leave both wearers compensated.
        /// </summary>
        [Trait("summary", "Through the registered resolver and on both Vanity API generations, the wearer whose back slot is not rendered downgrades to backpack-none while a visible-pack wearer keeps the base group, per wearer.")]
        [Trait("pins", "The per-wearer override flips per entity through either Vanity API generation, with no global mode change.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task VanitySignal_OverridesBackGroup_PerWearer_BothGenerations()
        {
            var hiddenWearer = new EntityPlayer();
            var visibleWearer = new EntityPlayer();

            FakeVanityClient248.HiddenEntity = hiddenWearer;
            var resolver248 = new HiddenBackpackEntityResolver(
                new IHiddenBackpackSignal[] { new VanityHiddenBackpackSignal(typeof(FakeVanitySystem248)) });
            Carry.RegisterEntityTransformGroupResolver(TestModId, resolver248);
            try
            {
                Assert.Equal(BackpackGroupNone,
                    Carry.ResolveEntityTransformGroupOverride(hiddenWearer, CarrySlot.Back, BackpackGroupLarge));
                Assert.True(BackpackGroupLarge ==
                    Carry.ResolveEntityTransformGroupOverride(visibleWearer, CarrySlot.Back, BackpackGroupLarge),
                    "the visible-pack wearer keeps the compensated group");
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver248);
                FakeVanityClient248.HiddenEntity = null;
            }

            FakeVanityClient251.HiddenEntity = hiddenWearer;
            var resolver251 = new HiddenBackpackEntityResolver(
                new IHiddenBackpackSignal[] { new VanityHiddenBackpackSignal(typeof(FakeVanitySystem251)) });
            Carry.RegisterEntityTransformGroupResolver(TestModId, resolver251);
            try
            {
                Assert.Equal(BackpackGroupNone,
                    Carry.ResolveEntityTransformGroupOverride(hiddenWearer, CarrySlot.Back, BackpackGroupLarge));
                Assert.True(BackpackGroupLarge ==
                    Carry.ResolveEntityTransformGroupOverride(visibleWearer, CarrySlot.Back, BackpackGroupLarge),
                    "the visible-pack wearer keeps the compensated group");
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver251);
                FakeVanityClient251.HiddenEntity = null;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// A transient miss is not latched: when Vanity's client system has not started yet
        /// (the static <c>Client</c> holds no instance), the signal reports not hidden but
        /// keeps re-inspecting, so once the client starts the same signal honours the slot.
        /// </summary>
        [Trait("summary", "A Vanity client that has not started yet reports not hidden without latching the override, and the next query once it starts re-inspects and honours the hidden slot.")]
        [Trait("pins", "A render query landing before Vanity's client system starts must not permanently deactivate the hidden-backpack override for the session.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task VanitySignal_TransientClientNotStarted_RetriedAfterClientStarts()
        {
            var hiddenWearer = new EntityPlayer();
            FakeVanitySystemLazy.Client = null;

            var signal = new VanityHiddenBackpackSignal(typeof(FakeVanitySystemLazy));
            Assert.False(signal.IsBackpackHidden(hiddenWearer),
                "a query landing before Vanity's client starts must report not hidden without latching");

            FakeVanitySystemLazy.Client = new FakeVanityClientLazy();
            try
            {
                FakeVanityClientLazy.HiddenEntity = hiddenWearer;
                Assert.True(signal.IsBackpackHidden(hiddenWearer),
                    "once Vanity's client is populated, the same signal re-inspects and honours the hidden slot");
            }
            finally
            {
                FakeVanityClientLazy.HiddenEntity = null;
                FakeVanitySystemLazy.Client = null;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// The hidden decision is memoized per wearer keyed by the combined signal change
        /// token: an unchanged token never re-queries the signals, a changed token
        /// re-queries, and a signal that returns a null token opts the whole list out of
        /// memoization so every call re-queries.
        /// </summary>
        [Trait("summary", "The hidden decision is memoized per wearer by the combined signal change token: an unchanged token never re-queries the signals, a changed token re-queries, and a null token opts the whole list out of memoization.")]
        [Trait("pins", "Per-frame resolution stays cheap: reflection and JSON probes run only when the underlying hidden state changes.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HiddenDecision_MemoizedBySignalChangeToken()
        {
            var signal = new CountingSignal { Hidden = true };
            var resolver = new HiddenBackpackEntityResolver(new IHiddenBackpackSignal[] { signal });
            var entity = new EntityPlayer();

            Carry.RegisterEntityTransformGroupResolver(TestModId, resolver);
            try
            {
                Assert.Equal(BackpackGroupNone,
                    Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, BackpackGroupLarge));
                Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, BackpackGroupLarge);
                Assert.True(1 == signal.HiddenCalls,
                    "an unchanged change token must not re-query the signal");

                signal.Token = "1";
                Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, BackpackGroupLarge);
                Assert.True(2 == signal.HiddenCalls,
                    "a changed change token must re-query the signal exactly once");

                signal.Token = null;
                Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, BackpackGroupLarge);
                Carry.ResolveEntityTransformGroupOverride(entity, CarrySlot.Back, BackpackGroupLarge);
                Assert.True(4 == signal.HiddenCalls,
                    "a null token opts the signal list out of memoization, so every call re-queries");
            }
            finally
            {
                Carry.UnregisterEntityTransformGroupResolver(resolver);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        private sealed class CountingSignal : IHiddenBackpackSignal
        {
            public string? Token = "0";

            public int HiddenCalls;

            public bool Hidden;

            public string SignalCode => "counting";

            public bool IsBackpackHidden(EntityPlayer entity)
            {
                HiddenCalls++;
                return Hidden;
            }

            public string? GetCacheSignature(EntityPlayer entity) => Token;
        }

        public enum FakeGearType248
        {
            Head,
            Waist,
            Back
        }

        public enum FakeEquipmentSlot251
        {
            ArmorHead,
            Waist,
            Back
        }

        public sealed class FakeSlot251
        {
        }

        public sealed class FakeVanityClient248
        {
            public static Entity? HiddenEntity;

            public bool ShouldRenderGear(Entity entity, int slotIndex) => true;

            public bool ShouldRenderGear(Entity entity, FakeGearType248 type)
                => type != FakeGearType248.Back || entity != HiddenEntity;
        }

        public sealed class FakeVanitySystem248
        {
            public static FakeVanityClient248 Client { get; } = new();
        }

        public sealed class FakeVanityClient251
        {
            public static Entity? HiddenEntity;

            public bool ShouldRenderSlot(Entity entity, FakeSlot251 slot) => true;

            public bool ShouldRenderSlot(Entity entity, FakeEquipmentSlot251 slot)
                => slot != FakeEquipmentSlot251.Back || entity != HiddenEntity;
        }

        public sealed class FakeVanitySystem251
        {
            public static FakeVanityClient251 Client { get; } = new();
        }

        public sealed class FakeVanityClientLazy
        {
            public static Entity? HiddenEntity;

            public bool ShouldRenderGear(Entity entity, int slotIndex) => true;

            public bool ShouldRenderGear(Entity entity, FakeGearType248 type)
                => type != FakeGearType248.Back || entity != HiddenEntity;
        }

        public sealed class FakeVanitySystemLazy
        {
            public static FakeVanityClientLazy? Client { get; set; }
        }

        public sealed class FakeVanityClientNoMatch
        {
            public bool ShouldRenderGear(Entity entity, int slotIndex) => true;
        }

        public sealed class FakeVanitySystemNoMatch
        {
            public static FakeVanityClientNoMatch Client { get; } = new();
        }

        public sealed class FakeVanitySystemNoClient
        {
        }
    }
}

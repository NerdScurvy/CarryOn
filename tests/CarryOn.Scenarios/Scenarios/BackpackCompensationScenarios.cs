using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Regression rows for the hidden-backpack compensation fix:
    /// (A) <c>BackpackTypes</c> is the one root config section without [TreeValue]
    /// members, so it must round-trip through the world-config tree explicitly - an
    /// absent key keeps the default, an explicit empty section stays empty, and a
    /// custom mapping survives together with its derived <c>BackpackMapping</c>.
    /// (B) <c>BackpackSelectionMode.None</c> resolves the no-backpack transform group
    /// instead of falling through the selection switch's default arm.
    /// </summary>
    [Trait("area", "Smoke")]
    [Trait("summary", "Hidden-backpack compensation rows: BackpackTypes round-trips through the world-config tree (custom mapping, absent key, explicit empty) and BackpackSelectionMode.None resolves the no-backpack group rather than falling through the selection switch default.")]
    public class BackpackCompensationScenarios : CarryOnScenarioBase
    {
        private const string BackpackTypesKey = "BackpackTypes";

        /// <summary>
        /// A: the round-trip the client consumes. Before the fix, ToTree omitted
        /// BackpackTypes, so a client read the property-initializer default no matter what
        /// the server file said. A custom mapping (plus its flattened BackpackMapping) must
        /// survive tree serialization unchanged.
        /// </summary>
        [Trait("summary", "A custom three-type BackpackTypes mapping survives ToTreeAttribute then FromTreeAttribute, with its derived BackpackMapping flattened correctly.")]
        [Trait("pins", "A server BackpackTypes edit actually reaches a client that reads the world-config tree.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, when ToTree emitted no BackpackTypes key and the client kept the property-initializer default.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BackpackTypes_CustomMapping_RoundTripsThroughTree()
        {
            var config = new CarryOnConfig
            {
                BackpackTypes = new Dictionary<string, string[]>
                {
                    ["small"] = ["game:hunterbackpack"],
                    ["large"] = ["game:backpack-normal"],
                    ["satchel"] = ["game:test-satchel"]
                }
            };
            config.InvalidateBackpackCache();

            var tree = config.ToTreeAttribute();
            Assert.True(tree.HasAttribute(BackpackTypesKey),
                "ToTree must emit an explicit BackpackTypes section; without it the client keeps defaults");

            var round = CarryOnConfig.FromTreeAttribute(tree);

            Assert.Equal(3, round.BackpackTypes.Count);
            Assert.Equal(["game:test-satchel"], round.BackpackTypes["satchel"]);
            Assert.Equal("satchel", round.BackpackMapping["game:test-satchel"]);
            Assert.Equal("small", round.BackpackMapping["game:hunterbackpack"]);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// A: a tree with no BackpackTypes key (older world saves, or any tree written
        /// before this fix) must keep the property-initializer default rather than
        /// deserializing to an empty mapping.
        /// </summary>
        [Trait("summary", "A tree with the BackpackTypes key removed keeps the property-initializer default mapping (hunterbackpack to small, backpack-normal to large).")]
        [Trait("pins", "Older saves and pre-fix trees still behave, so the explicit serializer is backward-compatible.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BackpackTypes_AbsentKey_KeepsDefault()
        {
            var tree = (TreeAttribute)new CarryOnConfig().ToTreeAttribute();
            tree.RemoveAttribute(BackpackTypesKey);

            var round = CarryOnConfig.FromTreeAttribute(tree);

            Assert.Equal("small", round.BackpackMapping["game:hunterbackpack"]);
            Assert.Equal("large", round.BackpackMapping["game:backpack-normal"]);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// A: a present-but-empty section is the user's deliberate "no mapping" and must
        /// stay empty - distinct from the absent-key case, which keeps the default. This
        /// is the null-vs-empty contract the explicit serializer exists to preserve.
        /// </summary>
        [Trait("summary", "A present-but-empty BackpackTypes section round-trips as empty in both BackpackTypes and BackpackMapping, distinct from the absent-key default.")]
        [Trait("pins", "The null-versus-empty distinction the explicit serializer exists to preserve, letting a user deliberately map no backpacks.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BackpackTypes_ExplicitEmpty_StaysEmpty()
        {
            var config = new CarryOnConfig { BackpackTypes = new Dictionary<string, string[]>() };
            config.InvalidateBackpackCache();

            var round = CarryOnConfig.FromTreeAttribute(config.ToTreeAttribute());

            Assert.NotNull(round.BackpackTypes);
            Assert.Empty(round.BackpackTypes);
            Assert.Empty(round.BackpackMapping);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// B: with a worn backpack that the default mapping resolves to backpack-small,
        /// <c>None</c> must return the no-backpack transform group (and hide the rendered
        /// slot) instead of behaving like LastFound. The LastFound probe proves the
        /// short-circuit is None-specific, not a broken scan.
        /// </summary>
        [Trait("summary", "With a worn hunter backpack, BackpackSelectionMode.None returns the no-backpack group and hides the rendered slot, while LastFound still resolves backpack-small.")]
        [Trait("pins", "None is genuinely a no-compensation mode, not a fall-through to the selection switch default arm.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SelectionModeNone_ResolvesNoBackpackGroup()
        {
            ITestPlayer player = await JoinPlayer("Tester");

            var backpackInv = player.Player.InventoryManager.GetOwnInventory("backpack");
            Assert.NotNull(backpackInv);
            var backpackSlot = backpackInv!.FirstOrDefault(s => s is ItemSlotBackpack)
                ?? throw new InvalidOperationException("joined player has no backpack slot");
            var stack = new ItemStack(World.Api.World.GetItem(new AssetLocation("game:hunterbackpack")));
            Assert.NotNull(stack.Collectible);
            backpackSlot.Itemstack = stack;
            backpackSlot.MarkDirty();

            var config = new CarryOnConfig();

            Assert.Equal(BackpackGroupPrefix + "small",
                player.Entity.ResolveCarryTransformGroupBase(config, CarrySlot.Back));

            config.CarryOptions.BackpackSelectionMode = BackpackSelectionMode.None;
            Assert.Equal(BackpackGroupNone,
                player.Entity.ResolveCarryTransformGroupBase(config, CarrySlot.Back));
            Assert.Null(player.Player.GetRenderedBackpackSlot(BackpackSelectionMode.None));
            Assert.NotNull(player.Player.GetRenderedBackpackSlot(BackpackSelectionMode.LastFound));

            await Task.CompletedTask;
        }
    }
}

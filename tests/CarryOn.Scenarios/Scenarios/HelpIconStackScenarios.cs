using System.Threading.Tasks;
using Atlas.XUnit;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// The interaction-help icon stack resolution contract: icon stacks are resolved fresh from the
    /// current world's item registry on every call.
    /// <para>
    /// The game builds a fresh <c>ClientMain</c>, item registry and atlases for every world
    /// run, so an icon stack captured in one world is an orphan in the next: it either renders
    /// fragments of other assets (stale atlas coordinates - observed as the fragment symptom
    /// during Phase 0) or, when its id is past the new registry's size, crashes the vanilla
    /// renderer in <c>InventoryItemRenderer.GetItemStackRenderInfo</c> (the reported
    /// <c>IndexOutOfRangeException</c>). The static cache that caused both is gone; the second
    /// assertion is the red row - under the cache, repeated calls returned the same array
    /// instance, keyed by asset code alone with no way to notice the world changed.
    /// </para>
    /// </summary>
    [Trait("area", "Smoke")]
    [Trait("summary", "Interaction-help icon stack rows: HelpIconStacks.Get resolves a fresh array from the current world's registry on every call, and an unknown key degrades to null rather than a cached stack.")]
    public class HelpIconStackScenarios : CarryOnScenarioBase
    {
        private const string IconKey = "icon-nohandsfree";

        /// <summary>
        /// Two calls in the same booted world return two different arrays, both backed by the
        /// world's current registry entry for the icon item. Fresh-per-call is the whole fix:
        /// a caller can only ever hold a stack built from the world it is rendering.
        /// </summary>
        [Trait("summary", "Two HelpIconStacks.Get calls in the same world return different array instances, both backed by the current world's registry entry for the icon.")]
        [Trait("pins", "Icon stacks are resolved from the current world's registry on every call, so a stack captured in one world can never render in another.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed with the old static code-keyed cache, which returned the same array instance on the second call.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Get_ReturnsFreshArrayBackedByCurrentWorldRegistry()
        {
            await World.Ticks(1);

            var world = World.Api.World;
            ItemStack[]? first = HelpIconStacks.Get(world, IconKey);
            ItemStack[]? second = HelpIconStacks.Get(world, IconKey);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotSame(first, second);

            var registryItem = world.GetItem(new AssetLocation(CarryCodes.GetCarryCode(IconKey)));
            Assert.True(registryItem != null,
                $"Row premise: {CarryCodes.GetCarryCode(IconKey)} must exist in the booted test "
                + "world, otherwise this row proves nothing about the registry backing.");
            Assert.Same(registryItem, first![0].Item);
            Assert.Same(registryItem, second![0].Item);
        }

        /// <summary>
        /// An unknown icon key degrades to null (help text without an icon) rather than
        /// throwing or returning a stale stack from a previous resolution of a similar key -
        /// the degrade-to-text semantics #87's fix must not change.
        /// </summary>
        [Trait("summary", "An unknown icon key returns null (help text without an icon) instead of throwing or returning a cached stack.")]
        [Trait("pins", "The degrade-to-text semantics the fix must not change.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Get_UnknownKey_ReturnsNull()
        {
            await World.Ticks(1);

            Assert.Null(HelpIconStacks.Get(World.Api.World, "icon-does-not-exist"));
        }
    }
}

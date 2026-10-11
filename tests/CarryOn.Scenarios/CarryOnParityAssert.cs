using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Per-array carryable state of one block, as the conditioning passes left it.
    /// <para>
    /// The two arrays are distinct storage: <c>BlockBehaviors</c> is the block form's
    /// behavior list and <c>CollectibleBehaviors</c> the item form's. Conditioning may
    /// only mirror its own mutations - an entry it adds must land in both views, an entry
    /// it removes must leave every view it put one in. So the row contract is
    /// <see cref="Mirrored"/>, not a bare <c>IsCarryable()</c> check:
    /// <c>IsCarryable()</c> is an OR over both arrays, so a one-sided leftover keeps a
    /// block carryable and hides the divergence.
    /// </para>
    /// <para>
    /// The engine itself ships legitimate one-sided carryables (a block whose item form is
    /// the operative view, e.g. <c>game:henbox-*</c>), so <see cref="Mirrored"/> alone is
    /// not a global invariant - only conditioning-owned mutations are pinned. Rows call
    /// <see cref="CarryOnParityAssert.AssertPresentInBoth"/> or
    /// <see cref="CarryOnParityAssert.AssertAbsentFromBoth"/>, which name the divergent
    /// side in the failure message.
    /// </para>
    /// </summary>
    public sealed class CarryableParity
    {
        public string Code { get; }
        public int BlockSideCount { get; }
        public int CollectibleSideCount { get; }
        public bool IsCarryable { get; }

        /// <summary>
        /// True when both arrays hold the <i>same</i> carryable instance. The two
        /// conditioning paths build differently: a patch-loaded (<c>/behaviors/-</c>) or
        /// remounted behavior is one object referenced from both arrays, while an automap
        /// clone is built twice - once per view - and the clones start out with different
        /// property objects. Rows use this to tell "the engine/patch put a shared behavior
        /// in both views" from "the automapper built a per-view clone", because only the
        /// second shape can end up one-sided after a reference-based removal.
        /// <para>
        /// Production counterpart, for the reciprocal pointer: the client mirror
        /// in <c>CarrySystem.Configuration.cs</c> (<c>syncedOwnedMirrors</c>,
        /// <c>SyncCarryabilityBehavior</c>) deliberately keeps whichever shape it mounted
        /// rather than normalizing to one - <c>RemoveSyncedBehaviors</c> removes by exact
        /// reference and so reconciles both. Do not "fix" this property into a single expected
        /// value; both results are correct outcomes.
        /// </para>
        /// </summary>
        public bool SharedInstance { get; }

        /// <summary>
        /// Highest <c>patchPriority</c> across both arrays, or -1 when no carryable is
        /// present. Read from the raw arrays rather than through <c>GetBehavior&lt;T&gt;()</c>:
        /// conditioning reassigns <c>block.BlockBehaviors</c> to a new array, so a behavior
        /// cache built at registration time can go stale and report no behavior for a block
        /// whose array provably holds one. The raw array is the truth these rows assert.
        /// </summary>
        public int MaxPriority { get; }

        public bool InBlockSide => BlockSideCount > 0;
        public bool InCollectibleSide => CollectibleSideCount > 0;
        public bool InBothSides => InBlockSide && InCollectibleSide;
        public bool InEitherSide => InBlockSide || InCollectibleSide;

        /// <summary>True when both arrays agree, i.e. no one-sided entry is left behind.</summary>
        public bool Mirrored => InBlockSide == InCollectibleSide;

        public CarryableParity(
            string code,
            int blockSideCount,
            int collectibleSideCount,
            bool isCarryable,
            bool sharedInstance,
            int maxPriority)
        {
            Code = code;
            BlockSideCount = blockSideCount;
            CollectibleSideCount = collectibleSideCount;
            IsCarryable = isCarryable;
            SharedInstance = sharedInstance;
            MaxPriority = maxPriority;
        }

        /// <summary>Per-array state, so a failure names the side that diverged.</summary>
        public string Describe()
            => $"{Code}: block-side={BlockSideCount}, collectible-side={CollectibleSideCount}, "
             + $"IsCarryable()={IsCarryable}, shared-instance={SharedInstance}, "
             + $"max-priority={MaxPriority}";
    }

    /// <summary>Shared per-array parity asserts for the conditioning rows.</summary>
    public static class CarryOnParityAssert
    {
        /// <summary>Reads both arrays and the OR-ed <c>IsCarryable()</c> for one block.</summary>
        public static CarryableParity Inspect(Block block)
        {
            var code = block.Code?.ToString() ?? "<null code>";
            var blockSide = block.BlockBehaviors.OfType<BlockBehaviorCarryable>().ToList();
            var collectibleSide = block.CollectibleBehaviors.OfType<BlockBehaviorCarryable>().ToList();
            var shared = blockSide.Count == 1
                && collectibleSide.Count == 1
                && ReferenceEquals(blockSide[0], collectibleSide[0]);
            var maxPriority = blockSide.Concat(collectibleSide)
                .Select(b => b.PatchPriority)
                .DefaultIfEmpty(-1)
                .Max();
            return new CarryableParity(
                code,
                blockSide.Count,
                collectibleSide.Count,
                block.IsCarryable(),
                shared,
                maxPriority);
        }

        /// <summary>
        /// The block must hold a carryable in <b>both</b> arrays. Any one-sided state fails
        /// here, so a removal that only reached one view cannot pass as "still carryable".
        /// </summary>
        public static void AssertPresentInBoth(Block block, string because)
        {
            var parity = Inspect(block);
            Assert.True(
                parity.InBothSides,
                $"Expected a carryable in both arrays - {because}. Actual: {parity.Describe()}");
            Assert.True(
                parity.IsCarryable,
                $"IsCarryable() must be true when both arrays hold the carryable - {because}. "
                + $"Actual: {parity.Describe()}");
        }

        /// <summary>
        /// The block must hold no carryable in <b>either</b> array, and must not report
        /// carryable. A survivor in just one array keeps <c>IsCarryable()</c> true (OR
        /// semantics), so this is the assertion that catches a half-applied removal.
        /// </summary>
        public static void AssertAbsentFromBoth(Block block, string because)
        {
            var parity = Inspect(block);
            Assert.True(
                !parity.InEitherSide,
                $"Expected no carryable in either array - {because}. Actual: {parity.Describe()}");
            Assert.False(
                parity.IsCarryable,
                $"IsCarryable() must be false when neither array holds a carryable - {because}. "
                + $"Actual: {parity.Describe()}");
        }

        /// <summary>
        /// The removal covered the union: whatever <paramref name="before"/> found, the
        /// matched pass must have emptied <b>both</b> arrays. Use this instead of
        /// <see cref="AssertAbsentFromBoth"/> when the unmatched state is measured rather
        /// than assumed - the engine's own one-sided entries mean "present in both arrays"
        /// is not a universal precondition, but "removed from every array" is.
        /// </summary>
        public static CarryableParity AssertRemovalCovered(CarryableParity before, Block block, string because)
        {
            var after = Inspect(block);
            Assert.True(
                before.InEitherSide,
                $"Row premise: the unmatched state must hold a carryable somewhere. Actual: {before.Describe()}");
            AssertAbsentFromBoth(block, $"{because} (unmatched state was: {before.Describe()})");
            return after;
        }

        /// <summary>
        /// The hard reversibility contract: the post-state must equal the measured
        /// pre-state <b>array for array</b>. Restoring into a view the pass never emptied
        /// fails here, which is the mirror half of the contract that a one-way strip
        /// cannot satisfy.
        /// </summary>
        public static void AssertRestoredTo(CarryableParity before, Block block, string because)
        {
            var after = Inspect(block);
            Assert.True(
                after.BlockSideCount == before.BlockSideCount
                && after.CollectibleSideCount == before.CollectibleSideCount
                && after.IsCarryable == before.IsCarryable,
                $"The post-state must match the measured pre-state array for array - {because}. "
                + $"Before: {before.Describe()}. After: {after.Describe()}");
        }
    }
}

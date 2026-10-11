using System;
using System.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Resolves vanilla fence-gate blocks for the interact-while-carrying rows. Gates
    /// (BlockFenceGate / BlockFenceGateRoughHewn : BlockBaseDoor) are variant-based:
    /// the closed default is placed directly and the open/close toggle exchanges the
    /// block for its state-variant sibling in place (closed ↔ opened via ExchangeBlock),
    /// so the gate cell's block code is the world-truth observable - no block entity
    /// involved.
    /// </summary>
    public static class GateFixture
    {
        /// <summary>
        /// Finds a deterministic closed variant of the given gate family (first code
        /// part, e.g. "wattlegate") from the loaded block registry, failing fast with
        /// the registered codes when the family or its state variant is missing.
        /// </summary>
        public static Block FindClosedGate(IWorldAccessor world, string firstCodePart)
        {
            var family = world.Blocks
                .Where(b => b?.Code != null && b.Code.FirstCodePart() == firstCodePart)
                .ToArray();

            var sample = string.Join(", ", family.Take(6).Select(b => b!.Code));
            Assert.True(family.Length > 0,
                $"No {firstCodePart} block variants registered in this world " +
                $"(expected the vanilla gate family; registry sample: {sample})");

            var closed = family
                .Where(b => b!.Variant != null
                    && b.Variant.TryGetValue("state", out var state)
                    && state == "closed")
                .OrderBy(b => b!.Code.Path, StringComparer.Ordinal)
                .FirstOrDefault();

            Assert.True(closed != null,
                $"No closed-state {firstCodePart} variant found (registered: {sample}) - " +
                "vanilla gate variant keys changed; update GateFixture.");

            return closed!;
        }

        /// <summary>The state variant ("closed"/"opened") of a block, or null when absent.</summary>
        public static string? StateOf(Block? block)
        {
            return block != null
                && block.Variant != null
                && block.Variant.TryGetValue("state", out var state)
                ? state
                : null;
        }
    }
}

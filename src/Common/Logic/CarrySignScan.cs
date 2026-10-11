using System.Collections.Generic;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    /// <summary>
    /// Structural scan for the wall signs attached to a block's pickup footprint.
    ///
    /// Pure geometry on purpose: no permission filter, so the server can ask the
    /// same question the client's optimistic path asked before it removed
    /// anything. That matters for pickup reconciliation - the client's local
    /// capture has no side filter, so it cleared <b>every</b> candidate sign cell,
    /// including the ones the server's claim filter later leaves behind. The
    /// server therefore has to know the full candidate set to tell the client
    /// which of those cells it got wrong.
    /// </summary>
    internal static class CarrySignScan
    {
        /// <summary>
        /// A wall sign hanging on a cell of the parent's pickup footprint.
        /// </summary>
        /// <param name="SignPos">The sign's own cell - the cell a client clears optimistically.</param>
        /// <param name="SupportPos">The footprint cell the sign hangs on.</param>
        /// <param name="Facing">The sign's facing, i.e. the direction from the sign towards its support.</param>
        internal readonly record struct Candidate(BlockPos SignPos, BlockPos SupportPos, BlockFacing Facing);

        /// <summary>
        /// Every wall sign that hangs on a cell of <paramref name="parentPos"/>'s
        /// pickup footprint, in footprint order. A sign qualifies when its own cell
        /// is outside the footprint, it is a wall sign, and the cell its facing
        /// points at is inside the footprint.
        /// </summary>
        /// <param name="world">The world accessor.</param>
        /// <param name="parentPos">The parent block's cell.</param>
        /// <param name="parentBlock">The parent block.</param>
        /// <param name="knownFootprint">
        /// The caller's already-computed footprint for the same parent, when it has
        /// one. <see cref="CarryFootprint.GetFootprint"/> costs one read plus
        /// arithmetic for a behavior multiblock (its box comes from the controller
        /// behavior and is read-free) and a bounded <c>GetBlock</c> walk only for the
        /// old-style fallback, so passing a set in keeps the sweep on the caller's
        /// computation. Null recomputes it: that is the server's reconciliation sweep,
        /// which runs before the pickup computes its own footprint — the computation
        /// exists a few lines later but cannot reach back into this call.
        /// </param>
        internal static List<Candidate> Scan(
            IWorldAccessor world, BlockPos parentPos, Block parentBlock, HashSet<BlockPos>? knownFootprint = null)
        {
            var candidates = new List<Candidate>();

            var footprint = knownFootprint ?? CarryFootprint.GetFootprint(world, parentPos, parentBlock);
            if (footprint.Count == 0) return candidates;

            var seen = new HashSet<BlockPos>();
            foreach (var footPos in footprint)
            {
                foreach (var face in BlockFacing.HORIZONTALS)
                {
                    var candidatePos = footPos.Copy();
                    candidatePos.Offset(face);
                    if (footprint.Contains(candidatePos) || !seen.Add(candidatePos)) continue;

                    var candidateBlock = world.BlockAccessor.GetBlock(candidatePos);
                    if (candidateBlock.Id == 0) continue;
                    if (candidateBlock.Class != BlockClasses.Sign) continue;
                    if (!IsWallSign(candidateBlock)) continue;

                    var signFacing = GetWallSignFacing(candidateBlock);
                    if (signFacing == null) continue;

                    var supportPos = candidatePos.Copy();
                    supportPos.Offset(signFacing);
                    if (!footprint.Contains(supportPos)) continue;

                    candidates.Add(new Candidate(candidatePos, supportPos, signFacing));
                }
            }

            return candidates;
        }

        /// <summary>
        /// Wall signs hang on a support block; a standing sign is not a candidate.
        /// </summary>
        internal static bool IsWallSign(Block block)
        {
            if (block.Variant == null) return false;
            return block.Variant.TryGetValue("attachment", out var attachment)
                && attachment == "wall";
        }

        /// <summary>
        /// Reads the facing from a sign code's last path segment (e.g.
        /// "plaque-fancy-bismuth-wall-north" -> north). Null when the code carries
        /// no facing segment.
        /// </summary>
        internal static BlockFacing? GetWallSignFacing(Block block)
        {
            var path = block.Code.Path;
            var parts = path.Split('-', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            var lastPart = parts[parts.Length - 1];
            return BlockFacing.FromCode(lastPart);
        }
    }
}

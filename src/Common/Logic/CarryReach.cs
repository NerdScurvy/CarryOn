using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Logic
{
    /// <summary>
    /// The single reach metric for every CarryOn interaction gate.
    ///
    /// Three properties are deliberate:
    ///
    /// <b>Server-side eye anchor.</b> <c>Entity.LocalEyePos</c> is a <c>Vec3d</c> offset from
    /// <c>Pos</c> that <c>EntityPlayer.updateEyeHeight</c> maintains on the server as well as on
    /// the client (crouch, sneak, floor-sit, swim, death and mount seats all included), while
    /// <c>EntityPlayer.CameraPos</c> is set only by the client and unusable here. Anchoring on the
    /// feet instead - the metric the three older gates shipped with - false-rejects vertical
    /// targets the crosshair reaches: at the default reach of 6, a block 6 up and 1 across is 6.08
    /// away by feet and 4.5 by eye. The eye anchor is also the entire vertical allowance, so there
    /// is no separate tolerance constant that can drift out of step with it.
    ///
    /// <b>Creative is exempt inside the predicate</b>, not at the call sites, so no caller can
    /// forget it. It resolves before any distance is computed, and a non-player actor is never
    /// exempt.
    ///
    /// <b>Two target overloads.</b> The block gates measure a cell centre; the entity gates measure
    /// an entity's own <c>Vec3d</c> position. A single <c>BlockPos</c> signature would round those
    /// through <c>AsBlockPos</c> and move their boundary by up to half a block.
    ///
    /// <b>The limit is a <c>double</c>.</b> Every configured source is an <c>int</c>, but
    /// <c>CarryPickupService</c> holds its resolved limit in a <c>double</c> local, so accepting a
    /// double lets that call site pass its own value instead of casting. Widening can never truncate
    /// a limit the way a narrowing cast would.
    ///
    /// Callers pass the already-resolved distance and keep their own config accessor. The
    /// written-cell leash for placement validation keeps its separate feet anchor: it bounds
    /// client-supplied cells, which is not an interaction range.
    /// </summary>
    internal static class CarryReach
    {
        /// <summary>
        /// Whether <paramref name="actor"/> may act on the block cell
        /// <paramref name="target"/>, measured to the cell's centre.
        /// </summary>
        internal static bool IsWithinReach(Entity actor, BlockPos target, double maxDistance)
            => IsWithinReach(actor, CellCentre(target), maxDistance);

        /// <summary>
        /// Whether <paramref name="actor"/> may act on the point
        /// <paramref name="target"/>.
        /// </summary>
        internal static bool IsWithinReach(Entity actor, Vec3d target, double maxDistance)
        {
            if (IsCreative(actor)) return true;
            return EyePosition(actor).DistanceTo(target) <= maxDistance;
        }

        /// <summary>
        /// Whether <paramref name="actor"/> is a creative-mode player, and therefore exempt from
        /// every reach gate.
        /// </summary>
        internal static bool IsCreative(Entity actor)
            => (actor as EntityPlayer)?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

        /// <summary>
        /// The actor's server-side eye position, falling back to its feet for anything that is not
        /// an <see cref="EntityPlayer"/>.
        /// </summary>
        internal static Vec3d EyePosition(Entity actor)
        {
            Vec3d pos = actor.Pos.XYZ;
            if (actor is not EntityPlayer player) return pos;

            Vec3d eye = player.LocalEyePos;
            return new Vec3d(pos.X + eye.X, pos.Y + eye.Y, pos.Z + eye.Z);
        }

        private static Vec3d CellCentre(BlockPos target)
            => new Vec3d(target.X + 0.5, target.Y + 0.5, target.Z + 0.5);
    }
}

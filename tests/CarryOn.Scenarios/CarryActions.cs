using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Driver that performs CarryOn carry actions against server state for a joined test
    /// player, mirroring the operations the client triggers over the mod channel.
    /// </summary>
    public sealed class CarryActions
    {
        private readonly IWorldAccessor world;
        private readonly ICarryManager carry;

        public CarryActions(IWorldAccessor world, ICarryManager carry)
        {
            this.world = world;
            this.carry = carry;
        }

        /// <summary>Picks the block at <paramref name="pos"/> into the hands carry slot.</summary>
        /// <returns>The carried block when successful, otherwise null.</returns>
        public CarriedBlock? PickUpIntoHands(EntityPlayer player, BlockPos pos, out string failureCode)
        {
            failureCode = "";
            if (!carry.TryPickUp(player, pos, CarrySlot.Hands, ref failureCode))
            {
                return null;
            }

            return carry.GetCarried(player, CarrySlot.Hands);
        }

        /// <summary>Picks the block at <paramref name="pos"/> into the given carry slot.</summary>
        /// <returns>The carried block when successful, otherwise null.</returns>
        public CarriedBlock? PickUpIntoSlot(EntityPlayer player, BlockPos pos, CarrySlot slot, out string failureCode)
        {
            failureCode = "";
            if (!carry.TryPickUp(player, pos, slot, ref failureCode))
            {
                return null;
            }

            return carry.GetCarried(player, slot);
        }

        /// <summary>Picks a bag out of the ground storage slot targeted by <paramref name="selection"/>.</summary>
        /// <returns>The carried bag when successful, otherwise null.</returns>
        public CarriedBlock? PickUpBagFromGroundStorage(EntityPlayer player, BlockSelection selection, out string failureCode)
        {
            failureCode = "";
            return carry.TryPickUpGroundStorageBag(player, selection, ref failureCode)
                ? carry.GetCarried(player, CarrySlot.Hands)
                : null;
        }

        /// <summary>Stows the carried bag into the player's vanilla backpack inventory.</summary>
        public bool StoreBagInBackpack(EntityPlayer player, out string failureCode)
        {
            failureCode = "";
            return carry.TryStoreBagInBackpack(player, ref failureCode);
        }

        /// <summary>Drops carried blocks from the given slots (world placement, then item/entity fallback).</summary>
        public void Drop(EntityPlayer player, params CarrySlot[] slots)
        {
            carry.DropCarried(player, slots);
        }

        /// <summary>Places a carried block from the given slot at the selection's position.</summary>
        /// <returns>The position the block was placed at when successful, otherwise null.</returns>
        public BlockPos? PlaceDown(IPlayer player, CarrySlot slot, BlockSelection selection, out string failureCode)
        {
            failureCode = "";
            return carry.TryPlaceDownAt(player, slot, selection, out var placedAt, ref failureCode)
                ? placedAt
                : null;
        }

        /// <summary>Builds a vanilla-style placement selection aiming into the empty cell at <paramref name="target"/>.</summary>
        public static BlockSelection AimDownAt(BlockPos target)
        {
            return new BlockSelection
            {
                Position = target,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(target.X + 0.5, target.Y, target.Z + 0.5),
                SelectionBoxIndex = 0,
            };
        }
    }
}
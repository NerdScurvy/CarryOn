using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Builds deterministic ground-storage rows for the bag-flow scenarios, replicating the
    /// server half of vanilla placement: the storage block is set, its block entity adopts
    /// the bag stack's storage properties, and the stack is deposited into the slot the
    /// hit position (0.25, 0.25, mesh angle 0) resolves to - slot 0 for every layout.
    /// </summary>
    public static class GroundStorageFixture
    {
        public const string BagCode = "hunterbackpack";

        /// <summary>
        /// Resolves a concrete item code from the loaded content by first code part
        /// (e.g. "basket" → the variant-coded hand basket), ordered by code path -
        /// never invented: codes with variants are read from the registry.
        /// </summary>
        public static string ResolveItemCode(IWorldAccessor world, string firstCodePart)
        {
            var match = world.Items
                .Where(item => item != null && item.Code != null
                    && item.Code.Domain == "game"
                    && (item.Code.Path == firstCodePart
                        || item.Code.Path.Split('-')[0] == firstCodePart))
                .OrderBy(item => item.Code!.ToString(), StringComparer.Ordinal)
                .ToList();

            return match.Count > 0
                ? match[0].Code!.ToString()
                : throw new InvalidOperationException(
                    $"No game item with first code part '{firstCodePart}' loaded in this world");
        }

        /// <summary>
        /// Places a ground storage block at <paramref name="pos"/> holding a single
        /// <paramref name="itemCode"/> stack, and returns the selection that targets that
        /// slot (X/Z 0.25 at mesh angle 0 always resolves to slot 0 via
        /// <see cref="BlockEntityGroundStorage.GetSlotAt"/>).
        /// </summary>
        public static BlockSelection PlaceBag(IWorldAccessor world, BlockPos pos, string itemCode)
        {
            var groundStorageBlock = world.GetBlock(new AssetLocation("groundstorage")) as BlockGroundStorage;
            if (groundStorageBlock == null)
                throw new InvalidOperationException("game:groundstorage block type not available in this world");

            world.BlockAccessor.SetBlock(groundStorageBlock.Id, pos);

            var beg = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage
                ?? throw new InvalidOperationException($"No BlockEntityGroundStorage materialized at {pos} after SetBlock");

            var stack = new ItemStack(world.GetItem(new AssetLocation(itemCode)));
            if (stack.Collectible == null || stack.Collectible.Code == null)
                throw new InvalidOperationException($"Item {itemCode} did not resolve to a collectible");

            var selection = TargetSlotSelection(pos);
            beg.MeshAngle = 0f;
            beg.DetermineStorageProperties(new DummySlot(stack));

            var slot = beg.GetSlotAt(selection)
                ?? throw new InvalidOperationException($"GetSlotAt returned null after DetermineStorageProperties at {pos}");
            slot.Itemstack = stack;
            slot.MarkDirty();
            beg.MarkDirty(true);

            return selection;
        }

        /// <summary>
        /// Places an empty ground storage block at <paramref name="pos"/> (no stack, no
        /// storage properties adopted) and returns the selection that would target its
        /// slot 0.
        /// </summary>
        public static BlockSelection PlaceEmptyStorage(IWorldAccessor world, BlockPos pos)
        {
            var groundStorageBlock = world.GetBlock(new AssetLocation("groundstorage")) as BlockGroundStorage;
            if (groundStorageBlock == null)
                throw new InvalidOperationException("game:groundstorage block type not available in this world");

            world.BlockAccessor.SetBlock(groundStorageBlock.Id, pos);

            var beg = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage
                ?? throw new InvalidOperationException($"No BlockEntityGroundStorage materialized at {pos} after SetBlock");
            beg.MeshAngle = 0f;

            return TargetSlotSelection(pos);
        }

        /// <summary>
        /// The selection whose hit position maps to slot 0 for every ground-storage layout
        /// at mesh angle 0.
        /// </summary>
        public static BlockSelection TargetSlotSelection(BlockPos pos)
        {
            return new BlockSelection
            {
                Position = pos,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.25, 0.5, 0.25),
                SelectionBoxIndex = 0,
            };
        }
    }
}
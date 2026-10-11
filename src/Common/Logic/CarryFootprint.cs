using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Common.Logic
{
    /// <summary>
    /// Shared footprint computation for carried blocks: the world cells a block
    /// occupies (pickup view - a dummy target resolves to its controller, and a
    /// structure's configured multiblock box is the footprint), and the cells a
    /// placement of a carried block would write (placement view - target cell,
    /// multiblock variant footprint, attached children at their estimated rotated
    /// offsets). Both views walk the same behavior-driven box anchored at the
    /// same controller cell, so the pickup and placement permission gates cannot
    /// disagree about a multiblock's write set. Used by the whole-footprint
    /// permission gates and the attached-sign capture sweep.
    /// </summary>
    internal static class CarryFootprint
    {
        /// <summary>
        /// The cell whose block actually owns <paramref name="pos"/>: a
        /// <c>BlockMultiblock</c> dummy resolves through <c>OffsetInv</c> to the
        /// controller, and every other target is its own origin. The returned block is
        /// the block at that cell - <paramref name="parentBlock"/> itself when
        /// <paramref name="pos"/> is not a dummy, otherwise one
        /// <c>GetBlock</c> read at the resolved cell.
        /// <para>
        /// Shared by <see cref="GetFootprint"/> and the pickup paths that need the
        /// controller's <b>identity</b> rather than its cell set (the whole-footprint
        /// write set and the inventory-open guard must agree on which cell is the
        /// controller, or they answer about different structures).
        /// </para>
        /// </summary>
        public static BlockPos ResolveController(IWorldAccessor world, BlockPos pos, Block parentBlock, out Block controllerBlock)
        {
            // A dummy target resolves to its controller; every other target (a
            // multiblock controller or a plain block) is its own origin. The dummy
            // check stays first: a block that is both a BlockMultiblock and somehow
            // carries the behavior must resolve through OffsetInv like any dummy.
            if (parentBlock is BlockMultiblock dummyTarget)
            {
                var controllerPos = pos.Copy().Add(dummyTarget.OffsetInv);
                controllerBlock = world.BlockAccessor.GetBlock(controllerPos);
                return controllerPos;
            }

            controllerBlock = parentBlock;
            return pos.Copy();
        }

        /// <summary>
        /// The world cells the block at <paramref name="pos"/> occupies: the pickup's
        /// write set. A dummy target resolves to its controller (the controller cell is
        /// part of the footprint), and a structure whose controller carries a
        /// <c>BlockBehaviorMultiblock</c> contributes exactly that behavior's configured
        /// box - the cells <c>OnBlockPlaced</c> filled with dummies and the cells
        /// <c>OnBlockRemoved</c> sweeps, which carries no pointing-back check - walked
        /// from the controller the same way <c>GetPlacementFootprint</c> walks it. A
        /// pre-behavior structure (a controller without the behavior) keeps the old
        /// scan as a fallback: the box the target dummy's own offset describes, keeping
        /// the dummy cells pointing back at the controller. A plain block yields only
        /// its own cell.
        /// </summary>
        public static HashSet<BlockPos> GetFootprint(IWorldAccessor world, BlockPos pos, Block parentBlock)
        {
            var footprint = new HashSet<BlockPos>();

            var origin = ResolveController(world, pos, parentBlock, out var controllerBlock);
            var dummyTarget = parentBlock as BlockMultiblock;

            if (controllerBlock.GetBehavior<BlockBehaviorMultiblock>() is { } behavior)
            {
                // The behavior's own box is the real write set, so walk it rather than
                // guessing a neighborhood radius: the engine placed a dummy in every
                // non-controller cell of it and removes every BlockMultiblock cell of
                // it, and a scan radius can only under-count a structure configured
                // wider than that radius. No world reads are needed - the behavior's
                // sizes are the only input. IterateOverEach reuses one BlockPos
                // instance across iterations, so every cell is copied out.
                behavior.IterateOverEach(origin, cell =>
                {
                    footprint.Add(cell.Copy());
                    return true;
                });

                // An in-structure target is already inside the box; kept so a
                // desynced dummy position still gates its own cell.
                footprint.Add(pos.Copy());
                return footprint;
            }

            if (dummyTarget != null)
            {
                // Old-style fallback: the controller carries no multiblock behavior,
                // so there are no configured sizes to walk. Scan the box this target
                // dummy's own offset describes and keep the dummy cells that point
                // back at the controller.
                var radiusX = Math.Abs(dummyTarget.OffsetInv.X);
                var radiusY = Math.Abs(dummyTarget.OffsetInv.Y);
                var radiusZ = Math.Abs(dummyTarget.OffsetInv.Z);
                for (int dx = -radiusX; dx <= radiusX; dx++)
                for (int dy = -radiusY; dy <= radiusY; dy++)
                for (int dz = -radiusZ; dz <= radiusZ; dz++)
                {
                    var checkPos = new BlockPos(origin.X + dx, origin.Y + dy, origin.Z + dz);
                    var checkBlock = world.BlockAccessor.GetBlock(checkPos);
                    if (checkBlock is BlockMultiblock checkMulti)
                    {
                        var checkOrigin = checkPos.Copy();
                        checkOrigin.Add(checkMulti.OffsetInv);
                        if (checkOrigin.Equals(origin))
                        {
                            footprint.Add(checkPos.Copy());
                        }
                    }
                }

                // A dummy target resolves to the structure: the controller cell
                // belongs to the footprint too.
                footprint.Add(origin.Copy());
            }

            footprint.Add(pos.Copy());
            return footprint;
        }

        /// <summary>
        /// The world cells a placement of <paramref name="carriedBlock"/> at
        /// <paramref name="selection"/> would write: the target cell, every multiblock
        /// footprint cell of the variant that will be placed, and every attached-child
        /// cell at its estimated rotated offset. Support cells are read-only and
        /// excluded. The multiblock footprint unions the resolved placement variant
        /// (look-aware for player placement, the drop-facing variant on the dropped
        /// path) and the carried block's own footprint, so the rare no-hotbar fallback
        /// path - which places the stack's own block rather than the variant - is
        /// covered conservatively.
        /// </summary>
        public static HashSet<BlockPos> GetPlacementFootprint(
            IWorldAccessor world,
            Entity entity,
            CarriedBlock carriedBlock,
            BlockSelection selection,
            bool dropped)
        {
            var target = selection.Position;
            var cells = new HashSet<BlockPos> { target.Copy() };

            var block = carriedBlock.Block;
            if (block != null)
            {
                foreach (var variant in ResolvePlacementVariants(world, entity, carriedBlock, selection, dropped))
                {
                    AddMultiblockFootprint(variant, target, cells);
                }
            }

            if (carriedBlock.AttachedBlocks != null)
            {
                var rotationSteps = CarryRotationHelper.EstimatePreflightRotation(carriedBlock, entity, selection, dropped);
                foreach (var child in carriedBlock.AttachedBlocks)
                {
                    var rotatedOffset = CarryRotationHelper.RotateOffset(child.RelativeOffset, rotationSteps);
                    var childPos = target.Copy();
                    childPos.Add(rotatedOffset);
                    cells.Add(childPos);
                }
            }

            return cells;
        }

        private static IEnumerable<Block> ResolvePlacementVariants(
            IWorldAccessor world,
            Entity entity,
            CarriedBlock carriedBlock,
            BlockSelection selection,
            bool dropped)
        {
            var block = carriedBlock.Block!;

            // The carried stack's own block covers the no-hotbar fallback path.
            yield return block;

            Block? variant = null;
            if (dropped)
            {
                // Same variant resolution the direct drop placement uses.
                var meshFacing = selection.Face;
                if (meshFacing != null)
                {
                    var assetLocation = block.Code.Clone();
                    var baseCode = assetLocation.FirstCodePart();
                    assetLocation.Path = $"{baseCode}-{meshFacing.Code}";
                    variant = world.GetBlock(assetLocation);
                }
            }
            else if (entity is EntityPlayer { Player: { } player })
            {
                try
                {
                    // The same seam vanilla placement resolves through
                    // (BlockBehaviorHorizontalOrientable and friends).
                    variant = block.GetInterface<ILookAwarePlacement>(world, selection.Position)
                        ?.GetLookAwareBlockVariant(player, carriedBlock.ItemStack, selection);
                }
                catch
                {
                    // Look-aware resolution throws when side variants are missing; the
                    // block's own footprint (yielded above) still covers the write set.
                    variant = null;
                }
            }

            if (variant != null && variant.Id != block.Id)
                yield return variant;
        }

        private static void AddMultiblockFootprint(Block variant, BlockPos target, HashSet<BlockPos> cells)
        {
            var multiblock = variant.GetBehavior<BlockBehaviorMultiblock>();
            if (multiblock == null) return;

            // IterateOverEach walks the variant's full footprint including the
            // controller cell, driven by the variant's own sizes/controller offset -
            // exactly the cells that variant's placement writes. It reuses one
            // BlockPos instance across iterations, so every cell is copied out.
            multiblock.IterateOverEach(target, cell =>
            {
                if (!cell.Equals(target))
                    cells.Add(cell.Copy());
                return true;
            });
        }
    }
}

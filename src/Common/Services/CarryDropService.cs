using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryDropService(ICoreAPI api, ICarryManager carryManager)
    {

        /// <summary>
        /// Drops carried blocks from the supplied carry slots.
        /// </summary>
        /// <param name="entity">Entity dropping carried blocks.</param>
        /// <param name="slots">Carry slots to evaluate for drops.</param>
        /// <param name="range">Search radius for attempted world placement.</param>
        public void DropCarried(Entity entity, IEnumerable<CarrySlot> slots, int range = 4)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(slots);
            if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));

            var remaining = slots
                .Select(s => entity.GetCarried(s))
                .OfType<CarriedBlock>()
                .OrderBy(c => c.Block.GetBehavior<BlockBehaviorMultiblock>() != null)
                .ToList();
            if (remaining.Count == 0) return;

            var blockPlacer = new BlockPlacer(entity.Api);
            foreach (var carriedBlock in remaining)
            {
                DropCarriedBlock(entity, carriedBlock, range, blockPlacer);
            }
        }

        /// <summary>
        /// Drops one carried block either by placing into world or converting to item drops.
        /// </summary>
        /// <param name="entity">Entity dropping the carried block.</param>
        /// <param name="carriedBlock">Carried block to drop.</param>
        /// <param name="range">Search radius for attempted world placement.</param>
        /// <param name="blockPlacer">Optional reusable block placer helper.</param>
        public void DropCarriedBlock(Entity entity, CarriedBlock carriedBlock, int range = 4, BlockPlacer? blockPlacer = null)
        {
            if (carriedBlock == null) return;

            ArgumentNullException.ThrowIfNull(entity);

            if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));

            var player = (entity as EntityPlayer)?.Player as IServerPlayer;

            var centerBlock = entity.Pos.AsBlockPos.UpCopy();
            blockPlacer ??= new BlockPlacer(entity.Api);

            var blockSelection = blockPlacer.FindBlockPlacement(carriedBlock.Block, centerBlock, range);
            if (blockSelection == null)
            {
                DropBlockAsItem(carriedBlock, centerBlock, player, entity);
                return;
            }

            if (carryManager.TryPlaceDown(entity, carriedBlock, blockSelection, dropped: true))
            {
                return;
            }

            DropBlockAsItem(carriedBlock, centerBlock, player, entity);
        }

        /// <summary>
        /// Drops the carried block as item entities, including any serialized inventory contents.
        /// </summary>
        /// <param name="carriedBlock">Carried block being dropped.</param>
        /// <param name="centerBlock">Center position for item spawning and audio.</param>
        /// <param name="player">Optional acting server player for contextual drops.</param>
        /// <param name="entity">Entity source for state mutation and events.</param>
        public void DropBlockAsItem(CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer? player, Entity entity)
        {
            var world = api.World;
            var blockDestroyed = false;
            var hadContents = false;
            var dropCount = 1;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            if (carriedBlock.BlockEntityData?["inventory"] is TreeAttribute inventory && inventory["slots"] is TreeAttribute invSlots)
            {
                foreach (var item in invSlots.Values.Cast<ItemstackAttribute>())
                {
                    var itemStack = (ItemStack)item.GetValue();
                    world.SpawnItemEntity(itemStack, dropVec3d);
                    hadContents = true;
                    dropCount++;
                }
                var carriedItemStack = carriedBlock.ItemStack.Clone();
                carriedItemStack.Attributes.Remove("contents");
                world.SpawnItemEntity(carriedItemStack, dropVec3d);
            }
            else
            {
                var itemStacks = carriedBlock.Block.GetDrops(world, centerBlock, player);
                if (itemStacks.Length == 1 && itemStacks[0].Id == carriedBlock.ItemStack.Id)
                {
                    world.SpawnItemEntity(carriedBlock.ItemStack, dropVec3d);
                }
                else
                {
                    blockDestroyed = true;
                    foreach (var itemStack in itemStacks)
                    {
                        world.SpawnItemEntity(itemStack, dropVec3d);
                        hadContents = true;
                        dropCount++;
                    }
                }
            }

            var breakSound = carriedBlock.Block.Sounds.GetBreakSound(player).Location ?? new AssetLocation("game:sounds/block/planks");
            world.PlaySoundAt(breakSound, (double)centerBlock.X, (double)centerBlock.Y, (double)centerBlock.Z);
            carryManager.RemoveCarried(entity, carriedBlock.Slot);

            if (blockDestroyed)
                world.Logger.Audit($"[{ModId}] Player {player?.PlayerName} dropped carried block {carriedBlock.Block.Code} at {centerBlock} and it was destroyed dropping {dropCount} items.");
            else
                world.Logger.Audit($"[{ModId}] Player {player?.PlayerName} dropped carried block {carriedBlock.Block.Code} as item at {centerBlock} spilling {dropCount} items from its contents.");

            carryManager.CarryEvents?.TriggerBlockDropped(centerBlock, entity, carriedBlock, blockDestroyed, hadContents, blockPlaced: false);
        }
    }
}
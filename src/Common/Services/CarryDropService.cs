using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryDropService(ICoreAPI api, ICarryManager carryManager, IConfigProvider configProvider)
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
                .Select(s => carryManager.GetCarried(entity, s))
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
        /// If the carried block has attached children, uses cluster-aware placement.
        /// </summary>
        /// <param name="entity">Entity dropping the carried block.</param>
        /// <param name="carriedBlock">Carried block to drop.</param>
        /// <param name="range">Search radius for attempted world placement.</param>
        /// <param name="blockPlacer">Optional reusable block placer helper.</param>
        public void DropCarriedBlock(Entity entity, CarriedBlock carriedBlock, int range = 4, BlockPlacer? blockPlacer = null)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (carriedBlock == null) return;

            if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));

            var player = (entity as EntityPlayer)?.Player as IServerPlayer;

            var centerBlock = entity.Pos.AsBlockPos.UpCopy();

            var dropMode = configProvider?.Config?.CarriedBlockEntity?.DropMode ?? DropMode.Items;

            if (dropMode == DropMode.EntityAlways)
            {
                DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity);
                return;
            }

            blockPlacer ??= new BlockPlacer(entity.Api);

            if (carriedBlock.HasAttachedBlocks)
            {
                DropClusterBlock(entity, carriedBlock, range, blockPlacer, centerBlock, player);
                return;
            }

            var blockSelection = blockPlacer.FindBlockPlacement(carriedBlock.Block, centerBlock, range);
            if (blockSelection == null)
            {
                if (dropMode == DropMode.EntityOnFailedPlacement)
                {
                    DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity: true);
                    return;
                }
                DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity);
                return;
            }

            var failureCode = FailureCode.Ignore;
            if (carryManager.TryPlaceDown(entity, carriedBlock, blockSelection, ref failureCode, dropped: true))
                return;

            if (dropMode == DropMode.EntityOnFailedPlacement)
            {
                DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity: true);
                return;
            }

            DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity);
        }

        private void DropClusterBlock(Entity entity, CarriedBlock carriedBlock, int range, BlockPlacer blockPlacer, BlockPos centerBlock, IServerPlayer? player)
        {
            var dropMode = configProvider?.Config?.CarriedBlockEntity?.DropMode ?? DropMode.Items;

            var blockSelection = blockPlacer.FindClusterPlacement(
                carriedBlock.Block,
                carriedBlock.AttachedBlocks as IReadOnlyList<AttachedCarriedBlock>,
                centerBlock,
                range);

            if (blockSelection != null)
            {
                var failureCode = FailureCode.Ignore;
                if (carryManager.TryPlaceDown(entity, carriedBlock, blockSelection, ref failureCode, dropped: true))
                    return;

                if (dropMode == DropMode.EntityOnFailedPlacement)
                {
                    DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity: true);
                    return;
                }
            }
            else if (dropMode == DropMode.EntityOnFailedPlacement)
            {
                DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity: true);
                return;
            }

            // Cluster placement failed -drop parent plus all children as items
            var world = api.World;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity);

            var attachedCount = carriedBlock.AttachedBlocks?.Count ?? 0;
            if (attachedCount > 0)
            {
                foreach (var child in carriedBlock.AttachedBlocks!)
                {
                    world.SpawnItemEntity(child.ItemStack, dropVec3d);
                }
                api.World.Logger.Audit($"[{ModId}] Player {player?.PlayerName ?? "unknown"} dropped {attachedCount} attached child block(s) from {carriedBlock.Block?.Code ?? "unknown"} at {centerBlock}");
            }
        }

        /// <summary>
        /// Drops the carried block as CarriedBlockEntity or item entities, including any serialized inventory contents.
        /// </summary>
        /// <param name="carriedBlock">Carried block being dropped.</param>
        /// <param name="centerBlock">Center position for item spawning and audio.</param>
        /// <param name="player">Optional acting server player for contextual drops.</param>
        /// <param name="entity">Entity source for state mutation and events.</param>
        /// <param name="forceEntity">When true, spawns a block entity instead of item drops regardless of DropMode.</param>
        public void DropBlockAsEntityOrItem(CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer? player, Entity entity, bool forceEntity = false)
        {
            if (api.Side == EnumAppSide.Server && (forceEntity || configProvider?.Config?.CarriedBlockEntity?.DropMode == DropMode.EntityAlways))
            {
                var carrySystem = api.ModLoader.GetModSystem<CarrySystem>();
                var entityService = carrySystem?.CarriedBlockEntityService;
                if (entityService != null)
                {
                    var playerUid = player?.PlayerUID ?? "unknown";
                    var candidatePos = new Vec3d(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
                    var config = configProvider?.Config?.CarriedBlockEntity;
                    var randomYaw = config?.RandomDropRotation ?? true;
                    var scale = config?.Scale ?? 1.0f;
                    entityService.SpawnCarriedBlockEntityWithGravity(carriedBlock, playerUid, candidatePos, randomYaw: randomYaw, scale: scale);
                    carryManager.RemoveCarried(entity, carriedBlock.Slot);

                    api.World.Logger.Audit($"[{ModId}] Player {player?.PlayerName ?? "unknown"} dropped carried block {carriedBlock.Block?.Code ?? "unknown"} as entity at {centerBlock}");
                    carryManager.CarryEvents?.TriggerBlockDropped(centerBlock, entity, carriedBlock, destroyed: false, hadContents: false, blockPlaced: false);
                    return;
                }
            }

            var world = api.World;
            var blockDestroyed = false;
            var hadContents = false;
            var dropCount = 1;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            // Spill items from configured beData attributes FIRST so they're removed
            // from BE data before the itemstack is spawned (prevents duplication).
            var beData = carriedBlock.BlockEntityData;
            if (beData != null)
            {
                var carryBehavior = carriedBlock.GetCarryableBehavior();
                var attrNames = carryBehavior?.DataAttributes;
                if (attrNames != null && attrNames.Length > 0)
                {
                    foreach (var attrName in attrNames)
                    {
                        if (string.IsNullOrEmpty(attrName)) continue;
                        if (beData[attrName] is not ItemstackAttribute itemAttr) continue;
                        var stack = itemAttr.GetValue() as ItemStack;
                        if (stack == null) continue;
                        world.SpawnItemEntity(stack, dropVec3d);
                        beData.RemoveAttribute(attrName);
                        hadContents = true;
                        dropCount++;
                    }
                }
            }

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

            var breakSound = carriedBlock.Block.Sounds.GetBreakSound(player).Location ?? new AssetLocation(CarryCode.SoundPath.DefaultBreak);
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
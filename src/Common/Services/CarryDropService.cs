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
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Services
{
    internal sealed class CarryDropService(ICoreAPI api, ICarryManager carryManager, IConfigProvider configProvider, GroundStorageItemPlacement groundStorage)
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

            var remaining = new List<CarriedBlock>();
            foreach (var slot in slots)
            {
                var read = carryManager.GetCarriedReadResult(entity, slot, out var rawTree);
                if (read.Carried is { } carried)
                {
                    remaining.Add(carried);
                    continue;
                }

                if (read.IsFailed)
                {
                    // Present-but-unreadable carry (e.g. block from a removed/disabled mod): drop it
                    // as a carried block entity preserving the untouched raw tree instead of losing
                    // it silently.
                    UnusableCarriedBlockDrop.TryDrop(api, carryManager, entity, slot, rawTree, entity.Pos.XYZ);
                }
            }

            if (remaining.Count == 0) return;

            var blockPlacer = new BlockPlacer(entity.Api);
            foreach (var carriedBlock in remaining.OrderBy(c => c.Block?.GetBehavior<BlockBehaviorMultiblock>() != null))
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

            // Item-class carry (e.g. a bag picked up from ground storage): there is no world
            // block to place. Route through the dedicated item branch, which applies the
            // DropMode gate (entity spawn or ground-storage placement) before falling back
            // to item drops.
            if (carriedBlock.Block == null)
            {
                if (api.Side == EnumAppSide.Server)
                {
                    DropCarriedItem(entity, carriedBlock, centerBlock, range, player);
                }
                return;
            }

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

            var failureCode = FailureCodes.Ignore;
            if (carryManager.TryPlaceDown(entity, carriedBlock, blockSelection, ref failureCode, dropped: true))
                return;

            if (dropMode == DropMode.EntityOnFailedPlacement)
            {
                DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity, forceEntity: true);
                return;
            }

            DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity);
        }

        /// <summary>
        /// Drops an item-class carried stack as an item entity at the player position and clears
        /// the carry slot. The stack's attributes (e.g. a bag's "backpack" contents) ride along.
        /// </summary>
        private void DropAsItemEntity(CarriedBlock carriedBlock, BlockPos centerBlock, Entity entity)
        {
            var world = api.World;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);
            world.SpawnItemEntity(carriedBlock.ItemStack, dropVec3d);
            world.PlaySoundAt(new AssetLocation(CarryCodes.SoundPaths.DefaultPlace), dropVec3d.X, dropVec3d.Y, dropVec3d.Z);
            carryManager.RemoveCarried(entity, carriedBlock.Slot);
            world.Logger.Audit($"[{ModId}] {CarryCodes.DisplayName(entity.GetName())} dropped carried item {carriedBlock.ItemStack.Collectible?.Code} at {centerBlock}");
        }

        /// <summary>
        /// Drops an item-class carried stack (e.g. a bag picked up from ground storage) through
        /// the DropMode pipeline: optional carried-block entity spawn, ground-storage placement
        /// (deposit into existing storage, then create new storage), then item drops with the
        /// bag's "backpack" contents spilled alongside.
        /// </summary>
        private void DropCarriedItem(Entity entity, CarriedBlock carriedBlock, BlockPos centerBlock, int range, IServerPlayer? player)
        {
            if (carriedBlock.ItemStack == null)
            {
                carryManager.RemoveCarried(entity, carriedBlock.Slot);
                return;
            }

            var dropMode = configProvider?.Config?.CarriedBlockEntity?.DropMode ?? DropMode.Items;

            if (dropMode == DropMode.EntityAlways && TrySpawnAsEntity(carriedBlock, centerBlock, player, entity))
                return;

            if (TryPlaceIntoGroundStorage(entity, carriedBlock, centerBlock, range))
                return;

            if (dropMode == DropMode.EntityOnFailedPlacement && TrySpawnAsEntity(carriedBlock, centerBlock, player, entity))
                return;

            DropCarriedBagContents(entity, carriedBlock, centerBlock);
        }

        /// <summary>
        /// Attempts to place an item-class carried stack into an eligible
        /// <see cref="BlockEntityGroundStorage"/> near the drop point: first depositing into
        /// an existing storage (including an empty one, which the deposit revives with the
        /// carried stack's layout), then creating new ground storage via vanilla placement
        /// rules (claims, attachment support, wall support for WallHalves). Placement and
        /// permission gates live in <see cref="GroundStorageItemPlacement"/>; success there
        /// is verified by the storage actually holding the stack, so no remainder spawn or
        /// loss handling is needed here. Only applies to ground-storable stacks.
        /// </summary>
        private bool TryPlaceIntoGroundStorage(Entity entity, CarriedBlock carriedBlock, BlockPos centerBlock, int range)
        {
            var itemStack = carriedBlock.ItemStack!;
            if (itemStack.Collectible?.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps == null)
                return false;

            var world = api.World;
            var blockPlacer = new BlockPlacer(entity.Api);
            var accessible = blockPlacer.GetAccessibleArea(centerBlock, range);

            // Existing storage: the storage block itself is not passable, so its cell never enters
            // the accessible set. Examine cells adjacent to accessible cells instead.
            var depositCandidates = new HashSet<BlockPos>(accessible);
            foreach (var cell in accessible)
            {
                depositCandidates.Add(cell.UpCopy());
                depositCandidates.Add(cell.DownCopy());
                depositCandidates.Add(cell.NorthCopy());
                depositCandidates.Add(cell.SouthCopy());
                depositCandidates.Add(cell.EastCopy());
                depositCandidates.Add(cell.WestCopy());
            }

            foreach (var candidate in depositCandidates.OrderBy(c => c.DistanceTo(centerBlock)))
            {
                var storage = world.BlockAccessor.GetBlockEntity(candidate) as BlockEntityGroundStorage;
                if (storage == null) continue;
                if (groundStorage.TryDeposit(entity, storage, carriedBlock, selection: null))
                {
                    FinishGroundStorageDrop(entity, carriedBlock, candidate);
                    return true;
                }
            }

            // New-storage creation routes through the player's hotbar slot (the vanilla
            // pipeline), so it needs a player entity.
            if (entity is not EntityPlayer) return false;

            foreach (var candidate in accessible.OrderBy(c => c.DistanceTo(centerBlock)))
            {
                if (groundStorage.TryCreate(entity, carriedBlock, candidate))
                {
                    FinishGroundStorageDrop(entity, carriedBlock, candidate);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Spills an item-class carried bag's "backpack" contents as item entities next to the
        /// (now empty) bag, mirroring how <see cref="ComputeAndSpawnDrops"/> handles block-carried
        /// inventories. Bags without a backpack attribute drop as a single item entity instead.
        /// </summary>
        private void DropCarriedBagContents(Entity entity, CarriedBlock carriedBlock, BlockPos centerBlock)
        {
            var backpack = carriedBlock.ItemStack?.Attributes?["backpack"] as ITreeAttribute;
            if (backpack == null)
            {
                DropAsItemEntity(carriedBlock, centerBlock, entity);
                return;
            }

            var world = api.World;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            var contents = InventoryConverter.ConvertBackpackToBlockInventory(backpack);
            if (contents?["slots"] is TreeAttribute invSlots)
            {
                foreach (var slot in invSlots.Values)
                {
                    if (slot is not ItemstackAttribute itemstackAttribute) continue;
                    if (itemstackAttribute.GetValue() is not ItemStack stack) continue;
                    world.SpawnItemEntity(stack, dropVec3d);
                }
            }

            var bagStack = carriedBlock.ItemStack!.Clone();
            bagStack.Attributes.Remove("backpack");
            world.SpawnItemEntity(bagStack, dropVec3d);
            world.PlaySoundAt(new AssetLocation(CarryCodes.SoundPaths.DefaultPlace), dropVec3d.X, dropVec3d.Y, dropVec3d.Z);
            carryManager.RemoveCarried(entity, carriedBlock.Slot);
            world.Logger.Audit($"[{ModId}] {CarryCodes.DisplayName(entity.GetName())} dropped carried bag {carriedBlock.ItemStack.Collectible?.Code} as items at {centerBlock}");
        }

        /// <summary>
        /// Shared bookkeeping for a bag placed into ground storage: carry removal,
        /// dropped-block event and audit log. The storage's place/remove sound is played by
        /// the placement itself (the shared service, on both deposit and creation).
        /// </summary>
        private void FinishGroundStorageDrop(Entity entity, CarriedBlock carriedBlock, BlockPos pos)
        {
            var world = api.World;

            world.BlockAccessor.MarkBlockDirty(pos);
            carryManager.RemoveCarried(entity, carriedBlock.Slot);
            carryManager.CarryEvents?.TriggerBlockDropped(pos, entity, carriedBlock, destroyed: false, hadContents: false, blockPlaced: true);

            world.Logger.Audit($"[{ModId}] {CarryCodes.DisplayName(entity.GetName())} dropped carried item {carriedBlock.ItemStack?.Collectible?.Code} into ground storage at {pos}");
        }

        private void DropClusterBlock(Entity entity, CarriedBlock carriedBlock, int range, BlockPlacer blockPlacer, BlockPos centerBlock, IServerPlayer? player)
        {
            var dropMode = configProvider?.Config?.CarriedBlockEntity?.DropMode ?? DropMode.Items;

            var blockSelection = blockPlacer.FindClusterPlacement(
                carriedBlock,
                centerBlock,
                range);

            if (blockSelection != null)
            {
                var failureCode = FailureCodes.Ignore;
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

            // Cluster placement failed - drop parent plus all children as items
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
                api.World.Logger.Audit($"[{ModId}] Player {CarryCodes.DisplayName(player?.PlayerName)} dropped {attachedCount} attached child block(s) from {CarryCodes.DisplayName(carriedBlock.Block?.Code)} at {centerBlock}");
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
            if (api.Side == EnumAppSide.Server
                && (forceEntity || configProvider?.Config?.CarriedBlockEntity?.DropMode == DropMode.EntityAlways)
                && TrySpawnAsEntity(carriedBlock, centerBlock, player, entity))
            {
                return;
            }

            var world = api.World;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            // Spill items from configured beData attributes FIRST so they're removed
            // from BE data before the itemstack is spawned (prevents duplication).
            var spilledFromBeData = carriedBlock.BlockEntityData != null
                ? SpillBlockEntityContents(world, carriedBlock, dropVec3d)
                : 0;

            var (blockDestroyed, additionalDrops, dropHadContents) = ComputeAndSpawnDrops(world, carriedBlock, centerBlock, player, dropVec3d);
            var dropCount = 1 + spilledFromBeData + additionalDrops;
            var hadContents = spilledFromBeData > 0 || dropHadContents;

            var breakSound = carriedBlock.Block.Sounds.GetBreakSound(player).Location ?? new AssetLocation(CarryCodes.SoundPaths.DefaultBreak);
            world.PlaySoundAt(breakSound, (double)centerBlock.X, (double)centerBlock.Y, (double)centerBlock.Z);
            carryManager.RemoveCarried(entity, carriedBlock.Slot);

            if (blockDestroyed)
                world.Logger.Audit($"[{ModId}] Player {player?.PlayerName} dropped carried block {carriedBlock.Block.Code} at {centerBlock} and it was destroyed dropping {dropCount} items.");
            else
                world.Logger.Audit($"[{ModId}] Player {player?.PlayerName} dropped carried block {carriedBlock.Block.Code} as item at {centerBlock} spilling {dropCount} items from its contents.");

            carryManager.CarryEvents?.TriggerBlockDropped(centerBlock, entity, carriedBlock, blockDestroyed, hadContents, blockPlaced: false);
        }

        private bool TrySpawnAsEntity(CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer? player, Entity entity)
        {
            var context = new EntitySpawnContext
            {
                CarriedBlock = carriedBlock,
                OwnerUid = player?.PlayerUID ?? "unknown",
                Position = new Vec3d(entity.Pos.X, entity.Pos.Y, entity.Pos.Z),
                BlockPosition = centerBlock,
                HitPosition = null,
                Face = null,
                Dropped = true,
                Api = api,
            };

            // Check if a custom entity handler should handle the spawn
            if (EntityHandlerSpawner.TrySpawn(carryManager, entity, context))
            {
                api.World.Logger.Audit($"[{ModId}] Player {CarryCodes.DisplayName(player?.PlayerName)} dropped carried block {CarryCodes.DisplayName(carriedBlock.Block?.Code)} as entity at {centerBlock}");
                carryManager.CarryEvents?.TriggerBlockDropped(centerBlock, entity, carriedBlock, destroyed: false, hadContents: false, blockPlaced: false);
                return true;
            }

            return false;
        }

        private int SpillBlockEntityContents(IWorldAccessor world, CarriedBlock carriedBlock, Vec3d dropPos)
        {
            var spilled = 0;
            var beData = carriedBlock.BlockEntityData;
            if (beData == null) return spilled;

            var carryBehavior = carriedBlock.GetCarryableBehavior();
            var attrNames = carryBehavior?.DataAttributes;
            if (attrNames == null) return spilled;

            foreach (var attrName in attrNames)
            {
                if (string.IsNullOrEmpty(attrName)) continue;
                if (beData[attrName] is not ItemstackAttribute itemAttr) continue;
                var stack = itemAttr.GetValue() as ItemStack;
                if (stack == null) continue;
                world.SpawnItemEntity(stack, dropPos);
                beData.RemoveAttribute(attrName);
                spilled++;
            }

            return spilled;
        }

        private (bool BlockDestroyed, int AdditionalDrops, bool HadContents) ComputeAndSpawnDrops(
            IWorldAccessor world, CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer? player, Vec3d dropPos)
        {
            var blockDestroyed = false;
            var additionalDrops = 0;
            var hadContents = false;

            if (carriedBlock.BlockEntityData?["inventory"] is TreeAttribute inventory && inventory["slots"] is TreeAttribute invSlots)
            {
                foreach (var item in invSlots.Values.Cast<ItemstackAttribute>())
                {
                    var itemStack = (ItemStack)item.GetValue();
                    world.SpawnItemEntity(itemStack, dropPos);
                    hadContents = true;
                    additionalDrops++;
                }
                var carriedItemStack = carriedBlock.ItemStack.Clone();
                carriedItemStack.Attributes.Remove("contents");
                world.SpawnItemEntity(carriedItemStack, dropPos);
            }
            else
            {
                var itemStacks = carriedBlock.Block.GetDrops(world, centerBlock, player);
                if (itemStacks.Length == 1 && itemStacks[0].Id == carriedBlock.ItemStack.Id)
                {
                    world.SpawnItemEntity(carriedBlock.ItemStack, dropPos);
                }
                else
                {
                    blockDestroyed = true;
                    foreach (var itemStack in itemStacks)
                    {
                        world.SpawnItemEntity(itemStack, dropPos);
                        hadContents = true;
                        additionalDrops++;
                    }
                }
            }

            return (blockDestroyed, additionalDrops, hadContents);
        }
    }
}
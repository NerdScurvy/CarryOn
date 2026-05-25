using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Delegates;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Encapsulates pickup, placement, and carried block world transfer behavior.
    /// </summary>
    internal sealed class CarryPlacementService
    {
        /// <summary>
        /// Gets the core API for world access and side checks.
        /// </summary>
        public ICoreAPI Api { get; }

        /// <summary>
        /// Gets the owning carry system and configuration.
        /// </summary>
        public CarrySystem CarrySystem { get; }

        /// <summary>
        /// Gets the carry manager facade used for cross-domain calls and events.
        /// </summary>
        public ICarryManager CarryManager { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CarryPlacementService"/> class.
        /// </summary>
        /// <param name="api">Core API instance.</param>
        /// <param name="carrySystem">Owning carry system.</param>
        /// <param name="carryManager">Carry manager facade.</param>
        public CarryPlacementService(ICoreAPI api, CarrySystem carrySystem, ICarryManager carryManager)
        {
            Api = api ?? throw new ArgumentNullException(nameof(api));
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
            CarryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
        }

        /// <summary>
        /// Checks whether an entity has permission to pick up the block at the given position.
        /// </summary>
        /// <param name="entity">The acting entity.</param>
        /// <param name="pos">Target block position.</param>
        /// <returns>True when pickup is allowed; otherwise false.</returns>
        public bool HasPermissionToCarry(Entity entity, BlockPos pos)
        {
            var isReinforced = entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) ?? false;
            if (entity is EntityPlayer playerEntity)
            {
                var delegates = entity.World.GetCarryEvents()?.CheckPermissionToCarry?.GetInvocationList();

                if (delegates != null)
                {
                    foreach (var checkPermissionToCarryDelegate in delegates.Cast<CheckPermissionToCarryDelegate>())
                    {
                        try
                        {
                            checkPermissionToCarryDelegate(playerEntity, pos, isReinforced, out bool? hasPermission);

                            if (hasPermission != null)
                            {
                                return hasPermission.Value;
                            }
                        }
                        catch (Exception e)
                        {
                            entity.World.Logger.Error(e.Message);
                        }
                    }
                }

                var isCreative = playerEntity.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;

                if (!isCreative && isReinforced) return false;

                return entity.World.Claims.TryAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak);
            }
            else
            {
                return !isReinforced;
            }
        }

        private bool CanPickUp(Entity entity, BlockPos pos, CarrySlot slot, CarriedBlock carried, ref string failureCode)
        {
            var delegates = CarryManager.CarryEvents?.BeforePickUpBlock?.GetInvocationList();
            if (delegates == null) return true;

            foreach (var del in delegates.Cast<BeforePickUpBlockDelegate>())
            {
                try
                {
                    del(entity, pos, slot, carried, out bool? canPickUp, out string delegateFailureCode);

                    if (delegateFailureCode != null) failureCode = delegateFailureCode;
                    if (canPickUp != null) return canPickUp.Value;
                }
                catch (Exception e)
                {
                    entity.World.Logger.Error(e.Message);
                }
            }

            return true;
        }

        /// <summary>
        /// Removes a block from the world and returns it as a carried block.
        /// </summary>
        /// <param name="pos">Source block position.</param>
        /// <param name="slot">Destination carry slot metadata.</param>
        /// <param name="checkIsCarryable">Whether to enforce carryability checks for the slot.</param>
        /// <returns>The carried block, or null when conversion fails.</returns>
        public CarriedBlock GetCarriedFromWorld(BlockPos pos, CarrySlot slot, bool checkIsCarryable = false)
        {
            var delegates = CarryManager.CarryEvents?.BeforeRemoveBlockFromWorld?.GetInvocationList();
            string failureCode = FailureCode.Ignore;
            return GetCarriedFromWorld(null, pos, slot, ref failureCode, delegates: delegates, checkIsCarryable: checkIsCarryable);
        }

        /// <summary>
        /// Removes a block from the world and returns it as a carried block with optional delegate hooks.
        /// </summary>
        /// <param name="entity">Acting entity when pickup preflight hooks should run.</param>
        /// <param name="pos">Source block position.</param>
        /// <param name="slot">Destination carry slot metadata.</param>
        /// <param name="failureCode">Failure code output when pickup preflight rejects.</param>
        /// <param name="delegates">Optional callbacks invoked before world block removal.</param>
        /// <param name="checkIsCarryable">Whether to enforce carryability checks for the slot.</param>
        /// <returns>The carried block, or null when conversion fails.</returns>
        public CarriedBlock GetCarriedFromWorld(Entity entity, BlockPos pos, CarrySlot slot, ref string failureCode, Delegate[] delegates = null, bool checkIsCarryable = false)
        {
            var world = Api.World;
            var carried = BlockUtils.CreateCarriedFromBlockPos(world, pos, slot);
            if (carried == null) return null;

            if (checkIsCarryable && !CarryManager.IsCarryable(carried.Block, slot)) return null;

            if (entity != null && !CanPickUp(entity, pos, slot, carried, ref failureCode)) return null;

            if (delegates != null)
            {
                foreach (var removeBlockDelegate in delegates.Cast<BeforeRemoveBlockDelegate>())
                {
                    try
                    {
                        removeBlockDelegate(carried, pos);
                    }
                    catch (Exception e)
                    {
                        world.Logger.Error(e.Message);
                    }
                }
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.ClearReinforcement(pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            return carried;
        }

        /// <summary>
        /// Restores serialized block entity data after a carried block is placed.
        /// </summary>
        /// <param name="world">World accessor for placement context.</param>
        /// <param name="carriedBlock">Carried block containing serialized block entity data.</param>
        /// <param name="pos">Target placement position.</param>
        /// <param name="dropped">Whether placement originated from drop flow.</param>
        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            if ((world.Side != EnumAppSide.Server) || (carriedBlock?.BlockEntityData == null)) return;

            var delegates = CarryManager.CarryEvents?.BeforeRestoreBlockEntityData?.GetInvocationList();
            RestoreBlockEntityData(world, carriedBlock, pos, delegates: delegates, dropped: dropped);
        }

        /// <summary>
        /// Restores serialized block entity data after a carried block is placed with explicit callbacks.
        /// </summary>
        /// <param name="world">World accessor for placement context.</param>
        /// <param name="carriedBlock">Carried block containing serialized block entity data.</param>
        /// <param name="pos">Target placement position.</param>
        /// <param name="delegates">Optional callbacks invoked before data is applied.</param>
        /// <param name="dropped">Whether placement originated from drop flow.</param>
        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, Delegate[] delegates = null, bool dropped = false)
        {
            if (carriedBlock?.BlockEntityData == null) return;

            var blockEntityData = carriedBlock.BlockEntityData;
            blockEntityData.SetInt("posx", pos.X);
            blockEntityData.SetInt("posy", pos.Y);
            blockEntityData.SetInt("posz", pos.Z);

            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);

            if (delegates != null)
            {
                foreach (var blockEntityDataDelegate in delegates.Cast<BlockEntityDataDelegate>())
                {
                    try
                    {
                        blockEntityDataDelegate(blockEntity, blockEntityData, dropped);
                    }
                    catch (Exception e)
                    {
                        world.Logger.Error(e.Message);
                    }
                }
            }

            blockEntity?.FromTreeAttributes(blockEntityData, world);
            blockEntity?.MarkDirty(true);
        }

        /// <summary>
        /// Attempts to pick up a world block into a carry slot.
        /// </summary>
        /// <param name="entity">The acting entity.</param>
        /// <param name="pos">Target block position.</param>
        /// <param name="slot">Destination carry slot.</param>
        /// <param name="failureCode">Failure code output when pickup fails.</param>
        /// <param name="checkIsCarryable">Whether to enforce carryability checks.</param>
        /// <param name="playSound">Whether to play pickup audio on success.</param>
        /// <returns>True when pickup succeeds; otherwise false.</returns>
        public bool TryPickUp(
            Entity entity,
            BlockPos pos,
            CarrySlot slot,
            ref string failureCode,
            bool checkIsCarryable = true,
            bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(pos);

            failureCode ??= FailureCode.Ignore;

            if (entity.GetCarried(slot) != null)
            {
                failureCode = "already-carrying";
                return false;
            }

            if (entity.Api.Side == EnumAppSide.Server && !HasPermissionToCarry(entity, pos))
            {
                failureCode = "no-permission";
                return false;
            }

            var block = entity.World.BlockAccessor.GetBlock(pos);
            if (checkIsCarryable && !CarryManager.IsCarryable(block, slot))
            {
                failureCode = "not-carryable";
                return false;
            }

            var carryBehavior = block.GetBehavior<BlockBehaviorCarryable>();
            if (entity.Api.Side == EnumAppSide.Client && carryBehavior != null && !carryBehavior.OptimisticPickup)
            {
                return true;
            }

            var optimisticPickup = carryBehavior?.OptimisticPickup ?? true;
            var delegates = CarryManager.CarryEvents?.BeforeRemoveBlockFromWorld?.GetInvocationList();
            var carried = GetCarriedFromWorld(entity, pos, slot, ref failureCode, delegates, checkIsCarryable);
            if (carried == null) return false;

            CarryManager.SetCarried(entity, carried);

            if (playSound)
            {
                PlaySound(carried.Block, pos, entity as EntityPlayer, dualCall: optimisticPickup);
            }

            if (entity.Api.Side == EnumAppSide.Server)
            {
                var entityName = entity?.GetName() ?? "Unknown Entity";
                entity.World.Logger.Audit($"[{ModId}] {entityName} picked up block {carried.Block.Code.GetName()} at {pos}");
            }

            return true;
        }

        /// <summary>
        /// Attempts to pick up a block without exposing a failure-code output.
        /// </summary>
        /// <param name="entity">The acting entity.</param>
        /// <param name="pos">Target block position.</param>
        /// <param name="slot">Destination carry slot.</param>
        /// <param name="checkIsCarryable">Whether to enforce carryability checks.</param>
        /// <param name="playSound">Whether to play pickup audio on success.</param>
        /// <returns>True when pickup succeeds; otherwise false.</returns>
        public bool TryPickUp(Entity entity, BlockPos pos, CarrySlot slot, bool checkIsCarryable = true, bool playSound = true)
        {
            string failureCode = FailureCode.Ignore;
            return TryPickUp(entity, pos, slot, ref failureCode, checkIsCarryable, playSound);
        }

        /// <summary>
        /// Attempts to place a carried block without exposing a failure-code output.
        /// </summary>
        /// <param name="entity">The acting entity.</param>
        /// <param name="carriedBlock">The carried block to place.</param>
        /// <param name="selection">Placement selection.</param>
        /// <param name="dropped">Whether to place via drop flow.</param>
        /// <param name="playSound">Whether to play placement audio on success.</param>
        /// <returns>True when placement succeeds; otherwise false.</returns>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped = false, bool playSound = true)
        {
            string failureCode = FailureCode.Ignore;
            return TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound);
        }

        /// <summary>
        /// Attempts to place a carried block into the world.
        /// </summary>
        /// <param name="entity">The acting entity.</param>
        /// <param name="carriedBlock">The carried block to place.</param>
        /// <param name="selection">Placement selection.</param>
        /// <param name="failureCode">Failure code output when placement fails.</param>
        /// <param name="dropped">Whether to place via drop flow.</param>
        /// <param name="playSound">Whether to play placement audio on success.</param>
        /// <returns>True when placement succeeds; otherwise false.</returns>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode, bool dropped = false, bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(selection);

            failureCode ??= FailureCode.Ignore;

            if (carriedBlock == null)
            {
                failureCode = "not-carrying";
                return false;
            }

            var world = Api.World;

            if (carriedBlock?.Block == null || carriedBlock.ItemStack == null) return false;
            if (!world.BlockAccessor.IsValidPos(selection.Position)) return false;

            failureCode ??= FailureCode.Ignore;

            var placed = (entity is EntityPlayer playerEntity && !dropped)
                ? TryPlaceDownAsPlayer(world, playerEntity, carriedBlock, selection, ref failureCode)
                : TryPlaceDownDirect(world, carriedBlock, selection);

            if (!placed) return false;

            FinalizePlacedBlock(world, entity, carriedBlock, selection.Position, dropped, playSound);
            return true;
        }

        private bool TryPlaceDownAsPlayer(IWorldAccessor world, EntityPlayer playerEntity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode)
        {
            var player = world.PlayerByUid(playerEntity.PlayerUID);
            var activeHotbarSlot = player?.InventoryManager?.ActiveHotbarSlot;
            if (player == null || activeHotbarSlot == null)
            {
                world.Logger.Error($"CarryOn: Failed to resolve player inventory while placing carried block at {selection.Position}. Falling back to direct placement.");
                return TryPlaceDownFallback(world, carriedBlock, selection);
            }

            var shift = playerEntity.Controls.ShiftKey;
            var ctrl = playerEntity.Controls.CtrlKey;

            try
            {
                activeHotbarSlot.Itemstack = carriedBlock.ItemStack;

                playerEntity.Controls.ShiftKey = true;
                playerEntity.Controls.CtrlKey = false;

                return carriedBlock.Block.TryPlaceBlock(world, player, carriedBlock.ItemStack, selection, ref failureCode);
            }
            finally
            {
                playerEntity.Controls.ShiftKey = shift;
                playerEntity.Controls.CtrlKey = ctrl;
                activeHotbarSlot.Itemstack = null;
            }
        }

        private bool TryPlaceDownFallback(IWorldAccessor world, CarriedBlock carriedBlock, BlockSelection selection)
        {
            if (carriedBlock?.Block == null || carriedBlock.ItemStack == null) return false;

            world.BlockAccessor.SetBlock(carriedBlock.Block.Id, selection.Position, carriedBlock.ItemStack);
            return true;
        }

        private bool TryPlaceDownDirect(IWorldAccessor world, CarriedBlock carriedBlock, BlockSelection selection)
        {
            var meshFacing = selection.Face;
            var droppedBlock = carriedBlock.Block;

            if (meshFacing != null)
            {
                var assetLocation = carriedBlock.Block.Code.Clone();
                var baseCode = assetLocation.FirstCodePart();
                assetLocation.Path = $"{baseCode}-{meshFacing.Code}";
                droppedBlock = world.GetBlock(assetLocation) ?? carriedBlock.Block;
            }

            world.BlockAccessor.ExchangeBlock(droppedBlock.Id, selection.Position);

            if (droppedBlock.EntityClass != null)
            {
                world.BlockAccessor.SpawnBlockEntity(droppedBlock.EntityClass, selection.Position, carriedBlock.ItemStack);
            }

            droppedBlock.OnBlockPlaced(world, selection.Position, carriedBlock.ItemStack);

            if (meshFacing != null)
            {
                carriedBlock.BlockEntityData?.SetFloat("meshAngle", -GetMeshAngle(meshFacing));
            }

            return true;
        }

        private void FinalizePlacedBlock(IWorldAccessor world, Entity entity, CarriedBlock carriedBlock, BlockPos position, bool dropped, bool playSound)
        {
            RestoreBlockEntityData(world, carriedBlock, position, dropped: dropped);

            world.BlockAccessor.MarkBlockDirty(position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(position);

            CarryManager.RemoveCarried(entity, carriedBlock.Slot);
            if (playSound)
            {
                PlaySound(carriedBlock.Block, position, dropped ? null : entity as EntityPlayer);
            }

            if (dropped)
            {
                CarryManager.CarryEvents?.TriggerBlockDropped(position, entity, carriedBlock);
            }
            if (world.Side == EnumAppSide.Server)
            {
                var entityName = entity?.GetName() ?? "Unknown Entity";
                Api.World.Logger.Audit($"[{ModId}] Player {entityName}  {(dropped ? "dropped" : "placed down")}  block {carriedBlock?.Block?.Code.GetName()} at {position}");
            }
        }

        /// <summary>
        /// Attempts to place a carried block in front of the selected block position.
        /// </summary>
        /// <param name="player">Acting player.</param>
        /// <param name="carrySlot">Source carry slot.</param>
        /// <param name="selection">Original selected block.</param>
        /// <param name="placedAt">Resolved placement position on success.</param>
        /// <returns>True when placement succeeds; otherwise false.</returns>
        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos placedAt)
        {
            string failureCode = FailureCode.Ignore;
            return TryPlaceDownAt(player, carrySlot, selection, out placedAt, ref failureCode);
        }

        /// <summary>
        /// Attempts to place a carried block in front of the selected block position.
        /// </summary>
        /// <param name="player">Acting player.</param>
        /// <param name="carrySlot">Source carry slot.</param>
        /// <param name="selection">Original selected block.</param>
        /// <param name="placedAt">Resolved placement position on success.</param>
        /// <param name="failureCode">Failure code output when placement fails.</param>
        /// <returns>True when placement succeeds; otherwise false.</returns>
        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos placedAt, ref string failureCode)
        {
            placedAt = null;
            var blockSelection = selection.Clone();
            var selectedBlock = player.Entity?.World?.BlockAccessor?.GetBlock(blockSelection.Position);

            if (selectedBlock == null) return false;

            var carried = player.Entity.GetCarried(carrySlot);
            if (carried == null)
            {
                failureCode = "not-carrying";
                return false;
            }

            if (selectedBlock.IsReplacableBy(carried.Block))
            {
                blockSelection.Face = BlockFacing.UP;
                blockSelection.HitPosition.Y = 0.5;
            }
            else
            {
                blockSelection.Position.Offset(blockSelection.Face);
                blockSelection.DidOffset = true;
            }

            placedAt = blockSelection.Position;
            return TryPlaceDown(player.Entity, carried, blockSelection, ref failureCode);
        }

        private float GetMeshAngle(BlockFacing facing)
        {
            switch (facing.Code)
            {
                case "north": return 0f;
                case "east": return (float)(Math.PI / 2);
                case "south": return (float)Math.PI;
                case "west": return (float)(3 * Math.PI / 2);
                default: return 0f;
            }
        }

        internal void PlaySound(Block block, BlockPos pos, EntityPlayer entityPlayer = null, bool dualCall = true)
        {
            const float SOUND_RANGE = 16.0F;
            const float SOUND_VOLUME = 1.0F;

            var sound = block.Sounds?.Place.Location ?? new AssetLocation("sounds/player/build");

            if (sound == null) return;

            var world = Api.World;
            var player = dualCall && (entityPlayer != null) && (world.Side == EnumAppSide.Server)
                ? entityPlayer?.Player : null;

            world.PlaySoundAt(sound,
                pos.X + 0.5, pos.Y + 0.25, pos.Z + 0.5, player,
                range: SOUND_RANGE, volume: SOUND_VOLUME);
        }
    }
}
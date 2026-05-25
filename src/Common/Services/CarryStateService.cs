using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Encapsulates carried-state storage, revision tracking, and slot-lock synchronization.
    /// </summary>
    internal sealed class CarryStateService
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
        /// Gets the carry manager facade used for cross-domain operations.
        /// </summary>
        public ICarryManager CarryManager { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CarryStateService"/> class.
        /// </summary>
        /// <param name="api">Core API instance.</param>
        /// <param name="carrySystem">Owning carry system.</param>
        /// <param name="carryManager">Carry manager facade.</param>
        public CarryStateService(ICoreAPI api, CarrySystem carrySystem, ICarryManager carryManager)
        {
            Api = api ?? throw new ArgumentNullException(nameof(api));
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
            CarryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
        }

        private bool AllowSprintWhileCarrying => CarrySystem?.Config?.CarryOptions?.AllowSprintWhileCarrying ?? false;
        private bool IgnoreCarrySpeedPenalty => CarrySystem?.Config?.CarryOptions?.IgnoreCarrySpeedPenalty ?? false;

        /// <summary>
        /// Gets all carried blocks currently held by the entity across all carry slots.
        /// </summary>
        /// <param name="entity">The entity to inspect.</param>
        /// <returns>An enumeration of carried blocks, excluding empty slots.</returns>
        public IEnumerable<CarriedBlock> GetAllCarried(Entity entity)
        {
            foreach (var slot in Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>())
            {
                var carried = GetCarried(entity, slot);
                if (carried != null) yield return carried;
            }
        }

        /// <summary>
        /// Gets the carried block in a specific slot for an entity.
        /// </summary>
        /// <param name="entity">The entity to inspect.</param>
        /// <param name="slot">The slot to read.</param>
        /// <returns>The carried block, or null if the slot is empty or invalid.</returns>
        public CarriedBlock GetCarried(Entity entity, CarrySlot slot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var entityCarriedKey = AttributeKey.Watched.EntityCarried;
            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute == null) return null;

            var stack = slotAttribute.GetItemstack("Stack");
            if (stack?.Class != EnumItemClass.Block) return null;
            if (stack.Block == null)
            {
                stack.ResolveBlockOrItem(entity.World);
                if (stack.Block == null) return null;
            }

            var blockEntityData = entity.WatchedAttributes.TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString(), "Data");

            return new CarriedBlock(slot, stack, blockEntityData);
        }

        /// <summary>
        /// Sets a carried block using default dirty-mark behavior.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="carriedBlock">The carried block to assign.</param>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock) => SetCarried(entity, carriedBlock, markDirty: true);

        /// <summary>
        /// Sets a carried block for the entity.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="carriedBlock">The carried block to assign.</param>
        /// <param name="markDirty">Whether to touch and mark carried attributes dirty.</param>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock, bool markDirty = true)
        {
            SetCarried(entity, carriedBlock.Slot, carriedBlock.ItemStack, carriedBlock.BlockEntityData, markDirty);
        }

        /// <summary>
        /// Sets a carried block from raw item stack and block entity data using default dirty-mark behavior.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="slot">The destination carry slot.</param>
        /// <param name="stack">The carried block item stack.</param>
        /// <param name="blockEntityData">Serialized block entity data to associate with the carried block.</param>
        public void SetCarried(Entity entity, CarrySlot slot, ItemStack stack, ITreeAttribute blockEntityData)
            => SetCarried(entity, slot, stack, blockEntityData, markDirty: true);

        /// <summary>
        /// Sets a carried block from raw item stack and block entity data.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="slot">The destination carry slot.</param>
        /// <param name="stack">The carried block item stack.</param>
        /// <param name="blockEntityData">Serialized block entity data to associate with the carried block.</param>
        /// <param name="markDirty">Whether to touch and mark carried attributes dirty.</param>
        public void SetCarried(Entity entity, CarrySlot slot, ItemStack stack, ITreeAttribute blockEntityData, bool markDirty = true)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var entityCarriedKey = AttributeKey.Watched.EntityCarried;
            entity.WatchedAttributes.Set(stack, entityCarriedKey, slot.ToString(), "Stack");

            if (blockEntityData != null)
            {
                entity.WatchedAttributes.Set(blockEntityData, entityCarriedKey, slot.ToString(), "Data");
            }

            if (entity.Api.Side == EnumAppSide.Server)
            {
                if (markDirty) TouchCarriedAttributes(entity);
            }

            var behavior = stack.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);
            var slotSettings = behavior.Slots[slot];

            if (slotSettings?.Animation != null)
            {
                entity.StartAnimation(slotSettings.Animation);
            }

            if (entity is EntityAgent agent)
            {
                var speed = IgnoreCarrySpeedPenalty ? 0.0f : slotSettings?.WalkSpeedModifier ?? 0.0F;
                if (speed != 0.0F && !AllowSprintWhileCarrying)
                {
                    agent.Stats.Set("walkspeed",
                        CarryOnCode(slot.ToString()), speed, false);
                }

                if (entity.Api.Side == EnumAppSide.Server)
                {
                    if (slot == CarrySlot.Hands) LockedItemSlot.Lock(agent.RightHandItemSlot);
                    if (slot != CarrySlot.Back) LockedItemSlot.Lock(agent.LeftHandItemSlot);
                    SendLockSlotsMessage(agent as EntityPlayer);
                }
            }
        }

        /// <summary>
        /// Removes a carried block using default dirty-mark behavior.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="slot">The slot to clear.</param>
        public void RemoveCarried(Entity entity, CarrySlot slot) => RemoveCarried(entity, slot, markDirty: true);

        /// <summary>
        /// Removes a carried block from the specified slot.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="slot">The slot to clear.</param>
        /// <param name="markDirty">Whether to touch and mark carried attributes dirty.</param>
        public void RemoveCarried(Entity entity, CarrySlot slot, bool markDirty = true)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var animation = entity.GetCarried(slot)?.GetCarryableBehavior()?.Slots?[slot]?.Animation;
            if (animation != null) entity.StopAnimation(animation);

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove("walkspeed", CarryOnCode(slot.ToString()));

                if (slot == CarrySlot.Hands) LockedItemSlot.Restore(agent.RightHandItemSlot);
                if (slot != CarrySlot.Back) LockedItemSlot.Restore(agent.LeftHandItemSlot);
                SendLockSlotsMessage(agent as EntityPlayer);
            }

            entity.WatchedAttributes.Remove(AttributeKey.Watched.EntityCarried, slot.ToString());
            if (markDirty) TouchCarriedAttributes(entity);
            entity.Attributes.Remove(AttributeKey.Watched.EntityCarried, slot.ToString());
        }

        /// <summary>
        /// Swaps carried blocks between two slots when both carried blocks are valid in the opposite slot.
        /// </summary>
        /// <param name="entity">The entity whose slots are swapped.</param>
        /// <param name="first">First slot.</param>
        /// <param name="second">Second slot.</param>
        /// <returns>True when at least one slot changed; otherwise false.</returns>
        public bool SwapCarried(Entity entity, CarrySlot first, CarrySlot second)
        {
            if (first == second) throw new ArgumentException("Slots can't be the same");

            bool isServer = entity.Api.Side == EnumAppSide.Server;

            var carriedFirst = GetCarried(entity, first);
            var carriedSecond = GetCarried(entity, second);
            if ((carriedFirst == null) && (carriedSecond == null)) return false;

            bool canSetFirst = carriedFirst == null || carriedFirst.Block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[second] != null;
            bool canSetSecond = carriedSecond == null || carriedSecond.Block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[first] != null;

            if (!canSetFirst || !canSetSecond) return false;

            RemoveCarried(entity, first, markDirty: false);
            RemoveCarried(entity, second, markDirty: false);

            carriedFirst?.Set(entity, second, markDirty: false);
            carriedSecond?.Set(entity, first, markDirty: false);

            if (isServer) TouchCarriedAttributes(entity);
            return true;
        }

        /// <summary>
        /// Sends a hotbar lock-state packet to a server player.
        /// </summary>
        /// <param name="player">The server player to notify.</param>
        public void LockHotbarSlots(IServerPlayer player)
        {
            var hotbar = player.InventoryManager.GetHotbarInventory();
            var slots = Enumerable.Range(0, hotbar.Count).Where(i => hotbar[i] is LockedItemSlot).ToList();
            CarrySystem.ServerChannel.SendPacket(new LockSlotsMessage(slots), player);
        }

        /// <summary>
        /// Resolves and sends hotbar lock-state updates for the provided player entity.
        /// </summary>
        /// <param name="player">The entity player to notify when available on server.</param>
        public void SendLockSlotsMessage(EntityPlayer player)
        {
            if ((player == null) || (player.World.PlayerByUid(player.PlayerUID) is not IServerPlayer serverPlayer)) return;
            LockHotbarSlots(serverPlayer);
        }

        /// <summary>
        /// Touches carried attributes by incrementing the carried revision on server.
        /// </summary>
        /// <param name="entity">The entity whose carried attributes are touched.</param>
        /// <returns>The current revision value after any increment.</returns>
        public int TouchCarriedAttributes(Entity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            var carriedRoot = entity.WatchedAttributes.TryGet<ITreeAttribute>(AttributeKey.Watched.EntityCarried) ?? new TreeAttribute();
            var revision = carriedRoot.GetInt(AttributeKey.CarriedRevision, 0);

            if (entity.World.Side == EnumAppSide.Server)
            {
                carriedRoot.SetInt(AttributeKey.CarriedRevision, ++revision);
                entity.WatchedAttributes.Set(carriedRoot, AttributeKey.Watched.EntityCarried);
                entity.WatchedAttributes.MarkPathDirty(AttributeKey.Watched.EntityCarried);
            }

            return revision;
        }

        /// <summary>
        /// Gets the current carried-state revision for an entity.
        /// </summary>
        /// <param name="entity">The entity to inspect.</param>
        /// <returns>The current carried revision, or 0 when not initialized.</returns>
        public int GetCarriedRevision(Entity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            var carriedRoot = entity.WatchedAttributes.TryGet<ITreeAttribute>(AttributeKey.Watched.EntityCarried);
            return carriedRoot?.GetInt(AttributeKey.CarriedRevision, 0) ?? 0;
        }
    }
}
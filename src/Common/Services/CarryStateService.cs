using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Logic;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryStateService(IConfigProvider configProvider, IServerNetworkChannel? serverChannel)
    {
        private const string AttrAnimation = "Animation";

        private readonly WalkSpeedModifierResolver walkSpeedModifierResolver = new();
        private readonly HungerRateModifierResolver hungerRateModifierResolver = new();

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
        public CarriedBlock? GetCarried(Entity entity, CarrySlot slot)
        {
            ArgumentNullException.ThrowIfNull(entity);

            var entityCarriedKey = AttributeKey.Watched.EntityCarried;
            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute == null) return null;

            return CarriedBlockTreeSerializer.Deserialize(slotAttribute, entity.World, slot);
        }

        /// <summary>
        /// Sets a carried block for the entity.
        /// </summary>
        /// <param name="entity">The target entity.</param>
        /// <param name="carriedBlock">The carried block to assign.</param>
        /// <param name="overrideSlot">When provided, overrides the slot stored in <paramref name="carriedBlock"/>.</param>
        /// <param name="markDirty">Whether to touch and mark carried attributes dirty.</param>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock, CarrySlot? overrideSlot = null, bool markDirty = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(carriedBlock);

            var slot = overrideSlot ?? carriedBlock.Slot;
            var stack = carriedBlock.ItemStack;
            var blockEntityData = carriedBlock.BlockEntityData;
            var attachedBlocks = carriedBlock.AttachedBlocks;
            var originalBlockCode = carriedBlock.OriginalBlockCode;
            var originalMeshAngle = carriedBlock.OriginalMeshAngle;

            var entityCarriedKey = AttributeKey.Watched.EntityCarried;
            entity.WatchedAttributes.Set(stack, entityCarriedKey, slot.ToString(), AttributeKey.CarriedBlock.Stack);

            if (blockEntityData != null)
            {
                entity.WatchedAttributes.Set(blockEntityData, entityCarriedKey, slot.ToString(), AttributeKey.CarriedBlock.Data);
            }

            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute != null)
            {
                slotAttribute.RemoveAttribute(AttributeKey.CarriedBlock.Children);

                var childrenTree = CarriedBlockTreeSerializer.BuildAttachedBlocksTree(attachedBlocks);
                if (childrenTree != null)
                    slotAttribute[AttributeKey.CarriedBlock.Children] = childrenTree;

                if (originalBlockCode != null)
                    slotAttribute.SetString(AttributeKey.CarriedBlock.OriginalBlockCode, originalBlockCode.ToString());
                else
                    slotAttribute.RemoveAttribute(AttributeKey.CarriedBlock.OriginalBlockCode);

                if (originalMeshAngle.HasValue)
                    slotAttribute.SetFloat(AttributeKey.CarriedBlock.OriginalMeshAngle, originalMeshAngle.Value);
                else
                    slotAttribute.RemoveAttribute(AttributeKey.CarriedBlock.OriginalMeshAngle);
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
                var slotAttr = entity.WatchedAttributes
                    .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
                slotAttr?.SetString(AttrAnimation, slotSettings.Animation);
            }

            if (entity is EntityAgent agent)
            {
                ApplyWalkSpeed(agent, slot, slotSettings, stack, behavior);
                ApplyHungerRate(agent, slot, slotSettings, stack, behavior);

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
            ArgumentNullException.ThrowIfNull(entity);

            var slotAttribute = entity.WatchedAttributes.TryGet<ITreeAttribute>(
                AttributeKey.Watched.EntityCarried, slot.ToString());
            var animation = slotAttribute?.GetString(AttrAnimation);
            if (animation != null && slotAttribute != null)
            {
                entity.StopAnimation(animation);
                slotAttribute.RemoveAttribute(AttrAnimation);
            }

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove("walkspeed", CarryOnCode(slot.ToString()));
                agent.Stats.Remove("hungerrate", CarryOnCode(slot.ToString()));
                SendLockSlotsMessage(agent as EntityPlayer);
            }

            entity.WatchedAttributes.Remove(AttributeKey.Watched.EntityCarried, slot.ToString());
            if (markDirty) TouchCarriedAttributes(entity);
            entity.Attributes.Remove(AttributeKey.Watched.EntityCarried, slot.ToString());

            // Restore hand locks only when no carriable remains in the Hands slot.
            // This handles stale locks from desync (Back remove with empty Hands)
            // while preserving locks when both Hands and Back are occupied.
            if (entity is EntityAgent agentForLocks && GetCarried(entity, CarrySlot.Hands) == null)
            {
                LockedItemSlot.Restore(agentForLocks.RightHandItemSlot);
                LockedItemSlot.Restore(agentForLocks.LeftHandItemSlot);
            }
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

            if (carriedFirst != null) SetCarried(entity, carriedFirst, second, markDirty: false);
            if (carriedSecond != null) SetCarried(entity, carriedSecond, first, markDirty: false);

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
            if (serverChannel == null) return;

            serverChannel.SendPacket(new LockSlotsMessage(slots), player);
        }

        /// <summary>
        /// Resolves and sends hotbar lock-state updates for the provided player entity.
        /// </summary>
        /// <param name="player">The entity player to notify when available on server.</param>
        public void SendLockSlotsMessage(EntityPlayer? player)
        {
            if ((player == null) || (player.World.PlayerByUid(player.PlayerUID) is not IServerPlayer serverPlayer)) return;
            LockHotbarSlots(serverPlayer);
        }

        private void ApplyWalkSpeed(EntityAgent agent, CarrySlot slot, SlotSettings? slotSettings = null, ItemStack? stack = null, BlockBehaviorCarryable? behavior = null)
        {
            var walkSpeedConfig = configProvider.Config.CarryWalkSpeed;
            if (walkSpeedConfig == null) return;

            if (!walkSpeedModifierResolver.IsEnabled(slot, walkSpeedConfig)) return;

            var speed = walkSpeedModifierResolver.Resolve(
                stack,
                behavior,
                slotSettings,
                slot,
                walkSpeedConfig.ModifierOverrides);

            if (speed != 0.0F)
            {
                agent.Stats.Set("walkspeed",
                    CarryOnCode(slot.ToString()), speed, false);
            }
        }

        private void ApplyHungerRate(EntityAgent agent, CarrySlot slot, SlotSettings? slotSettings = null, ItemStack? stack = null, BlockBehaviorCarryable? behavior = null)
        {
            var hungerRateConfig = configProvider.Config.CarryHungerRate;
            if (hungerRateConfig == null) return;

            if (!hungerRateModifierResolver.IsEnabled(slot, hungerRateConfig)) return;

            var modifier = hungerRateModifierResolver.Resolve(stack, behavior, slotSettings, slot, hungerRateConfig);
            if (modifier <= 0f) return;

            if (hungerRateConfig.MinSaturationThreshold > 0f)
            {
                var hunger = agent.GetBehavior<EntityBehaviorHunger>();
                if (hunger != null && hunger.Saturation < hungerRateConfig.MinSaturationThreshold)
                    modifier = 0f;
            }

            if (modifier > 0f)
            {
                agent.Stats.Set("hungerrate", CarryOnCode(slot.ToString()), modifier, true);
            }
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

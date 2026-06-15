using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryStateService(CarryOnConfig config, IServerNetworkChannel? serverChannel)
    {
        private const string AttrStack = "Stack";
        private const string AttrData = "Data";
        private const string AttrChildren = "Children";
        private const string AttrOffsetX = "OffsetX";
        private const string AttrOffsetY = "OffsetY";
        private const string AttrOffsetZ = "OffsetZ";
        private const string AttrOriginalFace = "OriginalFace";
        private const string AttrOriginalBlockCode = "OriginalBlockCode";
        private const string AttrOriginalMeshAngle = "OriginalMeshAngle";

        private readonly WalkSpeedModifierResolver walkSpeedModifierResolver = new();
        private readonly HungerRateModifierResolver hungerRateModifierResolver = new();

        private static bool CanSprintForSlot(CarrySlot slot, CarryWalkSpeedConfig config)
        {
            return slot switch
            {
                CarrySlot.Hands => config.InHandsAllowSprint,
                CarrySlot.Back => config.OnBackAllowSprint,
                _ => false
            };
        }

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

            var stack = slotAttribute.GetItemstack(AttrStack);
            if (stack?.Class != EnumItemClass.Block) return null;
            if (stack.Block == null)
            {
                stack.ResolveBlockOrItem(entity.World);
                if (stack.Block == null) return null;
            }

            var blockEntityData = entity.WatchedAttributes.TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString(), AttrData);

            var attachedBlocks = DeserializeAttachedBlocks(entity, slotAttribute);

            var originalCodeStr = slotAttribute.GetString(AttrOriginalBlockCode, null);
            var originalCode = originalCodeStr != null ? new AssetLocation(originalCodeStr) : null;

            float? originalMeshAngle = null;
            if (slotAttribute.HasAttribute(AttrOriginalMeshAngle))
                originalMeshAngle = slotAttribute.GetFloat(AttrOriginalMeshAngle);

            return new CarriedBlock(slot, stack, blockEntityData, attachedBlocks, originalCode, originalMeshAngle);
        }

        private List<AttachedCarriedBlock>? DeserializeAttachedBlocks(Entity entity, ITreeAttribute slotAttribute)
        {
            if (slotAttribute[AttrChildren] is not TreeAttribute childrenTree || childrenTree.Count == 0)
                return null;

            var attached = new List<AttachedCarriedBlock>();
            foreach (var key in childrenTree.Keys)
            {
                if (childrenTree[key] is not ITreeAttribute childAttr) continue;

                var childStack = childAttr.GetItemstack(AttrStack);
                if (childStack?.Class != EnumItemClass.Block) continue;
                if (childStack.Block == null)
                {
                    childStack.ResolveBlockOrItem(entity.World);
                    if (childStack.Block == null) continue;
                }

                var childData = childAttr[AttrData] as ITreeAttribute;

                var offsetX = childAttr.GetInt(AttrOffsetX, 0);
                var offsetY = childAttr.GetInt(AttrOffsetY, 0);
                var offsetZ = childAttr.GetInt(AttrOffsetZ, 0);
                var relativeOffset = new BlockPos(offsetX, offsetY, offsetZ);

                var faceCode = childAttr.GetString(AttrOriginalFace, null);
                var originalFace = faceCode != null ? BlockFacing.FromCode(faceCode) : null;

                var originalCodeStr = childAttr.GetString(AttrOriginalBlockCode, null);
                var originalCode = originalCodeStr != null ? new AssetLocation(originalCodeStr) : null;

                float? originalMeshAngle = null;
                if (childAttr.HasAttribute(AttrOriginalMeshAngle))
                    originalMeshAngle = childAttr.GetFloat(AttrOriginalMeshAngle);

                var carriedBlock = new CarriedBlock(CarrySlot.Attached, childStack, childData, null, originalCode, originalMeshAngle);
                attached.Add(new AttachedCarriedBlock(relativeOffset, carriedBlock, originalFace));
            }

            return attached.Count > 0 ? attached : null;
        }

        private static void SerializeAttachedBlocks(ITreeAttribute slotAttribute, IReadOnlyList<AttachedCarriedBlock>? attachedBlocks)
        {
            slotAttribute.RemoveAttribute(AttrChildren);

            if (attachedBlocks == null || attachedBlocks.Count == 0) return;

            var childrenTree = new TreeAttribute();
            for (int i = 0; i < attachedBlocks.Count; i++)
            {
                var child = attachedBlocks[i];
                var childAttr = new TreeAttribute();

                childAttr.SetItemstack(AttrStack, child.ItemStack);

                if (child.BlockEntityData != null)
                {
                    childAttr[AttrData] = child.BlockEntityData;
                }

                childAttr.SetInt(AttrOffsetX, child.RelativeOffset.X);
                childAttr.SetInt(AttrOffsetY, child.RelativeOffset.Y);
                childAttr.SetInt(AttrOffsetZ, child.RelativeOffset.Z);

                if (child.OriginalLocalFace != null)
                {
                    childAttr.SetString(AttrOriginalFace, child.OriginalLocalFace.Code);
                }

                if (child.OriginalBlockCode != null)
                    childAttr.SetString(AttrOriginalBlockCode, child.OriginalBlockCode.ToString());
                else
                    childAttr.RemoveAttribute(AttrOriginalBlockCode);

                if (child.OriginalMeshAngle.HasValue)
                    childAttr.SetFloat(AttrOriginalMeshAngle, child.OriginalMeshAngle.Value);
                else
                    childAttr.RemoveAttribute(AttrOriginalMeshAngle);

                childrenTree[$"child_{i}"] = childAttr;
            }

            slotAttribute[AttrChildren] = childrenTree;
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
            entity.WatchedAttributes.Set(stack, entityCarriedKey, slot.ToString(), AttrStack);

            if (blockEntityData != null)
            {
                entity.WatchedAttributes.Set(blockEntityData, entityCarriedKey, slot.ToString(), AttrData);
            }

            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute != null)
            {
                SerializeAttachedBlocks(slotAttribute, attachedBlocks);

                if (originalBlockCode != null)
                    slotAttribute.SetString(AttrOriginalBlockCode, originalBlockCode.ToString());
                else
                    slotAttribute.RemoveAttribute(AttrOriginalBlockCode);

                if (originalMeshAngle.HasValue)
                    slotAttribute.SetFloat(AttrOriginalMeshAngle, originalMeshAngle.Value);
                else
                    slotAttribute.RemoveAttribute(AttrOriginalMeshAngle);
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
                var speed = 0.0f;
                var walkSpeedConfig = config?.CarryWalkSpeed;
                if (walkSpeedConfig != null && walkSpeedModifierResolver.IsEnabled(slot, walkSpeedConfig))
                {
                    speed = walkSpeedModifierResolver.Resolve(
                        stack,
                        behavior,
                        slotSettings,
                        slot,
                        walkSpeedConfig.ModifierOverrides);
                }

                if (speed != 0.0F && !CanSprintForSlot(slot, walkSpeedConfig!))
                {
                    agent.Stats.Set("walkspeed",
                        CarryOnCode(slot.ToString()), speed, false);
                }

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

            var animation = entity.GetCarried(slot)?.GetCarryableBehavior()?.Slots?[slot]?.Animation;
            if (animation != null) entity.StopAnimation(animation);

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove("walkspeed", CarryOnCode(slot.ToString()));
                agent.Stats.Remove("hungerrate", CarryOnCode(slot.ToString()));

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

        private void ApplyHungerRate(EntityAgent agent, CarrySlot slot, SlotSettings? slotSettings = null, ItemStack? stack = null, BlockBehaviorCarryable? behavior = null)
        {
            var hungerRateConfig = config?.CarryHungerRate;
            if (hungerRateConfig == null) return;

            if (!hungerRateModifierResolver.IsEnabled(slot, hungerRateConfig)) return;

            var modifier = hungerRateModifierResolver.Resolve(stack, behavior, slotSettings, slot, hungerRateConfig);
            if (modifier <= 0f) return;

            if (hungerRateConfig.MinSaturationThreshold > 0f)
            {
                var hunger = agent.GetBehavior("hunger");
                if (hunger != null)
                {
                    var saturationProp = hunger.GetType().GetProperty("Saturation");
                    if (saturationProp != null)
                    {
                        var saturation = (float)saturationProp.GetValue(hunger)!;
                        if (saturation < hungerRateConfig.MinSaturationThreshold)
                            modifier = 0f;
                    }
                }
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
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
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Services
{
    internal sealed class CarryStateService
    {
        private const string AttrAnimation = "Animation";

        private readonly IConfigProvider configProvider;
        private readonly ICoreAPI api;
        private IServerNetworkChannel? serverChannel;

        private readonly WalkSpeedModifierResolver walkSpeedModifierResolver = new();
        private readonly HungerRateModifierResolver hungerRateModifierResolver = new();

        public CarryStateService(ICoreAPI api, IConfigProvider configProvider, IServerNetworkChannel? serverChannel)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(configProvider);
            this.api = api;
            this.configProvider = configProvider;
            this.serverChannel = serverChannel;
        }

        /// <summary>
        /// Configures the server network channel used to send hotbar lock-state packets.
        /// </summary>
        /// <param name="serverChannel">The server network channel.</param>
        internal void ConfigureServerChannel(IServerNetworkChannel serverChannel)
        {
            ArgumentNullException.ThrowIfNull(serverChannel);
            this.serverChannel = serverChannel;
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
            => ReadCarried(entity, slot, logFailure: true)?.Carried;

        /// <summary>
        /// Gets the full protected read result of a carry slot, including the untouched raw slot
        /// tree, so callers can distinguish a present-but-unusable carry (e.g. <c>missing-block</c>
        /// from a removed/disabled mod) from an empty slot without collapsing the distinction.
        /// </summary>
        /// <param name="entity">The entity to inspect.</param>
        /// <param name="slot">The slot to read.</param>
        /// <param name="rawTree">The untouched slot attribute tree when the slot is present, otherwise null.</param>
        /// <returns>The read result (never null; empty when the slot is absent).</returns>
        public CarriedBlockReadResult GetCarriedReadResult(Entity entity, CarrySlot slot, out ITreeAttribute? rawTree)
        {
            ArgumentNullException.ThrowIfNull(entity);
            rawTree = null;

            var entityCarriedKey = AttributeKeys.Watched.EntityCarried;
            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute == null) return CarriedBlockReadResult.Empty("no-slot");

            rawTree = slotAttribute;
            return ReadCarried(entity, slot, slotAttribute, logFailure: true);
        }

        /// <summary>
        /// Checks whether a carry slot is occupied without parsing or logging the carried tree.
        /// A slot holding a present-but-unreadable carry (e.g. a block whose mod was removed)
        /// still counts as occupied.
        /// </summary>
        public bool IsCarrySlotOccupied(Entity entity, CarrySlot slot)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return entity.WatchedAttributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried, slot.ToString()) != null;
        }

        /// <summary>
        /// Gets the carried block for a slot as it should be rendered. On the client only, a slot
        /// holding a present-but-unreadable carried tree (e.g. a block from a removed or disabled
        /// mod) is substituted with a render-only placeholder so the player can see something is
        /// carried. The underlying carried data is never modified; all non-render consumers keep
        /// using <see cref="GetCarried"/>. On the server this behaves exactly like
        /// <see cref="GetCarried"/>.
        /// </summary>
        public CarriedBlock? GetRenderableCarried(Entity entity, CarrySlot slot)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (entity.World.Side != EnumAppSide.Client) return GetCarried(entity, slot);

            var entityCarriedKey = AttributeKeys.Watched.EntityCarried;
            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute == null) return null;

            var result = ReadCarried(entity, slot, slotAttribute, logFailure: false);
            if (result.IsFailed)
                return BuildRenderPlaceholder(slot);

            return result.Carried;
        }

        private CarriedBlockReadResult? ReadCarried(Entity entity, CarrySlot slot, bool logFailure)
        {
            ArgumentNullException.ThrowIfNull(entity);

            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried, slot.ToString());
            if (slotAttribute == null) return null;

            return ReadCarried(entity, slot, slotAttribute, logFailure);
        }

        /// <summary>
        /// Carries that cannot be read keep failing on every read while the underlying cause (e.g. a
        /// removed mod) persists, and several client systems legitimately poll the slots every frame.
        /// Log the failure once per entity+slot until a read of that slot succeeds again, so missing
        /// blocks produce one diagnostic line instead of per-frame spam.
        /// </summary>
        private readonly HashSet<string> failedReadLogKeys = new();

        private static string FailedReadLogKey(Entity entity, CarrySlot slot)
            => $"{entity.EntityId}:{slot}";

        private CarriedBlockReadResult ReadCarried(Entity entity, CarrySlot slot, ITreeAttribute slotAttribute, bool logFailure)
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                AdoptLegacyData(entity, slot, slotAttribute);
            }

            var result = CarriedBlockTreeSerializer.DeserializeSafe(slotAttribute, api, slot);
            if (result.IsFailed)
            {
                if (logFailure && failedReadLogKeys.Add(FailedReadLogKey(entity, slot)))
                {
                    if (entity.World.Side == EnumAppSide.Server)
                    {
                        api.Logger.Warning(
                            $"carryon: carried data in slot {slot} of entity {entity.EntityId} could not be read ({result.Reason}); carried state was left untouched. Inspect with '/carryon debug dump'.");
                    }
                    else
                    {
                        api.Logger.Debug($"carryon: carried data in slot {slot} could not be read client-side ({result.Reason}).");
                    }
                }
            }
            else
            {
                failedReadLogKeys.Remove(FailedReadLogKey(entity, slot));
            }

            return result;
        }

        private ItemStack? renderPlaceholderStack;

        /// <summary>
        /// Builds a render-only placeholder carried block for a slot whose real block is missing
        /// (removed/disabled mod). Uses the opaque <c>carryon:carriedplaceholder</c> block so the
        /// player sees a recognizable "something is carried" shape on hands/back; the placeholder is
        /// never placed, dropped or serialized.
        /// </summary>
        private CarriedBlock? BuildRenderPlaceholder(CarrySlot slot)
        {
            if (renderPlaceholderStack == null && api.World.GetBlock(new AssetLocation("carryon", "carriedplaceholder")) is { } placeholderBlock)
            {
                renderPlaceholderStack = new ItemStack(placeholderBlock);
            }

            return renderPlaceholderStack == null ? null : new CarriedBlock(slot, renderPlaceholderStack, null);
        }

        /// <summary>
        /// Migrates 1.14-era carried data into the 2.0 watched layout. 1.14 stored the block
        /// entity <c>Data</c> in the non-synced legacy store (<see cref="Entity.Attributes"/>)
        /// while 2.0 reads it from the watched subtree; without adoption the slot resurfaces
        /// as an empty container after an upgrade. Only acts when the
        /// watched slot carries a <c>Stack</c> but no <c>Data</c>, so already-2.0 trees and
        /// genuinely content-less slots are never touched. The legacy copy is kept until a
        /// later safe point (format watermark) for forensic recovery.
        /// </summary>
        private void AdoptLegacyData(Entity entity, CarrySlot slot, ITreeAttribute slotAttribute)
        {
            if (slotAttribute[AttributeKeys.CarriedBlockData.Data] is ITreeAttribute) return;
            if (!slotAttribute.HasAttribute(AttributeKeys.CarriedBlockData.Stack)) return;

            var legacySlot = entity.Attributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried, slot.ToString());
            var legacyData = legacySlot?[AttributeKeys.CarriedBlockData.Data] as ITreeAttribute;
            if (legacyData == null) return;

            slotAttribute[AttributeKeys.CarriedBlockData.Data] = legacyData;
            entity.WatchedAttributes.MarkPathDirty(AttributeKeys.Watched.EntityCarried);

            api.Logger.Notification(
                $"carryon migration: adopted legacy 1.14 block entity data into carried slot {slot} of entity {entity.EntityId}; the legacy copy is retained for recovery.");
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
            if (stack == null) return;
            var behavior = stack.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);
            var slotSettings = behavior.Slots[slot];

            SerializeCarriedAttributes(entity, carriedBlock, slot);

            if (entity.Api.Side == EnumAppSide.Server && markDirty) TouchCarriedAttributes(entity);

            ApplySlotAnimation(entity, slot, slotSettings?.Animation);
            ApplySlotEffects(entity, slot, slotSettings, stack, behavior);
        }

        private void SerializeCarriedAttributes(Entity entity, CarriedBlock carriedBlock, CarrySlot slot)
        {
            var entityCarriedKey = AttributeKeys.Watched.EntityCarried;
            entity.WatchedAttributes.Set(carriedBlock.ItemStack, entityCarriedKey, slot.ToString(), AttributeKeys.CarriedBlockData.Stack);

            var blockEntityData = carriedBlock.BlockEntityData;
            if (blockEntityData != null)
            {
                entity.WatchedAttributes.Set(blockEntityData, entityCarriedKey, slot.ToString(), AttributeKeys.CarriedBlockData.Data);

                // Downgrade guard: mirror Data into the legacy (1.14) store so a later 1.14
                // downgrade sees its expected layout. 1.14 still reads Stack from the watched
                // tree, so mirroring Data alone is sufficient.
                entity.Attributes.Set(blockEntityData, entityCarriedKey, slot.ToString(), AttributeKeys.CarriedBlockData.Data);
            }
            else if (entity.Attributes.TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString()) is { } legacySlot)
            {
                legacySlot.RemoveAttribute(AttributeKeys.CarriedBlockData.Data);
            }

            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(entityCarriedKey, slot.ToString());
            if (slotAttribute != null)
            {
                slotAttribute.RemoveAttribute(AttributeKeys.CarriedBlockData.Children);

                var childrenTree = CarriedBlockTreeSerializer.BuildAttachedBlocksTree(carriedBlock.AttachedBlocks);
                if (childrenTree != null)
                    slotAttribute[AttributeKeys.CarriedBlockData.Children] = childrenTree;

                var originalBlockCode = carriedBlock.OriginalBlockCode;
                if (originalBlockCode != null)
                    slotAttribute.SetString(AttributeKeys.CarriedBlockData.OriginalBlockCode, originalBlockCode.ToString());
                else
                    slotAttribute.RemoveAttribute(AttributeKeys.CarriedBlockData.OriginalBlockCode);

                var originalItemCode = carriedBlock.ItemStack.Class == EnumItemClass.Item
                    ? carriedBlock.ItemStack.Collectible?.Code
                    : null;
                if (originalItemCode != null)
                    slotAttribute.SetString(AttributeKeys.CarriedBlockData.OriginalItemCode, originalItemCode.ToString());
                else
                    slotAttribute.RemoveAttribute(AttributeKeys.CarriedBlockData.OriginalItemCode);

                var originalMeshAngle = carriedBlock.OriginalMeshAngle;
                if (originalMeshAngle.HasValue)
                    slotAttribute.SetFloat(AttributeKeys.CarriedBlockData.OriginalMeshAngle, originalMeshAngle.Value);
                else
                    slotAttribute.RemoveAttribute(AttributeKeys.CarriedBlockData.OriginalMeshAngle);

                if (carriedBlock.SpawnerFactoryCode != null)
                    slotAttribute.SetString(AttributeKeys.CarriedBlockData.SpawnerFactoryCode, carriedBlock.SpawnerFactoryCode);
                else
                    slotAttribute.RemoveAttribute(AttributeKeys.CarriedBlockData.SpawnerFactoryCode);

                var spawnAsEntityOnPlacement = carriedBlock.SpawnAsEntityOnPlacement;
                if (spawnAsEntityOnPlacement.HasValue)
                    slotAttribute.SetBool(AttributeKeys.CarriedBlockData.SpawnAsEntityOnPlacement, spawnAsEntityOnPlacement.Value);
                else
                    slotAttribute.RemoveAttribute(AttributeKeys.CarriedBlockData.SpawnAsEntityOnPlacement);
            }
        }

        private void ApplySlotAnimation(Entity entity, CarrySlot slot, string? animation)
        {
            if (animation == null) return;

            entity.StartAnimation(animation);
            var slotAttr = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried, slot.ToString());
            slotAttr?.SetString(AttrAnimation, animation);
        }

        private void ApplySlotEffects(Entity entity, CarrySlot slot, SlotSettings? slotSettings, ItemStack stack, BlockBehaviorCarryable behavior)
        {
            if (entity is not EntityAgent agent) return;

            ApplyWalkSpeed(agent, slot, slotSettings, stack, behavior);
            ApplyHungerRate(agent, slot, slotSettings, stack, behavior);

            if (entity.Api.Side == EnumAppSide.Server)
            {
                // Hotbar lock logic:
                // Hands slot locks both hands (player carries in front, collected items cannot go into those slots).
                if (slot == CarrySlot.Hands)
                {
                    LockedItemSlot.Lock(agent.RightHandItemSlot);
                    LockedItemSlot.Lock(agent.LeftHandItemSlot);
                }
                SendLockSlotsMessage(agent as EntityPlayer);
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
                AttributeKeys.Watched.EntityCarried, slot.ToString());
            var animation = slotAttribute?.GetString(AttrAnimation);
            if (animation != null && slotAttribute != null)
            {
                entity.StopAnimation(animation);
                slotAttribute.RemoveAttribute(AttrAnimation);
            }

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove(CarryCodes.Stats.WalkSpeed, GetCarryCode(slot.ToString()));
                agent.Stats.Remove(CarryCodes.Stats.HungerRate, GetCarryCode(slot.ToString()));
            }

            entity.WatchedAttributes.Remove(AttributeKeys.Watched.EntityCarried, slot.ToString());
            if (markDirty) TouchCarriedAttributes(entity);
            entity.Attributes.Remove(AttributeKeys.Watched.EntityCarried, slot.ToString());

            // Restore hand locks when no carried block remains in the Hands slot.
            // Only Hands locks hotbar slots, so this covers all lock/unlock transitions
            // including desync recovery when the client and server state diverge.
            if (entity is EntityAgent agentForLocks && GetCarried(entity, CarrySlot.Hands) == null)
            {
                LockedItemSlot.Restore(agentForLocks.RightHandItemSlot);
                LockedItemSlot.Restore(agentForLocks.LeftHandItemSlot);
            }

            SendLockSlotsMessage(entity as EntityPlayer);
        }

        /// <summary>
        /// Swaps carried blocks between two slots. A slot holding a present-but-unreadable carry
        /// (e.g. a block whose mod was removed) is moved as its untouched raw tree so nothing is
        /// lost, since its content can no longer be deserialized and re-serialized.
        /// </summary>
        /// <param name="entity">The entity whose slots are swapped.</param>
        /// <param name="first">First slot.</param>
        /// <param name="second">Second slot.</param>
        /// <returns>True when at least one slot changed; otherwise false.</returns>
        public bool SwapCarried(Entity entity, CarrySlot first, CarrySlot second)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (first == second) throw new ArgumentException("Slots can't be the same");

            bool isServer = entity.Api.Side == EnumAppSide.Server;

            var firstTree = SlotTree(entity, first);
            var secondTree = SlotTree(entity, second);
            if (firstTree == null && secondTree == null) return false;

            var carriedFirst = firstTree == null ? null : ReadCarried(entity, first, firstTree, logFailure: false)?.Carried;
            var carriedSecond = secondTree == null ? null : ReadCarried(entity, second, secondTree, logFailure: false)?.Carried;

            bool firstUnreadable = firstTree != null && carriedFirst == null;
            bool secondUnreadable = secondTree != null && carriedSecond == null;

            // Both slots are present but cannot be deserialized: exchange the untouched raw trees
            // verbatim, preserving stack + block entity data + attached children for later pickup.
            if (firstUnreadable && secondUnreadable)
            {
                SwapRawSlotTrees(entity, first, second);
                if (isServer) TouchCarriedAttributes(entity);
                SyncLocksAfterSwap(entity);
                return true;
            }

            // A readable carried block may only be moved into a slot its carryable behavior allows.
            bool canSetFirst = carriedFirst == null
                || carriedFirst.Block?.GetBehavior<BlockBehaviorCarryable>()?.Slots?[second] != null;
            bool canSetSecond = carriedSecond == null
                || carriedSecond.Block?.GetBehavior<BlockBehaviorCarryable>()?.Slots?[first] != null;
            if (!canSetFirst || !canSetSecond) return false;

            if (firstUnreadable || secondUnreadable)
            {
                // Mixed: move the unreadable raw tree verbatim, and re-home the readable carry via
                // the normal remove/set path with slot effects for its destination slot.
                if (firstUnreadable)
                {
                    MoveRawSlotTree(entity, first, second);
                    if (carriedSecond != null)
                    {
                        SetCarried(entity, carriedSecond, first, markDirty: false);
                    }
                }
                else
                {
                    MoveRawSlotTree(entity, second, first);
                    if (carriedFirst != null)
                    {
                        SetCarried(entity, carriedFirst, second, markDirty: false);
                    }
                }

                if (isServer) TouchCarriedAttributes(entity);
                SyncLocksAfterSwap(entity);
                return true;
            }

            RemoveCarried(entity, first, markDirty: false);
            RemoveCarried(entity, second, markDirty: false);

            if (carriedFirst != null) SetCarried(entity, carriedFirst, second, markDirty: false);
            if (carriedSecond != null) SetCarried(entity, carriedSecond, first, markDirty: false);

            if (isServer) TouchCarriedAttributes(entity);
            return true;
        }

        /// <inheritdoc/>
        public bool SetCarriedRaw(Entity entity, ITreeAttribute? rawTree, CarrySlot slot)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (rawTree == null
                || entity.Api.Side != EnumAppSide.Server
                || IsCarrySlotOccupied(entity, slot))
            {
                return false;
            }

            // The raw tree is adopted verbatim (no parse possible for an unreadable carry), so its
            // original animation key and any slot stat modifiers are stale and must be cleared.
            StopSlotAnimationAndStats(entity, slot);

            var watchedRoot = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried) ?? new TreeAttribute();
            watchedRoot[slot.ToString()] = rawTree;
            entity.WatchedAttributes[AttributeKeys.Watched.EntityCarried] = watchedRoot;

            if (entity.Attributes.TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried) is { } legacyRoot)
            {
                legacyRoot[slot.ToString()] = rawTree;
            }

            TouchCarriedAttributes(entity);

            if (entity is EntityAgent agent && slot == CarrySlot.Hands)
            {
                LockedItemSlot.Lock(agent.RightHandItemSlot);
                LockedItemSlot.Lock(agent.LeftHandItemSlot);
            }

            SendLockSlotsMessage(entity as EntityPlayer);
            return true;
        }

        private static ITreeAttribute? SlotTree(Entity entity, CarrySlot slot)
            => entity.WatchedAttributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried, slot.ToString());

        /// <summary>
        /// Stops the slot-attached animation (if any) and removes the slot-scoped walk/hunger
        /// stat modifiers, mirroring the cleanup <see cref="RemoveCarried"/> performs without
        /// removing the slot tree itself.
        /// </summary>
        private void StopSlotAnimationAndStats(Entity entity, CarrySlot slot)
        {
            var slotAttribute = entity.WatchedAttributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried, slot.ToString());
            var animation = slotAttribute?.GetString(AttrAnimation);
            if (animation != null && slotAttribute != null)
            {
                entity.StopAnimation(animation);
                slotAttribute.RemoveAttribute(AttrAnimation);
            }

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove(CarryCodes.Stats.WalkSpeed, GetCarryCode(slot.ToString()));
                agent.Stats.Remove(CarryCodes.Stats.HungerRate, GetCarryCode(slot.ToString()));
            }
        }

        /// <summary>
        /// Moves a present-but-unreadable carry's untouched raw tree (plus its legacy mirror, when
        /// present) from one slot to another, replacing whatever occupied the destination slot.
        /// The destination's prior content must have been captured or re-serialized by the caller.
        /// </summary>
        private void MoveRawSlotTree(Entity entity, CarrySlot fromSlot, CarrySlot toSlot)
        {
            StopSlotAnimationAndStats(entity, fromSlot);

            var root = entity.WatchedAttributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried);
            var tree = root?[fromSlot.ToString()] as ITreeAttribute;
            if (root == null || tree == null) return;

            root.RemoveAttribute(fromSlot.ToString());
            root[toSlot.ToString()] = tree;

            var legacyRoot = entity.Attributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried);
            if (legacyRoot != null && legacyRoot[fromSlot.ToString()] is ITreeAttribute legacyTree)
            {
                legacyRoot.RemoveAttribute(fromSlot.ToString());
                legacyRoot[toSlot.ToString()] = legacyTree;
            }
        }

        /// <summary>
        /// Exchanges the untouched raw carried trees (plus their legacy mirrors, when present)
        /// between two slots, preserving the raw content of both.
        /// </summary>
        private void SwapRawSlotTrees(Entity entity, CarrySlot first, CarrySlot second)
        {
            StopSlotAnimationAndStats(entity, first);
            StopSlotAnimationAndStats(entity, second);

            var root = entity.WatchedAttributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried);
            if (root == null) return;

            var firstTree = root[first.ToString()] as ITreeAttribute;
            var secondTree = root[second.ToString()] as ITreeAttribute;

            root.RemoveAttribute(first.ToString());
            root.RemoveAttribute(second.ToString());
            if (secondTree != null) root[first.ToString()] = secondTree;
            if (firstTree != null) root[second.ToString()] = firstTree;

            var legacyRoot = entity.Attributes?
                .TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried);
            if (legacyRoot != null)
            {
                var legacyFirst = legacyRoot[first.ToString()] as ITreeAttribute;
                var legacySecond = legacyRoot[second.ToString()] as ITreeAttribute;
                legacyRoot.RemoveAttribute(first.ToString());
                legacyRoot.RemoveAttribute(second.ToString());
                if (legacySecond != null) legacyRoot[first.ToString()] = legacySecond;
                if (legacyFirst != null) legacyRoot[second.ToString()] = legacyFirst;
            }
        }

        /// <summary>
        /// After a raw-tree swap the hotbar locks must follow the Hands slot's occupancy, since the
        /// normal <see cref="SetCarried"/>/<see cref="RemoveCarried"/> lock logic is bypassed.
        /// Server-only: locks live in server inventory state.
        /// </summary>
        private void SyncLocksAfterSwap(Entity entity)
        {
            if (entity.Api.Side != EnumAppSide.Server || entity is not EntityAgent agent) return;

            var handsOccupied = SlotTree(entity, CarrySlot.Hands) != null;
            if (handsOccupied)
            {
                LockedItemSlot.Lock(agent.RightHandItemSlot);
                LockedItemSlot.Lock(agent.LeftHandItemSlot);
            }
            else
            {
                LockedItemSlot.Restore(agent.RightHandItemSlot);
                LockedItemSlot.Restore(agent.LeftHandItemSlot);
            }

            SendLockSlotsMessage(entity as EntityPlayer);
        }

        /// <summary>
        /// Sends a hotbar lock-state packet to a server player.
        /// </summary>
        /// <param name="player">The server player to notify.</param>
        public void LockHotbarSlots(IServerPlayer player)
        {
            var hotbar = player.InventoryManager.GetHotbarInventory();
            var slots = Enumerable.Range(0, hotbar.Count).Where(i => hotbar[i] is LockedItemSlot).ToList();
            if (serverChannel == null)
            {
                api.Logger.Warning("CarryOn: LockHotbarSlots called before serverChannel configured");
                return;
            }

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

        private void ApplyWalkSpeed(EntityAgent agent, CarrySlot slot, SlotSettings? slotSettings, ItemStack? stack, BlockBehaviorCarryable? behavior)
        {
            var walkSpeedConfig = configProvider.Config.CarryWalkSpeed;
            if (walkSpeedConfig == null) return;

            if (!walkSpeedModifierResolver.IsEnabled(slot, walkSpeedConfig)) return;

            var speed = walkSpeedModifierResolver.Resolve(
                stack,
                behavior,
                slotSettings,
                slot,
                walkSpeedConfig);

            if (speed != 0.0f)
            {
                agent.Stats.Set(CarryCodes.Stats.WalkSpeed,
                    GetCarryCode(slot.ToString()), speed, false);
            }
        }

        private void ApplyHungerRate(EntityAgent agent, CarrySlot slot, SlotSettings? slotSettings, ItemStack? stack, BlockBehaviorCarryable? behavior)
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
                agent.Stats.Set(CarryCodes.Stats.HungerRate, GetCarryCode(slot.ToString()), modifier, true);
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

            var carriedRoot = entity.WatchedAttributes.TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried) ?? new TreeAttribute();
            var revision = carriedRoot.GetInt(AttributeKeys.CarriedRevision, 0);

            if (entity.World.Side == EnumAppSide.Server)
            {
                carriedRoot.SetInt(AttributeKeys.CarriedRevision, ++revision);
                entity.WatchedAttributes.Set(carriedRoot, AttributeKeys.Watched.EntityCarried);
                entity.WatchedAttributes.MarkPathDirty(AttributeKeys.Watched.EntityCarried);
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
            var carriedRoot = entity.WatchedAttributes.TryGet<ITreeAttribute>(AttributeKeys.Watched.EntityCarried);
            return carriedRoot?.GetInt(AttributeKeys.CarriedRevision, 0) ?? 0;
        }
    }
}

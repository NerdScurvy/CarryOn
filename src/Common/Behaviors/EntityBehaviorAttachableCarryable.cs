using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Behaviors
{
    /// <summary>
    /// Behavior-based slot handler for entities with an attachable inventory (boats, packs, etc.).
    /// Implements <see cref="ICarryableEntitySlotHandler"/> directly on the behavior; the base
    /// <see cref="EntityBehaviorCarryable"/> auto-detects this implementation when the entity has
    /// no "slotHandlerCode" configured. Per-entity JSON attributes allow attachable-specific
    /// configuration (e.g. "slotInteractDelay") without cluttering the shared carry behavior.
    /// </summary>
    public class EntityBehaviorAttachableCarryable : EntityBehavior, ICarryableEntitySlotHandler
    {
        public static string Name { get; } = GetCarryCode("attachablecarryable");

        private const string AttachedContainerWorkspacePrefix = "att-cont-workspace";
        private const int SlotModeInjectionDelayMs = 1000;

        private static ICarryManager? carryManager;
        private static IConfigProvider? configProvider;

        private EntityBehaviorAttachable? attachable;
        private EntityBehaviorSelectionBoxes? selectionBoxes;
        private EntityBehaviorSeatable? seatable;
        private EntityBehaviorOwnable? ownable;

        private float? slotInteractDelay;
        private HashSet<string>? carrySlotExact;
        private Regex[]? carrySlotPatterns;
        private bool slotModeInjectionScheduled;

        /// <summary>
        /// Initializes the behavior with the carry manager for carry operations and config access.
        /// Must be called during mod startup on both client and server.
        /// </summary>
        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
            configProvider = manager as IConfigProvider
                ?? throw new ArgumentException("manager must implement IConfigProvider", nameof(manager));
        }

        public EntityBehaviorAttachableCarryable(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => Name;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            if (attributes != null)
            {
                if (JsonHelper.TryGetFloat(attributes, "slotInteractDelay", out var sd))
                    slotInteractDelay = sd;

                if (JsonHelper.TryGetStringArray(attributes, "carrySlots", out var slotArray)
                    && slotArray != null && slotArray.Length > 0)
                {
                    var exact = new HashSet<string>(StringComparer.Ordinal);
                    var patterns = new List<Regex>();
                    foreach (var s in slotArray)
                    {
                        if (string.IsNullOrEmpty(s)) continue;
                        if (s.Contains('*'))
                            patterns.Add(new Regex(WildcardMatcher.WildcardToRegex(s), RegexOptions.None));
                        else
                            exact.Add(s);
                    }
                    carrySlotExact = exact.Count > 0 ? exact : null;
                    carrySlotPatterns = patterns.Count > 0 ? patterns.ToArray() : null;
                }
            }

            var e = entity;
            if (e == null) return;

            attachable = e.GetBehavior<EntityBehaviorAttachable>();
            selectionBoxes = e.GetBehavior<EntityBehaviorSelectionBoxes>();
            seatable = e.GetBehavior<EntityBehaviorSeatable>();
            ownable = e.GetBehavior<EntityBehaviorOwnable>();

            // Backwards compatibility: old patches only specify this behavior. Inject the base
            // carry behavior in slot mode (no slotHandlerCode); this behavior is auto-detected
            // as the slot handler, so no code reference is needed.
            if (e.GetBehavior<EntityBehaviorCarryable>() == null)
            {
                DeferSlotModeInjection(e);
            }
        }

        /// <summary>
        /// Schedules the backwards-compat slot-mode <see cref="EntityBehaviorCarryable"/> injection
        /// once per pending callback, for a short delay after the entity's behavior collection is
        /// stable. Injecting during <see cref="Initialize"/> would modify the collection while it is
        /// still being initialized; the deferred callback runs after the spawn pipeline has settled.
        /// One-shot: at most one callback is pending per behavior instance, so repeated
        /// <see cref="Initialize"/> calls (spawn and chunk load) do not stack callbacks. After the
        /// pending callback runs, a later <see cref="Initialize"/> that still finds no base carry
        /// behavior re-schedules. No-op if the entity already has the behavior (e.g. both behaviors
        /// configured in JSON) or despawns first.
        /// </summary>
        private void DeferSlotModeInjection(Entity e)
        {
            var api = e.Api;
            if (api == null || slotModeInjectionScheduled) return;

            slotModeInjectionScheduled = true;
            api.Event.RegisterCallback(_ =>
            {
                slotModeInjectionScheduled = false;
                var current = entity;
                if (current == null || current.State == EnumEntityState.Despawned) return;
                if (current.GetBehavior<EntityBehaviorCarryable>() != null) return;
                current.AddBehavior(EntityBehaviorCarryable.CreateSlotMode(current));
            }, SlotModeInjectionDelayMs);
        }

        /// <inheritdoc/>
        public float GetInteractDelay(Entity entity, ICoreAPI api, int slotIndex)
            => slotInteractDelay ?? Defaults.PickUpSpeed;

        /// <inheritdoc/>
        public int GetSlotIndex(Entity entity, int selectionBoxIndex)
        {
            EnsureCachedBehaviors();

            // Selection box 0 is the entity body; storage boxes start at 1.
            if (selectionBoxIndex <= 0) return -1;
            int boxIndex = selectionBoxIndex - 1;

            // The logical slot index (0-based position in the attachable's inventory).
            var slotIndex = attachable?.GetSlotIndexFromSelectionBoxIndex(boxIndex) ?? -1;
            if (slotIndex < 0) return -1;

            if (carrySlotExact != null || carrySlotPatterns != null)
            {
                var apCode = GetAttachPointCode(boxIndex);
                if (apCode == null || !IsCarrySlot(apCode))
                    return -1;
            }

            return slotIndex;
        }

        /// <inheritdoc/>
        public bool IsValidSlot(Entity entity, int slotIndex)
        {
            EnsureCachedBehaviors();
            return GetSlot(slotIndex) != null;
        }

        /// <inheritdoc/>
        public bool IsSlotEmpty(Entity entity, int slotIndex)
        {
            EnsureCachedBehaviors();
            var slot = GetSlot(slotIndex);
            return slot != null && slot.Empty;
        }

        /// <inheritdoc/>
        public bool IsSlotCarryable(Entity entity, int slotIndex)
        {
            EnsureCachedBehaviors();
            var slot = GetSlot(slotIndex);
            return slot != null
                && !slot.Empty
                && slot.Itemstack?.Block?.HasBehavior<BlockBehaviorCarryable>() == true;
        }

        /// <inheritdoc/>
        public Vec3d? GetHelpPosition(Entity entity, int selectionBoxIndex)
        {
            EnsureCachedBehaviors();

            // Selection box 0 is the entity body; storage boxes start at 1.
            if (selectionBoxIndex <= 0) return null;
            return selectionBoxes?.GetCenterPosOfBox(selectionBoxIndex - 1)?.Add(0, 0.5, 0);
        }

        /// <inheritdoc/>
        public bool CanAttach(Entity entity, int slotIndex, IPlayer player, ICoreAPI api, out string failureCode)
        {
            EnsureCachedBehaviors();

            failureCode = FailureCodes.Ignore;
            if (slotIndex < 0)
            {
                failureCode = FailureCodes.SlotNotFound;
                return false;
            }

            var manager = carryManager;
            if (manager == null)
            {
                failureCode = FailureCodes.Internal;
                return false;
            }

            if (!ValidateTarget(player, out failureCode)) return false;

            if (attachable == null)
            {
                failureCode = FailureCodes.AttachableNotFound;
                return false;
            }

            var carriedBlock = manager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedBlock == null)
            {
                return false;
            }

            if (carriedBlock.BlockEntityData == null)
            {
                // No block-entity data means either an entity-carried block (custom entity
                // pickup, SpawnerFactoryCode set) that cannot round-trip through a slot inventory,
                // or a plain block with no block entity at all. Distinguish so the player sees
                // why the attach is rejected instead of a misleading "data missing" message.
                failureCode = carriedBlock.SpawnerFactoryCode != null
                    ? FailureCodes.SlotEntityNotAttachable
                    : FailureCodes.SlotIncompatibleBlock;
                return false;
            }

            var targetSlot = GetSlot(slotIndex);
            if (targetSlot == null || !targetSlot.Empty)
            {
                failureCode = FailureCodes.SlotNotEmpty;
                return false;
            }

            if (IsSeatOccupied(slotIndex))
            {
                failureCode = FailureCodes.SlotNotEmpty;
                return false;
            }

            var sourceItemSlot = PrepareAttachItemSlot(carriedBlock);
            if (sourceItemSlot == null)
            {
                // ToAttachItemSlot only fails on a missing Attributes tree - the BE-data null
                // case is intercepted above. Keep the internal data-missing code for this guard.
                failureCode = FailureCodes.SlotDataMissing;
                return false;
            }

            return ValidateAttachGates(entity, slotIndex, sourceItemSlot, targetSlot, carriedBlock, out failureCode);
        }

        /// <inheritdoc/>
        public bool TryAttach(Entity entity, int slotIndex, IPlayer player, ICoreAPI api, out string failureCode)
        {
            EnsureCachedBehaviors();

            if (!CanAttach(entity, slotIndex, player, api, out failureCode)) return false;

            var manager = carryManager;
            var attachable = this.attachable;
            if (manager == null || attachable == null)
            {
                failureCode = FailureCodes.Internal;
                return false;
            }

            var carriedBlock = manager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedBlock == null)
            {
                failureCode = FailureCodes.SlotDataMissing;
                return false;
            }

            var targetSlot = GetSlot(slotIndex);
            var sourceItemSlot = PrepareAttachItemSlot(carriedBlock);
            if (targetSlot == null || sourceItemSlot == null)
            {
                failureCode = FailureCodes.SlotDataMissing;
                return false;
            }

            var moved = sourceItemSlot.TryPutInto(entity.World, targetSlot) > 0;
            if (!moved)
            {
                failureCode = FailureCodes.AttachFailed;
                return false;
            }

            attachable.storeInv();
            AttachableEntitySync.MarkDirty(entity);

            manager.RemoveCarried(player.Entity, CarrySlot.Hands);

            targetSlot.Itemstack?.Collectible.GetCollectibleInterface<IAttachedListener>()?.OnAttached(targetSlot, slotIndex, entity, player.Entity);

            PlayPlaceSoundAt(carriedBlock.Block, entity, api);

            api.World.Logger.Audit($"[{ModId}] Player {player?.PlayerName} attached block {carriedBlock.Block.Code} to entity {entity.EntityId} {entity.GetName()} slot {slotIndex} at position {entity.Pos.AsBlockPos}");
            return true;
        }

        /// <inheritdoc/>
        public bool CanDetach(Entity entity, int slotIndex, IPlayer player, ICoreAPI api, out string failureCode)
        {
            EnsureCachedBehaviors();

            failureCode = FailureCodes.Ignore;
            if (slotIndex < 0)
            {
                failureCode = FailureCodes.SlotNotFound;
                return false;
            }

            var manager = carryManager;
            if (manager == null)
            {
                failureCode = FailureCodes.Internal;
                return false;
            }

            if (!ValidateTarget(player, out failureCode)) return false;

            if (attachable == null)
            {
                failureCode = FailureCodes.AttachableNotFound;
                return false;
            }

            var sourceSlot = GetSlot(slotIndex);
            if (sourceSlot == null || sourceSlot.Empty)
            {
                failureCode = FailureCodes.SlotEmpty;
                return false;
            }

            if (!sourceSlot.CanTake())
            {
                failureCode = FailureCodes.DetachUnavailable;
                return false;
            }

            var block = sourceSlot.Itemstack?.Block;
            var isCreative = player.WorldData?.CurrentGameMode == EnumGameMode.Creative;

            // An item-class stack (e.g. a seat placed in an attachable slot) is not a carried
            // block at all: its Block is null by construction, so it is never "unresolvable" and
            // must not be treated as one. Never CarryOn-detachable, in any game mode.
            if (sourceSlot.Itemstack?.Class != EnumItemClass.Block)
            {
                return false;
            }

            // Creative-only recovery for attached blocks that can no longer resolve: a stack
            // whose block's mod was removed/disabled surfaces with the missing-block placeholder
            // (IsMissing) or, worse, no block at all, and cannot render in the slot. Let a
            // creative player take it out so it is never stranded. A loaded block that is merely
            // not carryable stays non-detachable in every game mode.
            if (block != null
                && !block.HasBehavior<BlockBehaviorCarryable>()
                && !(isCreative && block.IsMissing))
            {
                failureCode = FailureCodes.SlotNotCarryable;
                return false;
            }

            if (block == null && !isCreative)
            {
                return false;
            }

            if (manager.GetCarried(player.Entity, CarrySlot.Hands) != null)
            {
                return false;
            }

            failureCode = FailureCodes.Ignore;
            return true;
        }

        /// <inheritdoc/>
        public bool TryDetach(Entity entity, int slotIndex, IPlayer player, ICoreAPI api, out string failureCode)
        {
            EnsureCachedBehaviors();

            if (!CanDetach(entity, slotIndex, player, api, out failureCode)) return false;

            var manager = carryManager;
            var attachable = this.attachable;
            if (manager == null || attachable == null)
            {
                failureCode = FailureCodes.Internal;
                return false;
            }

            var sourceSlot = GetSlot(slotIndex);
            var itemstack = sourceSlot?.Itemstack;
            var block = itemstack?.Block;
            if (sourceSlot == null || itemstack == null)
            {
                failureCode = FailureCodes.SlotEmpty;
                return false;
            }

            var carriedBlock = AttachableCarryConversion.ItemStackToCarriedBlock(itemstack);
            if (carriedBlock == null
                && itemstack.Class == EnumItemClass.Block
                && (itemstack.Block == null || itemstack.Block.IsMissing))
            {
                // Creative recovery of an attached block that cannot resolve (removed/disabled
                // mod): the stack re-resolves to vanilla's missing-block placeholder, which is a
                // non-null Block with a null Code (IsMissing) - carried verbatim as a raw
                // unreadable carry so it can be dropped/quick-dropped and re-renders (or fully
                // restores) once the mod returns. Contents are preserved through the carried Data
                // tree (backpack/carryonbackup).
                var rawTree = AttachableCarryConversion.ItemStackToCarriedTree(itemstack);
                if (rawTree == null || !manager.SetCarriedRaw(player.Entity, rawTree, CarrySlot.Hands))
                {
                    failureCode = FailureCodes.DetachUnavailable;
                    return false;
                }
            }
            else if (carriedBlock == null)
            {
                failureCode = FailureCodes.DetachUnavailable;
                return false;
            }
            else
            {
                manager.SetCarried(player.Entity, carriedBlock);
            }

            PlayPlaceSoundAt(block, entity, api);

            itemstack.Collectible.GetCollectibleInterface<IAttachedListener>()?.OnDetached(sourceSlot, slotIndex, entity, player.Entity);

            ClearCachedSlotStorage(api, slotIndex, entity, sourceSlot.Itemstack);
            sourceSlot.Itemstack = null;
            attachable.storeInv();

            AttachableEntitySync.MarkDirty(entity);
            var detachedCode = block?.Code ?? itemstack.Collectible?.Code;
            api.World.Logger.Audit($"[{ModId}] Player {player?.PlayerName} detached block {detachedCode} from entity {entity.EntityId} {entity.GetName()} slot {slotIndex} at position {entity.Pos.AsBlockPos}");

            return true;
        }

        /// <inheritdoc/>
        public void OnClientAttachmentToggled(Entity entity, int slotIndex, IPlayer player, bool isAttached)
        {
            EnsureCachedBehaviors();

            if (entity == null || player == null || slotIndex < 0) return;

            var slot = GetSlot(slotIndex);
            if (slot == null) return;

            var listener = slot.Itemstack?.Collectible?.GetCollectibleInterface<IAttachedListener>();
            if (isAttached)
                listener?.OnAttached(slot, slotIndex, entity, player.Entity);
            else
                listener?.OnDetached(slot, slotIndex, entity, player.Entity);

            AttachableEntitySync.MarkDirty(entity);

            if (!isAttached)
            {
                ClearCachedSlotStorage(entity.World.Api, slotIndex, entity, slot.Itemstack);
            }
        }

        private string? GetAttachPointCode(int boxIndex)
        {
            var boxes = selectionBoxes?.selectionBoxes;
            if (boxes == null || boxIndex < 0 || boxIndex >= boxes.Length) return null;
            return boxes[boxIndex].AttachPoint?.Code;
        }

        /// <summary>
        /// Re-resolves the cached vanilla behaviors on miss so a null capture from
        /// <see cref="Initialize"/> (e.g. behaviors injected after this behavior was initialized,
        /// or a changed JSON ordering) does not permanently disable slot functionality.
        /// Mirrors the lazy re-scan used by <see cref="EntityBehaviorCarryable.EnsureSlotHandler"/>.
        /// </summary>
        private void EnsureCachedBehaviors()
        {
            var e = entity;
            if (e == null) return;

            attachable ??= e.GetBehavior<EntityBehaviorAttachable>();
            selectionBoxes ??= e.GetBehavior<EntityBehaviorSelectionBoxes>();
            seatable ??= e.GetBehavior<EntityBehaviorSeatable>();
            ownable ??= e.GetBehavior<EntityBehaviorOwnable>();
        }

        private ItemSlot? GetSlot(int slotIndex)
        {
            var inv = attachable?.Inventory;
            if (inv == null || slotIndex < 0 || slotIndex >= inv.Count) return null;
            return inv[slotIndex];
        }

        private bool IsCarrySlot(string apCode)
        {
            if (carrySlotExact != null && carrySlotExact.Contains(apCode)) return true;
            if (carrySlotPatterns != null)
            {
                foreach (var pattern in carrySlotPatterns)
                    if (pattern.IsMatch(apCode)) return true;
            }
            return false;
        }

        private static void ClearCachedSlotStorage(ICoreAPI api, int slotIndex, Entity entity, ItemStack? stack)
        {
            if (slotIndex < 0 || entity == null || stack == null) return;
            ObjectCacheUtil.Delete(api, AttachedContainerWorkspacePrefix + "-" + slotIndex + "-" + entity.EntityId + "-" + stack.Id);
        }

        private bool ValidateTarget(IPlayer player, out string failureCode)
        {
            EnsureCachedBehaviors();

            failureCode = FailureCodes.Ignore;

            var e = entity;
            if (e == null)
            {
                failureCode = FailureCodes.EntityNotFound;
                return false;
            }

            // Eye anchor and creative exemption both come from CarryReach; the code stays
            // EntityOutOfReach because the target here is an entity, not a block cell. A null Pos
            // still passes, as it did under the lifted comparison this replaces.
            if (e.Pos != null
                && !CarryReach.IsWithinReach(player.Entity, e.Pos.XYZ, GetMaxInteractionDistance()))
            {
                failureCode = FailureCodes.EntityOutOfReach;
                return false;
            }

            if (attachable == null)
            {
                failureCode = FailureCodes.AttachableNotFound;
                return false;
            }

            if (ownable != null && !ownable.IsOwner(player.Entity))
            {
                failureCode = FailureCodes.RequiresOwnership;
                return false;
            }

            return true;
        }

        /// <summary>
        /// True if a passenger sits in a seat that interacts with the given logical slot index
        /// (matching the seat's attach point - <see cref="SeatConfig.SelectionBox"/> or
        /// <see cref="SeatConfig.APName"/> - against the slot's attach point code). The slot index
        /// is resolved to its attach point code through the vanilla box→slot mapping
        /// (<see cref="EntityBehaviorAttachable.GetSlotIndexFromSelectionBoxIndex"/>), so the
        /// comparison stays correct even when box order and slot order differ.
        /// </summary>
        private bool IsSeatOccupied(int slotIndex)
        {
            var seatable = this.seatable;
            if (seatable == null) return false;

            var apCode = GetAttachPointCodeForSlot(slotIndex);
            if (apCode == null) return false;

            foreach (var seat in seatable.Seats)
            {
                if (seat.Passenger == null) continue;

                var cfg = seat.Config;
                if (cfg.SelectionBox == apCode || cfg.APName == apCode) return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the attach point code of the selection box that maps to the given logical slot
        /// index, inverting <see cref="EntityBehaviorAttachable.GetSlotIndexFromSelectionBoxIndex"/>
        /// (vanilla exposes no slot→box mapping). Returns null when no box maps to the slot.
        /// </summary>
        private string? GetAttachPointCodeForSlot(int slotIndex)
        {
            var attachable = this.attachable;
            var boxes = selectionBoxes?.selectionBoxes;
            if (attachable == null || boxes == null || slotIndex < 0) return null;

            for (var boxIndex = 0; boxIndex < boxes.Length; boxIndex++)
            {
                var apCode = boxes[boxIndex].AttachPoint?.Code;
                if (apCode == null) continue;
                if (attachable.GetSlotIndexFromSelectionBoxIndex(boxIndex) == slotIndex) return apCode;
            }
            return null;
        }

        private ItemSlot? PrepareAttachItemSlot(CarriedBlock carriedBlock)
            => AttachableCarryConversion.ToAttachItemSlot(carriedBlock);

        private bool ValidateAttachGates(Entity entity, int slotIndex, ItemSlot sourceItemSlot, ItemSlot targetSlot, CarriedBlock carriedBlock, out string failureCode)
        {
            var itemstack = sourceItemSlot.Itemstack!;

            if (!AttachableGateValidator.IsAttachable(entity, itemstack))
            {
                failureCode = FailureCodes.SlotIncompatibleBlock;
                return false;
            }

            if (AttachableGateValidator.IsRetrieveOnlyStorage(itemstack))
            {
                failureCode = FailureCodes.SlotIncompatibleBlock;
                return false;
            }

            if (!targetSlot.CanTakeFrom(sourceItemSlot))
            {
                failureCode = FailureCodes.SlotIncompatibleBlock;
                return false;
            }

            if (carriedBlock.HasAttachedBlocks)
            {
                failureCode = FailureCodes.BlockHasAttachedBlocks;
                return false;
            }

            var carryableBehavior = itemstack.Block.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior?.PreventAttaching ?? false)
            {
                failureCode = FailureCodes.SlotPreventAttaching;
                return false;
            }

            var iai = itemstack.Collectible.GetCollectibleInterface<IAttachedInteractions>();
            if (iai?.OnTryAttach(sourceItemSlot, slotIndex, entity) == false)
            {
                failureCode = FailureCodes.AttachUnavailable;
                return false;
            }

            failureCode = FailureCodes.Ignore;
            return true;
        }

        private void PlayPlaceSoundAt(Block? block, Entity targetEntity, ICoreAPI api)
        {
            var sound = block?.Sounds?.Place.Location ?? new AssetLocation(SoundPaths.DefaultPlace);
            api.World.PlaySoundAt(sound, targetEntity, null, true, 16);
        }

        private int GetMaxInteractionDistance()
            => configProvider?.Config.CarryOptions?.MaxInteractionDistance ?? Defaults.MaxInteractionDistance;
    }
}

using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Logic;
using CarryOn.Common.Interfaces;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Client.Logic.Interaction
{
    internal sealed class CarryInteractionValidator
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;
        private readonly TransferLogic transferLogic;
        private readonly ICarryInteractionController controller;

        private Type[] preventSwapFromBackOnBehaviors = [];
        private string[] preventSwapFromBackOnClasses = [];
        private string[] preventSwapFromBackOnCodes = [];

        public bool RemoveInteractDelayWhileCarrying => configProvider.Config.CarryOptions?.RemoveInteractDelayWhileCarrying ?? false;
        public bool BackSlotEnabled => configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;
        public float InteractSpeedMultiplier => configProvider.Config.CarryOptions?.InteractSpeedMultiplier ?? 1.0f;
        public bool OpenGroundStorageBagWhileCarrying => configProvider.Config.CarryOptions?.OpenGroundStorageBagWhileCarrying ?? true;
        public bool PickupGroundStorageBagWhileCarrying => configProvider.Config.CarryOptions?.PickupGroundStorageBagWhileCarrying ?? true;

        public CarryInteractionValidator(
            ICoreClientAPI api,
            ICarryManager carryManager,
            IConfigProvider configProvider,
            TransferLogic transferLogic,
            ICarryInteractionController controller)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            this.transferLogic = transferLogic ?? throw new ArgumentNullException(nameof(transferLogic));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));

            RefreshConfigCache();
        }

        public void RefreshConfigCache()
        {
            var entries = configProvider.Config?.CarryOptions?.PreventSwapFromBackOnTarget ?? Array.Empty<string>();
            const string behaviorPrefix = "behavior::";
            const string classPrefix = "class::";
            const string codePrefix = "code::";

            preventSwapFromBackOnBehaviors = entries
                .Where(x => x.StartsWith(behaviorPrefix))
                .Select(x => api.ClassRegistry.GetBlockBehaviorClass(x.Substring(behaviorPrefix.Length)))
                .Where(x => x != null)
                .ToArray();

            preventSwapFromBackOnClasses = entries
                .Where(x => x.StartsWith(classPrefix))
                .Select(x => x.Substring(classPrefix.Length))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            preventSwapFromBackOnCodes = entries
                .Where(x => x.StartsWith(codePrefix))
                .Select(x => x.Substring(codePrefix.Length))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        public BeginResult TryBeginInteraction(bool isInteracting)
        {
            if (controller.Interaction.Action != CarryAction.None)
            {
                return BeginResult.Handled();
            }

            var world = this.api.World;
            var player = world.Player;

            if (!player.Entity.CanDoCarryAction(requireEmptyHanded: true))
            {
                return BeginResult.NotHandled;
            }

            BeginResult? result = null;
            if (isInteracting)
            {
                result = BeginEntityCarriedBlockInteraction()
                    ?? BeginSwapBackInteraction()
                    ?? BeginBlockEntityInteraction()
                    ?? BeginTransferInteraction()
                    ?? BeginGroundStorageBagPickupInteraction()
                    ?? BeginBlockCarryableInteraction();
            }

            if (result != null)
            {
                return result;
            }

            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);

            if ((carriedHands != null) || (isInteracting && (controller.Interaction.TimeHeld > 0.0f)))
            {
                return BeginResult.Handled();
            }

            return BeginResult.NotHandled;
        }

        private BeginResult? BeginEntityCarriedBlockInteraction()
        {
            var player = this.api.World.Player;
            var entitySelection = player.CurrentEntitySelection;
            var entity = entitySelection?.Entity;
            if (entity == null) return null;

            var behavior = entity.GetBehavior<EntityBehaviorCarryable>();
            if (behavior == null) return BeginResult.Handled(false);

            // A slot handler takes precedence while the player looks at a storage slot:
            // per-slot attach/detach with the carry key held; without the key the vanilla
            // interaction (opening the mount's storage, mounting) proceeds.
            if (behavior.IsSlotInteractable)
            {
                var slotHandler = behavior.SlotHandler;
                if (slotHandler != null)
                {
                    var slotIndex = slotHandler.GetSlotIndex(entity, entitySelection!.SelectionBoxIndex);
                    if (slotIndex >= 0 && slotHandler.IsValidSlot(entity, slotIndex))
                    {
                        if (!api.Input.IsCarryKeyPressed()) return BeginResult.Handled(false);
                        return BeginEntitySlotInteraction(slotHandler, entitySelection!, slotIndex);
                    }
                }
            }

            // Not looking at a storage slot: the entity handler governs whole-entity pickup.
            if (!behavior.IsCarryInteractable) return BeginResult.Handled(false);

            var handler = behavior.Handler;
            if (handler == null) return BeginResult.Handled(false);

            if (behavior.PickupMode == EntityPickupMode.PickupHotkey
                && !api.Input.IsCarryKeyPressed())
            {
                return BeginResult.Handled(false);
            }

            if (!handler.CanPickup(
                player.WorldData.CurrentGameMode,
                player.PlayerUID,
                entity, this.api))
            {
                CarryErrorHelper.ShowError(this.api, FailureCodes.NotOwner, "pickup-not-owner");
                return BeginResult.Handled();
            }

            return BeginResult.Begin(new CarryInteraction
            {
                Action = CarryAction.PickupEntity,
                TargetEntity = entity
            });
        }

        private BeginResult? BeginEntitySlotInteraction(
            ICarryableEntitySlotHandler slotHandler, EntitySelection entitySelection,
            int slotIndex)
        {
            var player = this.api.World.Player;
            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);

            var entity = entitySelection.Entity;

            var interaction = new CarryInteraction
            {
                TargetSlotIndex = slotIndex,
                TargetEntity = entity
            };

            if (carriedHands != null)
            {
                if (!slotHandler.IsSlotEmpty(entity, slotIndex))
                {
                    CarryErrorHelper.ShowError(this.api, FailureCodes.SlotNotEmpty);
                    return BeginResult.Complete();
                }
                if (!slotHandler.CanAttach(entity, slotIndex, player, this.api, out var attachFailure))
                {
                    return FailSlotPreCheck(attachFailure);
                }
                interaction.Action = CarryAction.Attach;
            }
            else
            {
                // Creative may attempt a detach even when the slot is not carryable, but only if
                // CanDetach - the same behavior class the server uses - approves: that happens
                // solely for an attachable block that can no longer resolve (missing-block
                // placeholder / null block from a removed/disabled mod). Item-class stacks (e.g.
                // a seat) and loaded, merely non-carryable blocks stay non-detachable, and the
                // vanilla interaction proceeds as before.
                if (!slotHandler.IsSlotCarryable(entity, slotIndex)
                    && !(player.WorldData?.CurrentGameMode == EnumGameMode.Creative
                         && slotHandler.CanDetach(entity, slotIndex, player, this.api, out _)))
                {
                    CarryErrorHelper.ShowError(this.api, FailureCodes.SlotEmpty);
                    return BeginResult.Complete(preventDefault: false);
                }
                if (!slotHandler.CanDetach(entity, slotIndex, player, this.api, out var detachFailure))
                {
                    return FailSlotPreCheck(detachFailure);
                }
                interaction.Action = CarryAction.Detach;
            }

            return BeginResult.Begin(interaction);
        }

        private BeginResult FailSlotPreCheck(string failureCode)
        {
            if (failureCode != FailureCodes.Ignore)
            {
                CarryErrorHelper.ShowError(this.api, failureCode);
            }
            return BeginResult.Complete();
        }

        private BeginResult? BeginSwapBackInteraction()
        {
            var world = this.api.World;
            var player = world.Player;
            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            var carriedBack = carryManager.GetCarried(player.Entity, CarrySlot.Back);
            bool handsUnreadable = carriedHands == null && carryManager.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands);
            bool backUnreadable = carriedBack == null && carryManager.IsCarrySlotOccupied(player.Entity, CarrySlot.Back);

            bool handsPresent = carriedHands != null || handsUnreadable;
            bool backPresent = carriedBack != null || backUnreadable;

            // A bag carried in hands (picked up from ground storage) is stowed into the vanilla
            // backpack on pick-up + swap-back, never into the back slot. The gate is vanilla's
            // own worn-bag slot rule (storage flags - covers held-only bags like the linen
            // sack, which occupies the slot but never renders on the player). Re-route before
            // any back-slot gating so the modifier combo works regardless of BackSlotEnabled
            // and even when no block is targeted. The alternative gesture - carry key +
            // right click while targeting nothing - begins the same swap-back, mirroring the
            // generic branch below.
            if (carriedHands?.ItemStack.IsBackpackStowable() == true
                && api.Input.IsCarryKeyPressed()
                && (api.Input.IsCarrySwapBackKeyPressed() || player.CurrentBlockSelection == null))
            {
                if (!player.Entity.CanInteract(requireEmptyHanded: true))
                {
                    return null;
                }

                return BeginResult.Begin(new CarryInteraction
                {
                    Action = CarryAction.SwapBack,
                    Slot = CarrySlot.Hands
                });
            }

            var backSlotEnabled = configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;
            if (handsPresent && !backSlotEnabled) return null;

            if (!player.Entity.CanInteract(requireEmptyHanded: true))
            {
                return null;
            }

            var selection = MultiblockUtils.GetMultiblockOriginSelection(world.BlockAccessor, player?.CurrentBlockSelection);
            ItemStack? itemStack = selection?.Block.SafeOnPickBlock(world, selection.Position);

            bool canCarryTarget = selection?.Block?.CanCarryInSlot(CarrySlot.Hands, itemStack) == true;

            bool carryKeyHeld = api.Input.IsCarryKeyPressed();
            bool swapKeyPressed = api.Input.IsCarrySwapBackKeyPressed();
            bool notTargetingBlock = selection == null;
            bool canSwapBackFromBackSlot = !canCarryTarget && backPresent && !handsPresent;

            if (carryKeyHeld && (swapKeyPressed || notTargetingBlock || canSwapBackFromBackSlot))
            {

                if (!handsPresent && !notTargetingBlock)
                {
                    var isSwapPrevented = SelectionPreventsSwap(selection);

                    if (isSwapPrevented)
                    {
                        return null;
                    }
                }

                var interaction = new CarryInteraction
                {
                    Action = CarryAction.SwapBack,
                    Slot = CarrySlot.Hands
                };

                if (carriedHands != null)
                {
                    if (!carriedHands.GetCarryableBehavior().CanCarryInSlot(CarrySlot.Back, carriedHands.ItemStack))
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCodes.CannotSwapBack);
                        return BeginResult.Complete();
                    }
                }

                if (!handsPresent && !backPresent)
                {
                    CarryErrorHelper.ShowError(this.api, FailureCodes.NothingCarried);
                    return BeginResult.Complete();
                }

                return BeginResult.Begin(interaction);
            }

            return null;
        }

        private BeginResult? BeginBlockEntityInteraction()
        {
            var world = this.api.World;
            var player = world.Player;
            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);

            if (carriedHands == null)
            {
                return null;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: false))
            {
                var selection = player.CurrentBlockSelection;
                selection = MultiblockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);

                if (selection?.Block?.HasBehavior<BlockBehaviorCarryableInteract>() == true)
                {
                    var interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                    if (interactBehavior != null && selection?.Position != null && interactBehavior.CanInteract(player))
                    {
                        return BeginResult.Begin(new CarryInteraction
                        {
                            Action = CarryAction.Interact,
                            TargetBlockPos = selection.Position
                        });
                    }
                }

                if (OpenGroundStorageBagWhileCarrying
                    && selection?.Block is BlockGroundStorage
                    && selection?.Position != null
                    && player.Entity.Controls.CtrlKey)
                {
                    var beg = world.BlockAccessor.GetBlockEntity(selection.Position)
                        as BlockEntityGroundStorage;
                    if (IsValidGroundStorageBagTarget(selection))
                    {
                        return BeginResult.Begin(new CarryInteraction
                        {
                            Action = CarryAction.Interact,
                            TargetBlockPos = selection.Position
                        });
                    }
                }

            }
            return null;
        }

        /// <summary>
        /// Returns whether the given selection still resolves to a bag in ground storage while
        /// the open-bag-GUI config is enabled and Ctrl (the vanilla bag-GUI key) is held. Used to
        /// gate the begin interaction and to re-validate during the hold so that moving the cursor
        /// off the bag slot does not fall through to an unwanted vanilla pickup/stack interaction.
        /// </summary>
        public bool IsValidGroundStorageBagTarget(BlockSelection? selection)
        {
            if (selection?.Block is not BlockGroundStorage
                || selection?.Position == null
                || !OpenGroundStorageBagWhileCarrying)
            {
                return false;
            }

            var player = this.api.World.Player;
            if (!player.Entity.Controls.CtrlKey) return false;

            var beg = this.api.World.BlockAccessor.GetBlockEntity(selection.Position)
                as BlockEntityGroundStorage;
            if (beg == null) return false;
            return beg.IsGroundStorageHeldBagTargeted(selection);
        }

        /// <summary>
        /// Returns whether the given selection resolves to a bag item in a ground storage slot that
        /// may be picked into the hands carry slot: the bag pickup config is enabled, the player has
        /// empty hands, holds the carry key, is allowed to interact with an empty-handed active slot,
        /// and the targeted slot actually contains a bag.
        /// </summary>
        public bool IsValidGroundStorageBagPickupTarget(BlockSelection? selection)
        {
            if (selection?.Block is not BlockGroundStorage
                || selection?.Position == null
                || !PickupGroundStorageBagWhileCarrying)
            {
                return false;
            }

            var player = this.api.World.Player;
            if (!player.Entity.CanDoCarryAction(requireEmptyHanded: true)
                || carryManager.GetCarried(player.Entity, CarrySlot.Hands) != null
                || !api.Input.IsCarryKeyPressed()
                || !player.Entity.CanInteract(requireEmptyHanded: true))
            {
                return false;
            }

            var beg = this.api.World.BlockAccessor.GetBlockEntity(selection.Position)
                as BlockEntityGroundStorage;
            if (beg == null) return false;
            return beg.IsGroundStorageBagTargeted(selection);
        }

        private BeginResult? BeginGroundStorageBagPickupInteraction()
        {
            if (!PickupGroundStorageBagWhileCarrying) return null;

            var player = this.api.World.Player;
            if (carryManager.GetCarried(player.Entity, CarrySlot.Hands) != null) return null;

            if (!api.Input.IsCarryKeyPressed()) return null;

            var selection = player.CurrentBlockSelection;
            if (selection?.Block is not BlockGroundStorage || selection?.Position == null) return null;

            if (!player.Entity.CanInteract(requireEmptyHanded: true)) return null;

            var beg = api.World.BlockAccessor.GetBlockEntity(selection.Position)
                as BlockEntityGroundStorage;
            if (beg == null) return null;
            if (!beg.IsGroundStorageBagTargeted(selection)) return null;

            if (!carryManager.HasPermissionAt(player.Entity, selection.Position))
            {
                CarryErrorHelper.ShowError(this.api, FailureCodes.PickUpNoPermission);
                return BeginResult.Handled();
            }

            return BeginResult.Begin(new CarryInteraction
            {
                Slot = CarrySlot.Hands,
                Action = CarryAction.PickUpGroundStorageBag,
                TargetBlockPos = selection.Position.Copy()
            });
        }

        private BeginResult? BeginBlockCarryableInteraction()
        {
            var world = this.api.World;
            var player = world.Player;

            if (!api.Input.IsCarryKeyPressed())
            {
                return null;
            }

            var selection = player.CurrentBlockSelection;
            var handsRead = carryManager.GetCarriedReadResult(player.Entity, CarrySlot.Hands, out _);
            var carriedHands = handsRead.Carried;
            var handsUnreadable = handsRead.IsFailed && handsRead.Reason == FailureCodes.MissingBlock;

            if (api.Input.IsCarrySwapBackKeyPressed() && selection != null)
            {
                var swapCheckSelection = MultiblockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);
                if (SelectionPreventsSwap(swapCheckSelection))
                {
                    var carryable = swapCheckSelection?.Block?.GetBehavior<BlockBehaviorCarryable>();
                    var allowForcePickup = carryable?.ForcePickupOnSwapBack == true;

                    if (!allowForcePickup)
                    {
                        return null;
                    }
                }
            }

            if (carriedHands != null)
            {
                if (selection != null)
                {
                    if (!player.Entity.CanInteract(requireEmptyHanded: false))
                    {
                        return BeginResult.Handled();
                    }
                    // Item-class carries (e.g. a bag from ground storage) have no Block: their
                    // place-down target is the ground-storage cell the stack would occupy.
                    var blockPos = carriedHands.Block != null
                        ? BlockUtils.GetPlacedPosition(world.BlockAccessor, selection, carriedHands.Block)
                        : BlockUtils.GetGroundStoragePlacementPosition(world.BlockAccessor, selection);
                    if (blockPos == null)
                    {
                        // No place-down spot: the click must still be consumed. Letting it fall
                        // through to the vanilla interaction would bypass the carryableinteract
                        // hold delay - e.g. Medieval Architecture gates (IMultiBlockInteract) run
                        // OnBlockInteractStart on press and toggle in OnBlockInteractStop on
                        // release, opening/closing the gate with no progress circle. Every other
                        // right-click while carrying in hands is blocked below in
                        // TryBeginInteraction; this case must not be the exception.
                        return BeginResult.Handled();
                    }

                    if (!carryManager.HasPermissionAt(player.Entity, blockPos))
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCodes.PlaceDownNoPermission);
                        return BeginResult.Handled();
                    }

                    return BeginResult.Begin(new CarryInteraction
                    {
                        TargetBlockPos = blockPos,
                        Action = CarryAction.PlaceDown,
                        Slot = carriedHands.Slot
                    });
                }
            }
            else if (handsUnreadable)
            {
                // The hands slot holds a present-but-unreadable carry (block from a removed or
                // disabled mod): allow the place-down interaction so the server can drop it as a
                // carried block entity. No client-side prediction is possible because the carried
                // block code is unknown.
                if (selection != null)
                {
                    if (!player.Entity.CanInteract(requireEmptyHanded: false))
                    {
                        return BeginResult.Handled();
                    }

                    return BeginResult.Begin(new CarryInteraction
                    {
                        TargetBlockPos = selection.Position,
                        Action = CarryAction.PlaceDown,
                        Slot = CarrySlot.Hands
                    });
                }
            }
            else if (player.Entity.CanInteract(requireEmptyHanded: true))
            {
                if (selection != null) selection = MultiblockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);

                ItemStack? itemStack = selection?.Block.SafeOnPickBlock(world, selection.Position);

                if (selection?.Block != null && selection.Block.CanCarryInSlot(CarrySlot.Hands, itemStack))
                {
                    if (!carryManager.HasPermissionAt(player.Entity, selection.Position))
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCodes.PickUpNoPermission);
                        return BeginResult.Handled();
                    }

                    if (selection.Position == null) return BeginResult.Handled(false);
                    return BeginResult.Begin(new CarryInteraction
                    {
                        Slot = CarrySlot.Hands,
                        Action = CarryAction.PickUp,
                        TargetBlockPos = selection.Position.Copy()
                    });
                }

            }
            return null;
        }

        private BeginResult? BeginTransferInteraction()
        {
            var world = this.api.World;
            var player = world.Player;

            if (!api.Input.IsCarryKeyPressed())
            {
                return null;
            }

            var selection = player.CurrentBlockSelection;
            if (selection == null)
            {
                return null;
            }

            if (api.Input.IsCarrySwapBackKeyPressed() && SelectionPreventsSwap(selection))
            {
                return null;
            }

            var blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
            if (blockEntity?.Block == null)
            {
                return null;
            }

            var carryableBehavior = blockEntity.Block.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled)
            {
                return null;
            }

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null)
            {
                return null;
            }

            var slotIndex = transferHandler.GetSlotIndex(blockEntity, selection);
            if (slotIndex < 0 || !transferHandler.IsValidSlot(blockEntity, slotIndex))
            {
                return null;
            }

            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);

            string failureCode;
            var interaction = new CarryInteraction
            {
                TargetBlockPos = selection.Position
            };

            if (carriedHands == null)
            {
                if (!this.transferLogic.CanTakeCarryable(player, blockEntity, slotIndex, out failureCode))
                {
                    return HandleCanTransferResponse(failureCode);
                }

                interaction.Action = CarryAction.Take;
            }
            else
            {
                if (!this.transferLogic.CanPutCarryable(player, blockEntity, slotIndex, out failureCode))
                {
                    return HandleCanTransferResponse(failureCode);
                }

                interaction.Action = CarryAction.Put;
            }

            interaction.TargetSlotIndex = slotIndex;
            interaction.TransferDelay = this.transferLogic.GetInteractDelay(player, blockEntity, slotIndex);
            return BeginResult.Begin(interaction);
        }

        private BeginResult? HandleCanTransferResponse(string? failureCode)
        {
            if (string.IsNullOrEmpty(failureCode))
            {
                return null;
            }

            if (failureCode == FailureCodes.Ignore
                || failureCode == FailureCodes.Stop
                || failureCode == FailureCodes.Internal)
            {
                return BeginResult.Complete();
            }

            if (failureCode == FailureCodes.Default)
            {
                return BeginResult.Complete(preventDefault: false);
            }

            CarryErrorHelper.ShowError(this.api, failureCode);
            return BeginResult.Complete();
        }

        private bool SelectionPreventsSwap(BlockSelection? selection)
        {
            if (selection?.Block == null) return false;

            var block = selection.Block;

            if (preventSwapFromBackOnClasses.Length > 0 && block.Class != null)
            {
                var blockClass = block.Class;
                foreach (var cls in preventSwapFromBackOnClasses)
                {
                    if (string.IsNullOrEmpty(cls)) continue;
                    if (string.Equals(blockClass, cls, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            if (preventSwapFromBackOnCodes.Length > 0 && block.Code != null)
            {
                foreach (var code in preventSwapFromBackOnCodes)
                {
                    if (block.Code == code) return true;
                }
            }

            if (preventSwapFromBackOnBehaviors.Length > 0)
            {
                foreach (var behaviorType in preventSwapFromBackOnBehaviors)
                {
                    if (block.HasBehavior(behaviorType)) return true;
                }
            }

            var carryable = block.GetBehavior<BlockBehaviorCarryable>();
            if (carryable?.ForcePickupOnSwapBack == true || carryable?.SwapBackKeyPassthrough == true) return true;

            return false;
        }
    }
}

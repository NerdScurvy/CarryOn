using System;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Client.Logic.Interaction
{
    internal sealed class CarryInteractionValidator
    {
        private readonly ICoreClientAPI api;
        private readonly CarryOnConfig config;
        private readonly TransferLogic transferLogic;
        private readonly ICarryInteractionController controller;

        private bool? allowSprintWhileCarrying;
        private bool? backSlotEnabled;
        private bool? removeInteractDelayWhileCarrying;
        private float? interactSpeedMultiplier;

        private readonly Type[] preventSwapFromBackOnBehaviors;
        private readonly string[] preventSwapFromBackOnClasses;
        private readonly string[] preventSwapFromBackOnCodes;

        public bool RemoveInteractDelayWhileCarrying => removeInteractDelayWhileCarrying ??= this.config?.CarryOptions?.RemoveInteractDelayWhileCarrying ?? false;
        public bool AllowSprintWhileCarrying => allowSprintWhileCarrying ??= this.config?.CarryWalkSpeed?.InHandsAllowSprint ?? false;
        public bool BackSlotEnabled => backSlotEnabled ??= this.config?.CarryOptions?.BackSlotEnabled ?? false;
        public float InteractSpeedMultiplier => interactSpeedMultiplier ??= this.config?.CarryOptions?.InteractSpeedMultiplier ?? 1.0f;

        public CarryInteractionValidator(
            ICoreClientAPI api,
            CarryOnConfig config,
            TransferLogic transferLogic,
            ICarryInteractionController controller)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.transferLogic = transferLogic ?? throw new ArgumentNullException(nameof(transferLogic));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));

            const string behaviorPrefix = "behavior::";
            const string classPrefix = "class::";
            const string codePrefix = "code::";

            if (config != null)
            {
                var entries = config?.CarryOptions?.PreventSwapFromBackOnTarget ?? Array.Empty<string>();

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
            else
            {
                preventSwapFromBackOnBehaviors = [];
                preventSwapFromBackOnClasses = [];
                preventSwapFromBackOnCodes = [];
            }
        }

        public void TryBeginInteraction(bool isInteracting, ref EnumHandling handled)
        {
            if (controller.Interaction.CarryAction != CarryAction.None)
            { handled = EnumHandling.PreventDefault; return; }

            var world = this.api.World;
            var player = world.Player;

            if (!player.Entity.CanDoCarryAction(requireEmptyHanded: true))
            {
                return;
            }

            if (isInteracting)
            {
                if (BeginEntityCarryableInteraction(ref handled)) return;
                if (BeginSwapBackInteraction(ref handled)) return;
                if (BeginBlockEntityInteraction(ref handled)) return;
                if (BeginTransferInteraction(ref handled)) return;
                if (BeginBlockCarryableInteraction(ref handled)) return;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            if ((carriedHands != null) || (isInteracting && (controller.Interaction.TimeHeld > 0.0F)))
                handled = EnumHandling.PreventDefault;
        }

        private bool BeginEntityCarryableInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            var carryAttachBehavior = player.CurrentEntitySelection?.Entity?.GetBehavior<EntityBehaviorAttachableCarryable>();

            bool isLookingAtEntity = player.CurrentEntitySelection != null;
            bool entityHasAttachable = carryAttachBehavior != null;
            bool carryKeyHeld = api.Input.IsCarryKeyPressed();

            bool shouldPreventInteraction = (isLookingAtEntity && !entityHasAttachable) || (entityHasAttachable && !carryKeyHeld);
            if (shouldPreventInteraction) return true;

            if (entityHasAttachable)
            {
                var attachBehavior = carryAttachBehavior;
                if (attachBehavior == null)
                {
                    return false;
                }

                var entitySelection = player.CurrentEntitySelection;

                int selBoxIndex = entitySelection?.SelectionBoxIndex ?? -1;
                int slotIndex = attachBehavior.GetSlotIndex(selBoxIndex);


                var behaviorAttachable = entitySelection?.Entity.GetBehavior<EntityBehaviorAttachable>();

                if (behaviorAttachable == null)
                {
                    this.api.Logger.Error("EntityBehaviorAttachable not found on entity {0}", entitySelection?.Entity?.Code);
                    CarryErrorHelper.ShowError(this.api, FailureCode.AttachableNotFound, "attachable-behavior-not-found");
                    return true;
                }

                controller.Interaction.TargetSlotIndex = behaviorAttachable.GetSlotIndexFromSelectionBoxIndex(selBoxIndex - 1);
                controller.Interaction.TargetEntity = entitySelection?.Entity;
                controller.Interaction.Slot = attachBehavior.GetItemSlot(slotIndex);

                if (controller.Interaction.Slot == null)
                {
                    controller.CompleteInteraction();
                    return true;
                }
                if (carriedHands != null)
                {
                    if (!controller.Interaction.Slot.Empty)
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCode.SlotNotEmpty);
                        controller.CompleteInteraction();
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                    controller.Interaction.CarryAction = CarryAction.Attach;
                }
                else
                {
                    if (controller.Interaction.Slot.Empty)
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCode.SlotEmpty);
                        controller.CompleteInteraction();
                        return true;
                    }

                    if (controller.Interaction.Slot?.Itemstack?.Block?.GetBehavior<BlockBehaviorCarryable>() == null)
                    {
                        controller.CompleteInteraction();
                        return true;
                    }
                    controller.Interaction.CarryAction = CarryAction.Detach;
                }
                handled = EnumHandling.PreventDefault;
                return true;
            }

            return false;
        }

        private bool BeginSwapBackInteraction(ref EnumHandling handled)
        {
            var backSlotEnabled = this.config?.CarryOptions?.BackSlotEnabled ?? false;

            if (!backSlotEnabled) return false;

            var world = this.api.World;
            var player = world.Player;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
            var carriedBack = player.Entity.GetCarried(CarrySlot.Back);

            if (!player.Entity.CanInteract(requireEmptyHanded: true))
            {
                return false;
            }

            var selection = BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, player?.CurrentBlockSelection);
            ItemStack? itemStack = selection?.Block?.OnPickBlock(world, selection.Position);

            bool canCarryTarget = selection?.Block?.CanCarryInSlot(CarrySlot.Hands, itemStack) == true;

            bool carryKeyHeld = api.Input.IsCarryKeyPressed();
            bool swapKeyPressed = api.Input.IsCarrySwapBackKeyPressed();
            bool notTargetingBlock = selection == null;
            bool canSwapBackFromBackSlot = !canCarryTarget && carriedBack != null && carriedHands == null;

            if (carryKeyHeld && (swapKeyPressed || notTargetingBlock || canSwapBackFromBackSlot))
            {

                if (carriedHands == null && !notTargetingBlock)
                {
                    var isSwapPrevented = SelectionPreventsSwap(selection);

                    if (isSwapPrevented)
                    {
                        return false;
                    }
                }

                if (carriedHands != null)
                {
                    if (!carriedHands.GetCarryableBehavior().CanCarryInSlot(CarrySlot.Back, carriedHands.ItemStack))
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCode.CannotSwapBack);
                        controller.CompleteInteraction();
                        return true;
                    }
                }

                if (carriedHands == null && carriedBack == null)
                {
                    CarryErrorHelper.ShowError(this.api, FailureCode.NothingCarried);
                    controller.CompleteInteraction();
                    return true;
                }

                controller.Interaction.CarryAction = CarryAction.SwapBack;

                controller.Interaction.CarrySlot = CarrySlot.Hands;
                handled = EnumHandling.PreventDefault;

                return true;
            }

            return false;
        }

        private bool BeginBlockEntityInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            if (carriedHands == null)
            {
                return false;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: carriedHands == null))
            {
                var selection = player.CurrentBlockSelection;
                selection = BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);

                if (selection?.Block?.HasBehavior<BlockBehaviorCarryableInteract>() == true)
                {
                    var interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                    if (interactBehavior != null && selection?.Position != null && interactBehavior.CanInteract(player))
                    {
                        controller.Interaction.CarryAction = CarryAction.Interact;
                        controller.Interaction.TargetBlockPos = selection.Position;
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                }

            }
            return false;
        }

        private bool BeginBlockCarryableInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;

            if (!api.Input.IsCarryKeyPressed())
            {
                return false;
            }

            var selection = player.CurrentBlockSelection;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            if (api.Input.IsCarrySwapBackKeyPressed() && selection != null)
            {
                var swapCheckSelection = BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);
                if (SelectionPreventsSwap(swapCheckSelection))
                {
                    var carryable = swapCheckSelection?.Block?.GetBehavior<BlockBehaviorCarryable>();
                    var allowForcePickup = carryable?.ForcePickupOnSwapBack == true;

                    if (!allowForcePickup)
                    {
                        return false;
                    }
                }
            }

            if (carriedHands != null)
            {
                if (selection != null)
                {
                    if (!player.Entity.CanInteract(requireEmptyHanded: false))
                    {
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                    var blockPos = BlockUtils.GetPlacedPosition(world.BlockAccessor, selection, carriedHands.Block);
                    if (blockPos == null) return true;

                    if (!player.Entity.HasPermissionToCarry(blockPos))
                    {
                        CarryErrorHelper.ShowError(this.api, FailureCode.PlaceDownNoPermission);
                        handled = EnumHandling.PreventDefault;
                        return false;
                    }


                    controller.Interaction.TargetBlockPos = blockPos;
                    controller.Interaction.CarryAction = CarryAction.PlaceDown;
                    controller.Interaction.CarrySlot = carriedHands.Slot;
                    handled = EnumHandling.PreventDefault;
                    return true;
                }
            }
            else if (player.Entity.CanInteract(requireEmptyHanded: true))
            {
                if (selection != null) selection = BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);

                ItemStack? itemStack = selection?.Block?.OnPickBlock(world, selection.Position);

                if (selection?.Block != null && selection.Block.CanCarryInSlot(CarrySlot.Hands, itemStack))
                {
                    controller.Interaction.CarrySlot = CarrySlot.Hands;
                    controller.Interaction.CarryAction = CarryAction.PickUp;
                    controller.Interaction.TargetBlockPos = selection.Position?.Copy()!;
                    handled = EnumHandling.PreventDefault;
                    return true;
                }

            }
            return false;
        }

        private bool BeginTransferInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;

            if (!api.Input.IsCarryKeyPressed())
            {
                return false;
            }

            var selection = player.CurrentBlockSelection;
            if (selection == null)
            {
                return false;
            }

            if (api.Input.IsCarrySwapBackKeyPressed() && SelectionPreventsSwap(selection))
            {
                return false;
            }

            var blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
            if (blockEntity?.Block == null)
            {
                return false;
            }

            var carryableBehavior = blockEntity.Block.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled)
            {
                return false;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            string? failureCode;
            string? onScreenErrorMessage;
            float? transferDelay;

            if (carriedHands == null)
            {
                if (!this.transferLogic.CanTakeCarryable(player, blockEntity, selection.SelectionBoxIndex, out transferDelay, out failureCode, out onScreenErrorMessage))
                {
                    return HandleCanTransferResponse(failureCode, onScreenErrorMessage, ref handled);
                }

                if (HandleCanTransferResponse(failureCode, null, ref handled))
                    return true;

                controller.Interaction.CarryAction = CarryAction.Take;
            }
            else
            {
                if (!this.transferLogic.CanPutCarryable(player, blockEntity, selection.SelectionBoxIndex, out transferDelay, out failureCode, out onScreenErrorMessage))
                {
                    return HandleCanTransferResponse(failureCode, onScreenErrorMessage, ref handled);
                }

                if (HandleCanTransferResponse(failureCode, null, ref handled))
                    return true;

                controller.Interaction.CarryAction = CarryAction.Put;
            }
            controller.Interaction.TransferDelay = transferDelay;
            controller.Interaction.TargetBlockPos = selection.Position;
            controller.Interaction.TargetSlotIndex = selection.SelectionBoxIndex;
            handled = EnumHandling.PreventDefault;
            return true;
        }

        internal bool HandleCanTransferResponse(string? failureCode, string? onScreenErrorMessage, ref EnumHandling handled)
        {
            if (onScreenErrorMessage != null)
            {
                CarryErrorHelper.ShowErrorIfMessage(this.api, failureCode, onScreenErrorMessage);
                controller.CompleteInteraction();
                handled = EnumHandling.PreventDefault;
                return true;
            }

            if (failureCode == FailureCode.Default)
            {
                controller.CompleteInteraction();
                return true;
            }

            if (failureCode == FailureCode.Stop)
            {
                handled = EnumHandling.PreventDefault;
                controller.CompleteInteraction();
                return true;
            }

            return false;
        }

        internal bool SelectionPreventsSwap(BlockSelection? selection)
        {
            if (selection?.Block == null) return false;

            var block = selection.Block;
            if (block == null) return false;

            if (preventSwapFromBackOnClasses != null && preventSwapFromBackOnClasses.Length > 0 && block.Class != null)
            {
                var blockClass = block.Class ?? "";
                foreach (var cls in preventSwapFromBackOnClasses)
                {
                    if (string.IsNullOrEmpty(cls)) continue;
                    if (string.Equals(blockClass, cls, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            if (preventSwapFromBackOnCodes != null && preventSwapFromBackOnCodes.Length > 0 && block.Code != null)
            {
                foreach (var code in preventSwapFromBackOnCodes)
                {
                    if (block.Code == code) return true;
                }
            }

            if (preventSwapFromBackOnBehaviors != null && preventSwapFromBackOnBehaviors.Length > 0)
            {
                foreach (var behaviorType in preventSwapFromBackOnBehaviors)
                {
                    if (behaviorType == null) continue;
                    if (block.HasBehavior(behaviorType)) return true;
                }
            }

            var carryable = block.GetBehavior<BlockBehaviorCarryable>();
            if (carryable?.ForcePickupOnSwapBack == true || carryable?.SwapBackKeyPassthrough == true) return true;

            return false;
        }
    }
}
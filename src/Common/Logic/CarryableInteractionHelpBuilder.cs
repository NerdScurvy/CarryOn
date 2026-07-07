using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Logic
{
    public static class CarryableInteractionHelpBuilder
    {
        private static ICarryManager? carryManager;
        private static IConfigProvider configProvider = null!;

        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
            configProvider = manager as IConfigProvider ?? throw new ArgumentException("manager must implement IConfigProvider", nameof(manager));
        }

        private static ItemStack[]? handsfreeStacks;
        private static ItemStack[]? nohandsfreeStacks;

        public static WorldInteraction[] BuildHelp(
            IWorldAccessor world,
            BlockSelection selection,
            IPlayer forPlayer,
            BlockBehaviorCarryable behavior)
        {
            if (behavior.Slots == null || behavior.Slots.Count == 0)
                return [];

            ItemStack? itemStack = selection?.Block?.OnPickBlock(world, selection.Position);
            if (selection?.Block?.CanCarryInSlot(CarrySlot.Hands, itemStack) == false)
                return [];

            handsfreeStacks ??= [new ItemStack(world.GetItem(new AssetLocation("carryon:icon-handsfree")))];
            nohandsfreeStacks ??= [new ItemStack(world.GetItem(new AssetLocation("carryon:icon-nohandsfree")))];

            bool canDoCarryAction = forPlayer?.Entity?.CanDoCarryAction(requireEmptyHanded: true) == true;
            bool isCarryingInHands = forPlayer?.Entity != null && carryManager?.GetCarried(forPlayer.Entity, CarrySlot.Hands) != null;
            var pickupStacks = canDoCarryAction ? handsfreeStacks : nohandsfreeStacks;

            bool isTargetCarryable = selection?.Block != null && itemStack != null;

            // Suppress pickup hints when the client-side permission check denies access
            if (configProvider.Config.CarryOptions?.ClientSidePermissionCheck == true &&
                forPlayer?.Entity != null && selection?.Position != null &&
                !carryManager!.HasPermissionAt(forPlayer.Entity, selection.Position))
            {
                return [];
            }

            CarryHintType defaultHints = CarryHintType.None;

            if (!isCarryingInHands && behavior.TransferBlockCarryAllowed(forPlayer, selection))
                defaultHints |= CarryHintType.BasePickup;

            if (behavior.ForcePickupOnSwapBack && isTargetCarryable)
                defaultHints |= CarryHintType.ForcePickup;

            if (defaultHints == CarryHintType.None)
                return [];

            var blockEntity = selection?.Position == null ? null : world.BlockAccessor.GetBlockEntity(selection.Position);

            var hintContext = new CarryHintContext
            {
                Player = forPlayer,
                Selection = selection,
                BlockEntity = blockEntity,
                SelectionBoxIndex = selection?.SelectionBoxIndex ?? -1,
                IsTargetCarryable = isTargetCarryable,
                IsForcePickupEnabled = behavior.ForcePickupOnSwapBack,
                CanDoCarryAction = canDoCarryAction,
                IsCarryingInHands = isCarryingInHands
            };

            var allowedHints = ResolveAllowedHints(behavior, hintContext, defaultHints);

            if (allowedHints == CarryHintType.None)
                return [];

            var interactions = new List<WorldInteraction>();

            if ((allowedHints & CarryHintType.BasePickup) != 0)
                interactions.Add(CreateBasePickupInteraction(pickupStacks));

            if ((allowedHints & CarryHintType.ForcePickup) != 0)
                interactions.Add(CreateForcePickupInteraction(pickupStacks));

            return interactions.Count > 0 ? [.. interactions] : [];
        }

        private static WorldInteraction CreateBasePickupInteraction(ItemStack[] itemStacks)
        {
            return new WorldInteraction
            {
                ActionLangCode = CarryOnCode("blockhelp-pickup"),
                HotKeyCode = HotKeyCode.Pickup,
                MouseButton = EnumMouseButton.Right,
                RequireFreeHand = true,
                Itemstacks = itemStacks
            };
        }

        private static WorldInteraction CreateForcePickupInteraction(ItemStack[] itemStacks)
        {
            return new WorldInteraction
            {
                ActionLangCode = CarryOnCode("blockhelp-pickup"),
                HotKeyCodes = [HotKeyCode.SwapBackModifier, HotKeyCode.Pickup],
                MouseButton = EnumMouseButton.Right,
                RequireFreeHand = true,
                Itemstacks = itemStacks
            };
        }

        private static CarryHintType ResolveAllowedHints(BlockBehaviorCarryable behavior, CarryHintContext context, CarryHintType defaultHints)
        {
            if (behavior.TransferHandlerBehavior is not ICarryableHintPolicy hintPolicy)
                return defaultHints;

            var allowedHints = hintPolicy.GetAllowedHints(context, defaultHints);
            return allowedHints & defaultHints;
        }
    }
}

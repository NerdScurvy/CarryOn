using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryConstants;

namespace CarryOn.Common.Behaviors
{
    public class EntityBehaviorCarryable : EntityBehavior, ICustomInteractionHelpPositioning
    {
        public static string Name { get; } = GetCarryCode("carryable");


        private static ICarryManager? carryManager;

        private string pickupHelpLangCode = GetCarryCode("entityhelp-pickup");
        private string pickupHelpIcon = GetCarryCode("icon-handsfree");
        private bool showHelp = true;
        private string handlerCode = "";
        private string slotHandlerCode = "";
        private bool slotHandlerResolved;
        private ICarryableEntitySlotHandler? slotHandler;
        private float? interactDelay;
        private EntityPickupMode pickupMode = EntityPickupMode.Interact;

        /// <summary>
        /// Initializes the behavior with the carry manager for handler registry lookups.
        /// Must be called during mod startup on both client and server.
        /// </summary>
        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// The handler managing this entity's lifecycle. Resolved lazily from
        /// the handler registry using the entity's type code.
        /// </summary>
        public ICarryableEntityPickupHandler? Handler { get; private set; }

        /// <summary>
        /// The handler managing per-slot attach/detach for this entity. Resolved lazily
        /// from the slot handler registry using the entity's slotHandlerCode.
        /// </summary>
        public ICarryableEntitySlotHandler? SlotHandler
        {
            get
            {
                EnsureSlotHandler();
                return slotHandler;
            }
        }

        /// <summary>
        /// Whether a valid entity handler was resolved for this entity.
        /// Entities without a registered handler cannot be picked up.
        /// </summary>
        public bool IsCarryInteractable => EnsureHandler();

        /// <summary>
        /// Whether a valid slot handler was resolved for this entity.
        /// Entities with a slot handler support per-slot attach/detach instead of pickup.
        /// </summary>
        public bool IsSlotInteractable => EnsureSlotHandler();

        /// <summary>
        /// Interaction delay in seconds for picking up this entity, or null to fall back
        /// to the handler default. Configurable via the "interactDelay" JSON attribute.
        /// </summary>
        public float? InteractDelay => interactDelay;

        /// <summary>
        /// Which input combination triggers whole-entity pickup.
        /// Configurable via the "pickupMode" JSON attribute.
        /// </summary>
        public EntityPickupMode PickupMode => pickupMode;

        public EntityBehaviorCarryable(Entity entity) : base(entity)
        {
        }

        /// <summary>
        /// Creates a slot-mode behavior. Slot handlers are auto-detected from the entity's
        /// behaviors or the entity itself; used by the compat shim <see cref="EntityBehaviorAttachableCarryable"/>
        /// to inject the unified behavior onto entities that still use the old behavior code.
        /// </summary>
        internal static EntityBehaviorCarryable CreateSlotMode(Entity entity)
        {
            return new EntityBehaviorCarryable(entity);
        }

        public override string PropertyName() => Name;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            if (attributes != null)
            {
                if (JsonHelper.TryGetString(attributes, "handlerCode", out var hc))
                    handlerCode = hc!;

                if (JsonHelper.TryGetString(attributes, "slotHandlerCode", out var shc))
                    slotHandlerCode = shc!;

                if (JsonHelper.TryGetBool(attributes, "showHelp", out var sh))
                    showHelp = sh;
                if (JsonHelper.TryGetString(attributes, "pickupHelpLangCode", out var lang))
                    pickupHelpLangCode = lang!;
                if (JsonHelper.TryGetString(attributes, "pickupHelpIcon", out var icon))
                    pickupHelpIcon = icon!;

                if (JsonHelper.TryGetFloat(attributes, "interactDelay", out var d))
                    interactDelay = d;

                if (JsonHelper.TryGetString(attributes, "pickupMode", out var mode))
                {
                    var normalized = mode!.Replace("-", "", StringComparison.Ordinal);
                    if (Enum.TryParse<EntityPickupMode>(normalized, ignoreCase: true, out var parsed))
                        pickupMode = parsed;
                    else
                        entity?.World?.Logger?.Warning(
                            "EntityBehaviorCarryable on {0}: unknown pickupMode '{1}'. Falling back to Interact.",
                            entity?.Code, mode);
                }
            }
        }

        private bool EnsureHandler()
        {
            if (Handler != null) return true;
            if (carryManager == null) return false;

            if (string.IsNullOrEmpty(handlerCode))
            {
                if (string.IsNullOrEmpty(slotHandlerCode))
                {
                    entity?.World?.Logger?.Warning(
                        "EntityBehaviorCarryable on {0} has handlerCode not set. Disabling carry behavior.",
                        entity?.Code);
                }
                return false;
            }

            // "auto" -> scan entity behaviors for ICarryableEntityPickupHandler
            if (handlerCode == CarryCodes.ReservedCodes.Auto)
            {
                if (entity == null) return false;

                foreach (var behavior in entity.SidedProperties?.Behaviors ?? [])
                {
                    if (behavior is ICarryableEntityPickupHandler pickupBehavior)
                    {
                        Handler = pickupBehavior;
                        return true;
                    }
                }

                entity?.World?.Logger?.Warning(
                    "EntityBehaviorCarryable: No ICarryableEntityPickupHandler behavior found on {0}. " +
                    "Disabling carry behavior.",
                    entity?.Code);
                return false;
            }

            // Named code -> registry lookup (backward compat for singleton handlers)
            if (carryManager.TryGetEntityPickupHandler(handlerCode, out var instance))
            {
                Handler = instance;
                return true;
            }

            entity?.World?.Logger?.Warning(
                "EntityBehaviorCarryable: No handler registered for code '{0}'. Disabling carry behavior.",
                handlerCode);
            return false;
        }

        private bool EnsureSlotHandler()
        {
            if (slotHandler != null) return true;
            if (slotHandlerResolved) return false;
            slotHandlerResolved = true;

            if (carryManager != null
                && !string.IsNullOrEmpty(slotHandlerCode)
                && slotHandlerCode != CarryCodes.ReservedCodes.Auto)
            {
                // Try the code as-is, then fall back to carryon: prefix
                if (carryManager.TryGetSlotHandler(slotHandlerCode, out var instance))
                {
                    slotHandler = instance;
                    return true;
                }

                entity?.World?.Logger?.Warning(
                    "EntityBehaviorCarryable: No slot handler registered for code '{0}'. Slot attach/detach disabled.",
                    slotHandlerCode);
                return false;
            }

            if (entity == null) return false;

            foreach (var behavior in entity.SidedProperties?.Behaviors ?? [])
            {
                if (behavior is ICarryableEntitySlotHandler slotBehavior)
                {
                    slotHandler = slotBehavior;
                    return true;
                }
            }

            if (entity is ICarryableEntitySlotHandler slotEntity)
            {
                slotHandler = slotEntity;
                return true;
            }

            return false;
        }

        public override WorldInteraction[]? GetInteractionHelp(
            IClientWorldAccessor world, EntitySelection es,
            IClientPlayer player, ref EnumHandling handled)
        {
            if (!showHelp) return null;

            // A slot handler takes precedence while the player looks at a storage slot;
            // anywhere else the entity handler governs whole-entity pickup help.
            var slotHandler = SlotHandler;
            if (slotHandler != null)
            {
                var slotIndex = slotHandler.GetSlotIndex(es.Entity, es.SelectionBoxIndex);
                if (slotIndex >= 0 && slotHandler.IsValidSlot(es.Entity, slotIndex))
                    return GetSlotInteractionHelp(world, es, player, es.Entity.Api, slotHandler, slotIndex);
            }

            if (!EnsureHandler()) return null;

            if (!player.Entity.CanDoCarryAction(requireEmptyHanded: true)) return null;

            var iconItem = world.GetItem(new AssetLocation(pickupHelpIcon));
            var stacks = iconItem != null
                ? new[] { new ItemStack(iconItem) }
                : null;

            return
            [
                new WorldInteraction
                {
                    ActionLangCode = pickupHelpLangCode,
                    MouseButton = EnumMouseButton.Right,
                    RequireFreeHand = true,
                    Itemstacks = stacks,
                    HotKeyCode = PickupMode == EntityPickupMode.PickupHotkey
                        ? HotKeyCodes.Pickup : null
                }
            ];
        }

        /// <inheritdoc/>
        public bool TransparentCenter => false;

        /// <inheritdoc/>
        public Vec3d? GetInteractionHelpPosition()
        {
            if (SlotHandler == null) return null;

            var capi = entity.Api as ICoreClientAPI;
            if (capi?.World?.Player?.CurrentEntitySelection == null) return null;

            return SlotHandler.GetHelpPosition(entity, capi.World.Player.CurrentEntitySelection.SelectionBoxIndex);
        }

        private static WorldInteraction[]? GetSlotInteractionHelp(
            IClientWorldAccessor world, EntitySelection es, IClientPlayer player,
            ICoreAPI api, ICarryableEntitySlotHandler slotHandler, int slotIndex)
        {
            if (slotIndex < 0 || !slotHandler.IsValidSlot(es.Entity, slotIndex)) return null;

            var putStacks = HelpIconStacks.Get(world, "icon-put");
            var takeStacks = HelpIconStacks.Get(world, "icon-take");
            var noHandsFreeStacks = HelpIconStacks.Get(world, "icon-nohandsfree");

            string? langCode = null;
            ItemStack[]? itemStacks = null;

            if (!slotHandler.IsSlotEmpty(es.Entity, slotIndex))
            {
                // Creative may detach a slot holding an attachable block that can no longer
                // resolve (removed/disabled mod) - mirror the exact eligibility CanDetach uses so
                // the help only advertises a recovery that would actually succeed.
                if (slotHandler.IsSlotCarryable(es.Entity, slotIndex)
                    || (player.WorldData?.CurrentGameMode == EnumGameMode.Creative
                        && slotHandler.CanDetach(es.Entity, slotIndex, player, api, out _)))
                {
                    langCode = GetCarryCode("blockhelp-detach");
                    itemStacks = player.Entity.CanDoCarryAction(requireEmptyHanded: true)
                        ? takeStacks
                        : noHandsFreeStacks;
                }
            }
            else
            {
                langCode = GetCarryCode("blockhelp-attach");
                itemStacks = carryManager?.GetCarried(player.Entity, CarrySlot.Hands) != null
                    ? putStacks
                    : noHandsFreeStacks;
            }

            if (langCode == null) return null;

            return
            [
                new WorldInteraction
                {
                    ActionLangCode = langCode,
                    Itemstacks = itemStacks,
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = HotKeyCodes.Pickup,
                    RequireFreeHand = true
                }
            ];
        }
    }
}

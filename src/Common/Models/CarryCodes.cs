using System.Collections.Generic;
using CarryOn.API.Common.Models;
using Vintagestory.API.Client;

namespace CarryOn.Common.Models
{
    public static class CarryCodes
    {
        public static string ModId { get; } = CarryConstants.ModId;

        public const string ConfigFile = "CarryOnConfig.json";
        public const int CurrentConfigVersion = 6;

        public static string GetCarryCode(string key) => CarryConstants.GetCarryCode(key);

        public static class Defaults
        {
            public const float PlaceSpeed = 0.75f;
            public const float SwapSpeed = 1.5f;
            public const float PickUpSpeed = 0.8f;
            public const float TransferSpeed = 0.8f;
            public const float InteractSpeed = 0.8f;
            public const int MaxInteractionDistance = 6;
            public const int HotbarSize = 10;
            public const int DoubleTapThresholdMs = 500;
            public const GlKeys PickupKeybind = GlKeys.ShiftLeft;
            public const GlKeys SwapBackModifierKeybind = GlKeys.ControlLeft;
            public const GlKeys FunctionKeybind = GlKeys.K;

            public const float MinSaturationThreshold = 150f;

            public static IReadOnlyDictionary<CarrySlot, float> WalkSpeedModifier { get; }
                = new Dictionary<CarrySlot, float> {
                    { CarrySlot.Hands, -0.25f },
                    { CarrySlot.Back , -0.15f }
                };

            public static IReadOnlyDictionary<CarrySlot, float> HungerRateModifier { get; }
                = new Dictionary<CarrySlot, float> {
                    { CarrySlot.Hands, 0.2f },
                    { CarrySlot.Back , 0.3f }
                };

            public static class CarriedBlockEntity
            {
                public const DropMode DropMode = Models.DropMode.EntityOnFailedPlacement;
                public const bool RandomDropRotation = true;
                public const bool ShowParticles = true;
                public const float DespawnAfterDays = 30f;
                public const float GracePeriodSeconds = 300f;
                public const float Scale = 0.6f;
                public const PickupAccess PickupAccess = Models.PickupAccess.OwnerFirst;
                public const bool SpawnAsEntityOnPlacementByDefault = false;
            }

        }

        public static class AttributeKeys
        {
            public static string EntityLastSneakTap { get; } = GetCarryCode("LastSneakTapMs");

            public const string CarriedRevision = "Rev";

            public const string Type = "type";

            // Marker written onto an attachable-slot itemstack by AttachableCarryConversion.ToAttachItemSlot.
            // A stack carrying this attribute is reserved to CarryOn and must only leave an attachable slot
            // through CarryOn's own detach interaction, never through a vanilla inventory transfer.
            public const string CarryOnBackup = "carryonbackup";

            public static class BlockEntityData
            {
                public const string PosX = "posx";
                public const string PosY = "posy";
                public const string PosZ = "posz";
                public const string MeshAngle = "meshAngle";
                public const string MeshAngleRad = "meshAngleRad";
                public const string HaveCenterPlacement = "haveCenterPlacement";
                public const string RotationSlotKeyPrefix = "rotation";
            }

            public static class Watched
            {
                public static string EntityCarried { get; } = GetCarryCode("Carried");

                public static string EntityDoubleTapDismountEnabled { get; } = GetCarryCode("DoubleTapDismountEnabled");

            }

            public static class CarriedBlockData
            {
                // Entity WatchedAttributes keys (top-level on the entity)
                public const string WatchedTree = "carriedBlock";
                public const string OwnerUid = "ownerUid";
                public const string DropTime = "dropTime";
                public const string DropTimeRealTicks = "dropTimeRealTicks";

                // Keys within the carried block serialization subtree (shared with CarryConstants)
                public const string Stack = CarryConstants.AttributeKeys.CarriedBlockData.Stack;
                public const string Data = CarryConstants.AttributeKeys.CarriedBlockData.Data;
                public const string Children = CarryConstants.AttributeKeys.CarriedBlockData.Children;
                public const string OffsetX = CarryConstants.AttributeKeys.CarriedBlockData.OffsetX;
                public const string OffsetY = CarryConstants.AttributeKeys.CarriedBlockData.OffsetY;
                public const string OffsetZ = CarryConstants.AttributeKeys.CarriedBlockData.OffsetZ;
                public const string OriginalFace = CarryConstants.AttributeKeys.CarriedBlockData.OriginalFace;
                public const string OriginalBlockCode = CarryConstants.AttributeKeys.CarriedBlockData.OriginalBlockCode;
                public const string OriginalItemCode = CarryConstants.AttributeKeys.CarriedBlockData.OriginalItemCode;
                public const string OriginalMeshAngle = CarryConstants.AttributeKeys.CarriedBlockData.OriginalMeshAngle;
                public const string SpawnerFactoryCode = CarryConstants.AttributeKeys.CarriedBlockData.SpawnerFactoryCode;
                public const string SpawnAsEntityOnPlacement = CarryConstants.AttributeKeys.CarriedBlockData.SpawnAsEntityOnPlacement;
            }
        }

        public static class FailureCodes
        {
            // Shared sentinel codes (from CarryConstants)
            public const string Continue = CarryConstants.FailureCodes.Continue;
            public const string Stop = CarryConstants.FailureCodes.Stop;
            public const string Default = CarryConstants.FailureCodes.Default;
            public const string Ignore = CarryConstants.FailureCodes.Ignore;

            // Internal sentinel
            public const string Internal = "__failure__";

            /// <summary>
            /// The action was refused and the player has already been told why by
            /// something other than CarryOn - currently the vanilla claim message the
            /// permission gate triggers through <c>Claims.TryAccess</c>. The handler skips
            /// its own message for this code, because a second toast naming the same refusal
            /// is noise rather than information. Purely additive: no existing code changes
            /// meaning, and a slot handler may use it to say "I already messaged".
            /// </summary>
            public const string AlreadyReported = "already-reported";

            // Entity failure codes
            public const string RequiresOwnership = "requiresownership";
            public const string EntityNotFound = "entity-not-found";
            public const string EntityOutOfReach = "entity-out-of-reach";
            public const string SlotNotEmpty = "slot-not-empty";
            public const string SlotEmpty = "slot-empty";

            // Carry placement failure codes
            public const string AlreadyCarrying = "already-carrying";
            public const string NoPermission = "no-permission";

            /// <summary>
            /// A cell in the write footprint is reinforced. Distinct from
            /// <see cref="NoPermission"/> because the permission scan resolves
            /// reinforcement before it reaches the claim check, and the two produce
            /// different messages: vanilla names the claim, nothing names the
            /// reinforcement, so this code is what lets the player be told.
            /// </summary>
            public const string Reinforced = "reinforced";
            public const string NotCarryable = "not-carryable";
            public const string NotCarrying = "not-carrying";

            // Carry attachment failure codes
            public const string SlotNotFound = "slot-not-found";
            public const string SlotDataMissing = "slot-data-missing";
            public const string SlotIncompatibleBlock = "slot-incompatible-block";
            public const string SlotEntityNotAttachable = "slot-entity-not-attachable";
            public const string SlotPreventAttaching = "slot-prevent-attaching";
            public const string BlockHasAttachedBlocks = "block-has-attached-blocks";
            public const string AttachUnavailable = "attach-unavailable";
            public const string AttachFailed = "attach-failed";
            public const string DetachUnavailable = "detach-unavailable";
            public const string SlotNotCarryable = "slot-not-carryable";
            public const string SlotInventoryOpen = "slot-inventory-open";

            // Event-driven failure codes
            public const string TooHot = "too-hot";

            // Carry handler failure codes
            public const string InvalidData = "invalid-data";
            public const string CannotInteract = "cannot-interact";
            public const string BlockOutOfReach = "block-out-of-reach";
            public const string CarriedBlockUnreadable = "carried-block-unreadable";
            public const string CarriedBlockMissing = "carried-block-missing";
            public const string MissingBlock = "missing-block";

            // Interaction logic failure codes
            public const string AttachableNotFound = "attachable-not-found";
            public const string CannotSwapBack = "cannot-swap-back";
            public const string NothingCarried = "nothing-carried";
            public const string PlaceDownNoPermission = "place-down-no-permission";
            public const string PickUpNoPermission = "pick-up-no-permission";

            // Default fallback codes
            public const string PickUpFailed = "pick-up-failed";
            public const string PlaceDownFailed = "place-down-failed";

            // Cluster carry failure codes
            public const string AttachedBlockNoClearance = "attached-block-no-clearance";
            public const string UnsupportedAttachment = "unsupported-attachment";

            // Entity pickup failure codes
            public const string NotOwner = "not-owner";
            public const string EntityHandlerNotConfigured = "entity-handler-not-configured";

            // Ground storage bag failure codes
            public const string BackpackSlotsFull = "backpack-slots-full";
        }

        public static class HotKeyCodes
        {
            public const string Pickup = CarryConstants.HotKeyCodes.Pickup;
            public const string SwapBackModifier = CarryConstants.HotKeyCodes.SwapBackModifier;
            public const string Toggle = CarryConstants.HotKeyCodes.Toggle;
            public const string QuickDrop = CarryConstants.HotKeyCodes.QuickDrop;
            public const string QuickDropAll = CarryConstants.HotKeyCodes.QuickDropAll;
            public const string ToggleDoubleTapDismount = CarryConstants.HotKeyCodes.ToggleDoubleTapDismount;
        }

        public static class SoundPaths
        {
            public const string DefaultPlace = "sounds/player/build";
            public const string DefaultBreak = "game:sounds/block/planks";
            public const string Throw = "sounds/player/throw";
        }

        public static class Stats
        {
            public const string WalkSpeed = "walkspeed";
            public const string HungerRate = "hungerrate";
        }

        public static class BehaviorJsonKeys
        {
            public const string EnabledCondition = "enabledCondition";
            public const string Animation = "animation";
            public const string AnimationSit = "animationSit";
            public const string AnimationCrouch = "animationCrouch";
            public const string WalkSpeedModifier = "walkSpeedModifier";
            public const string WalkSpeedModifierByBlockType = "walkSpeedModifierByBlockType";
            public const string WalkSpeedModifierByGroup = "walkSpeedModifierByGroup";
            public const string HungerModifierByBlockType = "hungerModifierByBlockType";
            public const string HungerModifierByGroup = "hungerModifierByGroup";
            public const string HungerModifier = "hungerModifier";
            public const string ExcludedTypes = "excludedTypes";
        }

        public static class BlockClasses
        {
            public const string Sign = "BlockSign";
        }

        public static class ReservedCodes
        {
            public const string Auto = "auto";
        }

        public const string DefaultTransformGroup = "default";
        public const string LabelTransformGroup = "labelTransform";
        public const string BagItemPoseTemplateCode = "carry-bag";
        public const string FrontCarryAttachmentPoint = "carryon:FrontCarry";
        public const string WorldConfigPrefix = "carryon:";
        public const string MountedBagInventoryPrefix = "mountedbaginv";
        // The vanilla bag-content GUI on a ground-storage slot binds to a per-slot wrapper
        // InventoryGeneric named with this class + "blockcontainedbag-slot{slotId}-{pos}" instance
        // (BlockEntityContainedBagWorkspace). InventoryIDs prefixed by it identify a bag that a
        // player currently has open.
        public const string BagContentInventoryPrefix = "blockcontainedbaginv";

        // Body-attachment render groups for the back slot. Keys derive from BackpackTypes config
        // (BackpackGroupPrefix + type); only these are wired to a SlotRenderSettings in
        // CarryRenderDispatcher, so a BackpackTypes entry whose group is missing here silently
        // skips rendering on the back. Validated at config-load (CarryOnConfig.ValidateBackpackTransformGroups).
        public const string BackpackGroupPrefix = "backpack-";
        public const string BackpackGroupNone = "backpack-none";
        public const string BackpackGroupSmall = "backpack-small";
        public const string BackpackGroupLarge = "backpack-large";

        public static readonly string[] KnownBackpackTransformGroups =
        {
            BackpackGroupNone, BackpackGroupSmall, BackpackGroupLarge
        };

        public static string DisplayName(string? value) => value ?? "unknown";

    }
}

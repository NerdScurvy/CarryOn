using System.Collections.Generic;
using CarryOn.API.Common.Models;
using Vintagestory.API.Client;

namespace CarryOn.Common.Models
{
    public static class CarryCodes
    {
        public static string ModId { get; } = CarryConstants.ModId;

        public const string ConfigFile = "CarryOnConfig.json";
        public const int CurrentConfigVersion = 5;

        public static class CarryOnLib
        {
            public const string ModId = "carryonlib";
        }

        public static string GetCarryCode(string key) => $"{ModId}:{key}";

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
                public const float DespawnAfterDays = 14f;
                public const float GracePeriodSeconds = 300f;
                public const bool ShowParticles = true;
                public static PickupAccess PickupAccess { get; } = PickupAccess.OwnerFirst;
            }

        }

        public static class AttributeKeys
        {
            public static string EntityLastSneakTap { get; } = GetCarryCode("LastSneakTapMs");

            public const string CarriedRevision = "Rev";

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
                public const string OriginalMeshAngle = CarryConstants.AttributeKeys.CarriedBlockData.OriginalMeshAngle;
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

            // Entity failure codes
            public const string RequiresOwnership = "requiresownership";
            public const string EntityNotFound = "entity-not-found";
            public const string EntityOutOfReach = "entity-out-of-reach";
            public const string SlotNotEmpty = "slot-not-empty";
            public const string SlotEmpty = "slot-empty";

            // Carry placement failure codes
            public const string AlreadyCarrying = "already-carrying";
            public const string NoPermission = "no-permission";
            public const string NotCarryable = "not-carryable";
            public const string NotCarrying = "not-carrying";

            // Carry attachment failure codes
            public const string SlotNotFound = "slot-not-found";
            public const string SlotDataMissing = "slot-data-missing";
            public const string SlotIncompatibleBlock = "slot-incompatible-block";
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

        public const string DefaultTransformGroup = "default";
        public const string FrontCarryAttachmentPoint = "carryon:FrontCarry";
        public const string WorldConfigPrefix = "carryon:";

    }
}

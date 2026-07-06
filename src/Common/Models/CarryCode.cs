using System.Collections.Generic;
using CarryOn.API.Common.Models;
using Vintagestory.API.Client;

namespace CarryOn.Common.Models
{
    public static class CarryCode
    {
        public static string ModId { get; } = "carryon";

        public const string ConfigFile = "CarryOnConfig.json";
        public const int CurrentConfigVersion = 4;

        public static class CarryOnLib
        {
            public static string ModId { get; } = "carryonlib";
        }

        public static string CarryOnCode(string key) => $"{ModId}:{key}";

        public static class Default
        {
            public static float PlaceSpeed { get; } = 0.75f;
            public static float SwapSpeed { get; } = 1.5f;
            public static float PickUpSpeed { get; } = 0.8f;
            public static float TransferSpeed { get; } = 0.8f;
            public static float InteractSpeed { get; } = 0.8f;
            public static int MaxInteractionDistance { get; } = 6;
            public static int HotbarSize { get; } = 10;
            public static int DoubleTapThresholdMs { get; } = 500;
            public static GlKeys PickupKeybind { get; } = GlKeys.ShiftLeft;
            public static GlKeys SwapBackModifierKeybind { get; } = GlKeys.ControlLeft;
            public static GlKeys FunctionKeybind { get; } = GlKeys.K;

            public static float MinSaturationThreshold { get; } = 150f;

            public static IReadOnlyDictionary<CarrySlot, float> WalkSpeedModifier { get; }
                = new Dictionary<CarrySlot, float> {
                    { CarrySlot.Hands, -0.25F },
                    { CarrySlot.Back , -0.15F }
                };

            public static IReadOnlyDictionary<CarrySlot, float> HungerRateModifier { get; }
                = new Dictionary<CarrySlot, float> {
                    { CarrySlot.Hands, 0.2F },
                    { CarrySlot.Back , 0.3F }
                };

            public static class CarriedBlockEntity
            {
                public static float DespawnAfterDays { get; } = 14f;
                public static float GracePeriodSeconds { get; } = 300f;
                public static bool ShowParticles { get; } = true;
                public static PickupAccess PickupAccess { get; } = PickupAccess.OwnerFirst;
            }

        }

        public static class AttributeKey
        {
            public static string EntityLastSneakTap { get; } = CarryOnCode("LastSneakTapMs");

            public static string CarriedRevision { get; } = "Rev";

            public static class Watched
            {
                public static string EntityCarried { get; } = CarryOnCode("Carried");

                public static string EntityDoubleTapDismountEnabled { get; } = CarryOnCode("DoubleTapDismountEnabled");

            }

            public static class CarriedBlock
            {
                // Entity WatchedAttributes keys (top-level on the entity)
                public static string WatchedTree { get; } = "carriedBlock";
                public static string OwnerUid { get; } = "ownerUid";
                public static string DropTime { get; } = "dropTime";
                public static string DropTimeRealTicks { get; } = "dropTimeRealTicks";

                // Keys within the carried block serialization subtree
                public static string Stack { get; } = "Stack";
                public static string Data { get; } = "Data";
                public static string Children { get; } = "Children";
                public static string OffsetX { get; } = "OffsetX";
                public static string OffsetY { get; } = "OffsetY";
                public static string OffsetZ { get; } = "OffsetZ";
                public static string OriginalFace { get; } = "OriginalFace";
                public static string OriginalBlockCode { get; } = "OriginalBlockCode";
                public static string OriginalMeshAngle { get; } = "OriginalMeshAngle";
            }
        }

        public static class FailureCode
        {
            public static string Continue { get; } = "__continue__";
            public static string Stop { get; } = "__stop__";
            public static string Default { get; } = "__default__";
            public static string Internal { get; } = "__failure__";
            public static string Ignore { get; } = "__ignore__";

            // Kept as the historical wire value for compatibility.
            public static string RequiresOwnership { get; } = "requiresownership";
            public static string EntityNotFound { get; } = "entity-not-found";
            public static string EntityOutOfReach { get; } = "entity-out-of-reach";
            public static string SlotNotEmpty { get; } = "slot-not-empty";
            public static string SlotEmpty { get; } = "slot-empty";

            // Carry placement failure codes
            public static string AlreadyCarrying { get; } = "already-carrying";
            public static string NoPermission { get; } = "no-permission";
            public static string NotCarryable { get; } = "not-carryable";
            public static string NotCarrying { get; } = "not-carrying";

            // Carry attachment failure codes
            public static string SlotNotFound { get; } = "slot-not-found";
            public static string SlotDataMissing { get; } = "slot-data-missing";
            public static string SlotIncompatibleBlock { get; } = "slot-incompatible-block";
            public static string SlotPreventAttaching { get; } = "slot-prevent-attaching";
            public static string BlockHasAttachedBlocks { get; } = "block-has-attached-blocks";
            public static string AttachUnavailable { get; } = "attach-unavailable";
            public static string AttachFailed { get; } = "attach-failed";
            public static string DetachUnavailable { get; } = "detach-unavailable";
            public static string SlotNotCarryable { get; } = "slot-not-carryable";
            public static string SlotInventoryOpen { get; } = "slot-inventory-open";

            // Event-driven failure codes
            public static string TooHot { get; } = "too-hot";

            // Carry handler failure codes
            public static string InvalidData { get; } = "invalid-data";
            public static string CannotInteract { get; } = "cannot-interact";

            // Interaction logic failure codes
            public static string AttachableNotFound { get; } = "attachable-not-found";
            public static string CannotSwapBack { get; } = "cannot-swap-back";
            public static string NothingCarried { get; } = "nothing-carried";
            public static string PlaceDownNoPermission { get; } = "place-down-no-permission";
            public static string PickUpNoPermission { get; } = "pick-up-no-permission";

            // Default fallback codes
            public static string PickUpFailed { get; } = "pick-up-failed";
            public static string PlaceDownFailed { get; } = "place-down-failed";

            // Cluster carry failure codes
            public static string AttachedBlockNoClearance { get; } = "attached-block-no-clearance";
            public static string UnsupportedAttachment { get; } = "unsupported-attachment";

            // Entity pickup failure codes
            public static string NotOwner { get; } = "not-owner";
        }

        public static class HotKeyCode
        {
            public static string Pickup { get; } = "carryonpickupkey";
            public static string SwapBackModifier { get; } = "carryonswapbackmodifierkey";
            public static string Toggle { get; } = "carryontogglekey";
            public static string QuickDrop { get; } = "carryonquickdropkey";
            public static string QuickDropAll { get; } = "carryonquickdropallkey";
            public static string ToggleDoubleTapDismount { get; } = "carryontoggledoubletapdismountkey";

        }

        public static class SoundPath
        {
            public static string DefaultPlace { get; } = "sounds/player/build";
            public static string DefaultBreak { get; } = "game:sounds/block/planks";
            public static string Throw { get; } = "sounds/player/throw";
        }

        public static string DefaultTransformGroup { get; } = "default";
        public static string FrontCarryAttachmentPoint { get; } = "carryon:FrontCarry";
        public static string WorldConfigPrefix { get; } = "carryon:";

    }
}

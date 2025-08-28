using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common;
using CarryOn.Server.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.API.Common.CarryCode;

namespace CarryOn.Common.Behaviors
{
    /// <summary> Block behavior which, when added to a block, will allow
    ///           said block to be picked up by players and carried around. </summary>
    public class BlockBehaviorCarryable : BlockBehavior, IConditionalBlockBehavior
    {
        public static string Name { get; } = "Carryable";

        public static WorldInteraction[] Interactions { get; }
            = { new WorldInteraction {
                ActionLangCode  = CarryOnCode("blockhelp-pickup"),
                HotKeyCode      = HotKeyCode.Pickup,
                MouseButton     = EnumMouseButton.Right,
                RequireFreeHand = true,
            } };

        public static BlockBehaviorCarryable Default { get; }
            = new BlockBehaviorCarryable(null);

        public static ModelTransform DefaultBlockTransform => new()
        {
            Translation = new Vec3f(0.0F, 0.0F, 0.0F),
            Rotation = new Vec3f(0.0F, 0.0F, 0.0F),
            Origin = new Vec3f(0.5F, 0.5F, 0.5F),
            ScaleXYZ = new Vec3f(0.5F, 0.5F, 0.5F)
        };

        public static readonly IReadOnlyDictionary<CarrySlot, float> DefaultWalkSpeed
            = new Dictionary<CarrySlot, float> {
                { CarrySlot.Hands    , -0.25F },
                { CarrySlot.Back     , -0.15F },
                { CarrySlot.Shoulder , -0.15F },
            };

        public static readonly IReadOnlyDictionary<CarrySlot, string> DefaultAnimation
            = new Dictionary<CarrySlot, string> {
                { CarrySlot.Hands    , CarryOnCode("holdheavy") },
                { CarrySlot.Shoulder , CarryOnCode("shoulder")  }
            };

        public float InteractDelay { get; private set; } = CarryCode.Default.PickUpSpeed;

        public float TransferDelay { get; private set; } = CarryCode.Default.TransferSpeed;

        public ModelTransform DefaultTransform { get; private set; } = DefaultBlockTransform;

        public SlotStorage Slots { get; } = new SlotStorage();

        public Vec3i MultiblockOffset { get; private set; } = null;

        public int PatchPriority { get; private set; } = 0;

        public bool PreventAttaching { get; private set; } = false;

        public bool TransferEnabled { get; private set; } = false;

        public Type TransferHandlerType { get; set; } = null;

        public string EnabledCondition { get; set; }

        public CollectibleBehavior TransferHandlerBehavior { get; private set; } = null;

        public ICarryableTransfer TransferHandler { get; private set; } = null;

        public BlockBehaviorCarryable(Block block)
            : base(block) { }

        public JsonObject Properties { get; set; }
        

        public override void Initialize(JsonObject properties)
        {
            Properties = properties;
            base.Initialize(properties);
            if (JsonHelper.TryGetInt(properties, "patchPriority", out var p)) PatchPriority = p;

            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;

            if (JsonHelper.TryGetFloat(properties, "transferDelay", out var t)) TransferDelay = t;

            if (JsonHelper.TryGetVec3i(properties, "multiblockOffset", out var o)) MultiblockOffset = o;

            if (JsonHelper.TryGetBool(properties, "preventAttaching", out var a)) PreventAttaching = a;

            if (JsonHelper.TryGetString(properties, "enabledCondition", out var e)) EnabledCondition = e;

            DefaultTransform = JsonHelper.GetTransform(properties, DefaultBlockTransform);
            Slots.Initialize(properties["slots"], DefaultTransform);

        }

        public void ProcessConditions(ICoreAPI api, Block block)
        {
            // Check each slot's SlotSettings.EnabledCondition and remove if condition is false
            var config = api.World.Config;
            var slotsToRemove = new List<CarrySlot>();
            foreach (var kvp in Slots._dict.ToList())
            {
                var slot = kvp.Key;
                var settings = kvp.Value;
                if (!string.IsNullOrWhiteSpace(settings.EnabledCondition))
                {
                    bool enabled = config.EvaluateDotNotationLogic(api, settings.EnabledCondition);
                    if (!enabled)
                    {
                        slotsToRemove.Add(slot);
                    }
                }
            }
            
            if (slotsToRemove.Count > 0)
            {
                // Removing slot from instance and propertiesAtString to disable the slot
                var jObj = JObject.Parse(propertiesAtString);
                var jSlots = jObj["slots"] as JObject;

                // Remove slots that are disabled
                foreach (var slot in slotsToRemove)
                {
                    jSlots?.Remove(slot.ToString());
                    Slots.RemoveSlot(slot); // For server instance
                }
                // Updating propertiesAtString which is sent to the client
                propertiesAtString = jObj.ToString();
            }
        }

        public void ConfigureTransferBehavior(Type type, ICoreAPI api)
        {
            TransferHandlerBehavior = block?.GetBehavior(type);
            if (TransferHandlerBehavior != null && TransferHandlerBehavior is ICarryableTransfer transferHandler)
            {
                TransferEnabled = transferHandler.IsTransferEnabled(api);
                TransferHandlerType = type;
                TransferHandler = transferHandler;
            }
            else
            {
                TransferEnabled = false;
            }

        }


        /// <summary>
        /// If the block has a transfer handler, checks if the main block is allowed to be picked up and carried.
        /// </summary>
        public bool TransferBlockCarryAllowed(IPlayer player, BlockSelection selection)
        {
            if (TransferEnabled && TransferHandler != null)
            {
                return TransferHandler.IsBlockCarryAllowed(player, selection);
            }

            return true; // If no transfer handler, assume carry is allowed
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
                    IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handled)
        {
            // Don't show interaction help if the block is not carryable
            if (Slots == null || Slots.Count == 0)
            {
                return null;
            }

            if (forPlayer.Entity.GetCarried(CarrySlot.Hands) != null)
            {
                return null; // Don't show interaction help if player is already carrying something
            }

            if (!TransferBlockCarryAllowed(forPlayer, selection))
            {
                return null;
            }

            return Interactions;
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            if (world.Api.Side == EnumAppSide.Server)
            {
                DroppedBlockInfo.Remove(pos, world.Api);
            }
            base.OnBlockRemoved(world, pos, ref handling);
        }

        public class SlotSettings
        {
            public ModelTransform Transform { get; set; }

            public string Animation { get; set; }

            public float WalkSpeedModifier { get; set; } = 0.0F;

            public string EnabledCondition { get; set; }
        }

        public class SlotStorage
        {
            public readonly Dictionary<CarrySlot, SlotSettings> _dict = new();
            public void RemoveSlot(CarrySlot slot)
            {
                _dict.Remove(slot);
            }

            public SlotSettings this[CarrySlot slot]
                => _dict.TryGetValue(slot, out var settings) ? settings : null;

            public int Count => _dict.Count;

            public void Initialize(JsonObject properties, ModelTransform defaultTansform)
            {
                _dict.Clear();
                if (properties?.Exists != true)
                {
                    if (!DefaultAnimation.TryGetValue(CarrySlot.Hands, out var anim)) anim = null;
                    _dict.Add(CarrySlot.Hands, new SlotSettings { Animation = anim });
                }
                else
                {
                    foreach (var slot in Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>())
                    {
                        var slotProperties = properties[slot.ToString()];
                        if (slotProperties?.Exists != true) continue;

                        if (!_dict.TryGetValue(slot, out var settings))
                        {
                            if (!DefaultAnimation.TryGetValue(slot, out var anim)) anim = null;
                            _dict.Add(slot, settings = new SlotSettings { Animation = anim });
                        }

                        settings.Transform = JsonHelper.GetTransform(slotProperties, defaultTansform);
                        settings.Animation = slotProperties["animation"].AsString(settings.Animation);

                        if (!DefaultWalkSpeed.TryGetValue(slot, out var speed)) speed = 0.0F;
                        settings.WalkSpeedModifier = slotProperties["walkSpeedModifier"].AsFloat(speed);

                        if (JsonHelper.TryGetString(slotProperties, "enabledCondition", out var e)) settings.EnabledCondition = e;
                    }
                }
            }
        }
    }
}

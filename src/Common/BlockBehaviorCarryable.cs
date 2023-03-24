using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common;
using CarryOn.Server;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Common
{
    /// <summary> Block behavior which, when added to a block, will allow
    ///           said block to be picked up by players and carried around. </summary>
    public class BlockBehaviorCarryable : BlockBehavior
    {
        public static string Name { get; } = "Carryable";

        public static WorldInteraction[] Interactions { get; }
            = { new WorldInteraction {
                ActionLangCode  = CarrySystem.ModId + ":blockhelp-pickup",
                HotKeyCode      = "sneak",
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
                { CarrySlot.Hands    , $"{ CarrySystem.ModId }:holdheavy" },
                { CarrySlot.Shoulder , $"{ CarrySystem.ModId }:shoulder"  },
            };

        public float InteractDelay { get; private set; } = CarrySystem.PickUpSpeedDefault;

        public ModelTransform DefaultTransform { get; private set; } = DefaultBlockTransform;

        public SlotStorage Slots { get; } = new SlotStorage();

        public Vec3i MultiblockOffset {get; private set;} = null;

        public BlockBehaviorCarryable(Block block)
            : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;

            if(JsonHelper.TryGetVec3i(properties, "multiblockOffset", out var o)) MultiblockOffset = o;

            DefaultTransform = JsonHelper.GetTransform(properties, DefaultBlockTransform);
            Slots.Initialize(properties["slots"], DefaultTransform);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handled)
                => Interactions;

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
        }

        public class SlotStorage
        {
            private readonly Dictionary<CarrySlot, SlotSettings> _dict = new();

            public SlotSettings this[CarrySlot slot]
                => _dict.TryGetValue(slot, out var settings) ? settings : null;

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
                    }
                }
            }
        }
    }
}

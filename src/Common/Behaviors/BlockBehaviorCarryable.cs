using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Behaviors
{
    public class BlockBehaviorCarryable : BlockBehavior, IConditionalBlockBehavior
    {
        public static string Name { get; } = "Carryable";

        public string[] TransformTemplates { get; private set; } = [];

        public bool RenderRootFirst { get; set; } = false;

        public string? RootRenderFacing { get; set; }

        public string? RootRenderVariant { get; set; }

        public string? TransformGroupResolver { get; set; }

        public string? RootGroupResolver { get; set; }

        public string? AttachmentGroupResolver { get; set; }

        public string[]? DataAttributes { get; private set; } = [];

        public string? DataAttributesPrefix { get; private set; }

        public bool HasLocalTransformGroups { get; private set; } = false;

        public Dictionary<string, TransformSettings[]> ResolvedTransformGroups { get; private set; } = [];

        public static BlockBehaviorCarryable Default { get; }
            = new BlockBehaviorCarryable(null);

        public static ModelTransform DefaultBlockTransform => new()
        {
            Translation = new Vec3f(0.0F, 0.0F, 0.0F),
            Rotation = new Vec3f(0.0F, 0.0F, 0.0F),
            Origin = new Vec3f(0.5F, 0.5F, 0.5F),
            ScaleXYZ = new Vec3f(0.5F, 0.5F, 0.5F)
        };

        public static readonly IReadOnlyDictionary<CarrySlot, string> DefaultAnimation
            = new Dictionary<CarrySlot, string> {
                { CarrySlot.Hands    , CarryOnCode("holdheavy") }
            };

        public float InteractDelay { get; private set; } = CarryCode.Default.PickUpSpeed;

        public float TransferDelay { get; private set; } = CarryCode.Default.TransferSpeed;

        public ModelTransform DefaultTransform { get; private set; } = DefaultBlockTransform.Clone();

        public LabelRenderSettings? LabelRenderSettings { get; set; }

        public SlotStorage Slots { get; } = new SlotStorage();

        public int PatchPriority { get; private set; } = 0;

        public bool OverrideExistingProperties { get; private set; } = false;

        public bool PreventAttaching { get; private set; } = false;

        public bool OptimisticPickup { get; private set; } = true;

        public bool ForcePickupOnSwapBack { get; set; } = false;

        public bool SwapBackKeyPassthrough { get; private set; } = false;

        public bool TransferEnabled { get; private set; } = false;

        public Type? TransferHandlerType { get; set; }

        public string EnabledCondition { get; set; } = string.Empty;

        public CollectibleBehavior? TransferHandlerBehavior { get; private set; }

        public ICarryableTransfer? TransferHandler { get; private set; }

        public IDictionary<string, string> TypeGroup { get; private set; } = new Dictionary<string, string>();

        public BlockBehaviorCarryable(Block? block)
            : base(block) { }

        public JsonObject? Properties { get; set; }

        public override void Initialize(JsonObject properties)
        {
            Properties = properties;
            base.Initialize(properties);
            if (JsonHelper.TryGetInt(properties, "patchPriority", out var p)) PatchPriority = p;
            if (JsonHelper.TryGetBool(properties, "overrideExistingProperties", out var oep)) OverrideExistingProperties = oep;
            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;
            if (JsonHelper.TryGetFloat(properties, "transferDelay", out var t)) TransferDelay = t;
            if (JsonHelper.TryGetBool(properties, "preventAttaching", out var a)) PreventAttaching = a;
            if (JsonHelper.TryGetBool(properties, "optimisticPickup", out var o)) OptimisticPickup = o;
            if (JsonHelper.TryGetBool(properties, "forcePickupOnSwapBack", out var fp)) ForcePickupOnSwapBack = fp;
            if (JsonHelper.TryGetBool(properties, "swapBackKeyPassthrough", out var sbkp)) SwapBackKeyPassthrough = sbkp;
            if (!SwapBackKeyPassthrough && JsonHelper.TryGetBool(properties, "preventSwapBack", out var psb)) SwapBackKeyPassthrough = psb;
            if (JsonHelper.TryGetString(properties, "enabledCondition", out var e)) EnabledCondition = e ?? string.Empty;
            if (JsonHelper.TryGetBool(properties, "renderRootFirst", out var rootFirst)) RenderRootFirst = rootFirst;
            if (JsonHelper.TryGetString(properties, "rootRenderFacing", out var drf)) RootRenderFacing = drf;
            if (JsonHelper.TryGetString(properties, "rootRenderVariant", out var drv)) RootRenderVariant = drv;
            if (JsonHelper.TryGetString(properties, "transformGroupResolver", out var g)) TransformGroupResolver = g;
            if (JsonHelper.TryGetString(properties, "rootGroupResolver", out var rg)) RootGroupResolver = rg;
            if (JsonHelper.TryGetString(properties, "attachmentGroupResolver", out var ag)) AttachmentGroupResolver = ag;

            if (TransformGroupResolver != null)
            {
                RootGroupResolver ??= TransformGroupResolver;
                AttachmentGroupResolver ??= TransformGroupResolver;
            }

            if (properties.KeyExists("dataAttributes"))
            {
                var attrs = properties["dataAttributes"].AsArray<string>();
                DataAttributes = attrs?.Select(a => a ?? string.Empty).ToArray() ?? [];
            }
            if (JsonHelper.TryGetString(properties, "dataAttributesPrefix", out var bp))
                DataAttributesPrefix = bp;

            if (properties.KeyExists("transformTemplates"))
            {
                var templates = properties["transformTemplates"]?.AsArray<string>();
                if (templates != null)
                    TransformTemplates = templates.Select(s => s?.ToLowerInvariant() ?? "").ToArray();
            }

            if (JsonHelper.TryGetObject(properties, "transformGroups", out var transformGroupsObj))
            {
                if (transformGroupsObj?.Properties().Any() == true)
                    HasLocalTransformGroups = true;
            }

            if (JsonHelper.TryGetObject(properties, "groups", out var groupJObj) && groupJObj != null)
            {
                var groupObj = properties["groups"];
                foreach (var prop in groupJObj.Properties())
                {
                    if (!JsonHelper.TryGetArray(groupObj, prop.Name, out string?[]? types)) continue;
                    foreach (var type in types ?? [])
                    {
                        if (string.IsNullOrWhiteSpace(type) || TypeGroup.ContainsKey(type)) continue;
                        TypeGroup[type] = prop.Name;
                    }
                }
            }

            if (properties.KeyExists("labelRenderSettings"))
                LabelRenderSettings = LabelRenderSettingsParser.Parse(properties["labelRenderSettings"], transformInChildObject: true);
            else if (properties.KeyExists("labelTransform"))
                LabelRenderSettings = LabelRenderSettingsParser.Parse(properties["labelTransform"], transformInChildObject: false);

            DefaultTransform = ModelTransformParser.GetTransform(properties, DefaultBlockTransform);
            Slots.Initialize(properties["slots"]);
        }

        public void ResolveTransformGroups(TransformTemplateManager manager)
        {
            if (HasLocalTransformGroups)
            {
                var jObj = JObject.Parse(propertiesAtString);
                if (manager.TryParseTransformGroups(new JsonObject(jObj), out var localTransformGroups))
                    ResolvedTransformGroups = manager.ResolveAndFlattenTransformGroups(TransformTemplates, localTransformGroups);
                else
                    ResolvedTransformGroups = manager.ResolveAndFlattenTransformGroups(TransformTemplates);
            }
            else
            {
                ResolvedTransformGroups = manager.ResolveAndFlattenTransformGroups(TransformTemplates);
            }
        }

        public void ProcessConditions(ICoreAPI api, Block block)
        {
            var config = api.World.Config;
            var slotsToRemove = new List<CarrySlot>();
            foreach (var kvp in Slots.SlotSettingsDict.ToList())
            {
                var slot = kvp.Key;
                var settings = kvp.Value;
                if (!string.IsNullOrWhiteSpace(settings.EnabledCondition))
                {
                    bool enabled = config.EvaluateDotNotationLogic(api, settings.EnabledCondition);
                    if (!enabled)
                        slotsToRemove.Add(slot);
                }
            }

            if (slotsToRemove.Count > 0)
            {
                var jObj = JObject.Parse(propertiesAtString);
                var jSlots = jObj["slots"] as JObject;
                foreach (var slot in slotsToRemove)
                {
                    jSlots?.Remove(slot.ToString());
                    Slots.RemoveSlot(slot);
                }
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

        public string GetTransformGroupName(CarriedBlock carried, string baseGroup, bool checkExists = true)
        {
            var type = carried.ItemStack?.Attributes?.GetString("type");

            if (!string.IsNullOrEmpty(type) && TypeGroup != null && TypeGroup.Count > 0)
            {
                TypeGroup.TryGetValue(type, out var typeGroupName);
                if (typeGroupName != null)
                    typeGroupName = $"-{typeGroupName}";

                if (!checkExists || TransformGroupExists(carried, baseGroup + typeGroupName))
                    baseGroup += typeGroupName;
            }
            return baseGroup;
        }

        public bool TransformGroupExists(CarriedBlock carried, string group)
        {
            if (carried == null || Slots == null || Slots[carried.Slot] == null || ResolvedTransformGroups.Count == 0)
                return false;
            return ResolvedTransformGroups.ContainsKey(group);
        }

        public bool CanCarryInSlot(CarrySlot slot, ItemStack? itemStack)
        {
            var slotStorage = Slots?[slot];
            if (slotStorage == null)
                return false;

            if (itemStack != null && itemStack.Attributes.HasAttribute("type"))
            {
                var itemType = itemStack.Attributes.GetString("type");
                if (!string.IsNullOrEmpty(itemType) && slotStorage.ExcludedTypes != null && slotStorage.ExcludedTypes.Contains(itemType))
                    return false;
            }
            return true;
        }

        public bool TransferBlockCarryAllowed(IPlayer? player, BlockSelection? selection)
        {
            if (TransferEnabled && TransferHandler != null && player != null && selection != null)
                return TransferHandler.IsBlockCarryAllowed(player, selection);

            return true;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handled)
        {
            return CarryableInteractionHelpBuilder.BuildHelp(world, selection, forPlayer, this);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            world.GetCarryEvents()?.TriggerBlockRemoved(pos);
            base.OnBlockRemoved(world, pos, ref handling);
        }
    }
}

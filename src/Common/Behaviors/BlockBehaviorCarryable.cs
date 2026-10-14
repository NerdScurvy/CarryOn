using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Behaviors
{
    public class BlockBehaviorCarryable : BlockBehavior, IConditionalBlockBehavior
    {
        public static string Name { get; } = GetCarryCode("carryable");

        private static ICarryManager? carryManager;

        /// <summary>
        /// Initializes the behavior with the carry manager for transfer handler registry lookups.
        /// Must be called during mod startup on both client and server.
        /// </summary>
        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// Whether CarryOn's own factory constructed this instance (a baseline remount
        /// or an automap clone), as opposed to the engine loading it from a blocktype
        /// or patch, another mod registering it, or test code appending it. Internal
        /// and not serialized: it is the un-create predicate of the conditioning
        /// baseline's restore pass, invisible to the wire, to snapshots and to mod authors. Set only by the
        /// factory - <see cref="Server.Logic.BehavioralConditions.ConditioningBaseline"/>
        /// remounts and the automapper's appends.
        /// </summary>
        internal bool CreatedByConditioning;

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

        public static BlockBehaviorCarryable Default { get; }
            = new BlockBehaviorCarryable(null);

        public static ModelTransform DefaultBlockTransform => new()
        {
            Translation = new Vec3f(0.0f, 0.0f, 0.0f),
            Rotation = new Vec3f(0.0f, 0.0f, 0.0f),
            Origin = new Vec3f(0.5f, 0.5f, 0.5f),
            ScaleXYZ = new Vec3f(0.5f, 0.5f, 0.5f)
        };

        public static readonly IReadOnlyDictionary<CarrySlot, string> DefaultAnimation
            = new Dictionary<CarrySlot, string> {
                { CarrySlot.Hands    , GetCarryCode("holdheavy") }
            };

        public float InteractDelay { get; private set; } = CarryCodes.Defaults.PickUpSpeed;

        public float TransferDelay { get; private set; } = CarryCodes.Defaults.TransferSpeed;

        public ModelTransform DefaultTransform { get; private set; } = DefaultBlockTransform.Clone();

        public LabelRenderSettings? LabelRenderSettings { get; set; }

        public SlotStorage Slots { get; } = new SlotStorage();

        public int PatchPriority { get; private set; } = 0;

        public bool OverrideExistingProperties { get; private set; } = false;

        public bool PreventAttaching { get; private set; } = false;

        public bool OptimisticPickup { get; private set; } = true;

        public bool ForcePickupOnSwapBack { get; set; } = false;

        public bool SwapBackKeyPassthrough { get; private set; } = false;

        private bool transferEnabled;

        public bool TransferEnabled
        {
            get
            {
                EnsureTransferHandler();
                return transferEnabled;
            }
            private set => transferEnabled = value;
        }

        public Type? TransferHandlerType { get; set; }

        public string EnabledCondition { get; set; } = string.Empty;

        private string transferCode = "";

        private ICarryableBlockSlotHandler? transferHandler;

        private bool transferHandlerResolved;

        public ICarryableBlockSlotHandler? TransferHandler
        {
            get
            {
                EnsureTransferHandler();
                return transferHandler;
            }
            private set => transferHandler = value;
        }

        private void EnsureTransferHandler()
        {
            if (transferHandlerResolved) return;

            if (string.IsNullOrEmpty(transferCode))
            {
                transferHandlerResolved = true;
                return;
            }

            if (transferCode == ReservedCodes.Auto)
            {
                if (carryManager == null) return;

                foreach (var behavior in block.BlockBehaviors)
                {
                    if (behavior is ICarryableBlockSlotHandler transfer)
                    {
                        ConfigureTransferBehavior(transfer, carryManager.Api);
                        transferHandlerResolved = true;
                        return;
                    }
                }

                carryManager.Api.Logger.Debug("Failed to resolve transfer handler for block {0}", block.Code);
                transferHandlerResolved = true;
                return;
            }

            if (carryManager != null
                && carryManager.TryGetTransferHandler(transferCode, out var handler)
                && handler != null)
            {
                ConfigureTransferBehavior(handler, carryManager.Api);
                transferHandlerResolved = true;
                return;
            }

            if (carryManager != null)
            {
                carryManager.Api.Logger.Debug("Failed to resolve transfer handler for block {0}", block.Code);
            }

            transferHandlerResolved = false;
        }

        public IDictionary<string, string> TypeGroup { get; private set; } = new Dictionary<string, string>();

        public BlockBehaviorCarryable(Block? block)
            : base(block) { }

        public JsonObject? Properties { get; set; }

        /// <summary>
        /// The behavior's property JSON with slot-level condition removals already applied:
        /// <c>ProcessConditions</c> rewrites <c>propertiesAtString</c> after dropping
        /// condition-disabled slots, so a snapshot taken after conditioning carries the
        /// faithful final slots.
        /// </summary>
        internal string CurrentPropertiesJson
            => propertiesAtString ?? Properties?.Token?.ToString() ?? string.Empty;

        public override void Initialize(JsonObject properties)
        {
            Properties = properties;
            base.Initialize(properties);

            ParseBaseSettings(properties);
            ParseRenderSettings(properties);
            ParseResolverSettings(properties);
            ParseDataAttributes(properties);
            ParseTransformTemplates(properties);
            ParseLocalTransformGroups(properties);
            ParseTypeGroup(properties);
            ParseLabelRenderSettings(properties);

            DefaultTransform = ModelTransformParser.GetTransform(properties, DefaultBlockTransform);
            Slots.Initialize(properties["slots"]);
        }

        private void ParseBaseSettings(JsonObject properties)
        {
            if (JsonHelper.TryGetInt(properties, "patchPriority", out var p)) PatchPriority = p;
            if (JsonHelper.TryGetBool(properties, "overrideExistingProperties", out var oep)) OverrideExistingProperties = oep;
            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;
            if (JsonHelper.TryGetFloat(properties, "transferDelay", out var t)) TransferDelay = t;
            if (JsonHelper.TryGetBool(properties, "preventAttaching", out var a)) PreventAttaching = a;
            if (JsonHelper.TryGetBool(properties, "optimisticPickup", out var o)) OptimisticPickup = o;
            if (JsonHelper.TryGetBool(properties, "forcePickupOnSwapBack", out var fp)) ForcePickupOnSwapBack = fp;
            if (JsonHelper.TryGetBool(properties, "swapBackKeyPassthrough", out var sbkp)) SwapBackKeyPassthrough = sbkp;
            if (!SwapBackKeyPassthrough && JsonHelper.TryGetBool(properties, "preventSwapBack", out var psb)) SwapBackKeyPassthrough = psb;
            if (JsonHelper.TryGetString(properties, BehaviorJsonKeys.EnabledCondition, out var e)) EnabledCondition = e ?? string.Empty;
            if (JsonHelper.TryGetString(properties, "transferCode", out var tc)) transferCode = tc ?? string.Empty;
        }

        private void ParseRenderSettings(JsonObject properties)
        {
            if (JsonHelper.TryGetBool(properties, "renderRootFirst", out var rootFirst)) RenderRootFirst = rootFirst;
            if (JsonHelper.TryGetString(properties, "rootRenderFacing", out var drf)) RootRenderFacing = drf;
            if (JsonHelper.TryGetString(properties, "rootRenderVariant", out var drv)) RootRenderVariant = drv;
        }

        private void ParseResolverSettings(JsonObject properties)
        {
            if (JsonHelper.TryGetString(properties, "transformGroupResolver", out var g)) TransformGroupResolver = g;
            if (JsonHelper.TryGetString(properties, "rootGroupResolver", out var rg)) RootGroupResolver = rg;
            if (JsonHelper.TryGetString(properties, "attachmentGroupResolver", out var ag)) AttachmentGroupResolver = ag;

            if (TransformGroupResolver != null)
            {
                RootGroupResolver ??= TransformGroupResolver;
                AttachmentGroupResolver ??= TransformGroupResolver;
            }
        }

        private void ParseDataAttributes(JsonObject properties)
        {
            if (properties.KeyExists("dataAttributes"))
            {
                var attrs = properties["dataAttributes"].AsArray<string>();
                DataAttributes = attrs?.Select(a => a ?? string.Empty).ToArray() ?? [];
            }
            if (JsonHelper.TryGetString(properties, "dataAttributesPrefix", out var bp))
                DataAttributesPrefix = bp;
        }

        private void ParseTransformTemplates(JsonObject properties)
        {
            if (properties.KeyExists("transformTemplates"))
            {
                var templates = properties["transformTemplates"]?.AsArray<string>();
                if (templates != null)
                    TransformTemplates = templates.Select(s => s?.ToLowerInvariant() ?? "").ToArray();
            }
        }

        private void ParseLocalTransformGroups(JsonObject properties)
        {
            if (JsonHelper.TryGetObject(properties, "transformGroups", out var transformGroupsObj))
            {
                if (transformGroupsObj?.Properties().Any() == true)
                    HasLocalTransformGroups = true;
            }
        }

        private void ParseTypeGroup(JsonObject properties)
        {
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
        }

        private void ParseLabelRenderSettings(JsonObject properties)
        {
            if (properties.KeyExists("labelRenderSettings"))
                LabelRenderSettings = LabelRenderSettingsParser.Parse(properties["labelRenderSettings"], transformInChildObject: true);
            else if (properties.KeyExists(LabelTransformGroup))
                LabelRenderSettings = LabelRenderSettingsParser.Parse(properties[LabelTransformGroup], transformInChildObject: false);
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

        private void ConfigureTransferBehavior(ICarryableBlockSlotHandler transferHandler, ICoreAPI api)
        {
            TransferEnabled = true;
            TransferHandlerType = transferHandler.GetType();
            this.transferHandler = transferHandler;
        }

        public bool CanCarryInSlot(CarrySlot slot, ItemStack? itemStack)
        {
            var slotStorage = Slots?[slot];
            if (slotStorage == null)
                return false;

            if (itemStack != null && itemStack.Attributes.HasAttribute(AttributeKeys.Type))
            {
                var itemType = itemStack.Attributes.GetString(AttributeKeys.Type);
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
            world.GetCarryEvents().TriggerBlockRemoved(pos);
            base.OnBlockRemoved(world, pos, ref handling);
        }
    }
}

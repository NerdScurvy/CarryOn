using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Server.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Behaviors
{
    /// <summary> Block behavior which, when added to a block, will allow
    ///           said block to be picked up by players and carried around. </summary>
    public class BlockBehaviorCarryable : BlockBehavior, IConditionalBlockBehavior

    {
        public static string Name { get; } = "Carryable";

        /// <summary>
        /// If present, stores the list of transformTemplates to be processed during asset finalization on the client side.
        /// </summary>
        public string[] TransformTemplates { get; private set; } = [];

        public bool RenderRootFirst { get; set; } = false;

        public string? TransformGroupResolver { get; set; }

        public bool HasLocalTransformGroups { get; private set; } = false;

        // Resolved and flattened transform groups from transformTemplates and transformGroups.
        public Dictionary<string, TransformSettings[]> ResolvedTransformGroups { get; private set; } = [];

        private static ItemStack[]? handsfreeStacks;
        private static ItemStack[]? nohandsfreeStacks;

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
                { CarrySlot.Back     , -0.15F }
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

        /// When true, looking at this block while the swap-back modifier is held will trigger force-pickup
        /// instead of the back-slot swap. Set via JSON properties or programmatically in OnLoaded.
        public bool ForcePickupOnSwapBack { get; set; } = false;


        /// When true, while swap-back modifier is held and player targets this block,
        /// CarryOn yields input so the underlying block interaction can handle it.
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


        /// <summary>
        /// Initializes the carryable behavior by reading the relevant properties from the block's JSON definition. 
        /// This includes setting up transform templates, transform groups, slot settings, label render settings, 
        /// and other carry-related properties based on the block's configuration. The method also checks for the 
        /// presence of local transform groups defined in the block's JSON and sets the HasLocalTransformGroups flag 
        /// accordingly to determine how transform groups will be resolved on the client side.
        /// </summary>
        /// <param name="properties">The JSON properties containing the configuration for the carryable behavior.</param>
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
            // Backward-compatible alias, only applied if new key was not provided.
            if (!SwapBackKeyPassthrough && JsonHelper.TryGetBool(properties, "preventSwapBack", out var psb)) SwapBackKeyPassthrough = psb;

            if (JsonHelper.TryGetString(properties, "enabledCondition", out var e)) EnabledCondition = e ?? string.Empty;

            if (JsonHelper.TryGetBool(properties, "renderRootFirst", out var rootFirst))
            {
                RenderRootFirst = rootFirst;
            }

            if (JsonHelper.TryGetString(properties, "transformGroupResolver", out var g)) TransformGroupResolver = g;

            // Record transformTemplates for later processing (client-side asset finalization)
            if (properties.KeyExists("transformTemplates"))
            {
                var templates = properties["transformTemplates"]?.AsArray<string>();
                if (templates != null)
                {
                    TransformTemplates = templates
                        .Select(s => s?.ToLowerInvariant() ?? "")
                        .ToArray();
                }
            }

            // Check if block has local transform groups defined and set HasLocalTransformGroups accordingly so we know what to parse client side
            if (JsonHelper.TryGetObject(properties, "transformGroups", out var transformGroupsObj))
            {
                if (transformGroupsObj?.Properties().Any() == true)
                {
                    HasLocalTransformGroups = true;
                }
            }


            if (JsonHelper.TryGetObject(properties, "groups", out var groupJObj) && groupJObj != null)
            {
                var groupObj = properties["groups"];
                foreach (var prop in groupJObj.Properties())
                {
                    if (!JsonHelper.TryGetArray(groupObj, prop.Name, out string?[]? types))
                    {
                        continue;
                    }
                    foreach (var type in types ?? [])
                    {
                        if (string.IsNullOrWhiteSpace(type) || TypeGroup.ContainsKey(type))
                        {
                            continue;
                        }
                        TypeGroup[type] = prop.Name;
                    }
                }
            }

            if (properties.KeyExists("labelRenderSettings"))
            {
                LabelRenderSettings = GetLabelRenderSettings(properties["labelRenderSettings"], transformInChildObject: true);
            }
            else if (properties.KeyExists("labelTransform"))
            {
                // Backward compatibility for older patches where label transform and metadata were in one object.
                LabelRenderSettings = GetLabelRenderSettings(properties["labelTransform"], transformInChildObject: false);
            }

            DefaultTransform = JsonHelper.GetTransform(properties, DefaultBlockTransform);
            Slots.Initialize(properties["slots"]);
        }

        private static LabelRenderSettings? GetLabelRenderSettings(JsonObject json, bool transformInChildObject)
        {
            if (json == null || !json.Exists)
            {
                return null;
            }

            var settings = new LabelRenderSettings();
            var hasAnyValue = false;

            var transformJson = transformInChildObject ? json["transform"] : json;
            if (JsonHelper.HasAnyTransformValue(transformJson))
            {
                settings.Transform = JsonHelper.GetTransform(transformJson, null);
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetInt(json, "maxWidth", out var maxWidth))
            {
                settings.MaxWidth = maxWidth;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetInt(json, "maxHeight", out var maxHeight))
            {
                settings.MaxHeight = maxHeight;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetInt(json, "iconPixelSize", out var iconPixelSize))
            {
                settings.IconPixelSize = iconPixelSize;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetFloat(json, "iconScale", out var iconScale))
            {
                settings.IconScale = iconScale;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetBool(json, "iconFromInventory", out var iconFromInventory))
            {
                settings.IconFromInventory = iconFromInventory;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetString(json, "fontName", out var fontName))
            {
                settings.FontName = fontName ?? string.Empty;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetString(json, "verticalAlign", out var verticalAlign))
            {
                settings.VerticalAlign = verticalAlign ?? string.Empty;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetBool(json, "boldFont", out var boldFont))
            {
                settings.BoldFont = boldFont;
                hasAnyValue = true;
            }

            if (json.KeyExists("additionalTransforms"))
            {
                var arr = json["additionalTransforms"]?.AsArray();
                if (arr != null && arr.Length > 0)
                {
                    var additional = new List<ModelTransform>(arr.Length);
                    foreach (var entry in arr)
                    {
                        if (JsonHelper.HasAnyTransformValue(entry))
                        {
                            additional.Add(JsonHelper.GetTransform(entry, null));
                        }
                    }
                    if (additional.Count > 0)
                    {
                        settings.AdditionalTransforms = additional;
                        hasAnyValue = true;
                    }
                }
            }

            return hasAnyValue ? settings : null;
        }

        public void ResolveTransformGroups(TransformTemplateManager manager)
        {
            // If there are local transform groups, then first parse the BlockBehaviorCarryable's own transform groups from JSON, then resolve templates if any.
            if (HasLocalTransformGroups)
            {
                // Parse propertiesAtString to get the local transform groups defined at the top level of the block's JSON
                var jObj = JObject.Parse(propertiesAtString);

                if (manager.TryParseTransformGroups(new JsonObject(jObj), out var localTransformGroups))
                {
                    ResolvedTransformGroups = manager.ResolveAndFlattenTransformGroups(TransformTemplates, localTransformGroups);
                }
                else
                {
                    // Malformed/missing transformGroups: fall back to templates only
                    ResolvedTransformGroups = manager.ResolveAndFlattenTransformGroups(TransformTemplates);
                }
            }
            else
            {
                ResolvedTransformGroups = manager.ResolveAndFlattenTransformGroups(TransformTemplates);
            }
        }

        /// <summary>
        /// Checks the block's SlotSettings for any EnabledCondition and evaluates it. 
        /// If the condition is false, the slot is removed from the block's Slots and propertiesAtString to disable the slot and any associated functionality
        ///  (e.g. carrying in that slot, applying transforms, etc). 
        /// This allows dynamic enabling/disabling of slots based on world config or other conditions.
        /// </summary>
        /// <param name="api"> The core API instance. </param>
        /// <param name="block"> The block to process conditions for. </param>
        public void ProcessConditions(ICoreAPI api, Block block)
        {
            // Check each slot's SlotSettings.EnabledCondition and remove if condition is false
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

        /// <summary>
        /// If the block has a carry transfer handler behavior of the specified type, checks if transfer is enabled and sets TransferHandler 
        /// and TransferHandlerType accordingly to allow checking transfer conditions and handling block transfer logic in the handler. 
        /// If no valid handler is found or transfer is not enabled, TransferEnabled will be set to false to indicate that no special transfer 
        /// handling should be applied to this block.
        /// This method should be called during block initialization to set up transfer handling based on the block's behaviors and properties.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="api"></param>
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

        /// <summary> Returns the transform group name for the carried block, based on its type attribute.
        /// If the type has a corresponding type group, it is appended to the baseGroup.
        /// If checkExists is false or the resulting group exists in the slot's transforms,
        /// then the type group is appended to the baseGroup.
        /// If not, it returns the baseGroup unchanged. 
        /// </summary>
        /// <param name="carried"> The carried block for which to get the transform group name. </param>
        /// <param name="baseGroup"> The base transform group name to which any type group will be appended. </param>
        /// <param name="checkExists"> Whether to check if the resulting transform group exists. </param>
        /// <returns> The transform group name for the carried block. </returns>
        public string GetTransformGroupName(CarriedBlock carried, string baseGroup, bool checkExists = true)
        {
            // Get carried block's type from itemstack attribute 
            var type = carried.ItemStack?.Attributes?.GetString("type");

            // Lookup type group for the type, if any and append to transformsGroup if found
            if (!string.IsNullOrEmpty(type) && TypeGroup != null && TypeGroup.Count > 0)
            {
                TypeGroup.TryGetValue(type, out var typeGroupName);

                if (typeGroupName != null)
                {
                    typeGroupName = $"-{typeGroupName}";
                }

                if (!checkExists || TransformGroupExists(carried, baseGroup + typeGroupName))
                {
                    baseGroup += typeGroupName;
                }
            }
            return baseGroup;
        }

        /// <summary>
        /// Checks if a transform group with the specified name exists for the carried block.
        /// </summary>
        /// <param name="carried"> The carried block to check. </param>
        /// <param name="group"> The name of the transform group to check for. </param>
        /// <returns> True if the transform group exists, false otherwise. </returns>
        public bool TransformGroupExists(CarriedBlock carried, string group)
        {
            if (carried == null || Slots == null || Slots[carried.Slot] == null || ResolvedTransformGroups.Count == 0)
            {
                return false;
            }
            return ResolvedTransformGroups.ContainsKey(group);
        }

        /// <summary>
        /// Checks if the block can be carried in the specified slot.
        /// </summary>
        /// <param name="slot"> The carry slot to check. </param>
        /// <param name="itemStack"> The item stack being carried, used to check for any type-based restrictions defined in the slot's SlotSettings. </param>
        /// <returns> True if the block can be carried in the specified slot, false otherwise. </returns>
        public bool CanCarryInSlot(CarrySlot slot, ItemStack? itemStack)
        {
            var slotStorage = Slots?[slot];
            if (slotStorage == null)
            {
                return false;
            }

            // Prevent specific item types from being carried in this slot
            if (itemStack != null && itemStack.Attributes.HasAttribute("type"))
            {
                var itemType = itemStack.Attributes.GetString("type");
                if (!string.IsNullOrEmpty(itemType))
                {
                    // Check if the item type is excluded from the slot
                    if (slotStorage.ExcludedTypes != null && slotStorage.ExcludedTypes.Contains(itemType))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// If the block has a transfer handler, checks if the main block is allowed to be picked up and carried.
        /// </summary>
        /// <param name="player"> The player attempting to carry the block. </param>
        /// <param name="selection"> The block selection context for the carry action. </param>
        /// <returns> True if the block is allowed to be picked up and carried, false otherwise. </returns>
        public bool TransferBlockCarryAllowed(IPlayer? player, BlockSelection? selection)
        {
            if (TransferEnabled && TransferHandler != null && player != null && selection != null)
            {
                return TransferHandler.IsBlockCarryAllowed(player, selection);
            }

            return true; // If no transfer handler, assume carry is allowed
        }


        private static WorldInteraction CreateBasePickupInteraction(ItemStack[] itemStacks)
        {
            return new WorldInteraction {
                ActionLangCode  = CarryOnCode("blockhelp-pickup"),
                HotKeyCode      = HotKeyCode.Pickup,
                MouseButton     = EnumMouseButton.Right,
                RequireFreeHand = true,
                Itemstacks      = itemStacks
            };
        }

        private static WorldInteraction CreateForcePickupInteraction(ItemStack[] itemStacks)
        {
            return new WorldInteraction {
                ActionLangCode  = CarryOnCode("blockhelp-pickup"),
                HotKeyCodes     = [HotKeyCode.SwapBackModifier, HotKeyCode.Pickup],
                MouseButton     = EnumMouseButton.Right,
                RequireFreeHand = true,
                Itemstacks      = itemStacks
            };
        }

        private CarryHintType ResolveAllowedHints(CarryHintContext context, CarryHintType defaultHints)
        {
            if (TransferHandlerBehavior is not ICarryableHintPolicy hintPolicy)
            {
                return defaultHints;
            }

            var allowedHints = hintPolicy.GetAllowedHints(context, defaultHints);

            // Policy can only filter out hints from defaultHints, never add impossible hints.
            return allowedHints & defaultHints;
        }


        /// <summary>
        /// Checks if the block can be carried in the specified slot. If the block has a transfer handler and transfer is enabled, 
        /// also checks if the transfer handler allows the block to be carried based on the player and block selection context.
        /// </summary>
        /// <param name="world"> The world accessor. </param>
        /// <param name="selection"> The block selection context. </param>
        /// <param name="forPlayer"> The player attempting the interaction. </param>
        /// <param name="handled"> The handling status of the interaction. </param>
        /// <returns> An array of world interactions available for the placed block. </returns>
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
                    IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handled)
        {
            // Don't show interaction help if the block is not carryable.
            if (Slots == null || Slots.Count == 0)
            {
                return [];
            }

            // Check the block can be carried in hands (some types may not be allowed)
            ItemStack? itemStack = selection?.Block?.OnPickBlock(world, selection.Position);
            if (selection?.Block?.CanCarryInSlot(CarrySlot.Hands, itemStack) == false)
            {
                return [];
            }

            handsfreeStacks ??= [new ItemStack(world.GetItem(new AssetLocation("carryon:icon-handsfree")))];

            nohandsfreeStacks ??= [new ItemStack(world.GetItem(new AssetLocation("carryon:icon-nohandsfree")))];

            bool canDoCarryAction = forPlayer?.Entity?.CanDoCarryAction(requireEmptyHanded: true) == true;
            bool isCarryingInHands = forPlayer?.Entity?.GetCarried(CarrySlot.Hands) != null;
            var pickupStacks = canDoCarryAction ? handsfreeStacks : nohandsfreeStacks;

            bool isTargetCarryable = selection?.Block != null && itemStack != null;

            CarryHintType defaultHints = CarryHintType.None;

            if (!isCarryingInHands && TransferBlockCarryAllowed(forPlayer, selection))
            {
                defaultHints |= CarryHintType.BasePickup;
            }

            if (ForcePickupOnSwapBack && isTargetCarryable)
            {
                defaultHints |= CarryHintType.ForcePickup;
            }

            if (defaultHints == CarryHintType.None)
            {
                return [];
            }

            var blockEntity = selection?.Position == null ? null : world.BlockAccessor.GetBlockEntity(selection.Position);

            var hintContext = new CarryHintContext {
                Player = forPlayer,
                Selection = selection,
                BlockEntity = blockEntity,
                SelectionBoxIndex = selection?.SelectionBoxIndex ?? -1,
                IsTargetCarryable = isTargetCarryable,
                IsForcePickupEnabled = ForcePickupOnSwapBack,
                CanDoCarryAction = canDoCarryAction,
                IsCarryingInHands = isCarryingInHands
            };

            var allowedHints = ResolveAllowedHints(hintContext, defaultHints);

            if (allowedHints == CarryHintType.None)
            {
                return [];
            }

            var interactions = new List<WorldInteraction>();

            if ((allowedHints & CarryHintType.BasePickup) != 0)
            {
                interactions.Add(CreateBasePickupInteraction(pickupStacks));
            }

            if ((allowedHints & CarryHintType.ForcePickup) != 0)
            {
                interactions.Add(CreateForcePickupInteraction(pickupStacks));
            }

            return interactions.Count > 0 ? [.. interactions] : [];
        }

        /// <summary>
        /// When the block is removed from the world, any associated dropped block info is also removed to prevent stale data from accumulating.
        /// </summary>
        /// <param name="world"> The world accessor. </param>
        /// <param name="pos"> The position of the block being removed. </param>
        /// <param name="handling"> The handling status of the interaction. </param>
        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            world.GetCarryEvents()?.TriggerBlockRemoved(pos);
            
            base.OnBlockRemoved(world, pos, ref handling);
        }

        /// <summary>
        /// Represents the settings for a carry slot, including animation, walk speed modifier, enabled condition, and excluded types.
        /// </summary>
        public class SlotSettings
        {

            public string? Animation { get; set; }

            public string? AnimationSit { get; set; }

            public string? AnimationCrouch { get; set; }

            public float WalkSpeedModifier { get; set; } = 0.0F;

            public IDictionary<string, float> WalkSpeedModifierByType { get; set; }
                = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            public IDictionary<string, float> WalkSpeedModifierByGroup { get; set; }
                = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            public string? EnabledCondition { get; set; }

            public string?[]? ExcludedTypes { get; set; }
        }

        /// <summary>
        /// Manages the carry slots and their associated settings for a carryable block. It allows for dynamic enabling/disabling of slots based 
        /// on conditions defined in SlotSettings, and provides access to the settings for each slot. The Initialize method populates the slot 
        /// settings from the block's JSON properties, allowing for flexible configuration of carry behavior based on the block's definition.
        /// </summary>
        public class SlotStorage
        {

            public readonly Dictionary<CarrySlot, SlotSettings> SlotSettingsDict = [];
            public void RemoveSlot(CarrySlot slot)
            {
                SlotSettingsDict.Remove(slot);
            }

            public SlotSettings? this[CarrySlot slot]
                => SlotSettingsDict.TryGetValue(slot, out var settings) ? settings : null;

            public int Count => SlotSettingsDict.Count;


            /// <summary>
            /// Initializes the slot settings from the provided JSON properties. It reads the configuration for each carry slot defined in the 
            /// block's JSON and populates the SlotSettingsDict accordingly.
            /// </summary>
            /// <param name="properties">The JSON properties containing the configuration for each carry slot.</param>
            public void Initialize(JsonObject properties)
            {
                SlotSettingsDict.Clear();
                if (properties?.Exists != true)
                {
                    if (!DefaultAnimation.TryGetValue(CarrySlot.Hands, out string? anim)) anim = null;
                    SlotSettingsDict.Add(CarrySlot.Hands, new SlotSettings { Animation = anim });
                }
                else
                {
                    foreach (var slot in Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>())
                    {
                        var slotProperties = properties[slot.ToString()];
                        if (slotProperties?.Exists != true) continue;

                        if (!SlotSettingsDict.TryGetValue(slot, out var settings))
                        {
                            if (!DefaultAnimation.TryGetValue(slot, out string? anim)) anim = null;
                            SlotSettingsDict.Add(slot, settings = new SlotSettings { Animation = anim });
                        }

                        settings.Animation = slotProperties["animation"].AsString(settings.Animation);
                        settings.AnimationSit = slotProperties["animationSit"].AsString(settings.AnimationSit);
                        settings.AnimationCrouch = slotProperties["animationCrouch"].AsString(settings.AnimationCrouch);

                        if (!DefaultWalkSpeed.TryGetValue(slot, out var speed)) speed = 0.0F;
                        settings.WalkSpeedModifier = slotProperties["walkSpeedModifier"].AsFloat(speed);

                        settings.WalkSpeedModifierByType = JsonHelper.ParseFloatMap(slotProperties["walkSpeedModifierByBlockType"]);
                        settings.WalkSpeedModifierByGroup = JsonHelper.ParseFloatMap(slotProperties["walkSpeedModifierByGroup"]);

                        if (JsonHelper.TryGetString(slotProperties, "enabledCondition", out var e)) settings.EnabledCondition = e;

                        if (JsonHelper.TryGetStringArray(slotProperties, "excludedTypes", out var x)) settings.ExcludedTypes = x;

                    }
                }
            }

        }
    }
}

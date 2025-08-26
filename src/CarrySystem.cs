
using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common;
using CarryOn.API.Event;
using CarryOn.Client;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Handlers;
using CarryOn.Common.Network;
using CarryOn.Config;
using CarryOn.Server;
using CarryOn.Utility;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static CarryOn.API.Common.CarryCode;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.21.0")]
[assembly: ModDependency("carryonlib", "1.0.0")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public class CarrySystem : ModSystem
    {
        public static float PlaceSpeedDefault = 0.75f;
        public static float SwapSpeedDefault = 1.5f;
        public static float PickUpSpeedDefault = 0.8f;

        public static float TransferSpeedDefault = 0.8f;

        public static float InteractSpeedDefault = 0.8f;

        public static GlKeys PickupKeyDefault = GlKeys.ShiftLeft;
        public static GlKeys SwapBackModifierDefault = GlKeys.ControlLeft;
        public static GlKeys ToggleDefault = GlKeys.K;


        // Combine with Alt + Ctrl to drop carried block        
        public static GlKeys QuickDropDefault = GlKeys.K;

        // Combine with Ctrl to toggle double tap dismount
        public static GlKeys ToggleDoubleTapDismountDefault = GlKeys.K;

        public static readonly int DoubleTapThresholdMs = 500;

        public ICoreAPI Api { get { return ClientApi ?? ServerApi as ICoreAPI; } }

        // Client
        public ICoreClientAPI ClientApi { get; private set; }
        public IClientNetworkChannel ClientChannel { get; private set; }
        public EntityCarryRenderer EntityCarryRenderer { get; private set; }
        public HudOverlayRenderer HudOverlayRenderer { get; private set; }

        // Server
        public ICoreServerAPI ServerApi { get; private set; }
        public IServerNetworkChannel ServerChannel { get; private set; }
        public DeathHandler DeathHandler { get; private set; }

        // Common
        public CarryHandler CarryHandler { get; private set; }

        public CarryEvents CarryEvents { get; private set; }

        public CarryOnLib.Core CarryOnLib { get; private set; }

        public ICarryManager CarryManager => CarryOnLib?.CarryManager;

        private Harmony harmony;

        public static string GetLang(string key) => Lang.Get(CarryOnCode(key)) ?? key;

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            ModConfig.ReadConfig(api);

            if (ModConfig.HarmonyPatchEnabled)
            {
                try
                {
                    this.harmony = new Harmony("CarryOn");
                    this.harmony.PatchAll();
                    api.World.Logger.Notification("CarryOn: Harmony patches enabled.");
                }
                catch (Exception ex)
                {
                    api.World.Logger.Error($"CarryOn: Exception during Harmony patching: {ex}");
                }
            }
            else
            {
                api.World.Logger.Notification("CarryOn: Harmony patches are disabled by config.");
                // If runtime config changes are supported, call this.harmony.UnpatchAll("CarryOn") here
            }

            api.World.Logger.Event("started 'CarryOn' mod");
        }

        public override void Start(ICoreAPI api)
        {
            // Legacy support for EntityBoatCarryOn - pre.1
            api.RegisterEntity("EntityBoatCarryOn", typeof(EntityBoat));

            api.Register<BlockBehaviorCarryable>();
            api.Register<BlockBehaviorCarryableInteract>();
            api.Register<EntityBehaviorAttachableCarryable>();

            CarryHandler = new CarryHandler(this);
            CarryEvents = new CarryEvents();

            CarryOnLib = api.ModLoader.GetModSystem<CarryOnLib.Core>();
            if (CarryOnLib != null)
            {
                CarryOnLib.CarryManager = new CarryManager(this);
            }
            else
            {
                api.World.Logger.Error("CarryOn: Failed to load CarryOnLib mod system");
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;
            ClientChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>()
                .RegisterMessageType<AttachMessage>()
                .RegisterMessageType<DetachMessage>()
                .RegisterMessageType<PutMessage>()
                .RegisterMessageType<TakeMessage>()
                .RegisterMessageType<QuickDropMessage>()
                .RegisterMessageType<DismountMessage>()
                .RegisterMessageType<PlayerAttributeUpdateMessage>();

            EntityCarryRenderer = new EntityCarryRenderer(api);
            HudOverlayRenderer = new HudOverlayRenderer(api);
            CarryHandler.InitClient(api);
            InitEvents(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerApi = api;
            ServerChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>()
                .RegisterMessageType<AttachMessage>()
                .RegisterMessageType<DetachMessage>()
                .RegisterMessageType<PutMessage>()
                .RegisterMessageType<TakeMessage>()
                .RegisterMessageType<QuickDropMessage>()
                .RegisterMessageType<DismountMessage>()
                .RegisterMessageType<PlayerAttributeUpdateMessage>();

            DeathHandler = new DeathHandler(api);
            CarryHandler.InitServer(api);
            InitEvents(api);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server)
            {
                RemoveDisabledCarryableBehaviours(api);

                ManuallyAddCarryableBehaviors(api);
                ResolveMultipleCarryableBehaviors(api);

                AutoMapSimilarCarryables(api);
                AutoMapSimilarCarryableInteract(api);
                RemoveExcludedCarryableBehaviours(api);
            }

            base.AssetsFinalize(api);
        }

        private void RemoveDisabledCarryableBehaviours(ICoreAPI api)
        {
            // Find all blocks with disabled carryable behaviors
            var blocksWithEnabledKey = api.World.Blocks.Where(b => b.HasBehavior<BlockBehaviorCarryable>());

            IAttribute carryOnConfig, carryables;

            // TODO: Allow checking in different world config locations
            // Current implementation is only looking at carryon.Carryables

            if (!api.World.Config.TryGetAttribute("carryon", out carryOnConfig)) return;

            carryables = carryOnConfig.TryGet("Carryables");
            if (carryables == null)
            {
                api.Logger.Warning("CarryOn: Cannot find carryon.Carryables in world config");
                return;
            }

            if (carryables is not TreeAttribute carryablesTree)
            {
                api.Logger.Warning("CarryOn: carryon.Carryables is not a TreeAttribute");
                return;
            }

            foreach (var block in blocksWithEnabledKey)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryable>();

                if (string.IsNullOrWhiteSpace(behavior.EnabledConditionKey)) continue;

                var isEnabled = carryablesTree.TryGetBool(behavior.EnabledConditionKey);
                if (!isEnabled.HasValue)
                {
                    api.Logger.Warning($"CarryOn: {behavior.EnabledConditionKey} is not a boolean");
                }

                if (isEnabled.Value) continue;

                block.BlockBehaviors = RemoveCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray()).OfType<BlockBehavior>().ToArray();
                block.CollectibleBehaviors = RemoveCarryableBehaviours(block.CollectibleBehaviors);
            }
        }

        public override void Dispose()
        {
            if (this.harmony != null)
            {
                this.harmony.UnpatchAll("CarryOn");
                this.harmony = null;
            }

            if (ClientApi != null)
            {
                EntityCarryRenderer?.Dispose();
                HudOverlayRenderer?.Dispose();

                CarryHandler?.Dispose();
            }
            base.Dispose();
        }

        // Helper to create, initialize, and append BlockBehaviorCarryable to a collection
        private void AddCarryableBehavior(Block block, ref BlockBehavior[] blockBehaviors, ref CollectibleBehavior[] collectibleBehaviors, JsonObject properties)
        {
            var blockBehavior = new BlockBehaviorCarryable(block);
            blockBehaviors = blockBehaviors.Append(blockBehavior);
            blockBehavior.Initialize(properties);

            collectibleBehaviors = collectibleBehaviors.Append(blockBehavior);
        }
        private void ManuallyAddCarryableBehaviors(ICoreAPI api)
        {
            try
            {
                if (ModConfig.HenboxEnabled)
                {
                    var block = api.World.BlockAccessor.GetBlock("henbox");
                    if (block != null)
                    {
                        // Only allow default hand slot 
                        var properties = JsonObject.FromJson("{slots:{Hands:{}}}");
                        AddCarryableBehavior(block, ref block.BlockBehaviors, ref block.CollectibleBehaviors, properties);
                    }
                }
            }
            catch (Exception e)
            {
                api.Logger.Error($"Error in ManuallyAddCarryableBehaviors: {e.Message}");
            }
        }

        private void RemoveExcludedCarryableBehaviours(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.DebuggingOptions.LoggingEnabled;
            var filters = ModConfig.ServerConfig.CarryablesFilters;

            var removeArray = filters.RemoveCarryableBehaviour;
            if (removeArray == null || removeArray.Length == 0)
            {
                return;
            }

            foreach (var block in api.World.Blocks.Where(b => b.Code != null))
            {
                foreach (var remove in removeArray)
                {
                    if (block.Code.ToString().StartsWith(remove))
                    {
                        var count = block.BlockBehaviors.Length;
                        block.BlockBehaviors = RemoveCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray()).OfType<BlockBehavior>().ToArray();
                        block.CollectibleBehaviors = RemoveCarryableBehaviours(block.CollectibleBehaviors);

                        if (count != block.BlockBehaviors.Length && loggingEnabled)
                        {
                            api.Logger.Debug($"CarryOn Removed Carryable Behaviour: {block.Code}");
                        }
                    }
                }
            }
        }

        private void ResolveMultipleCarryableBehaviors(ICoreAPI api)
        {
            var filters = ModConfig.ServerConfig.CarryablesFilters;

            foreach (var block in api.World.Blocks)
            {
                bool removeBaseBehavior = false;
                if (block.Code == null || block.Id == 0) continue;
                foreach (var match in filters.RemoveBaseCarryableBehaviour)
                {
                    if (block.Code.ToString().StartsWith(match))
                    {
                        removeBaseBehavior = true;
                        break;
                    }
                }
                block.BlockBehaviors = RemoveOverriddenCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray(), removeBaseBehavior).OfType<BlockBehavior>().ToArray();
                block.CollectibleBehaviors = RemoveOverriddenCarryableBehaviours(block.CollectibleBehaviors, removeBaseBehavior);
            }
        }

        private CollectibleBehavior[] RemoveOverriddenCarryableBehaviours(CollectibleBehavior[] behaviours, bool removeBaseBehavior = false)
        {
            var behaviourList = behaviours.ToList();
            var carryableList = FindCarryables(behaviourList);
            if (carryableList.Count > 1)
            {
                var priorityCarryable = carryableList.First(p => p.PatchPriority == carryableList.Max(m => m.PatchPriority));
                if (priorityCarryable != null)
                {
                    if (!(removeBaseBehavior && priorityCarryable.PatchPriority == 0))
                    {
                        carryableList.Remove(priorityCarryable);
                    }
                    behaviourList.RemoveAll(r => carryableList.Contains(r));
                }
            }
            else if (removeBaseBehavior && carryableList.Count == 1 && carryableList[0].PatchPriority == 0)
            {
                // Remove base behavior
                behaviourList.RemoveAll(r => carryableList.Contains(r));
            }
            return behaviourList.ToArray();
        }

        private CollectibleBehavior[] RemoveCarryableBehaviours(CollectibleBehavior[] behaviours)
        {
            var behaviourList = behaviours.ToList();
            var carryableList = FindCarryables(behaviourList);

            if (carryableList.Count == 0) return behaviours;

            behaviourList.RemoveAll(r => carryableList.Contains(r));

            return behaviourList.ToArray();
        }

        private List<BlockBehaviorCarryable> FindCarryables<T>(List<T> behaviors)
        {
            var carryables = new List<BlockBehaviorCarryable>();
            foreach (var behavior in behaviors)
            {
                if (behavior is BlockBehaviorCarryable carryable)
                {
                    carryables.Add(carryable);
                }
            }
            return carryables;
        }

        private void AutoMapSimilarCarryableInteract(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.DebuggingOptions.LoggingEnabled;
            var filters = ModConfig.ServerConfig.CarryablesFilters;

            if (!filters.AutoMapSimilar) return;

            var matchKeys = new List<string>();
            foreach (var interactBlock in api.World.Blocks.Where(b => b.IsCarryableInteract()))
            {
                if (interactBlock.EntityClass == null || interactBlock.EntityClass == "Generic") continue;

                if (!matchKeys.Contains(interactBlock.EntityClass))
                {
                    matchKeys.Add(interactBlock.EntityClass);
                }
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryableInteract()
                && matchKeys.Contains(w.EntityClass)
                && !filters.AutoMatchIgnoreMods.Contains(w?.Code?.Domain)))
            {
                block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorCarryableInteract(block));
                block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorCarryableInteract(block));
                if (loggingEnabled) api.Logger.Debug($"CarryOn AutoMatch Interact: {block.Code} key: {block.EntityClass}");
            }
        }

        private void AutoMapSimilarCarryables(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.DebuggingOptions.LoggingEnabled;

            var filters = ModConfig.ServerConfig.CarryablesFilters;

            if (!filters.AutoMapSimilar) return;

            var matchBehaviors = new Dictionary<string, BlockBehaviorCarryable>();
            foreach (var carryableBlock in api.World.Blocks.Where(b => b.IsCarryable() && b.Code.Domain == "game"))
            {
                var shapePath = carryableBlock?.ShapeInventory?.Base?.Path ?? carryableBlock?.Shape?.Base?.Path;
                var shapeKey = shapePath != null && shapePath != "block/basic/cube" ? $"Shape:{shapePath}" : null;

                string entityClassKey = null;

                if (carryableBlock.EntityClass != null && carryableBlock.EntityClass != "Generic" && carryableBlock.EntityClass != "Transient")
                {
                    entityClassKey = $"EntityClass:{carryableBlock.EntityClass}";
                    if (!matchBehaviors.ContainsKey(entityClassKey))
                    {
                        matchBehaviors[entityClassKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {entityClassKey} carryableBlock: {carryableBlock.Code}");
                    }
                }

                string classKey = null;
                if (carryableBlock.Class != "Block")
                {
                    classKey = $"Class:{carryableBlock.Class}";
                    if (!matchBehaviors.ContainsKey(classKey))
                    {
                        matchBehaviors[classKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {classKey} carryableBlock: {carryableBlock.Code}");
                    }
                }

                if (shapeKey != null)
                {
                    if (entityClassKey != null)
                    {
                        var key = $"{entityClassKey}|{shapeKey}";
                        if (!matchBehaviors.ContainsKey(key))
                        {
                            matchBehaviors[key] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                            if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {key} carryableBlock: {carryableBlock.Code}");
                        }
                    }

                    if (classKey != null)
                    {
                        var key = $"{classKey}|{shapeKey}";
                        if (!matchBehaviors.ContainsKey(key))
                        {
                            matchBehaviors[key] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                            if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {key} carryableBlock: {carryableBlock.Code}");
                        }
                    }

                    if (filters.AllowedShapeOnlyMatches.Contains(shapePath) && !matchBehaviors.ContainsKey(shapeKey))
                    {
                        matchBehaviors[shapeKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();

                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {shapeKey} carryableBlock: {carryableBlock.Code}");
                    }
                }
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryable() && !filters.AutoMatchIgnoreMods.Contains(w?.Code?.Domain)))
            {
                if (block.EntityClass == null) continue;
                string key = null;

                var classKey = $"Class:{block.Class}";
                var entityClassKey = $"EntityClass:{block.EntityClass}";
                var shapePath = block?.ShapeInventory?.Base?.Path ?? block?.Shape?.Base?.Path;
                var shapeKey = shapePath != null ? $"Shape:{shapePath}" : null;

                var matchKeys = new List<string>
                {
                    $"{classKey}|{shapeKey}",
                    $"{entityClassKey}|{shapeKey}",
                    shapeKey,
                    classKey
                };

                foreach (var matchKey in matchKeys)
                {
                    if (matchBehaviors.ContainsKey(matchKey))
                    {
                        key = matchKey;
                        if (loggingEnabled) api.Logger.Debug($"CarryOn AutoMatch: {block.Code} key: {key}");
                        break;
                    }
                }

                if (key != null)
                {
                    var behavior = matchBehaviors[key];

                    var newBehavior = new BlockBehaviorCarryable(block);
                    block.BlockBehaviors = block.BlockBehaviors.Append(newBehavior);
                    newBehavior.Initialize(behavior.Properties);

                    newBehavior = new BlockBehaviorCarryable(block);
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(newBehavior);
                    newBehavior.Initialize(behavior.Properties);
                }
            }
        }



        // TODO: Consider renaming since it also contains TransferHandlerType init 
        private void InitEvents(ICoreAPI api)
        {
            var ignoreMods = new[] { "game", "creative", "survival" };

            var assemblies = api.ModLoader.Mods.Where(m => !ignoreMods.Contains(m.Info.ModID))
                                               .Select(s => s.Systems)
                                               .SelectMany(o => o.ToArray())
                                               .Select(t => t.GetType().Assembly)
                                               .Distinct();

            foreach (var assembly in assemblies)
            {
                // Initialise all ICarryEvent 
                foreach (Type type in assembly.GetTypes().Where(t => t.GetInterfaces().Contains(typeof(ICarryEvent))))
                {
                    try
                    {
                        (Activator.CreateInstance(type) as ICarryEvent)?.Init(CarryOnLib.CarryManager);
                    }
                    catch (Exception e)
                    {
                        api.Logger.Error(e.Message);
                    }
                }
            }
        }

    }
}

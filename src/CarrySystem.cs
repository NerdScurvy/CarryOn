using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common;
using CarryOn.API.Event;
using CarryOn.Client;
using CarryOn.Common;
using CarryOn.Common.Network;
using CarryOn.Server;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

[assembly: ModInfo("Carry On", 
    modID: "carryon",
    Version = "1.8.0-rc.3",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.20.0")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public class CarrySystem : ModSystem
    {
        public static string ModId = "carryon";
        public static float PlaceSpeedDefault = 0.75f;
        public static float SwapSpeedDefault = 1.5f;
        public static float PickUpSpeedDefault = 0.8f;
        public static float InteractSpeedDefault = 0.8f;

        public static string PickupKeyCode = "carryonpickupkey";
        public static GlKeys PickupKeyDefault = GlKeys.ShiftLeft;
        public static string SwapBackModifierKeyCode = "carryonswapbackmodifierkey";
        public static GlKeys SwapBackModifierDefault = GlKeys.ControlLeft;
        public static string ToggleKeyCode = "carryontogglekey";        
        public static GlKeys ToggleDefault = GlKeys.K;

        public ICoreAPI Api { get { return ClientAPI ?? ServerAPI as ICoreAPI; } }

        // Client
        public ICoreClientAPI ClientAPI { get; private set; }
        public IClientNetworkChannel ClientChannel { get; private set; }
        public EntityCarryRenderer EntityCarryRenderer { get; private set; }
        public HudOverlayRenderer HudOverlayRenderer { get; private set; }

        // Server
        public ICoreServerAPI ServerAPI { get; private set; }
        public IServerNetworkChannel ServerChannel { get; private set; }
        public DeathHandler DeathHandler { get; private set; }

        // Common
        public CarryHandler CarryHandler { get; private set; }

        public CarryEvents CarryEvents { get; private set; }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            ModConfig.ReadConfig(api);
            api.World.Logger.Event("started 'CarryOn' mod");
        }

        public override void Start(ICoreAPI api)
        {
            api.Register<BlockBehaviorCarryable>( );
            api.Register<BlockBehaviorCarryableInteract>();

            CarryHandler = new CarryHandler(this);
            CarryEvents = new CarryEvents();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientAPI = api;
            ClientChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>();

            EntityCarryRenderer = new EntityCarryRenderer(api);
            HudOverlayRenderer = new HudOverlayRenderer(api);
            CarryHandler.InitClient();
            InitEvents();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerAPI = api;
            ServerChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>();

            DeathHandler = new DeathHandler(api);
            CarryHandler.InitServer();
            InitEvents();
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server){
                ResolveMultipleCarryableBehaviors(api);
                AutoMapSimilarCarryables(api);
                AutoMapSimilarCarryableInteract(api);
                RemoveExcludedCarryableBehaviours(api);
            }

            base.AssetsFinalize(api);
        }

        private void RemoveExcludedCarryableBehaviours(ICoreAPI api){
            var loggingEnabled = ModConfig.ServerConfig.LoggingEnabled;
            var removeArray = ModConfig.ServerConfig.RemoveCarryableBehaviour;
            if(removeArray == null || removeArray.Length == 0){
                return;
            }

            foreach (var block in api.World.Blocks.Where(b => b.Code != null))
            {
                foreach(var remove in removeArray){
                    if(block.Code.ToString().StartsWith(remove)){
                        var count = block.BlockBehaviors.Length;
                        block.BlockBehaviors = RemoveCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray()).OfType<BlockBehavior>().ToArray();
                        block.CollectibleBehaviors = RemoveCarryableBehaviours(block.CollectibleBehaviors);

                        if(count != block.BlockBehaviors.Length && loggingEnabled){
                            api.Logger.Debug($"CarryOn Removed Carryable Behaviour: {block.Code}");
                        }
                    }
                }
            }
        }

        private void ResolveMultipleCarryableBehaviors(ICoreAPI api)
        {
            foreach (var block in api.World.Blocks)
            {
                bool removeBaseBehavior = false;
                if (block.Code == null || block.Id == 0) continue;
                foreach(var match in ModConfig.ServerConfig.RemoveBaseCarryableBehaviour){
                    if(block.Code.ToString().StartsWith(match)){
                        removeBaseBehavior = true;
                        break;
                    }
                }
                block.BlockBehaviors = RemoveOveriddenCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray(), removeBaseBehavior).OfType<BlockBehavior>().ToArray();
                block.CollectibleBehaviors = RemoveOveriddenCarryableBehaviours(block.CollectibleBehaviors, removeBaseBehavior);
            }
        }

        private CollectibleBehavior[] RemoveOveriddenCarryableBehaviours(CollectibleBehavior[] behaviours, bool removeBaseBehavior = false)
        {
            var behaviourList = behaviours.ToList();
            var carryableList = FindCarryables(behaviourList);
            if (carryableList.Count > 1)
            {
                var priorityCarryable = carryableList.First(p => p.PatchPriority == carryableList.Max(m => m.PatchPriority));
                if (priorityCarryable != null)
                {
                    if (!(removeBaseBehavior && priorityCarryable.PatchPriority == 0)){
                        carryableList.Remove(priorityCarryable);
                    }
                    behaviourList.RemoveAll(r => carryableList.Contains(r));
                }
            }else if(removeBaseBehavior && carryableList.Count == 1 && carryableList[0].PatchPriority == 0){
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

        private List<BlockBehaviorCarryable> FindCarryables<T>(List<T> behaviors){
            var carryables = new List<BlockBehaviorCarryable>();
            foreach(var behavior in behaviors){
                if(behavior is BlockBehaviorCarryable carryable){
                    carryables.Add(carryable);
                }
            }
            return carryables;
        }

        private void AutoMapSimilarCarryableInteract(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.LoggingEnabled;

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
                && !ModConfig.ServerConfig.AutoMatchIgnoreMods.Contains(w?.Code?.Domain)))
            {
                block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorCarryableInteract(block));
                block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorCarryableInteract(block));
                if (loggingEnabled) api.Logger.Debug($"CarryOn AutoMatch Interact: {block.Code} key: {block.EntityClass}");
            }
        }

        private void AutoMapSimilarCarryables(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.LoggingEnabled;

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

                    if (ModConfig.ServerConfig.AllowedShapeOnlyMatches.Contains(shapePath) && !matchBehaviors.ContainsKey(shapeKey))
                    {
                        matchBehaviors[shapeKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();

                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {shapeKey} carryableBlock: {carryableBlock.Code}");
                    }
                }
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryable() && !ModConfig.ServerConfig.AutoMatchIgnoreMods.Contains(w?.Code?.Domain)))
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

        private void InitEvents()
        {
            var ignoreMods = new[] { "game", "creative", "survival" };

            var assemblies = Api.ModLoader.Mods.Where(m => !ignoreMods.Contains(m.Info.ModID))
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
                        (Activator.CreateInstance(type) as ICarryEvent)?.Init(this);
                    }
                    catch (Exception e)
                    {
                        Api.Logger.Error(e.Message);
                    }
                }
            }
        }
    }
}

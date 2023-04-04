using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CarryOn.API.Common;
using CarryOn.API.Event;
using CarryOn.Client;
using CarryOn.Common;
using CarryOn.Common.Network;
using CarryOn.Server;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

[assembly: ModInfo("Carry On",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.17.0")]

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
        public override bool AllowRuntimeReload => true;

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
            base.AssetsFinalize(api);
            if (api.Side != EnumAppSide.Server) return;

            ResolveMultipleCarryableBehaviors(api);
            AutoMapSimilarCarryables(api);
            AutoMapSimilarCarryableInteract(api);
        }

        private void ResolveMultipleCarryableBehaviors(ICoreAPI api)
        {
            foreach (var block in api.World.Blocks)
            {
                if (block.Code == null || block.Id == 0) continue;

                block.BlockBehaviors = RemoveOveriddenCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray()).OfType<BlockBehavior>().ToArray();
                block.CollectibleBehaviors = RemoveOveriddenCarryableBehaviours(block.CollectibleBehaviors);
            }
        }

        private CollectibleBehavior[] RemoveOveriddenCarryableBehaviours(CollectibleBehavior[] behaviours)
        {
            var behaviourList = behaviours.ToList();
            var carryableList = FindCarryables(behaviourList);
            if (carryableList.Count > 1)
            {
                var priorityCarryable = carryableList.First(p => p.PatchPriority == carryableList.Max(m => m.PatchPriority));
                if (priorityCarryable != null)
                {
                    carryableList.Remove(priorityCarryable);
                    behaviourList.RemoveAll(r => carryableList.Contains(r));
                }
            }
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
            foreach (var carryableBlock in api.World.Blocks.Where(b => b.IsCarryable()))
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
                    }
                }

                string classKey = null;
                if (carryableBlock.Class != "Block")
                {
                    classKey = $"Class:{carryableBlock.Class}";
                    if (!matchBehaviors.ContainsKey(classKey))
                    {
                        matchBehaviors[classKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
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
                        }
                    }

                    if (classKey != null)
                    {
                        var key = $"{classKey}|{shapeKey}";
                        if (!matchBehaviors.ContainsKey(key))
                        {
                            matchBehaviors[key] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                        }
                    }

                    if (ModConfig.ServerConfig.AllowedShapeOnlyMatches.Contains(shapePath) && !matchBehaviors.ContainsKey(shapeKey))
                    {
                        matchBehaviors[shapeKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
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
                    classKey,
                    entityClassKey
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

                    if (block.Code.Path.Contains("chest"))
                    {
                        var test = block;
                    }
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

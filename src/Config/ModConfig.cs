using System;
using Vintagestory.API.Common;
using static CarryOn.API.Common.CarryCode;

namespace CarryOn.Config
{
    static class ModConfig
    {

        public static CarryOnConfig ServerConfig { get; private set; }
        public static IWorldAccessor World { get; private set; }

        private static readonly string allowSprintKey = CarryOnCode("AllowSprintWhileCarrying");
        private static readonly string ignoreSpeedPenaltyKey = CarryOnCode("IgnoreCarrySpeedPenalty");
        private static readonly string removeInteractDelayKey = CarryOnCode("RemoveInteractDelayWhileCarrying");
        private static readonly string interactSpeedMultiplierKey = CarryOnCode("InteractSpeedMultiplier");
        private static readonly string harmonyPatchEnabledKey = CarryOnCode("HarmonyPatchEnabled");
        private static readonly string backSlotEnabledKey = CarryOnCode("BackSlotEnabled");
        private static readonly string henboxEnabledKey = CarryOnCode("HenboxEnabled");

        public static bool AllowSprintWhileCarrying
        {
            get
            {
                return World?.Config?.GetBool(allowSprintKey, false) ?? false;
            }
            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set AllowSprintWhileCarrying.");
                World.Config.SetBool(allowSprintKey, value);
            }
        }
        public static bool IgnoreCarrySpeedPenalty
        {
            get
            {
                return World?.Config?.GetBool(ignoreSpeedPenaltyKey, false) ?? false;
            }
            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set IgnoreCarrySpeedPenalty.");
                World.Config.SetBool(ignoreSpeedPenaltyKey, value);
            }
        }
        public static bool RemoveInteractDelayWhileCarrying
        {
            get
            {
                return World?.Config?.GetBool(removeInteractDelayKey, false) ?? false;
            }
            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set RemoveInteractDelayWhileCarrying.");
                World.Config.SetBool(removeInteractDelayKey, value);
            }
        }
        public static float InteractSpeedMultiplier
        {
            get
            {
                return World?.Config?.GetFloat(interactSpeedMultiplierKey, 1f) ?? 1f;
            }

            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set InteractSpeedMultiplier.");

                if (value < 0.01f) value = 0.01f;
                else if (value > 20f) value = 20f;
                World.Config.SetFloat(interactSpeedMultiplierKey, value);
            }
        }
        public static bool HarmonyPatchEnabled
        {
            get
            {
                return World?.Config?.GetBool(harmonyPatchEnabledKey, true) ?? true;
            }
            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set HarmonyPatchEnabled.");
                World.Config.SetBool(harmonyPatchEnabledKey, value);
            }
        }
        public static bool BackSlotEnabled
        {
            get
            {
                return World?.Config?.GetBool(backSlotEnabledKey, true) ?? true;
            }
            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set BackSlotEnabled.");
                World.Config.SetBool(backSlotEnabledKey, value);
            }
        }

        public static bool HenboxEnabled
        {
            get
            {
                return World?.Config?.GetBool(henboxEnabledKey, true) ?? true;
            }
            set
            {
                if (World?.Config == null)
                    throw new InvalidOperationException("World or World.Config is null. Cannot set HenboxEnabled.");
                World.Config.SetBool(henboxEnabledKey, value);
            }
        }

        private const string ConfigFile = "CarryOnConfig.json";

        public static void ReadConfig(ICoreAPI api)
        {
            World = api.World;
            if (api.Side == EnumAppSide.Server)
            {
                try
                {
                    ServerConfig = LoadConfig(api);

                    if (ServerConfig == null)
                    {
                        // Save default config
                        StoreConfig(api);
                        ServerConfig = LoadConfig(api);
                    }
                    else
                    {
                        StoreConfig(api, ServerConfig);
                    }
                }
                catch
                {
                    // Save default config
                    StoreConfig(api);
                    ServerConfig = LoadConfig(api);
                }

                var worldConfig = api?.World?.Config;

                if (worldConfig == null)
                {
                    api.Logger.Error("CarryOn: Unable to access world config. CarryOn features may not work correctly.");
                    return;
                }

                if (ServerConfig == null)
                {
                    api.Logger.Error("CarryOn: ServerConfig did not load correctly. CarryOn features may not work correctly.");
                    return;
                }

                // Sections below save the value to the world config so it is available for both server and client

                // Carryables
                worldConfig.SetBool(CarryOnCode("AnvilEnabled"), ServerConfig.Carryables.Anvil);
                worldConfig.SetBool(CarryOnCode("BarrelEnabled"), ServerConfig.Carryables.Barrel);
                worldConfig.SetBool(CarryOnCode("BookshelfEnabled"), ServerConfig.Carryables.Bookshelf);
                worldConfig.SetBool(CarryOnCode("BunchOCandlesEnabled"), ServerConfig.Carryables.BunchOCandles);
                worldConfig.SetBool(CarryOnCode("ChandelierEnabled"), ServerConfig.Carryables.Chandelier);
                worldConfig.SetBool(CarryOnCode("ChestLabeledEnabled"), ServerConfig.Carryables.ChestLabeled);
                worldConfig.SetBool(CarryOnCode("ChestTrunkEnabled"), ServerConfig.Carryables.ChestTrunk);
                worldConfig.SetBool(CarryOnCode("ChestEnabled"), ServerConfig.Carryables.Chest);
                worldConfig.SetBool(CarryOnCode("ClutterEnabled"), ServerConfig.Carryables.Clutter);
                worldConfig.SetBool(CarryOnCode("CrateEnabled"), ServerConfig.Carryables.Crate);
                worldConfig.SetBool(CarryOnCode("DisplayCaseEnabled"), ServerConfig.Carryables.DisplayCase);
                worldConfig.SetBool(CarryOnCode("FlowerpotEnabled"), ServerConfig.Carryables.Flowerpot);
                worldConfig.SetBool(CarryOnCode("ForgeEnabled"), ServerConfig.Carryables.Forge);
                worldConfig.SetBool(CarryOnCode("LogWithResinEnabled"), ServerConfig.Carryables.LogWithResin);
                worldConfig.SetBool(CarryOnCode("MoldRackEnabled"), ServerConfig.Carryables.MoldRack);
                worldConfig.SetBool(CarryOnCode("MoldsEnabled"), ServerConfig.Carryables.Molds);
                worldConfig.SetBool(CarryOnCode("LootVesselEnabled"), ServerConfig.Carryables.LootVessel);
                worldConfig.SetBool(CarryOnCode("OvenEnabled"), ServerConfig.Carryables.Oven);
                worldConfig.SetBool(CarryOnCode("PlanterEnabled"), ServerConfig.Carryables.Planter);
                worldConfig.SetBool(CarryOnCode("QuernEnabled"), ServerConfig.Carryables.Quern);
                worldConfig.SetBool(CarryOnCode("ReedBasketEnabled"), ServerConfig.Carryables.ReedBasket);
                worldConfig.SetBool(CarryOnCode("ResonatorEnabled"), ServerConfig.Carryables.Resonator);
                worldConfig.SetBool(CarryOnCode("ShelfEnabled"), ServerConfig.Carryables.Shelf);
                worldConfig.SetBool(CarryOnCode("SignEnabled"), ServerConfig.Carryables.Sign);
                worldConfig.SetBool(CarryOnCode("StorageVesselEnabled"), ServerConfig.Carryables.StorageVessel);
                worldConfig.SetBool(CarryOnCode("ToolRackEnabled"), ServerConfig.Carryables.ToolRack);
                worldConfig.SetBool(CarryOnCode("TorchHolderEnabled"), ServerConfig.Carryables.TorchHolder);

                worldConfig.SetBool(CarryOnCode("BookshelfAndClutterEnabled"), ServerConfig.Carryables.Bookshelf && ServerConfig.Carryables.Clutter);
                HenboxEnabled = ServerConfig.Carryables.Henbox;


                // Interactables
                worldConfig.SetBool(CarryOnCode("InteractDoorEnabled"), ServerConfig.Interactables.Door);
                worldConfig.SetBool(CarryOnCode("InteractBarrelEnabled"), ServerConfig.Interactables.Barrel);
                worldConfig.SetBool(CarryOnCode("InteractStorageEnabled"), ServerConfig.Interactables.Storage);

                // Transferables
                worldConfig.SetBool(CarryOnCode("MoldRackTransferEnabled"), ServerConfig.Transferables.MoldRack);

                // CarryOptions
                worldConfig.SetBool(CarryOnCode("AllowChestTrunksOnBack"), ServerConfig.CarryOptions.AllowChestTrunksOnBack);
                worldConfig.SetBool(CarryOnCode("AllowLargeChestsOnBack"), ServerConfig.CarryOptions.AllowLargeChestsOnBack);
                worldConfig.SetBool(CarryOnCode("AllowCratesOnBack"), ServerConfig.CarryOptions.AllowCratesOnBack);


                AllowSprintWhileCarrying = ServerConfig.CarryOptions.AllowSprintWhileCarrying;
                IgnoreCarrySpeedPenalty = ServerConfig.CarryOptions.IgnoreCarrySpeedPenalty;
                BackSlotEnabled = ServerConfig.CarryOptions.BackSlotEnabled;
                InteractSpeedMultiplier = ServerConfig.CarryOptions.InteractSpeedMultiplier;
                RemoveInteractDelayWhileCarrying = ServerConfig.CarryOptions.RemoveInteractDelayWhileCarrying;

                // Debugging Options
                HarmonyPatchEnabled = !ServerConfig.DebuggingOptions.DisableHarmonyPatch;

            }
        }

        private static CarryOnConfig LoadConfig(ICoreAPI api)
        {
            // Check version of config
            var version = api.LoadModConfig<CarryOnConfigVersion>(ConfigFile);
            if (version != null)
            {
                if (version.ConfigVersion == null)
                {
                    // No versioning information present so treat as legacy config
                    var legacyConfig = api.LoadModConfig<CarryOnConfigLegacy>(ConfigFile);
                    if (legacyConfig != null)
                    {
                        // Save backup of legacy config
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        api.Logger?.Debug($"Saving backup of {ConfigFile} to {ConfigFile}-{timestamp}.bak");
                        api.StoreModConfig(legacyConfig, $"{ConfigFile}-{timestamp}.bak");

                        // Convert legacy config to new format and save
                        api.Logger?.Debug($"Converting legacy config to newer format");
                        var newConfig = legacyConfig.Convert();
                        api.StoreModConfig(newConfig, ConfigFile);
                    }
                }

            }
            // Load the actual CarryOnConfig
            return api.LoadModConfig<CarryOnConfig>(ConfigFile);
        }

        private static void StoreConfig(ICoreAPI api)
        {
            api.StoreModConfig(new CarryOnConfig(), ConfigFile);
        }

        private static void StoreConfig(ICoreAPI api, CarryOnConfig previousConfig)
        {
            api.StoreModConfig(new CarryOnConfig(previousConfig), ConfigFile);
        }

            return source?.ToArray() ?? [];
        }        

    }
}
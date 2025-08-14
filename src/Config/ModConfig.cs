using System;
using Vintagestory.API.Common;
using static CarryOn.CarrySystem;

namespace CarryOn.Config
{
    static class ModConfig
    {

        public static CarryOnConfig ServerConfig { get; private set; }
        public static IWorldAccessor World { get; private set; }

        private static string GetConfigKey(string key) => $"{ModId}:{key}";

        private static readonly string allowSprintKey = GetConfigKey("AllowSprintWhileCarrying");
        private static readonly string ignoreSpeedPenaltyKey = GetConfigKey("IgnoreCarrySpeedPenalty");
        private static readonly string removeInteractDelayKey = GetConfigKey("RemoveInteractDelayWhileCarrying");
        private static readonly string interactSpeedMultiplierKey = GetConfigKey("InteractSpeedMultiplier");
        private static readonly string harmonyPatchEnabledKey = GetConfigKey("HarmonyPatchEnabled");
        private static readonly string backSlotEnabledKey = GetConfigKey("BackSlotEnabled");
        private static readonly string henboxEnabledKey = GetConfigKey("HenboxEnabled");

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
                World?.Config?.SetFloat(interactSpeedMultiplierKey, value);
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

                // Sections below save the value to the world config so it is available for both server and client

                // Carryables
                worldConfig.SetBool(GetConfigKey("AnvilEnabled"), ServerConfig.Carryables.Anvil);
                worldConfig.SetBool(GetConfigKey("BarrelEnabled"), ServerConfig.Carryables.Barrel);
                worldConfig.SetBool(GetConfigKey("BookshelfEnabled"), ServerConfig.Carryables.Bookshelf);
                worldConfig.SetBool(GetConfigKey("BunchOCandlesEnabled"), ServerConfig.Carryables.BunchOCandles);
                worldConfig.SetBool(GetConfigKey("ChandelierEnabled"), ServerConfig.Carryables.Chandelier);
                worldConfig.SetBool(GetConfigKey("ChestLabeledEnabled"), ServerConfig.Carryables.ChestLabeled);
                worldConfig.SetBool(GetConfigKey("ChestTrunkEnabled"), ServerConfig.Carryables.ChestTrunk);
                worldConfig.SetBool(GetConfigKey("ChestEnabled"), ServerConfig.Carryables.Chest);
                worldConfig.SetBool(GetConfigKey("ClutterEnabled"), ServerConfig.Carryables.Clutter);
                worldConfig.SetBool(GetConfigKey("CrateEnabled"), ServerConfig.Carryables.Crate);
                worldConfig.SetBool(GetConfigKey("DisplayCaseEnabled"), ServerConfig.Carryables.DisplayCase);
                worldConfig.SetBool(GetConfigKey("FlowerpotEnabled"), ServerConfig.Carryables.Flowerpot);
                worldConfig.SetBool(GetConfigKey("ForgeEnabled"), ServerConfig.Carryables.Forge);
                worldConfig.SetBool(GetConfigKey("LogWithResinEnabled"), ServerConfig.Carryables.LogWithResin);
                worldConfig.SetBool(GetConfigKey("MoldRackEnabled"), ServerConfig.Carryables.MoldRack);
                worldConfig.SetBool(GetConfigKey("MoldsEnabled"), ServerConfig.Carryables.Molds);
                worldConfig.SetBool(GetConfigKey("LootVesselEnabled"), ServerConfig.Carryables.LootVessel);
                worldConfig.SetBool(GetConfigKey("OvenEnabled"), ServerConfig.Carryables.Oven);
                worldConfig.SetBool(GetConfigKey("PlanterEnabled"), ServerConfig.Carryables.Planter);
                worldConfig.SetBool(GetConfigKey("QuernEnabled"), ServerConfig.Carryables.Quern);
                worldConfig.SetBool(GetConfigKey("ReedBasketEnabled"), ServerConfig.Carryables.ReedBasket);
                worldConfig.SetBool(GetConfigKey("ResonatorEnabled"), ServerConfig.Carryables.Resonator);
                worldConfig.SetBool(GetConfigKey("ShelfEnabled"), ServerConfig.Carryables.Shelf);
                worldConfig.SetBool(GetConfigKey("SignEnabled"), ServerConfig.Carryables.Sign);
                worldConfig.SetBool(GetConfigKey("StorageVesselEnabled"), ServerConfig.Carryables.StorageVessel);
                worldConfig.SetBool(GetConfigKey("ToolRackEnabled"), ServerConfig.Carryables.ToolRack);
                worldConfig.SetBool(GetConfigKey("TorchHolderEnabled"), ServerConfig.Carryables.TorchHolder);

                worldConfig.SetBool(GetConfigKey("BookshelfAndClutterEnabled"), ServerConfig.Carryables.Bookshelf && ServerConfig.Carryables.Clutter);
                HenboxEnabled = ServerConfig.Carryables.Henbox;


                // Interactables
                worldConfig.SetBool(GetConfigKey("InteractDoorEnabled"), ServerConfig.Interactables.Door);
                worldConfig.SetBool(GetConfigKey("InteractBarrelEnabled"), ServerConfig.Interactables.Barrel);
                worldConfig.SetBool(GetConfigKey("InteractStorageEnabled"), ServerConfig.Interactables.Storage);

                // CarryOptions
                worldConfig.SetBool(GetConfigKey("AllowChestTrunksOnBack"), ServerConfig.CarryOptions.AllowChestTrunksOnBack);
                worldConfig.SetBool(GetConfigKey("AllowLargeChestsOnBack"), ServerConfig.CarryOptions.AllowLargeChestsOnBack);
                worldConfig.SetBool(GetConfigKey("AllowCratesOnBack"), ServerConfig.CarryOptions.AllowCratesOnBack);

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

    }
}
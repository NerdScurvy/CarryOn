using System;
using Vintagestory.API.Common;
using static CarryOn.CarrySystem;

namespace CarryOn
{
    static class ModConfig
    {
        public static CarryOnClientConfig ClientConfig;
        public static CarryOnConfig ServerConfig;

        public static IWorldAccessor World;

        static private readonly string allowSprintKey = ModId + ":AllowSprintWhileCarrying";
        static private readonly string ignoreSpeedPenaltyKey = ModId + ":IgnoreCarrySpeedPenalty";
        static private readonly string removeInteractDelayKey = ModId + ":RemoveInteractDelayWhileCarrying";
        static private readonly string interactSpeedMultiplierKey = ModId + ":InteractSpeedMultiplier";
        static private readonly string harmonyPatchEnabledKey = ModId + ":HarmonyPatchEnabled";
        static private readonly string backSlotEnabledKey = ModId + ":BackSlotEnabled";
        static private readonly string henboxEnabledKey = ModId + ":HenboxEnabled";

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
        private const string ClientConfigFile = "CarryOnClientConfig.json";



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
                        GenerateConfig(api);
                        ServerConfig = LoadConfig(api);
                    }
                    else
                    {
                        GenerateConfig(api, ServerConfig);
                    }
                }
                catch
                {
                    GenerateConfig(api);
                    ServerConfig = LoadConfig(api);
                }

                var worldConfig = api?.World?.Config;

                if (worldConfig == null)
                {
                    api.Logger.Error("CarryOn: Unable to access world config. CarryOn features may not work correctly.");
                    return;
                }

                worldConfig.SetBool(ModId + ":AnvilEnabled", ServerConfig.AnvilEnabled);
                worldConfig.SetBool(ModId + ":BarrelEnabled", ServerConfig.BarrelEnabled);
                worldConfig.SetBool(ModId + ":BookshelfEnabled", ServerConfig.BookshelfEnabled);
                worldConfig.SetBool(ModId + ":BunchOCandlesEnabled", ServerConfig.BunchOCandlesEnabled);
                worldConfig.SetBool(ModId + ":ChandelierEnabled", ServerConfig.ChandelierEnabled);
                worldConfig.SetBool(ModId + ":ChestLabeledEnabled", ServerConfig.ChestLabeledEnabled);
                worldConfig.SetBool(ModId + ":ChestTrunkEnabled", ServerConfig.ChestTrunkEnabled);
                worldConfig.SetBool(ModId + ":ChestEnabled", ServerConfig.ChestEnabled);
                worldConfig.SetBool(ModId + ":ClutterEnabled", ServerConfig.ClutterEnabled);
                worldConfig.SetBool(ModId + ":CrateEnabled", ServerConfig.CrateEnabled);
                worldConfig.SetBool(ModId + ":CrateLegacyEnabled", ServerConfig.CrateLegacyEnabled);
                worldConfig.SetBool(ModId + ":DisplayCaseEnabled", ServerConfig.DisplayCaseEnabled);
                worldConfig.SetBool(ModId + ":FlowerpotEnabled", ServerConfig.FlowerpotEnabled);
                worldConfig.SetBool(ModId + ":ForgeEnabled", ServerConfig.ForgeEnabled);
                worldConfig.SetBool(ModId + ":LogWithResinEnabled", ServerConfig.LogWithResinEnabled);
                worldConfig.SetBool(ModId + ":MoldRackEnabled", ServerConfig.MoldRackEnabled);
                worldConfig.SetBool(ModId + ":MoldsEnabled", ServerConfig.MoldsEnabled);
                worldConfig.SetBool(ModId + ":LootVesselEnabled", ServerConfig.LootVesselEnabled);
                worldConfig.SetBool(ModId + ":OvenEnabled", ServerConfig.OvenEnabled);
                worldConfig.SetBool(ModId + ":PlanterEnabled", ServerConfig.PlanterEnabled);
                worldConfig.SetBool(ModId + ":QuernEnabled", ServerConfig.QuernEnabled);
                worldConfig.SetBool(ModId + ":ShelfEnabled", ServerConfig.ShelfEnabled);
                worldConfig.SetBool(ModId + ":SignEnabled", ServerConfig.SignEnabled);
                worldConfig.SetBool(ModId + ":ReedBasketEnabled", ServerConfig.ReedBasketEnabled);
                worldConfig.SetBool(ModId + ":StorageVesselEnabled", ServerConfig.StorageVesselEnabled);
                worldConfig.SetBool(ModId + ":ToolRackEnabled", ServerConfig.ToolRackEnabled);
                worldConfig.SetBool(ModId + ":TorchHolderEnabled", ServerConfig.TorchHolderEnabled);

                worldConfig.SetBool(ModId + ":BookshelfAndClutterEnabled", ServerConfig.BookshelfEnabled && ServerConfig.ClutterEnabled);

                worldConfig.SetBool(ModId + ":InteractDoorEnabled", ServerConfig.InteractDoorEnabled);
                worldConfig.SetBool(ModId + ":InteractStorageEnabled", ServerConfig.InteractStorageEnabled);

                worldConfig.SetBool(ModId + ":AllowChestTrunksOnBack", ServerConfig.AllowChestTrunksOnBack);
                worldConfig.SetBool(ModId + ":AllowLargeChestsOnBack", ServerConfig.AllowLargeChestsOnBack);
                worldConfig.SetBool(ModId + ":AllowCratesOnBack", ServerConfig.AllowCratesOnBack);


                AllowSprintWhileCarrying = ServerConfig.AllowSprintWhileCarrying;
                IgnoreCarrySpeedPenalty = ServerConfig.IgnoreCarrySpeedPenalty;
                RemoveInteractDelayWhileCarrying = ServerConfig.RemoveInteractDelayWhileCarrying;
                InteractSpeedMultiplier = ServerConfig.InteractSpeedMultiplier;
                HarmonyPatchEnabled = ServerConfig.HarmonyPatchEnabled;
                BackSlotEnabled = ServerConfig.BackSlotEnabled;
                HenboxEnabled = ServerConfig.HenboxEnabled;

            }
            else
            {
                // Load client side config
                try
                {
                    ClientConfig = LoadClientConfig(api);

                    if (ClientConfig == null)
                    {
                        GenerateClientConfig(api);
                        ClientConfig = LoadClientConfig(api);
                    }
                    else
                    {
                        GenerateClientConfig(api, ClientConfig);
                    }
                }
                catch
                {
                    GenerateClientConfig(api);
                    ClientConfig = LoadClientConfig(api);
                }
            }
        }

        private static CarryOnConfig LoadConfig(ICoreAPI api)
        {
            return api.LoadModConfig<CarryOnConfig>(ConfigFile);
        }

        private static void GenerateConfig(ICoreAPI api)
        {
            api.StoreModConfig(new CarryOnConfig(), ConfigFile);
        }

        private static void GenerateConfig(ICoreAPI api, CarryOnConfig previousConfig)
        {
            api.StoreModConfig(new CarryOnConfig(previousConfig), ConfigFile);
        }

        private static CarryOnClientConfig LoadClientConfig(ICoreAPI api)
        {
            return api.LoadModConfig<CarryOnClientConfig>(ClientConfigFile);
        }

        private static void GenerateClientConfig(ICoreAPI api)
        {
            api.StoreModConfig(new CarryOnClientConfig(), ClientConfigFile);
        }
        private static void GenerateClientConfig(ICoreAPI api, CarryOnClientConfig previousConfig)
        {
            api.StoreModConfig(new CarryOnClientConfig(previousConfig), ClientConfigFile);
        }
    }
}
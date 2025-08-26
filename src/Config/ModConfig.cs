using System;
using System.Collections.Generic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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

        public static string ConfigFile = "CarryOnConfig.json";

        public static void ReadConfig(ICoreAPI api)
        {
            World = api.World;
            if (api.Side == EnumAppSide.Server)
            {
                const int currentVersion = 2;
                try
                {
                    var loadedConfig = api.LoadModConfig<CarryOnConfig>(ConfigFile);
                    if (loadedConfig != null)
                    {
                        loadedConfig.UpgradeVersion();
                    }
                    else
                    {
                        loadedConfig = new CarryOnConfig(currentVersion);
                    }

                    new CarryOnConfig(currentVersion);

                    // DEBUG:DISABLES              api.StoreModConfig(ServerConfig, ConfigFile);

                    ServerConfig = loadedConfig;

                }
                catch (Exception ex)
                {
                    // Log the exception and create a default config
                    api.Logger.Error("CarryOn: Exception loading config: " + ex);
                    ServerConfig = new CarryOnConfig(currentVersion);
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

                // Cleanup old world config: Remove all keys starting with "carryon:"
                var keysToRemove = new List<string>();
                foreach (var key in (worldConfig as TreeAttribute)?.Keys)
                {
                    if (key.StartsWith("carryon:", StringComparison.OrdinalIgnoreCase))
                    {
                        keysToRemove.Add(key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    worldConfig.RemoveAttribute(key);
                }

                // Save the value to the world config so it is available for both server and client
                worldConfig.GetOrAddTreeAttribute(ModId).MergeTree(ServerConfig.ToTreeAttribute());

            }
        }

        public static string[] CloneArray(string[] source)
        {
            return source != null ? (string[])source.Clone() : [];
        }        

    }
}
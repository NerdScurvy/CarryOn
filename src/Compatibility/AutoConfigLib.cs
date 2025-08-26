using System;
using CarryOn.Config;
using HarmonyLib;
using Vintagestory.API.Common;

namespace CarryOn.Compatibility
{
    public static class AutoConfigLib
    {
        public static bool HadPatches(ICoreAPI api)
        {
            try
            {
                var harmony = new Harmony("autoconfiglib");
                if (Harmony.HasAnyPatches("autoconfiglib"))
                {
                    var readConfigMethod = AccessTools.Method(typeof(ModConfig), "ReadConfig");
                    var loadConfigMethod = AccessTools.Method(typeof(ModConfig), "LoadConfig");
                    harmony.Unpatch(readConfigMethod, HarmonyPatchType.All, "autoconfiglib");
                    harmony.Unpatch(loadConfigMethod, HarmonyPatchType.All, "autoconfiglib");
                    return true;
                }
            }
            catch (Exception ex)
            {
                api.World.Logger.Error($"CarryOn: Exception during disabling CarryOn AutoConfigLib patches: {ex}");
            }
            return false;
        }
    }
}
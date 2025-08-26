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
            var hadPatch = false;
            try
            {
                var harmonyId = "autoconfiglib";

                var harmony = new Harmony(harmonyId);
                var readConfigMethod = AccessTools.DeclaredMethod(typeof(ModConfig), nameof(ModConfig.ReadConfig));
                var loadConfigMethod = AccessTools.DeclaredMethod(typeof(ModConfig), nameof(ModConfig.LoadConfig));



                if (readConfigMethod != null)
                {
                    harmony.Unpatch(readConfigMethod, HarmonyPatchType.All, harmonyId);
                    hadPatch = true;
                }
                if (loadConfigMethod != null)
                {
                    harmony.Unpatch(loadConfigMethod, HarmonyPatchType.All, harmonyId);
                    hadPatch = true;
                }

                if (hadPatch)
                {
                    api.Logger.Notification("CarryOn: Disabled AutoConfigLib patches.");
                }   


            }
            catch (Exception ex)
            {
                api.Logger.Error($"CarryOn: Exception during disabling CarryOn AutoConfigLib patches: {ex}");
            }
            return hadPatch;
        }
    }
}
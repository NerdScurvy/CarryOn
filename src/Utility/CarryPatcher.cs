using System;
using HarmonyLib;
using Vintagestory.API.Common;

namespace CarryOn.Utility
{
    internal sealed class CarryPatcher
    {
        private const string HarmonyId = "carryon";
        private static Harmony? harmony;

        public static void Apply(ICoreAPI api)
        {
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll();
                api.World.Logger.Notification("CarryOn: Harmony patches enabled.");
            }
            catch (Exception ex)
            {
                api.World.Logger.Error($"CarryOn: Exception during Harmony patching: {ex}");
            }
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }
    }
}
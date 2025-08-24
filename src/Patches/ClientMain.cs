using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace CarryOn.Patches
{
    [HarmonyPatch(typeof(ClientMain), "sendRuntimeSettings")]
    public static class ClientMain_sendRuntimeSettings_Patch
    {
        // Postfix method signature must match the original method's parameters, plus __instance and optionally __result
        public static void Postfix(ClientMain __instance)
        {

            var carrySystem = __instance.Api.ModLoader.GetModSystem<CarrySystem>();

            if (carrySystem == null)
            {
                __instance.Api.World.Logger.Error("CarryOn: Failed to find CarrySystem on client connect.");
                return;
            }

            try
            {
                // Find HudElementInteractionHelp from __instance.LoadedGuis
                FieldInfo internalField = typeof(ClientMain).GetField("LoadedGuis", BindingFlags.NonPublic | BindingFlags.Instance);
                var loadedGuis = internalField.GetValue(__instance) as List<GuiDialog>;
                var hudHelp = loadedGuis?.FirstOrDefault(gui => gui is HudElementInteractionHelp) as HudElementInteractionHelp;

                carrySystem.CarryHandler.HudHelp = hudHelp;
            }
            catch (System.Exception ex)
            {
                __instance.Api.World.Logger.Error("CarryOn: Failed to initialize CarryHandler HudHelp: " + ex.Message);
            }

        }
    }

}
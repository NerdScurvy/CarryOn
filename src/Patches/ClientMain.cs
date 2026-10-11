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
                // Reflection on private field 'LoadedGuis' - verified against Vintage Story 1.22.
                // Inherent fragility of Harmony patching private API; update if game internals change.
                FieldInfo? internalField = typeof(ClientMain).GetField("LoadedGuis", BindingFlags.NonPublic | BindingFlags.Instance);
                if (internalField == null) return;

                var loadedGuis = internalField.GetValue(__instance) as List<GuiDialog>;
                if (loadedGuis == null) return;

                var hudHelp = loadedGuis.FirstOrDefault(gui => gui is HudElementInteractionHelp) as HudElementInteractionHelp;

                carrySystem.CarryClientHandlers?.SetHudHelp(hudHelp);
            }
            catch (System.Exception ex)
            {
                __instance.Api.World.Logger.Error("CarryOn: Failed to initialize CarryClientHandlers HudHelp: " + ex);
            }

        }
    }

}
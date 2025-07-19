using CarryOn.API.Common;
using CarryOn.Common;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Patches
{
    
    [HarmonyPatch(typeof(Vintagestory.GameContent.EntityBoat), "OnInteract")]
    public class Patch_EntityBoat_OnInteract
    {
        [HarmonyPrefix]
        public static bool Prefix(Vintagestory.GameContent.EntityBoat __instance, EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            // Try to attach or detach a carryable block first if applicable
            if (byEntity.IsCarryKeyHeld())
            {
                EnumHandling handled = EnumHandling.PassThrough;
                var carryBehavior = __instance.GetBehavior<EntityBehaviorAttachableCarryable>();
                if (carryBehavior != null)
                {
                    carryBehavior.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                    if (handled == EnumHandling.PreventSubsequent)
                        return false; // Skip original method
                }

            }

            return true; // Continue with original method
        }
    }    
}
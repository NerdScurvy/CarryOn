using HarmonyLib;
using Vintagestory.GameContent;
using Vintagestory.API.Common;
using static CarryOn.API.Common.CarryCode;

namespace CarryOn.Patches
{
    [HarmonyPatch(typeof(BlockToolMold), "TryPlaceBlock")]
    public static class BlockToolMold_TryPlaceBlock_Patch
    {

        public static bool Prefix(BlockToolMold __instance, IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode, ref bool __result)
        {
            if(failureCode == FailureCode.Ignore)
                byPlayer.Entity.Controls.ShiftKey = true; // Force sneak to always be true for placement
            return true;
        }
    }
}

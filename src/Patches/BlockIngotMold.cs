using HarmonyLib;
using Vintagestory.GameContent;
using Vintagestory.API.Common;
using static CarryOn.API.Common.CarryCode;

namespace CarryOn.Patches
{
    [HarmonyPatch(typeof(BlockIngotMold), "TryPlaceBlock")]
    public static class BlockIngotMold_TryPlaceBlock_Patch
    {

        public static bool Prefix(BlockIngotMold __instance, IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode, ref bool __result)
        {
            if(failureCode == FailureCode.Ignore)
                byPlayer.Entity.Controls.ShiftKey = true; // Force sneak to always be true for placement
            return true;
        }
    }
}

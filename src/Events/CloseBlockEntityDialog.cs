using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CarryOn.Events
{

    /// <summary>
    /// Closes the block entity dialog when the block is removed.
    /// </summary>
    public class CloseBlockEntityDialog : ICarryEvent
    {
        private ICarryManager? carryManager;

        public void Init(ICarryManager carryManager)
        {
            if (carryManager.Api.Side != EnumAppSide.Client) return;
            this.carryManager = carryManager;

            carryManager.CarryEvents.BeforeRemoveBlockFromWorld += OnBeforeRemoveBlockFromWorld;
        }

        private void OnBeforeRemoveBlockFromWorld(CarriedBlock carriedBlock, BlockPos pos)
        {
            // Get BlockEntity from pos
            var api = carryManager?.Api;
            if (api == null) return;

            var blockEntity = api.World.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity == null) return;


            if (blockEntity is BlockEntityBarrel blockEntityBarrel)
            {
                // Try to close dialog - this will only work for the client removing the block
                // TODO: Review code after vanilla bug is addressed
                //       https://github.com/anegostudios/VintageStory-Issues/issues/6875
                var result = AccessTools.Field(typeof(BlockEntityBarrel), "invDialog").GetValue(blockEntityBarrel);
                if (result != null && result is GuiDialogBarrel invDialog)
                {
                    invDialog?.TryClose();
                }
            }
        }
    }
}
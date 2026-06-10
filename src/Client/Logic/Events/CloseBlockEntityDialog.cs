using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.Events
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
                // Close the barrel's inventory dialog when the block is removed.
                // Uses reflection to access the private "invDialog" field because there is no public API for this.
                // TODO: Remove reflection after vanilla bug is addressed
                //       https://github.com/anegostudios/VintageStory-Issues/issues/6875
                try
                {
                    var result = AccessTools.Field(typeof(BlockEntityBarrel), "invDialog")?.GetValue(blockEntityBarrel);
                    if (result is GuiDialogBarrel invDialog)
                    {
                        invDialog.TryClose();
                    }
                }
                catch (Exception ex)
                {
                    carryManager?.Api?.Logger.Debug($"CarryOn: Could not close barrel dialog during carry: {ex.Message}");
                }
            }
        }
    }
}

using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Event.Data;
using CarryOn.Server.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    /// <summary>
    /// Tracks dropped blocks and player permissions to retrieve them.
    /// </summary>
    public class DroppedBlockTracker : ICarryEvent
    {
        private ICarryManager carryManager;

        private bool loggingEnabled = false;

        public void Init(ICarryManager carryManager)
        {
            if (carryManager == null) throw new ArgumentNullException(nameof(carryManager));
            this.carryManager = carryManager;
            var api = carryManager.Api ?? throw new ArgumentNullException(nameof(carryManager.Api));
            var world = api.World ?? throw new ArgumentNullException(nameof(api.World));
            var carrySystem = world.GetCarrySystem();
            this.loggingEnabled = carrySystem?.Config?.DebuggingOptions?.LoggingEnabled ?? false;

            var events = carryManager.CarryEvents ?? throw new InvalidOperationException("CarryEvents not initialized");

            if (api.Side == EnumAppSide.Client)
            {
                events.OnCheckPermissionToCarry += OnCheckPermissionToCarryClient;
                return;
            }

            events.OnCheckPermissionToCarry += OnCheckPermissionToCarry;
            events.BlockDropped += OnCarriedBlockDropped;

            events.BlockRemoved += OnCarryableBlockRemoved;
        }

        public void OnCheckPermissionToCarryClient(EntityPlayer playerEntity, BlockPos pos, bool isReinforced, out bool? hasPermission)
        {
            // Allow client side permission so checks are done server side unless is reinforced
            hasPermission = isReinforced ? null : true;
        }

        /// <summary>
        /// Called when checking permission to carry a block. If the block is not reinforced, checks if it was dropped by a player.
        /// For dropped blocks all claims are ignored.
        /// </summary>
        /// <param name="playerEntity"></param>
        /// <param name="pos"></param>
        /// <param name="isReinforced"></param>
        /// <param name="hasPermission"></param>
        public void OnCheckPermissionToCarry(EntityPlayer playerEntity, BlockPos pos, bool isReinforced, out bool? hasPermission)
        {
            // A null value means the server should continue to check other delegates
            hasPermission = null;

            if (isReinforced) return;

            var world = playerEntity.Api.World;

            // Check if block was dropped by a player
            var droppedBlock = DroppedBlockInfo.Get(pos, playerEntity.Player);
            if (droppedBlock != null)
            {
                if (this.loggingEnabled) world.Logger.Debug($"Dropped block found at '{pos}'");
                hasPermission = true;
                return;
            }
            if (this.loggingEnabled) world.Logger.Debug($"No dropped block found at '{pos}'");
        }

        /// <summary>
        /// Called when a block is dropped while being carried.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnCarriedBlockDropped(object sender, BlockDroppedEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            if (e.Entity is EntityPlayer entityPlayer)
            {
                // Only track if block was placed
                if (e.BlockPlaced)
                    DroppedBlockInfo.Create(e.Position, entityPlayer.Player, e.CarriedBlock.BlockEntityData);
            }
        }

        /// <summary>
        /// Called when a carryable block is removed from the world.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnCarryableBlockRemoved(object sender, BlockRemovedEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (carryManager.Api.Side == EnumAppSide.Server)
            {
                DroppedBlockInfo.Remove(e.Position, carryManager.Api);
            }
        }
    }
}
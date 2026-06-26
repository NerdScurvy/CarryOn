using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Event.Data;
using CarryOn.Server.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    /// <summary>
    /// Tracks dropped blocks and player permissions to retrieve them.
    /// </summary>
    public class DroppedBlockTracker : ICarryEvent
    {
        private ICarryManager? carryManager;

        private bool loggingEnabled = false;

        public void Init(ICarryManager carryManager)
        {
            ArgumentNullException.ThrowIfNull(carryManager);
            this.carryManager = carryManager;
            this.loggingEnabled = carryManager.Config?.DebuggingOptions?.LoggingEnabled ?? false;

            if (carryManager.Config?.CarryOptions?.TrackDroppedBlocks != true)
                return;

            var events = carryManager.CarryEvents ?? throw new InvalidOperationException("CarryEvents not initialized");

            if (carryManager.Api.Side == EnumAppSide.Client)
            {
                events.CheckPermissionToCarry += OnCheckPermissionToCarryClient;
                return;
            }

            events.CheckPermissionToCarry += OnCheckPermissionToCarry;
            events.BlockDropped += OnCarriedBlockDropped;

            events.BlockRemoved += OnCarryableBlockRemoved;
        }

        public void OnCheckPermissionToCarryClient(EntityPlayer playerEntity, BlockPos pos, bool isReinforced, out bool? hasPermission)
        {
            hasPermission = null;
            if (carryManager?.Config?.CarryOptions?.TrackDroppedBlocks != true)
                return;
            
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

            if (carryManager?.Config?.CarryOptions?.TrackDroppedBlocks != true)
                return;

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
        public void OnCarriedBlockDropped(object? sender, BlockDroppedEventArgs e)
        {
            if (carryManager?.Config?.CarryOptions?.TrackDroppedBlocks != true)
                return;

            ArgumentNullException.ThrowIfNull(e);
            if (!e.BlockPlaced || e.Position == null) return;
            if (e.CarriedBlock == null) return;

            if (e.Entity is EntityPlayer entityPlayer)
            {
                var blockEntityData = e.CarriedBlock.BlockEntityData ?? new TreeAttribute();
                DroppedBlockInfo.Create(e.Position, entityPlayer.Player, blockEntityData);
            }
        }

        /// <summary>
        /// Called when a carryable block is removed from the world.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnCarryableBlockRemoved(object? sender, BlockRemovedEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            var manager = carryManager;
            if (manager?.Api?.Side == EnumAppSide.Server && e.Position != null)
            {
                DroppedBlockInfo.Remove(e.Position, manager.Api);
            }
        }
    }
}
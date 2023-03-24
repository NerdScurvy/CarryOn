using CarryOn.API.Common;
using CarryOn.API.Event;
using CarryOn.API.Event.Data;
using CarryOn.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    public class DroppedBlockTracker : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            var events = carrySystem.CarryEvents;

            events.OnCheckPermissionToCarry += OnCheckPermissionToCarry;
            events.BlockDropped += OnCarriedBlockDropped;

            events.BlockRemoved += OnCarryableBlockRemoved;
        }

        public void OnCheckPermissionToCarry(EntityPlayer playerEntity, BlockPos pos, out bool? hasPermission)
        {
            hasPermission = null;
            var world = playerEntity.Api.World;

            // Check if block was dropped by a player
            var droppedBlock = DroppedBlockInfo.Get(pos, playerEntity.Player);
            if (droppedBlock != null)
            {
                world.Logger.Debug($"Dropped block found at '{pos}'");
                hasPermission = true;
                return;
            }
            world.Logger.Debug($"No dropped block found at '{pos}'");
        }

        public void OnCarriedBlockDropped(object sender, BlockDroppedEventArgs e)
        {
            if (e.Entity is EntityPlayer entityPlayer)
            {
                DroppedBlockInfo.Create(e.Position, entityPlayer.Player, e.CarriedBlock.BlockEntityData);
            }
        }

        public void OnCarryableBlockRemoved(object sender, BlockRemovedEventArgs e)
        {
            if (e.World.Api.Side == EnumAppSide.Server)
            {
                DroppedBlockInfo.Remove(e.Position, e.World.Api);
            }
        }
    }
}
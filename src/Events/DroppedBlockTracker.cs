using CarryOn.API.Common;
using CarryOn.API.Event.Data;
using CarryOn.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    public class DroppedBlockTracker : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            var events = carrySystem.CarryEvents;

            if (carrySystem.Api.Side == EnumAppSide.Client) {
                events.OnCheckPermissionToCarry += OnCheckPermissionToCarryClient;
                return;
            }

            events.OnCheckPermissionToCarry += OnCheckPermissionToCarry;
            events.BlockDropped += OnCarriedBlockDropped;

            events.BlockRemoved += OnCarryableBlockRemoved;
        }

        public void OnCheckPermissionToCarryClient(EntityPlayer playerEntity, BlockPos pos, bool isReinforced, out bool? hasPermission){
            // Allow client side permission so checks are done server side unless is reinforced
            hasPermission = isReinforced?null:true;
        }

        public void OnCheckPermissionToCarry(EntityPlayer playerEntity, BlockPos pos, bool isReinforced, out bool? hasPermission)
        {
            hasPermission = null;

            if(isReinforced) return;

            var world = playerEntity.Api.World;

            // Check if block was dropped by a player
            var droppedBlock = DroppedBlockInfo.Get(pos, playerEntity.Player);
            if (droppedBlock != null)
            {
                if (ModConfig.ServerConfig.LoggingEnabled) world.Logger.Debug($"Dropped block found at '{pos}'");
                hasPermission = true;
                return;
            }
            if (ModConfig.ServerConfig.LoggingEnabled) world.Logger.Debug($"No dropped block found at '{pos}'");
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
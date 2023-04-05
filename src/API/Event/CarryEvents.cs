using System;
using CarryOn.API.Common;
using CarryOn.API.Event.Data;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CarryOn.API.Event
{
    public class CarryEvents
    {
        public BlockEntityDataDelegate OnRestoreEntityBlockData;

        public CheckPermissionToCarryDelegate OnCheckPermissionToCarry;

        public event EventHandler<BlockDroppedEventArgs> BlockDropped;

        public event EventHandler<BlockRemovedEventArgs> BlockRemoved;

        public void TriggerBlockDropped(IWorldAccessor world, BlockPos position, Entity entity, CarriedBlock carriedBlock, bool destroyed = false, bool hadContents = false)
        {
            var args = new BlockDroppedEventArgs
            {
                World = world,
                Entity = entity,
                Position = position,
                CarriedBlock = carriedBlock,
                Destroyed = destroyed,
                HadContents = hadContents
            };

            OnBlockDropped(args);
        }

        protected virtual void OnBlockDropped(BlockDroppedEventArgs e)
        {
            BlockDropped?.Invoke(this, e);
        }

        public void TriggerBlockRemoved(IWorldAccessor world, BlockPos position)
        {
            var args = new BlockRemovedEventArgs
            {
                World = world,
                Position = position
            };

            OnBlockRemoved(args);
        }

        protected virtual void OnBlockRemoved(BlockRemovedEventArgs e)
        {
            BlockRemoved?.Invoke(this, e);
        }
    }
}
using System;
using Vintagestory.API.Common;

namespace CarryOn.API.Common
{
    public class CarryManager:ICarryManager
    {

        public CarrySystem CarrySystem { get; private set; }

        public CarryManager(CarrySystem carrySystem)
        {
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
        }

        public CarriedBlock GetCarriedBlock(IPlayer player, CarrySlot slot)
        {
            // Implementation here
            CarriedBlock carriedBlock = player.Entity.GetCarried(slot);
            return carriedBlock;
        }
    }
}
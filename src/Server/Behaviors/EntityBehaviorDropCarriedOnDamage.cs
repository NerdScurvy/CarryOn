using System;
using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using static CarryOn.API.Common.CarryCode;
using static CarryOn.Utility.Extensions;

namespace CarryOn.Server.Behaviors
{
    public class EntityBehaviorDropCarriedOnDamage : EntityBehavior
    {
        public static string Name { get; }
            = CarryOnCode("dropondamage");

        private static readonly CarrySlot[] DropFrom
            = [CarrySlot.Hands, CarrySlot.Shoulder];

        public override string PropertyName() => Name;

        public EntityBehaviorDropCarriedOnDamage(Entity entity)
            : base(entity)
        { 
      
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (damageSource.Type != EnumDamageType.Heal)
                GetCarryManager(entity.Api).DropCarried(entity, DropFrom, 2);
        }
    }
}

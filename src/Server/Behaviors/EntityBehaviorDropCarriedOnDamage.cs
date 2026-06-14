using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Server.Behaviors
{
    public class EntityBehaviorDropCarriedOnDamage : EntityBehavior
    {
        public static string Name { get; }
            = CarryOnCode("dropondamage");

        public static ICarryManager? CarryManager { get; set; }
        public static bool Enabled { get; set; } = true;
        public static float DamageThreshold { get; set; } = 0f;
        public static int DropRange { get; set; } = 2;

        private static readonly CarrySlot[] DropFrom
            = [CarrySlot.Hands];

        public override string PropertyName() => Name;

        public EntityBehaviorDropCarriedOnDamage(Entity entity)
            : base(entity)
        { 
      
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (!Enabled) return;
            if (damageSource.Type == EnumDamageType.Heal) return;
            if (damage <= DamageThreshold) return;
            CarryManager?.DropCarried(entity, DropFrom, DropRange);
        }
    }
}

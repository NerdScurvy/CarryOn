using System.Collections.Generic;
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
        public static DropCarriedOnDamageConfig? Config { get; set; }

        public override string PropertyName() => Name;

        public EntityBehaviorDropCarriedOnDamage(Entity entity)
            : base(entity)
        {
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (Config == null) return;
            if (damageSource.Type == EnumDamageType.Heal) return;

            var slotsToDrop = new List<CarrySlot>(2);
            if (Config.HandsEnabled && damage > Config.HandsDamageThreshold)
                slotsToDrop.Add(CarrySlot.Hands);
            if (Config.BackEnabled && damage > Config.BackDamageThreshold)
                slotsToDrop.Add(CarrySlot.Back);

            if (slotsToDrop.Count == 0) return;
            CarryManager?.DropCarried(entity, slotsToDrop, Config.DropRange);
        }
    }
}

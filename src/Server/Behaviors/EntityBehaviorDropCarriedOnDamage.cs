using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Server.Behaviors
{
    public class EntityBehaviorDropCarriedOnDamage : EntityBehavior
    {
        public static string Name { get; }
            = CarryOnCode("dropondamage");

        private static ICarryManager? carryManager;
        private static DropCarriedOnDamageConfig? config;

        public static void Init(ICarryManager manager, DropCarriedOnDamageConfig? dropConfig)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
            config = dropConfig;
        }

        public override string PropertyName() => Name;

        public EntityBehaviorDropCarriedOnDamage(Entity entity)
            : base(entity)
        {
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (config == null) return;
            if (damageSource.Type == EnumDamageType.Heal) return;

            var slotsToDrop = new List<CarrySlot>(2);
            if (config.HandsEnabled && damage > config.HandsDamageThreshold)
                slotsToDrop.Add(CarrySlot.Hands);
            if (config.BackEnabled && damage > config.BackDamageThreshold)
                slotsToDrop.Add(CarrySlot.Back);

            if (slotsToDrop.Count == 0) return;
            carryManager?.DropCarried(entity, slotsToDrop, config.DropRange);
        }
    }
}

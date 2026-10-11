using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Interfaces;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Server.Behaviors
{
    public class EntityBehaviorDropCarriedOnDamage : EntityBehavior
    {
        public static string Name { get; } = GetCarryCode("dropondamage");

        private static ICarryManager? carryManager;
        private static IConfigProvider? configProvider;

        /// <summary>
        /// Initializes static fields with the carry manager. Must be called once during
        /// server startup (called from CarrySystem.StartServerSide). Calling again with a
        /// different manager instance would silently keep the stale reference - the singleton
        /// mod lifecycle means this should not happen in practice.
        /// </summary>
        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
            configProvider = manager as IConfigProvider
                ?? throw new ArgumentException("manager must implement IConfigProvider", nameof(manager));
        }

        public override string PropertyName() => Name;

        public EntityBehaviorDropCarriedOnDamage(Entity entity)
            : base(entity)
        {
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            var config = configProvider?.Config.DropCarriedOnDamage;
            if (config == null) return;
            if (damageSource == null) return;
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

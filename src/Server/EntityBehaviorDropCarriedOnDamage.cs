using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CarryOn.Server
{
    public class EntityBehaviorDropCarriedOnDamage : EntityBehavior
    {
        public static string Name { get; }
            = $"{CarrySystem.ModId}:dropondamage";

        private static readonly CarrySlot[] DropFrom
            = new[] { CarrySlot.Hands, CarrySlot.Shoulder };

        public override string PropertyName() => Name;

        public EntityBehaviorDropCarriedOnDamage(Entity entity)
            : base(entity) { }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (damageSource.Type != EnumDamageType.Heal)
                entity.DropCarried(DropFrom, 1, 2);
        }
    }
}

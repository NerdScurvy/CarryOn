using Vintagestory.API.Common.Entities;

namespace CarryOn.Common.Behaviors
{
    public class EntityBehaviorCarryableLegacy : EntityBehaviorCarryable
    {
        public EntityBehaviorCarryableLegacy(Entity entity) : base(entity) { }

        public override string PropertyName() => "Carryable";
    }
}

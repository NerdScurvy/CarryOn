using Vintagestory.API.Common;

namespace CarryOn.Common
{
    public class BlockBehaviorCarryTest : BlockBehavior
    {

        public static string Name { get; } = "CarryTest";

        public BlockBehaviorCarryTest(Block block) : base(block)
        {
        }


        public bool CanInsertCarryable()
        {
            return true;
        }

        public bool CanExtractCarryable()
        {
            return true;
        }

    }
}
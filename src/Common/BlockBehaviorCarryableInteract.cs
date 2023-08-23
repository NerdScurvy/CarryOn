using System.Collections.Generic;
using CarryOn.API.Common;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Common
{
    /// <summary> Block behavior which, when added to a block, will allow
    ///           said block to be picked up by players and carried around. </summary>
    public class BlockBehaviorCarryableInteract : BlockBehavior
    {
        public static string Name { get; } = "CarryableInteract";

        public static BlockBehaviorCarryableInteract Default { get; }
            = new BlockBehaviorCarryableInteract(null);

        public float InteractDelay { get; private set; } = CarrySystem.InteractSpeedDefault;

        public IList<AllowedCarryable> AllowedCarryables { get; } = new List<AllowedCarryable>();

        public BlockBehaviorCarryableInteract(Block block)
            : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;

            if (!properties.KeyExists("allowedCarryables")) return;

            foreach (var allowedJson in properties["allowedCarryables"]?.AsArray())
            {
                AllowedCarryables.Add(
                    new AllowedCarryable()
                    {
                        Code = allowedJson["code"]?.AsString(),
                        Class = allowedJson["class"]?.AsString()
                    }
                );
            }
        }

        public bool CanInteract(IPlayer player){
            if(AllowedCarryables.Count == 0) return true;

            var carriedBlock = player?.Entity?.GetCarried(CarrySlot.Hands);
            foreach(var allowed in AllowedCarryables){
                if(allowed.IsMatch(carriedBlock.Block)){
                    return true;
                }
            }

            return false;
        }

        public class AllowedCarryable
        {
            public string Code { get; set; }
            public string Class { get; set; }

            public bool IsMatch(Block block){
                if(Code != null && block.Code.GetName() == Code) return true;
                if(Class != null && block.Class == Class) return true;
                return false;
            }
        }
    }
}

using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Behaviors
{
    /// <summary> Block behavior which, when added to a block, will allow
    ///           said block to be picked up by players and carried around. </summary>
    public class BlockBehaviorCarryableInteract : BlockBehavior, IConditionalBlockBehavior
    {
        public static string Name { get; } = "CarryableInteract";

        public float InteractDelay { get; private set; } = Default.InteractSpeed;

        public string EnabledCondition { get; set; }

        public IList<AllowedCarryable> AllowedCarryables { get; } = new List<AllowedCarryable>();

        public BlockBehaviorCarryableInteract(Block block)
            : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;
            if (JsonHelper.TryGetString(properties, "enabledCondition", out var e)) EnabledCondition = e;

            // Whitelist of carryable blocks that can be used to interact with a block entity
            if (!properties.KeyExists("allowedCarryables")) return;
            var arr = properties["allowedCarryables"]?.AsArray();

            foreach (var allowedJson in arr)
            {
                var code = allowedJson["code"]?.AsString()?.Trim();
                var @class = allowedJson["class"]?.AsString()?.Trim();
                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(@class)) continue;
                AllowedCarryables.Add(
                    new AllowedCarryable()
                    {
                        Code = code,
                        Class = @class
                    }
                );
            }
        }

        /// <summary>
        /// Checks if the carried block can be used to interact with block entity
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public bool CanInteract(IPlayer player)
        {
            if (AllowedCarryables.Count == 0) return true;
            if (player?.Entity == null) return false;

            var carried = player.Entity.GetCarried(CarrySlot.Hands);
            var block = carried?.Block;
            if (block == null) return false;

            foreach (var allowed in AllowedCarryables)
            {
                if (allowed.IsMatch(block)) return true;
            }

            return false;
        }

        public void ProcessConditions(ICoreAPI api, Block block)
        {

        }

        public class AllowedCarryable
        {
            public string Code { get; set; }
            public string Class { get; set; }

            public bool IsMatch(Block block)
            {
                if (Code != null && block.Code.GetName() == Code) return true;
                if (Class != null && block.Class == Class) return true;
                return false;
            }
        }
    }
}

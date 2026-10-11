using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Behaviors
{
    /// <summary> Block behavior which, when added to a block, will allow
    ///           said block to be picked up by players and carried around. </summary>
    public class BlockBehaviorCarryableInteract : BlockBehavior, IConditionalBlockBehavior
    {
        public static string Name { get; } = GetCarryCode("carryableinteract");

        private static ICarryManager? carryManager;

        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// Whether CarryOn's own factory constructed this instance (a baseline remount
        /// or an automap clone), as opposed to the engine loading it from a blocktype
        /// or patch, another mod registering it, or test code appending it. Internal
        /// and not serialized: it is the un-create predicate of the conditioning
        /// baseline's restore pass, invisible to the wire, to snapshots and to mod authors. Set only by the
        /// factory - <see cref="Server.Logic.BehavioralConditions.ConditioningBaseline"/>
        /// remounts and the automapper's appends.
        /// </summary>
        internal bool CreatedByConditioning;

        public float InteractDelay { get; private set; } = Defaults.InteractSpeed;

        public string EnabledCondition { get; set; } = string.Empty;

        public IList<AllowedCarryable> AllowedCarryables { get; } = new List<AllowedCarryable>();

        /// <summary>
        /// The behavior's property JSON as initialized (interact conditioning is a no-op, so
        /// the initialized set is the effective set). Used by the carryability snapshot so
        /// the client can re-mount a faithful instance. Falls back to the captured
        /// <see cref="Properties"/> for instances CarryOn's own factory builds (automap
        /// twins, baseline remounts), mirroring <c>BlockBehaviorCarryable</c>.
        /// </summary>
        internal string CurrentPropertiesJson
            => propertiesAtString ?? Properties?.Token?.ToString() ?? string.Empty;

        /// <summary>
        /// The properties the engine, a patch or CarryOn's factory initialized this
        /// instance with - the donor handle the interact automapper's twin-creation pass
        /// reads (mirrors <c>BlockBehaviorCarryable.Properties</c>).
        /// </summary>
        public JsonObject? Properties { get; set; }

        public BlockBehaviorCarryableInteract(Block block)
            : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            Properties = properties;
            base.Initialize(properties);
            if (JsonHelper.TryGetFloat(properties, "interactDelay", out var d)) InteractDelay = d;
            if (JsonHelper.TryGetString(properties, BehaviorJsonKeys.EnabledCondition, out var e)) EnabledCondition = e ?? string.Empty;

            // Whitelist of carryable blocks that can be used to interact with a block entity
            if (!properties.KeyExists("allowedCarryables")) return;
            var arr = properties["allowedCarryables"]?.AsArray();
            if (arr == null) return;

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

            var carried = carryManager?.GetCarried(player.Entity, CarrySlot.Hands);
            var block = carried?.Block;
            if (block == null) return false;

            foreach (var allowed in AllowedCarryables)
            {
                if (allowed.IsMatch(block)) return true;
            }

            return false;
        }

        /// <summary>
        /// No conditional processing needed for interact-only behavior.
        /// Implemented to satisfy <see cref="IConditionalBlockBehavior"/>.
        /// </summary>
        public void ProcessConditions(ICoreAPI api, Block block)
        {
        }

        public class AllowedCarryable
        {
            public string? Code { get; set; }
            public string? Class { get; set; }

            public bool IsMatch(Block block)
            {
                if (Code != null && block.Code.GetName() == Code) return true;
                if (Class != null && block.Class == Class) return true;
                return false;
            }
        }
    }
}

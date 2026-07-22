using CarryOn.Common.Models;
using CarryOn.Server.Logic.BehavioralConditions;
using Vintagestory.API.Common;

namespace CarryOn.Server.Logic
{
    public class BehavioralConditioning
    {
        public CarryOnConfig Config { get; private set; } = null!;

        private BlockFilter? blockFilter;
        private BehaviorAutoMapper? autoMapper;
        private MultipleBehaviorResolver? behaviorResolver;

        public void Init(ICoreAPI api, CarryOnConfig config)
        {
            if (config == null)
            {
                api.Logger.Error("CarryOn: Config is null in BehavioralConditioning.Init");
                return;
            }
            this.Config = config;

            blockFilter = new BlockFilter(config);
            autoMapper = new BehaviorAutoMapper(config);
            behaviorResolver = new MultipleBehaviorResolver(config);

            blockFilter.RemoveDisabledConditionalBehaviors(api);
            behaviorResolver.Resolve(api);
            autoMapper.AutoMapSimilarCarryables(api);
            autoMapper.AutoMapSimilarCarryableInteract(api);
            blockFilter.RemoveExcludedCarryableBehaviors(api);
        }
    }
}

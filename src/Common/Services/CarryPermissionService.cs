using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Common.Services
{
    internal sealed class CarryPermissionService
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;

        public CarryPermissionService(ICarryManager carryManager, IConfigProvider configProvider)
        {
            this.carryManager = carryManager;
            this.configProvider = configProvider;
        }

        public bool HasPermissionAt(Entity entity, BlockPos pos, bool showErrorMessage = true)
        {
            var isReinforced = entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) ?? false;
            if (entity is EntityPlayer playerEntity)
            {
                var result = entity.World.GetCarryEvents()?.TriggerCheckPermissionAt(playerEntity, pos, isReinforced);
                if (result.HasValue) return result.Value;

                var isCreative = playerEntity.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;
                if (!isCreative && isReinforced) return false;

                // Client-side best-effort: Claims.TestAccess/TryAccess always return Granted on client,
                // so iterate Claims.All for a zero-traffic early warning
                if (entity.Api.Side == EnumAppSide.Client)
                {
                    return HasPermissionAtClient(playerEntity.Player, pos);
                }

                if (showErrorMessage)
                    return entity.World.Claims.TryAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak);
                else
                    return entity.World.Claims.TestAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;
            }
            else
            {
                return !isReinforced;
            }
        }

        private bool HasPermissionAtClient(IPlayer player, BlockPos pos)
        {
            if (configProvider?.Config?.CarryOptions?.ClientSidePermissionCheck != true)
                return true;

            var claims = player.Entity?.World?.Claims?.All;
            if (claims == null)
                return true;

            foreach (var claim in claims)
            {
                if (claim.PositionInside(pos))
                {
                    if (claim.TestPlayerAccess(player, EnumBlockAccessFlags.BuildOrBreak) == EnumPlayerAccessResult.Denied)
                        return false;
                }
            }

            return true;
        }
    }
}

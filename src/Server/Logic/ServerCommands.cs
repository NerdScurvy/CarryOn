using CarryOn.Common.Logic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CarryOn.Server.Logic
{
    public class ServerCommands(ICoreServerAPI api, CarryOnConfigService configService)
    {
        private readonly ICoreServerAPI api = api;
        private readonly CarryOnConfigService configService = configService;

        public void Register()
        {
            api.ChatCommands
                .Create("carryon-reload")
                .WithDescription("Reload CarryOnConfig.json from disk")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ =>
                {
                    configService.Reload();
                    return TextCommandResult.Success("CarryOn config reloaded from file.");
                });
        }
    }
}

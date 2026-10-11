using System.Linq;
using CarryOn.Common.Logic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CarryOn.Server.Logic
{
    public class ServerCommands(ICoreServerAPI api, CarryOnConfigService configService)
    {
        private readonly ICoreServerAPI api = api;
        private readonly CarryOnConfigService configService = configService;

        private const float DefaultRecoverRadius = 15;

        public void Register()
        {
            api.ChatCommands
                .Create("carryon-reload")
                .WithDescription("Reload CarryOnConfig.json from disk")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(args =>
                {
                    api.Logger.Notification("CarryOn: /carryon-reload command received, reloading config from disk");
                    configService.Reload(
                        ConfigReloadOrigin.Command,
                        args.Caller?.Player as IServerPlayer);
                    return BuildReloadCommandResult(configService.LastReloadResult);
                });

            var recovery = new AttachableRecoveryService(api);
            api.ChatCommands
                .Create("carryon-recover")
                .WithDescription("Remove broken attachments (e.g. collapsed chests, trunks) from attachable entities so they render again. Carryable blocks are restored as carried block entities with contents; others are dropped as items near you. Usage: /carryon-recover [radius]; 0 = all loaded entities")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(api.ChatCommands.Parsers.OptionalFloat("radius"))
                .HandleWith(args =>
                {
                    float radius = args[0] is float r ? r : DefaultRecoverRadius;
                    var player = args.Caller?.Player?.Entity;
                    var origin = player?.Pos?.XYZ;
                    var ownerUid = args.Caller?.Player?.PlayerUID;
                    return TextCommandResult.Success(recovery.Sweep(origin, radius, ownerUid, origin));
                });
        }

        /// <summary>
        /// Builds the /carryon-reload reply from the reload outcome: an error joined with
        /// the notices when the reload was rejected (the pre-fix handler returned success
        /// unconditionally, even over a rejected file), otherwise the success line plus the
        /// warning notices. Informational notices are skipped - the success line itself is
        /// the applied confirmation, and repeating it would say the same thing twice.
        /// </summary>
        private static TextCommandResult BuildReloadCommandResult(ConfigReloadResult? result)
        {
            if (result == null)
            {
                return TextCommandResult.Error("CarryOn config was not reloaded (reload result unavailable).");
            }

            if (!result.Applied)
            {
                return TextCommandResult.Error(string.Join("\n", result.Notices.Select(n => n.Message)));
            }

            const string status = "CarryOn config reloaded from file.";
            var warnings = result.Notices
                .Where(n => n.Type == EnumChatType.CommandError)
                .Select(n => n.Message)
                .ToList();
            return warnings.Count == 0
                ? TextCommandResult.Success(status)
                : TextCommandResult.Success(status + "\n" + string.Join("\n", warnings));
        }
    }
}

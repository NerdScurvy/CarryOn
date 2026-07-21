using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Commands
{
    public partial class ClientCommands
    {
        protected TextCommandResult CmdCarryOnAttachedRender(TextCommandCallingArgs args)
        {
            var result = ToggleBoolSetting(args,
                () => this.clientModConfig.Config?.RenderAttachedBlocks ?? true,
                v => { var cfg = this.clientModConfig.Config; cfg?.RenderAttachedBlocks = v; },
                "Rendering of attached wall signs");
            entityCarryRenderer?.SetRenderAttachedBlocks(this.clientModConfig.Config?.RenderAttachedBlocks ?? true);
            return result;
        }

        protected TextCommandResult CmdCarryOnAttachedPickup(TextCommandCallingArgs args)
        {
            return ToggleBoolSetting(args,
                () => this.clientModConfig.Config?.CaptureAttachedWallSigns ?? true,
                v => { var cfg = this.clientModConfig.Config; cfg?.CaptureAttachedWallSigns = v; },
                "Capture of attached wall signs on pickup");
        }
    }
}

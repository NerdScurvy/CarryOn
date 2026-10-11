using System;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Models;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Commands
{
    public partial class ClientCommands
    {
        protected TextCommandResult CmdCarryOnIconMode(TextCommandCallingArgs args)
        {
            if (!TryGetConfig(out var cfg, out var configError))
                return configError!;

            var currentMode = CarryLabelManager.IconTextureMode;

            string detectedGl = CarryLabelManager.GlSupportsTextureSubImage ? "4.3+" : "<4.3";

            if (args[0] is string arg)
            {
                if (!Enum.TryParse<IconTextureMode>(arg, true, out var mode))
                    return TextCommandResult.Error($"Invalid mode '{arg}'. Use: standalone, atlas, standalone-fallback, disabled");

                if (mode == currentMode)
                    return TextCommandResult.Success($"CarryOn icon mode already set to {mode}.");

                CarryLabelManager.IconTextureMode = mode;
                cfg.IconTextureMode = mode;
                this.clientModConfig.Save(this.api);


                return TextCommandResult.Success(
                    $"CarryOn icon mode set to {mode}. (Detected GL: {detectedGl})");
            }

            IconTextureMode next = currentMode switch
            {
                IconTextureMode.Standalone => IconTextureMode.Atlas,
                IconTextureMode.Atlas => IconTextureMode.StandaloneFallback,
                IconTextureMode.StandaloneFallback => IconTextureMode.Disabled,
                _ => IconTextureMode.Standalone
            };

            CarryLabelManager.IconTextureMode = next;
            cfg.IconTextureMode = next;
            this.clientModConfig.Save(this.api);

            return TextCommandResult.Success(
                $"CarryOn icon mode: {currentMode} -> {next}. (Detected GL: {detectedGl})");
        }
    }
}

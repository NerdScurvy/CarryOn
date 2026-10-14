using System;
using System.Diagnostics.CodeAnalysis;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Commands
{
    public partial class ClientCommands
    {
        private readonly ClientModConfig clientModConfig;
        private readonly ICoreClientAPI api;
        private readonly EntityCarryRenderer? entityCarryRenderer;

        public ClientCommands(ICoreClientAPI api, ClientModConfig clientModConfig, EntityCarryRenderer? entityCarryRenderer = null)
        {
            ArgumentNullException.ThrowIfNull(api);
            this.api = api;
            this.clientModConfig = clientModConfig;
            this.entityCarryRenderer = entityCarryRenderer;
        }

        public void Register()
        {
            try
            {
                api.ChatCommands.Create("carryon")
                    .BeginSubCommand("gui")
                        .BeginSubCommand("bg")
                            .WithDescription("Configure anchor background fill (enable/disable/color/alpha/show)")
                            .BeginSubCommand("enable")
                                .WithDescription("Enable anchor background fill")
                                .HandleWith(this.CmdCarryOnGuiBgEnable)
                            .EndSubCommand()
                            .BeginSubCommand("disable")
                                .WithDescription("Disable anchor background fill")
                                .HandleWith(this.CmdCarryOnGuiBgDisable)
                            .EndSubCommand()
                            .BeginSubCommand("color")
                                .WithDescription("Set anchor background fill color as hex (e.g. #e4c4a6)")
                                .WithArgs(api.ChatCommands.Parsers.Word("hex"))
                                .HandleWith(this.CmdCarryOnGuiBgColor)
                            .EndSubCommand()
                            .BeginSubCommand("alpha")
                                .WithDescription("Set anchor background alpha (0.0 - 1.0)")
                                .WithArgs(api.ChatCommands.Parsers.Float("alpha"))
                                .HandleWith(this.CmdCarryOnGuiBgAlpha)
                            .EndSubCommand()
                            .BeginSubCommand("show")
                                .WithDescription("Show current anchor background settings (runtime and saved)")
                                .HandleWith(this.CmdCarryOnGuiBgShow)
                            .EndSubCommand()
                            .BeginSubCommand("reset")
                                .WithDescription("Reset anchor background to defaults (enabled, color, alpha)")
                                .HandleWith(this.CmdCarryOnGuiBgReset)
                            .EndSubCommand()
                        .EndSubCommand()
                        .BeginSubCommand("border")
                            .WithDescription("Configure anchor border outline (enable/disable/color/alpha/show/reset)")
                            .BeginSubCommand("enable")
                                .WithDescription("Enable anchor border outline")
                                .HandleWith(this.CmdCarryOnGuiBorderEnable)
                            .EndSubCommand()
                            .BeginSubCommand("disable")
                                .WithDescription("Disable anchor border outline")
                                .HandleWith(this.CmdCarryOnGuiBorderDisable)
                            .EndSubCommand()
                            .BeginSubCommand("color")
                                .WithDescription("Set anchor border color as hex (e.g. #45372D)")
                                .WithArgs(api.ChatCommands.Parsers.Word("hex"))
                                .HandleWith(this.CmdCarryOnGuiBorderColor)
                            .EndSubCommand()
                            .BeginSubCommand("alpha")
                                .WithDescription("Set anchor border alpha (0.0 - 1.0)")
                                .WithArgs(api.ChatCommands.Parsers.Float("alpha"))
                                .HandleWith(this.CmdCarryOnGuiBorderAlpha)
                            .EndSubCommand()
                            .BeginSubCommand("reset")
                                .WithDescription("Reset anchor border to defaults")
                                .HandleWith(this.CmdCarryOnGuiBorderReset)
                            .EndSubCommand()
                            .BeginSubCommand("show")
                                .WithDescription("Show current anchor border settings (runtime and saved)")
                                .HandleWith(this.CmdCarryOnGuiBorderShow)
                            .EndSubCommand()
                        .EndSubCommand()
                        .BeginSubCommand("highlight")
                            .WithDescription("Configure icon highlight (enable/disable/color/alpha/show/reset)")
                            .BeginSubCommand("enable")
                                .WithDescription("Enable icon highlight")
                                .HandleWith(this.CmdCarryOnGuiHighlightEnable)
                            .EndSubCommand()
                            .BeginSubCommand("disable")
                                .WithDescription("Disable icon highlight")
                                .HandleWith(this.CmdCarryOnGuiHighlightDisable)
                            .EndSubCommand()
                            .BeginSubCommand("color")
                                .WithDescription("Set icon highlight color as hex (e.g. #FFFFFF)")
                                .WithArgs(api.ChatCommands.Parsers.Word("hex"))
                                .HandleWith(this.CmdCarryOnGuiHighlightColor)
                            .EndSubCommand()
                            .BeginSubCommand("alpha")
                                .WithDescription("Set icon highlight alpha (0.0 - 1.0)")
                                .WithArgs(api.ChatCommands.Parsers.Float("alpha"))
                                .HandleWith(this.CmdCarryOnGuiHighlightAlpha)
                            .EndSubCommand()
                            .BeginSubCommand("reset")
                                .WithDescription("Reset icon highlight to defaults")
                                .HandleWith(this.CmdCarryOnGuiHighlightReset)
                            .EndSubCommand()
                            .BeginSubCommand("show")
                                .WithDescription("Show current icon highlight settings (runtime and saved)")
                                .HandleWith(this.CmdCarryOnGuiHighlightShow)
                            .EndSubCommand()
                        .EndSubCommand()
                        .BeginSubCommand("show")
                            .WithDescription("Show current CarryOn GUI anchor assignments")
                            .HandleWith(this.CmdCarryOnGuiShow)
                        .EndSubCommand()
                        .BeginSubCommand("reset")
                            .WithDescription("Reset CarryOn GUI to defaults (anchors and visuals)")
                            .HandleWith(this.CmdCarryOnGuiReset)
                        .EndSubCommand()
                        .BeginSubCommand("set")
                            .WithDescription("Set or clear carry slot anchors. Usage: .carryon gui set L1 hands | R2 back | R1 clear")
                            .WithArgs(api.ChatCommands.Parsers.Word("anchor"), api.ChatCommands.Parsers.Word("slot"))
                            .HandleWith(this.CmdCarryOnGuiSet)
                        .EndSubCommand()
                    .EndSubCommand()
                    .BeginSubCommand("attachedRender")
                        .WithDescription("Toggle or set rendering of attached wall signs on carried blocks. Usage: .carryon attachedRender [true|false]")
                        .WithArgs(api.ChatCommands.Parsers.OptionalWord("enabled"))
                        .HandleWith(CmdCarryOnAttachedRender)
                    .EndSubCommand()
                    .BeginSubCommand("attachedPickup")
                        .WithDescription("Toggle or set whether attached wall signs are captured when picking up a block. Usage: .carryon attachedPickup [true|false]")
                        .WithArgs(api.ChatCommands.Parsers.OptionalWord("enabled"))
                        .HandleWith(CmdCarryOnAttachedPickup)
                    .EndSubCommand()
                    .BeginSubCommand("iconmode")
                        .WithDescription("Set icon texture mode. Usage: .carryon iconmode [auto|atlas|standalone]")
                        .WithArgs(api.ChatCommands.Parsers.OptionalWord("mode"))
                        .HandleWith(CmdCarryOnIconMode)
                    .EndSubCommand();
            }
            catch (System.Exception ex)
            {
                api.World.Logger.Warning("CarryOn: Failed to register client chat command for GUI debug: " + ex.Message);
            }
        }

        private TextCommandResult ApplySetting(
            Action<CarryOnClientConfig> updateConfig,
            string actionDesc,
            string successMsg)
        {
            return ApplyAndSaveConfig(
                    updateConfig,
                    $"Error {actionDesc}",
                    $"Failed {actionDesc} due to an error.")
                ?? TextCommandResult.Success(successMsg);
        }

        private TextCommandResult ShowSetting(
            string formatted,
            string elementName)
        {
            return TextCommandResult.Success($"CarryOn {elementName} - {formatted}");
        }

        private TextCommandResult MoveAnchor(
            HudCarried.Anchor target,
            System.Func<CarryOnClientConfig, HudCarried.Anchor> getSlot,
            Action<CarryOnClientConfig, HudCarried.Anchor> setSlot,
            System.Func<CarryOnClientConfig, HudCarried.Anchor> getOtherSlot,
            Action<CarryOnClientConfig, HudCarried.Anchor> setOtherSlot,
            string slotName)
        {
            if (!TryGetConfig(out var cfg, out var configError))
                return configError!;

            if (getSlot(cfg) == target)
                return TextCommandResult.Success($"{slotName} already at {target}");

            if (getSlot(cfg) != HudCarried.Anchor.None)
                setSlot(cfg, HudCarried.Anchor.None);

            if (getOtherSlot(cfg) == target)
                setOtherSlot(cfg, HudCarried.Anchor.None);

            setSlot(cfg, target);

            return SaveWithErrorHandling($"Error moving {slotName} anchor", $"Failed to move {slotName} anchor due to an error.")
                ?? TextCommandResult.Success($"{slotName} moved to {target}");
        }

        private TextCommandResult ToggleBoolSetting(
            TextCommandCallingArgs args,
            System.Func<bool> getCurrentValue,
            Action<bool> updateConfigValue,
            string displayName)
        {
            if (!TryGetConfig(out var cfg, out var configError))
                return configError!;

            bool newValue;
            if (args[0] is string arg)
            {
                if (arg == "true") newValue = true;
                else if (arg == "false") newValue = false;
                else return TextCommandResult.Error($"Usage: .carryon {displayName.ToLowerInvariant()} [true|false]");
            }
            else
            {
                newValue = !getCurrentValue();
            }

            updateConfigValue(newValue);
            this.clientModConfig.Save(this.api);

            string state = newValue ? "enabled" : "disabled";
            return TextCommandResult.Success($"{displayName}: {state}");
        }

        private TextCommandResult ResetAndPersist(
            Action<CarryOnClientConfig> applyDefaults,
            string description)
        {
            return ApplyAndSaveConfig(
                    applyDefaults,
                    $"Error resetting {description}",
                    $"Failed to reset {description} due to an error.")
                ?? TextCommandResult.Success($"CarryOn GUI reset to defaults");
        }

        private bool TryGetConfig([NotNullWhen(true)] out CarryOnClientConfig? cfg, out TextCommandResult? error)
        {
            cfg = this.clientModConfig.Config;
            if (cfg == null)
            {
                error = TextCommandResult.Error("Client config not available.");
                return false;
            }
            error = null;
            return true;
        }

        private TextCommandResult? SaveWithErrorHandling(string logPrefix, string failureMessage)
        {
            try
            {
                this.clientModConfig.Save(this.api);
                return null;
            }
            catch (Exception ex)
            {
                this.api.Logger.Error($"{logPrefix}: " + ex);
                return TextCommandResult.Error(failureMessage);
            }
        }

        private TextCommandResult? ApplyAndSaveConfig(
            Action<CarryOnClientConfig> apply,
            string logPrefix,
            string failureMessage)
        {
            try
            {
                var cfg = this.clientModConfig.Config;
                if (cfg != null)
                {
                    apply(cfg);
                    this.clientModConfig.Save(this.api);
                }
            }
            catch (Exception ex)
            {
                this.api.Logger.Error($"{logPrefix}: " + ex);
                return TextCommandResult.Error(failureMessage);
            }
            return null;
        }
    }
}

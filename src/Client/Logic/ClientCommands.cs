using System;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
    
namespace CarryOn.Client.Logic
{


    public class ClientCommands

    {
        private readonly ClientModConfig clientModConfig;
        private readonly ICoreClientAPI api;

        public ClientCommands(ICoreClientAPI api, ClientModConfig clientModConfig)
        {
            ArgumentNullException.ThrowIfNull(api);
            this.api = api;
            this.clientModConfig = clientModConfig;
        }
        /// <summary>
        /// Register client-side chat commands for the CarryOn mod.
        /// </summary>
        public void Register()
        {
            try
            {
                api.ChatCommands.Create("carryon")
                    .BeginSubCommand("gui")
                        // .carryon gui bg ... (background fill settings)
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
                        // .carryon gui border ... (border outline settings)
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
                        // .carryon gui highlight ... (icon highlight settings)
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
                        // .carryon gui show
                        .BeginSubCommand("show")
                            .WithDescription("Show current CarryOn GUI anchor assignments")
                            .HandleWith(this.CmdCarryOnGuiShow)
                        .EndSubCommand()
                        // .carryon gui reset
                        .BeginSubCommand("reset")
                            .WithDescription("Reset CarryOn GUI to defaults (anchors and visuals)")
                            .HandleWith(this.CmdCarryOnGuiReset)
                        .EndSubCommand()
                        // .carryon gui <anchor> <hands|back|clear>
                        .BeginSubCommand("set")
                            .WithDescription("Set or clear carry slot anchors. Usage: .carryon gui set L1 hands | R2 back | R1 clear")
                            .WithArgs(api.ChatCommands.Parsers.Word("anchor"), api.ChatCommands.Parsers.Word("slot"))
                            .HandleWith(this.CmdCarryOnGuiSet)
                        .EndSubCommand()
                    .EndSubCommand();
            }
            catch (System.Exception ex)
            {
                api.World.Logger.Warning("CarryOn: Failed to register client chat command for GUI debug: " + ex.Message);
            }
        }

        protected TextCommandResult CmdCarryOnGuiSet(TextCommandCallingArgs args)
        {
            string anchorStr = (args[0] as string)?.ToUpperInvariant() ?? "";
            string slotStr = (args[1] as string)?.ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(anchorStr) || string.IsNullOrEmpty(slotStr))
            {
                return TextCommandResult.Error("Usage: .carryon gui set L1 hands | R2 back | R1 clear");
            }

            // Parse anchor
            if (!System.Enum.TryParse<HudCarried.Anchor>(anchorStr, true, out var anchor))
            {
                return TextCommandResult.Error("Invalid anchor. Use L1,L2,L3,R1,R2,R3");
            }

            // Determine action: assign Hands, Back, or clear
            if (slotStr == "hands")
            {
                // Remove hands from any previous anchor
                if (HudCarried.HandsAnchor != HudCarried.Anchor.None)
                {
                    // If moving to same anchor, do nothing
                    if (HudCarried.HandsAnchor == anchor)
                    {
                        return TextCommandResult.Success($"Hands already at {anchor}");
                    }
                    HudCarried.HandsAnchor = HudCarried.Anchor.None;
                }

                // If Back is currently at the destination anchor, clear it
                if (HudCarried.BackAnchor == anchor) HudCarried.BackAnchor = HudCarried.Anchor.None;

                HudCarried.HandsAnchor = anchor;

                // Persist change to client config
                try
                {
                    var cfg = this.clientModConfig.Config;
                    if (cfg != null)
                    {
                        cfg.HandsAnchor = HudCarried.HandsAnchor.ToString();
                        this.clientModConfig.Save(this.api);
                    }
                }
                catch (System.Exception ex)
                {
                    this.api.Logger.Error("Error moving Hands anchor: " + ex);
                    return TextCommandResult.Error("Failed to move Hands anchor due to an error.");
                }

                return TextCommandResult.Success($"Hands moved to {anchor}");
            }

            if (slotStr == "back")
            {
                if (HudCarried.BackAnchor != HudCarried.Anchor.None)
                {
                    if (HudCarried.BackAnchor == anchor)
                    {
                        return TextCommandResult.Success($"Back already at {anchor}");
                    }
                    HudCarried.BackAnchor = HudCarried.Anchor.None;
                }

                if (HudCarried.HandsAnchor == anchor) HudCarried.HandsAnchor = HudCarried.Anchor.None;

                HudCarried.BackAnchor = anchor;

                // Persist change to client config
                try
                {
                    var cfg = this.clientModConfig.Config;
                    if (cfg != null)
                    {
                        cfg.BackAnchor = HudCarried.BackAnchor.ToString();
                        this.clientModConfig.Save(this.api);
                    }
                }
                catch (System.Exception ex)
                {
                    this.api.Logger.Error("Error moving Back anchor: " + ex);
                    return TextCommandResult.Error("Failed to move Back anchor due to an error.");
                }

                return TextCommandResult.Success($"Back moved to {anchor}");
            }

            if (slotStr == "clear")
            {
                // Clear whatever is at the anchor
                bool cleared = false;
                if (HudCarried.HandsAnchor == anchor)
                {
                    HudCarried.HandsAnchor = HudCarried.Anchor.None;
                    cleared = true;
                }
                if (HudCarried.BackAnchor == anchor)
                {
                    HudCarried.BackAnchor = HudCarried.Anchor.None;
                    cleared = true;
                }

                if (cleared)
                {
                    try
                    {
                        var cfg = this.clientModConfig.Config;
                        if (cfg != null)
                        {
                            cfg.HandsAnchor = HudCarried.HandsAnchor.ToString();
                            cfg.BackAnchor = HudCarried.BackAnchor.ToString();
                            this.clientModConfig.Save(this.api);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        this.api.Logger.Error("Error clearing anchor: " + ex);
                        return TextCommandResult.Error("Failed to clear anchor due to an error.");
                    }

                    return TextCommandResult.Success($"Cleared anchor {anchor}");
                }

                return TextCommandResult.Error($"Anchor {anchor} was already empty");
            }

            return TextCommandResult.Error("Invalid slot. Use 'hands', 'back', or 'clear'");
        }

        protected TextCommandResult CmdCarryOnGuiReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            // Reset anchors to defaults and reset all GUI visuals (background, border, highlight) to defaults
            HudCarried.HandsAnchor = HudCarried.HandsAnchorDefault;
            HudCarried.BackAnchor = HudCarried.BackAnchorDefault;

            // Reset background settings
            HudCarried.AnchorBackgroundEnabled = true;
            HudCarried.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
            HudCarried.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;

            // Reset border settings
            HudCarried.AnchorBorderEnabled = true;
            HudCarried.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
            HudCarried.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;

            // Reset icon highlight settings
            HudCarried.IconHighlightEnabled = true;
            HudCarried.IconHighlightColor = HudCarried.IconHighlightColorDefault;
            HudCarried.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;

            // Refresh parsed color cache so HUD updates immediately
            HudCarried.UpdateParsedColors();

            try
            {
                var cfg = this.clientModConfig.Config;
                if (cfg != null)
                {
                    cfg.HandsAnchor = HudCarried.HandsAnchor.ToString();
                    cfg.BackAnchor = HudCarried.BackAnchor.ToString();

                    cfg.AnchorBackgroundEnabled = HudCarried.AnchorBackgroundEnabled;
                    cfg.AnchorBackgroundColor = HudCarried.AnchorBackgroundColor;
                    cfg.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlpha;

                    cfg.AnchorBorderEnabled = HudCarried.AnchorBorderEnabled;
                    cfg.AnchorBorderColor = HudCarried.AnchorBorderColor;
                    cfg.AnchorBorderAlpha = HudCarried.AnchorBorderAlpha;

                    cfg.IconHighlightEnabled = HudCarried.IconHighlightEnabled;
                    cfg.IconHighlightColor = HudCarried.IconHighlightColor;
                    cfg.IconHighlightAlpha = HudCarried.IconHighlightAlpha;

                    this.clientModConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error resetting CarryOn GUI anchors and visuals: " + ex);
                return TextCommandResult.Error("Failed to reset CarryOn GUI anchors and visuals due to an error.");
            }

            string msg = $"CarryOn GUI reset: Anchors -> Hands={HudCarried.HandsAnchor}, Back={HudCarried.BackAnchor}; visuals reset to defaults";
            return TextCommandResult.Success(msg);
        }

        protected TextCommandResult CmdCarryOnGuiShow(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            // Show the effective anchors (runtime values)
            var hands = HudCarried.HandsAnchor.ToString();
            var back = HudCarried.BackAnchor.ToString();

            // If a client config exists, include saved values too
            string? saved = null;
            try
            {
                var cfg = this.clientModConfig.Config;
                if (cfg != null)
                {
                    saved = $"Saved: Hands={cfg.HandsAnchor}, Back={cfg.BackAnchor}";
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error showing CarryOn GUI anchors: " + ex);
            }

            string msg = $"CarryOn GUI anchors — Runtime: Hands={hands}, Back={back}" + (saved != null ? " | " + saved : "");
            return TextCommandResult.Success(msg);
        }

        private enum GuiCategory { Background, Border, Highlight }

        private TextCommandResult HandleGuiEnable(GuiCategory cat, TextCommandCallingArgs _)
        {
            var (setEnabled, setCfg) = cat switch
            {
                GuiCategory.Background => (
                    (Action<bool>)(v => HudCarried.AnchorBackgroundEnabled = v),
                    (Action<CarryOnClientConfig, bool>)((cfg, v) => cfg.AnchorBackgroundEnabled = v)),
                GuiCategory.Border => (
                    v => HudCarried.AnchorBorderEnabled = v,
                    (cfg, v) => cfg.AnchorBorderEnabled = v),
                GuiCategory.Highlight => (
                    v => HudCarried.IconHighlightEnabled = v,
                    (cfg, v) => cfg.IconHighlightEnabled = v),
                _ => throw new ArgumentOutOfRangeException(nameof(cat))
            };
            return ApplySetting(
                () => setEnabled(true),
                cfg => setCfg(cfg, true),
                $"enabling CarryOn {cat.ToString().ToLowerInvariant()}",
                $"CarryOn {cat.ToString().ToLowerInvariant()}: enabled");
        }

        private TextCommandResult HandleGuiDisable(GuiCategory cat, TextCommandCallingArgs _)
        {
            var (setEnabled, setCfg) = cat switch
            {
                GuiCategory.Background => (
                    (Action<bool>)(v => HudCarried.AnchorBackgroundEnabled = v),
                    (Action<CarryOnClientConfig, bool>)((cfg, v) => cfg.AnchorBackgroundEnabled = v)),
                GuiCategory.Border => (
                    v => HudCarried.AnchorBorderEnabled = v,
                    (cfg, v) => cfg.AnchorBorderEnabled = v),
                GuiCategory.Highlight => (
                    v => HudCarried.IconHighlightEnabled = v,
                    (cfg, v) => cfg.IconHighlightEnabled = v),
                _ => throw new ArgumentOutOfRangeException(nameof(cat))
            };
            return ApplySetting(
                () => setEnabled(false),
                cfg => setCfg(cfg, false),
                $"disabling CarryOn {cat.ToString().ToLowerInvariant()}",
                $"CarryOn {cat.ToString().ToLowerInvariant()}: disabled");
        }

        private TextCommandResult HandleGuiColor(GuiCategory cat, TextCommandCallingArgs args)
        {
            string hex = (args[0] as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error($"Usage: .carryon gui {cat.ToString().ToLowerInvariant()} color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out var normalized))
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            hex = normalized!;

            var (setColor, setCfg) = cat switch
            {
                GuiCategory.Background => (
                    (Action<string>)(v => HudCarried.AnchorBackgroundColor = v),
                    (Action<CarryOnClientConfig, string>)((cfg, v) => cfg.AnchorBackgroundColor = v)),
                GuiCategory.Border => (
                    v => HudCarried.AnchorBorderColor = v,
                    (cfg, v) => cfg.AnchorBorderColor = v),
                GuiCategory.Highlight => (
                    v => HudCarried.IconHighlightColor = v,
                    (cfg, v) => cfg.IconHighlightColor = v),
                _ => throw new ArgumentOutOfRangeException(nameof(cat))
            };
            return ApplySetting(
                () => setColor(hex),
                cfg => setCfg(cfg, hex),
                $"setting CarryOn {cat.ToString().ToLowerInvariant()} color",
                $"CarryOn {cat.ToString().ToLowerInvariant()} color set to {hex}");
        }

        private TextCommandResult HandleGuiAlpha(GuiCategory cat, TextCommandCallingArgs args)
        {
            float a = args[0] is float floatVal ? floatVal : 0f;
            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            var (setAlpha, setCfg) = cat switch
            {
                GuiCategory.Background => (
                    (Action<float>)(v => HudCarried.AnchorBackgroundAlpha = v),
                    (Action<CarryOnClientConfig, float>)((cfg, v) => cfg.AnchorBackgroundAlpha = v)),
                GuiCategory.Border => (
                    v => HudCarried.AnchorBorderAlpha = v,
                    (cfg, v) => cfg.AnchorBorderAlpha = v),
                GuiCategory.Highlight => (
                    v => HudCarried.IconHighlightAlpha = v,
                    (cfg, v) => cfg.IconHighlightAlpha = v),
                _ => throw new ArgumentOutOfRangeException(nameof(cat))
            };
            return ApplySetting(
                () => setAlpha(a),
                cfg => setCfg(cfg, a),
                $"setting CarryOn {cat.ToString().ToLowerInvariant()} alpha",
                $"CarryOn {cat.ToString().ToLowerInvariant()} alpha set to {a:0.##}");
        }

        private TextCommandResult HandleGuiShow(GuiCategory cat, TextCommandCallingArgs _)
        {
            System.Func<bool> runtimeEnabled;
            System.Func<string> runtimeColor;
            System.Func<float> runtimeAlpha;
            System.Func<CarryOnClientConfig, bool> cfgEnabled;
            System.Func<CarryOnClientConfig, string> cfgColor;
            System.Func<CarryOnClientConfig, float> cfgAlpha;
            string name;

            switch (cat)
            {
                case GuiCategory.Background:
                    runtimeEnabled = () => HudCarried.AnchorBackgroundEnabled;
                    runtimeColor = () => HudCarried.AnchorBackgroundColor;
                    runtimeAlpha = () => HudCarried.AnchorBackgroundAlpha;
                    cfgEnabled = cfg => cfg.AnchorBackgroundEnabled;
                    cfgColor = cfg => cfg.AnchorBackgroundColor;
                    cfgAlpha = cfg => cfg.AnchorBackgroundAlpha;
                    name = "anchor background";
                    break;
                case GuiCategory.Border:
                    runtimeEnabled = () => HudCarried.AnchorBorderEnabled;
                    runtimeColor = () => HudCarried.AnchorBorderColor;
                    runtimeAlpha = () => HudCarried.AnchorBorderAlpha;
                    cfgEnabled = cfg => cfg.AnchorBorderEnabled;
                    cfgColor = cfg => cfg.AnchorBorderColor;
                    cfgAlpha = cfg => cfg.AnchorBorderAlpha;
                    name = "anchor border";
                    break;
                case GuiCategory.Highlight:
                    runtimeEnabled = () => HudCarried.IconHighlightEnabled;
                    runtimeColor = () => HudCarried.IconHighlightColor;
                    runtimeAlpha = () => HudCarried.IconHighlightAlpha;
                    cfgEnabled = cfg => cfg.IconHighlightEnabled;
                    cfgColor = cfg => cfg.IconHighlightColor;
                    cfgAlpha = cfg => cfg.IconHighlightAlpha;
                    name = "icon highlight";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cat));
            }

            return ShowSetting(
                $"Runtime: enabled={runtimeEnabled()}, color={runtimeColor()}, alpha={runtimeAlpha():0.##}",
                cfg => $"Saved: enabled={cfgEnabled(cfg)}, color={cfgColor(cfg)}, alpha={cfgAlpha(cfg):0.##}",
                name);
        }

        private TextCommandResult HandleGuiReset(GuiCategory cat, TextCommandCallingArgs _)
        {
            Action resetRuntime;
            Action<CarryOnClientConfig> resetCfg;
            Func<string> defaultsDesc;

            switch (cat)
            {
                case GuiCategory.Background:
                    resetRuntime = () =>
                    {
                        HudCarried.AnchorBackgroundEnabled = true;
                        HudCarried.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
                        HudCarried.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;
                    };
                    resetCfg = cfg =>
                    {
                        cfg.AnchorBackgroundEnabled = HudCarried.AnchorBackgroundEnabled;
                        cfg.AnchorBackgroundColor = HudCarried.AnchorBackgroundColor;
                        cfg.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlpha;
                    };
                    defaultsDesc = () => $"enabled={HudCarried.AnchorBackgroundEnabled}, color={HudCarried.AnchorBackgroundColor}, alpha={HudCarried.AnchorBackgroundAlpha:0.##}";
                    break;
                case GuiCategory.Border:
                    resetRuntime = () =>
                    {
                        HudCarried.AnchorBorderEnabled = true;
                        HudCarried.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
                        HudCarried.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;
                    };
                    resetCfg = cfg =>
                    {
                        cfg.AnchorBorderEnabled = HudCarried.AnchorBorderEnabled;
                        cfg.AnchorBorderColor = HudCarried.AnchorBorderColor;
                        cfg.AnchorBorderAlpha = HudCarried.AnchorBorderAlpha;
                    };
                    defaultsDesc = () => $"enabled={HudCarried.AnchorBorderEnabled}, color={HudCarried.AnchorBorderColor}, alpha={HudCarried.AnchorBorderAlpha:0.##}";
                    break;
                case GuiCategory.Highlight:
                    resetRuntime = () =>
                    {
                        HudCarried.IconHighlightEnabled = true;
                        HudCarried.IconHighlightColor = HudCarried.IconHighlightColorDefault;
                        HudCarried.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;
                    };
                    resetCfg = cfg =>
                    {
                        cfg.IconHighlightEnabled = HudCarried.IconHighlightEnabled;
                        cfg.IconHighlightColor = HudCarried.IconHighlightColor;
                        cfg.IconHighlightAlpha = HudCarried.IconHighlightAlpha;
                    };
                    defaultsDesc = () => $"enabled={HudCarried.IconHighlightEnabled}, color={HudCarried.IconHighlightColor}, alpha={HudCarried.IconHighlightAlpha:0.##}";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cat));
            }

            return ApplySetting(
                resetRuntime,
                resetCfg,
                $"resetting CarryOn {cat.ToString().ToLowerInvariant()}",
                $"CarryOn {cat.ToString().ToLowerInvariant()} reset to defaults: {defaultsDesc()}");
        }

        private TextCommandResult ApplySetting(
            Action updateRuntime,
            Action<CarryOnClientConfig> updateConfig,
            string actionDesc,
            string successMsg)
        {
            try
            {
                updateRuntime();
                HudCarried.UpdateParsedColors();
                var cfg = this.clientModConfig.Config;
                if (cfg != null)
                {
                    updateConfig(cfg);
                    this.clientModConfig.Save(this.api);
                }
            }
            catch (Exception ex)
            {
                this.api.Logger.Error($"Error {actionDesc}: " + ex);
                return TextCommandResult.Error($"Failed {actionDesc} due to an error.");
            }
            return TextCommandResult.Success(successMsg);
        }

        private TextCommandResult ShowSetting(
            string runtimeFormatted,
            System.Func<CarryOnClientConfig, string> formatSaved,
            string elementName)
        {
            string saved = "Saved: (none)";
            try
            {
                var cfg = this.clientModConfig.Config;
                if (cfg != null) saved = formatSaved(cfg);
            }
            catch (Exception ex)
            {
                this.api.Logger.Error($"Error showing CarryOn GUI {elementName} settings: " + ex);
            }
            return TextCommandResult.Success($"CarryOn {elementName} — {runtimeFormatted} | {saved}");
        }

        // === Background subcommand handlers ===

        protected TextCommandResult CmdCarryOnGuiBgEnable(TextCommandCallingArgs args) => HandleGuiEnable(GuiCategory.Background, args);
        protected TextCommandResult CmdCarryOnGuiBgDisable(TextCommandCallingArgs args) => HandleGuiDisable(GuiCategory.Background, args);
        protected TextCommandResult CmdCarryOnGuiBgColor(TextCommandCallingArgs args) => HandleGuiColor(GuiCategory.Background, args);
        protected TextCommandResult CmdCarryOnGuiBgAlpha(TextCommandCallingArgs args) => HandleGuiAlpha(GuiCategory.Background, args);
        protected TextCommandResult CmdCarryOnGuiBgShow(TextCommandCallingArgs args) => HandleGuiShow(GuiCategory.Background, args);
        protected TextCommandResult CmdCarryOnGuiBgReset(TextCommandCallingArgs args) => HandleGuiReset(GuiCategory.Background, args);

        // === Border subcommand handlers ===

        protected TextCommandResult CmdCarryOnGuiBorderEnable(TextCommandCallingArgs args) => HandleGuiEnable(GuiCategory.Border, args);
        protected TextCommandResult CmdCarryOnGuiBorderDisable(TextCommandCallingArgs args) => HandleGuiDisable(GuiCategory.Border, args);
        protected TextCommandResult CmdCarryOnGuiBorderColor(TextCommandCallingArgs args) => HandleGuiColor(GuiCategory.Border, args);
        protected TextCommandResult CmdCarryOnGuiBorderAlpha(TextCommandCallingArgs args) => HandleGuiAlpha(GuiCategory.Border, args);
        protected TextCommandResult CmdCarryOnGuiBorderShow(TextCommandCallingArgs args) => HandleGuiShow(GuiCategory.Border, args);
        protected TextCommandResult CmdCarryOnGuiBorderReset(TextCommandCallingArgs args) => HandleGuiReset(GuiCategory.Border, args);

        // === Highlight subcommand handlers ===

        protected TextCommandResult CmdCarryOnGuiHighlightEnable(TextCommandCallingArgs args) => HandleGuiEnable(GuiCategory.Highlight, args);
        protected TextCommandResult CmdCarryOnGuiHighlightDisable(TextCommandCallingArgs args) => HandleGuiDisable(GuiCategory.Highlight, args);
        protected TextCommandResult CmdCarryOnGuiHighlightColor(TextCommandCallingArgs args) => HandleGuiColor(GuiCategory.Highlight, args);
        protected TextCommandResult CmdCarryOnGuiHighlightAlpha(TextCommandCallingArgs args) => HandleGuiAlpha(GuiCategory.Highlight, args);
        protected TextCommandResult CmdCarryOnGuiHighlightShow(TextCommandCallingArgs args) => HandleGuiShow(GuiCategory.Highlight, args);
        protected TextCommandResult CmdCarryOnGuiHighlightReset(TextCommandCallingArgs args) => HandleGuiReset(GuiCategory.Highlight, args);
    }
}

namespace CarryOn.Client.Logic
{
    using System;
    using CarryOn.Client.Models;
    using CarryOn.Utility;
    using Vintagestory.API.Common;

    public class ClientCommands

    {
        private readonly CarrySystem carrySystem;
        private readonly Vintagestory.API.Client.ICoreClientAPI api = null!;

        public ClientCommands(CarrySystem carrySystem)
        {
            ArgumentNullException.ThrowIfNull(carrySystem);
            this.carrySystem = carrySystem;
            this.api = carrySystem.ClientApi ?? throw new InvalidOperationException("Client API not available in ClientCommands");
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
                    var clientCfg = this.carrySystem?.ClientConfig;
                    if (clientCfg != null)
                    {
                        var cfg = clientCfg.Config!;
                        cfg.HandsAnchor = HudCarried.HandsAnchor.ToString();
                        clientCfg.Save(this.api);
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
                    var clientCfg = this.carrySystem?.ClientConfig;
                    if (clientCfg != null)
                    {
                        var cfg = clientCfg.Config!;
                        cfg.BackAnchor = HudCarried.BackAnchor.ToString();
                        clientCfg.Save(this.api);
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
                        var clientCfg = this.carrySystem?.ClientConfig;
                        if (clientCfg != null)
                        {
                            var cfg = clientCfg.Config!;
                            cfg.HandsAnchor = HudCarried.HandsAnchor.ToString();
                            cfg.BackAnchor = HudCarried.BackAnchor.ToString();
                            clientCfg.Save(this.api);
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
                var clientCfg = this.carrySystem?.ClientConfig;
                if (clientCfg != null)
                {
                    var cfg = clientCfg.Config!;
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

                    clientCfg.Save(this.api);
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
                var cfg = this.carrySystem?.ClientConfig?.Config;
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
                var clientCfg = this.carrySystem?.ClientConfig;
                if (clientCfg != null && clientCfg.Config != null)
                {
                    updateConfig(clientCfg.Config);
                    clientCfg.Save(this.api);
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
                var cfg = this.carrySystem?.ClientConfig?.Config;
                if (cfg != null) saved = formatSaved(cfg);
            }
            catch (Exception ex)
            {
                this.api.Logger.Error($"Error showing CarryOn GUI {elementName} settings: " + ex);
            }
            return TextCommandResult.Success($"CarryOn {elementName} — {runtimeFormatted} | {saved}");
        }

        // === Background subcommand handlers ===

        protected TextCommandResult CmdCarryOnGuiBgEnable(Vintagestory.API.Common.TextCommandCallingArgs args) =>
            ApplySetting(
                () => HudCarried.AnchorBackgroundEnabled = true,
                cfg => cfg.AnchorBackgroundEnabled = true,
                "enabling CarryOn anchor background",
                "CarryOn anchor background: enabled");

        protected TextCommandResult CmdCarryOnGuiBgDisable(Vintagestory.API.Common.TextCommandCallingArgs args) =>
            ApplySetting(
                () => HudCarried.AnchorBackgroundEnabled = false,
                cfg => cfg.AnchorBackgroundEnabled = false,
                "disabling CarryOn anchor background",
                "CarryOn anchor background: disabled");

        protected TextCommandResult CmdCarryOnGuiBgColor(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string hex = (args[0] as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error("Usage: .carryon gui bg color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out var normalized))
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            hex = normalized!;

            return ApplySetting(
                () => HudCarried.AnchorBackgroundColor = hex,
                cfg => cfg.AnchorBackgroundColor = hex,
                "setting CarryOn anchor background color",
                $"CarryOn anchor background color set to {hex}");
        }

        protected TextCommandResult CmdCarryOnGuiBgAlpha(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            float a = args[0] is float floatVal ? floatVal : 0f;
            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            return ApplySetting(
                () => HudCarried.AnchorBackgroundAlpha = a,
                cfg => cfg.AnchorBackgroundAlpha = a,
                "setting CarryOn anchor background alpha",
                $"CarryOn anchor background alpha set to {a:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiBgShow(Vintagestory.API.Common.TextCommandCallingArgs args) =>
            ShowSetting(
                $"Runtime: enabled={HudCarried.AnchorBackgroundEnabled}, color={HudCarried.AnchorBackgroundColor}, alpha={HudCarried.AnchorBackgroundAlpha:0.##}",
                cfg => $"Saved: enabled={cfg.AnchorBackgroundEnabled}, color={cfg.AnchorBackgroundColor}, alpha={cfg.AnchorBackgroundAlpha:0.##}",
                "anchor background");

        protected TextCommandResult CmdCarryOnGuiBgReset(Vintagestory.API.Common.TextCommandCallingArgs args) =>
            ApplySetting(
                () =>
                {
                    HudCarried.AnchorBackgroundEnabled = true;
                    HudCarried.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
                    HudCarried.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;
                },
                cfg =>
                {
                    cfg.AnchorBackgroundEnabled = HudCarried.AnchorBackgroundEnabled;
                    cfg.AnchorBackgroundColor = HudCarried.AnchorBackgroundColor;
                    cfg.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlpha;
                },
                "resetting CarryOn anchor background",
                $"CarryOn anchor background reset to defaults: enabled={HudCarried.AnchorBackgroundEnabled}, color={HudCarried.AnchorBackgroundColor}, alpha={HudCarried.AnchorBackgroundAlpha:0.##}");

        // === Border subcommand handlers ===

        protected TextCommandResult CmdCarryOnGuiBorderEnable(TextCommandCallingArgs args) =>
            ApplySetting(
                () => HudCarried.AnchorBorderEnabled = true,
                cfg => cfg.AnchorBorderEnabled = true,
                "enabling CarryOn anchor border",
                "CarryOn anchor border: enabled");

        protected TextCommandResult CmdCarryOnGuiBorderDisable(TextCommandCallingArgs args) =>
            ApplySetting(
                () => HudCarried.AnchorBorderEnabled = false,
                cfg => cfg.AnchorBorderEnabled = false,
                "disabling CarryOn anchor border",
                "CarryOn anchor border: disabled");

        protected TextCommandResult CmdCarryOnGuiBorderColor(TextCommandCallingArgs args)
        {
            string hex = (args[0] as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error("Usage: .carryon gui border color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out var normalized))
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            hex = normalized!;

            return ApplySetting(
                () => HudCarried.AnchorBorderColor = hex,
                cfg => cfg.AnchorBorderColor = hex,
                "setting CarryOn anchor border color",
                $"CarryOn anchor border color set to {hex}");
        }

        protected TextCommandResult CmdCarryOnGuiBorderAlpha(TextCommandCallingArgs args)
        {
            float a = args[0] is float floatVal ? floatVal : 0f;
            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            return ApplySetting(
                () => HudCarried.AnchorBorderAlpha = a,
                cfg => cfg.AnchorBorderAlpha = a,
                "setting CarryOn anchor border alpha",
                $"CarryOn anchor border alpha set to {a:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiBorderShow(TextCommandCallingArgs args) =>
            ShowSetting(
                $"Runtime: enabled={HudCarried.AnchorBorderEnabled}, color={HudCarried.AnchorBorderColor}, alpha={HudCarried.AnchorBorderAlpha:0.##}",
                cfg => $"Saved: enabled={cfg.AnchorBorderEnabled}, color={cfg.AnchorBorderColor}, alpha={cfg.AnchorBorderAlpha:0.##}",
                "anchor border");

        protected TextCommandResult CmdCarryOnGuiBorderReset(TextCommandCallingArgs args) =>
            ApplySetting(
                () =>
                {
                    HudCarried.AnchorBorderEnabled = true;
                    HudCarried.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
                    HudCarried.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;
                },
                cfg =>
                {
                    cfg.AnchorBorderEnabled = HudCarried.AnchorBorderEnabled;
                    cfg.AnchorBorderColor = HudCarried.AnchorBorderColor;
                    cfg.AnchorBorderAlpha = HudCarried.AnchorBorderAlpha;
                },
                "resetting CarryOn anchor border",
                $"CarryOn anchor border reset to defaults: enabled={HudCarried.AnchorBorderEnabled}, color={HudCarried.AnchorBorderColor}, alpha={HudCarried.AnchorBorderAlpha:0.##}");

        // === Highlight subcommand handlers ===

        protected TextCommandResult CmdCarryOnGuiHighlightEnable(TextCommandCallingArgs args) =>
            ApplySetting(
                () => HudCarried.IconHighlightEnabled = true,
                cfg => cfg.IconHighlightEnabled = true,
                "enabling CarryOn icon highlight",
                "CarryOn icon highlight: enabled");

        protected TextCommandResult CmdCarryOnGuiHighlightDisable(TextCommandCallingArgs args) =>
            ApplySetting(
                () => HudCarried.IconHighlightEnabled = false,
                cfg => cfg.IconHighlightEnabled = false,
                "disabling CarryOn icon highlight",
                "CarryOn icon highlight: disabled");

        protected TextCommandResult CmdCarryOnGuiHighlightColor(TextCommandCallingArgs args)
        {
            string hex = (args[0] as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error("Usage: .carryon gui highlight color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out var normalized))
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            hex = normalized!;

            return ApplySetting(
                () => HudCarried.IconHighlightColor = hex,
                cfg => cfg.IconHighlightColor = hex,
                "setting CarryOn icon highlight color",
                $"CarryOn icon highlight color set to {hex}");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightAlpha(TextCommandCallingArgs args)
        {
            float a = args[0] is float floatVal ? floatVal : 0f;
            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            return ApplySetting(
                () => HudCarried.IconHighlightAlpha = a,
                cfg => cfg.IconHighlightAlpha = a,
                "setting CarryOn icon highlight alpha",
                $"CarryOn icon highlight alpha set to {a:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightShow(TextCommandCallingArgs args) =>
            ShowSetting(
                $"Runtime: enabled={HudCarried.IconHighlightEnabled}, color={HudCarried.IconHighlightColor}, alpha={HudCarried.IconHighlightAlpha:0.##}",
                cfg => $"Saved: enabled={cfg.IconHighlightEnabled}, color={cfg.IconHighlightColor}, alpha={cfg.IconHighlightAlpha:0.##}",
                "icon highlight");

        protected TextCommandResult CmdCarryOnGuiHighlightReset(TextCommandCallingArgs args) =>
            ApplySetting(
                () =>
                {
                    HudCarried.IconHighlightEnabled = true;
                    HudCarried.IconHighlightColor = HudCarried.IconHighlightColorDefault;
                    HudCarried.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;
                },
                cfg =>
                {
                    cfg.IconHighlightEnabled = HudCarried.IconHighlightEnabled;
                    cfg.IconHighlightColor = HudCarried.IconHighlightColor;
                    cfg.IconHighlightAlpha = HudCarried.IconHighlightAlpha;
                },
                "resetting CarryOn icon highlight",
                $"CarryOn icon highlight reset to defaults: enabled={HudCarried.IconHighlightEnabled}, color={HudCarried.IconHighlightColor}, alpha={HudCarried.IconHighlightAlpha:0.##}");
    }
}

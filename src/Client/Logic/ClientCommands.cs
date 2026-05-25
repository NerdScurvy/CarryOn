namespace CarryOn.Client.Logic
{
    using CarryOn.Utility;
    using Vintagestory.API.Common;
    using Vintagestory.API.MathTools;

    public class ClientCommands

    {
        private readonly CarrySystem carrySystem;
        private readonly Vintagestory.API.Client.ICoreClientAPI api;

        public ClientCommands(CarrySystem carrySystem)
        {
            if (carrySystem == null)
                throw new System.ArgumentNullException(nameof(carrySystem));
            this.carrySystem = carrySystem;
            this.api = carrySystem.ClientApi;
        }
        /// <summary>
        /// Register client-side chat commands for the CarryOn mod.
        /// </summary>
        public void Register()
        {
            try
            {
                api.ChatCommands.Create("carryon")
                    .BeginSubCommand("carriedlight")
                        .WithDescription("Enable or disable carried block lighting for all players. Usage: .carryon carriedlight true|false")
                        .WithArgs(api.ChatCommands.Parsers.Bool("enabled"))
                        .HandleWith(this.CmdCarryOnCarriedLight)
                    .EndSubCommand()
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

        protected TextCommandResult CmdCarryOnCarriedLight(TextCommandCallingArgs args)
        {
            bool enabled = (bool)args[0];

            var clientConfig = this.carrySystem?.ClientConfig;

            if (clientConfig == null)
            {
                return TextCommandResult.Error("Client config not available. Cannot update carried block lighting setting.");
            }

            clientConfig.Config.CarriedLightEnabled = enabled;

            clientConfig.Save(this.api);

            if (!enabled)
            {
                // Reset LightHsv for all player entities
                foreach (var player in api.World.AllPlayers)
                {
                    var entity = player.Entity;
                    if (entity != null)
                    {
                        entity.LightHsv = new ThreeBytes(0);
                    }
                }
                return TextCommandResult.Success("Carried block lighting disabled. All player lights reset.");
            }
            else
            {
                // Optionally, re-enable by triggering attribute update (if needed)
                return TextCommandResult.Success("Carried block lighting enabled.");
            }
        }

        protected TextCommandResult CmdCarryOnGuiSet(TextCommandCallingArgs args)
        {
            string anchorStr = ((string)args[0])?.ToUpperInvariant();
            string slotStr = ((string)args[1])?.ToLowerInvariant();

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
                    if (this.carrySystem?.ClientConfig != null)
                    {
                        this.carrySystem.ClientConfig.Config.HandsAnchor = HudCarried.HandsAnchor.ToString();
                        this.carrySystem.ClientConfig.Save(this.api);
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
                    if (this.carrySystem?.ClientConfig != null)
                    {
                        this.carrySystem.ClientConfig.Config.BackAnchor = HudCarried.BackAnchor.ToString();
                        this.carrySystem.ClientConfig.Save(this.api);
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
                        if (this.carrySystem?.ClientConfig != null)
                        {
                            this.carrySystem.ClientConfig.Config.HandsAnchor = HudCarried.HandsAnchor.ToString();
                            this.carrySystem.ClientConfig.Config.BackAnchor = HudCarried.BackAnchor.ToString();
                            this.carrySystem.ClientConfig.Save(this.api);
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
                if (this.carrySystem?.ClientConfig != null)
                {
                    var cfg = this.carrySystem.ClientConfig.Config;
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

                    this.carrySystem.ClientConfig.Save(this.api);
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
            string saved = null;
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

        // === Background subcommand handlers ===
        protected TextCommandResult CmdCarryOnGuiBgEnable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                // Update runtime and client config
                HudCarried.AnchorBackgroundEnabled = true;
                HudCarried.UpdateParsedColors();

                // runtime value updated (HudCarried) and saved to client config below
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundEnabled = true;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error enabling CarryOn anchor background: " + ex);
                return TextCommandResult.Error("Failed to enable CarryOn anchor background due to an error.");
            }

            return TextCommandResult.Success("CarryOn anchor background: enabled");
        }

        protected TextCommandResult CmdCarryOnGuiBgDisable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBackgroundEnabled = false;
                HudCarried.UpdateParsedColors();

                // runtime value updated (HudCarried) and saved to client config below
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundEnabled = false;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error disabling CarryOn anchor background: " + ex);
                return TextCommandResult.Error("Failed to disable CarryOn anchor background due to an error.");
            }

            return TextCommandResult.Success("CarryOn anchor background: disabled");
        }

        protected TextCommandResult CmdCarryOnGuiBgColor(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error("Usage: .carryon gui bg color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out hex))
            {
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            }

            try
            {
                // Apply to runtime and persist
                HudCarried.AnchorBackgroundColor = hex;
                HudCarried.UpdateParsedColors();

                // runtime value updated (HudCarried) and saved to client config below
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundColor = hex;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn anchor background color: " + ex);
                return TextCommandResult.Error("Failed to set CarryOn anchor background color due to an error.");
            }

            return TextCommandResult.Success($"CarryOn anchor background color set to {hex}");
        }

        protected TextCommandResult CmdCarryOnGuiBgAlpha(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            float a = 0f;
            try
            {
                a = (float)args[0];
            }
            catch
            {
                return TextCommandResult.Error("Usage: .carryon gui bg alpha 0.0-1.0");
            }

            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            try
            {
                HudCarried.AnchorBackgroundAlpha = a;
                HudCarried.UpdateParsedColors();

                // runtime value updated (HudCarried) and saved to client config below
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundAlpha = a;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn anchor background alpha: " + ex);
                return TextCommandResult.Error("Failed to set CarryOn anchor background alpha due to an error.");
            }

            return TextCommandResult.Success($"CarryOn anchor background alpha set to {a:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiBgShow(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string runtime = $"Runtime: enabled={HudCarried.AnchorBackgroundEnabled}, color={HudCarried.AnchorBackgroundColor}, alpha={HudCarried.AnchorBackgroundAlpha:0.##}";
            string saved = "Saved: (none)";
            try
            {
                var cfg = this.carrySystem?.ClientConfig?.Config;
                if (cfg != null)
                {
                    saved = $"Saved: enabled={cfg.AnchorBackgroundEnabled}, color={cfg.AnchorBackgroundColor}, alpha={cfg.AnchorBackgroundAlpha:0.##}";
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error showing CarryOn GUI anchor background settings: " + ex);
            }

            return TextCommandResult.Success("CarryOn anchor background — " + runtime + " | " + saved);
        }

        protected TextCommandResult CmdCarryOnGuiBgReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBackgroundEnabled = true;
                HudCarried.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
                HudCarried.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundEnabled = HudCarried.AnchorBackgroundEnabled;
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundColor = HudCarried.AnchorBackgroundColor;
                    this.carrySystem.ClientConfig.Config.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlpha;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error resetting CarryOn anchor background: " + ex);
                return TextCommandResult.Error("Failed to reset CarryOn anchor background due to an error.");
            }

            return TextCommandResult.Success($"CarryOn anchor background reset to defaults: enabled={HudCarried.AnchorBackgroundEnabled}, color={HudCarried.AnchorBackgroundColor}, alpha={HudCarried.AnchorBackgroundAlpha:0.##}");
        }

        // === Border subcommand handlers ===
        protected TextCommandResult CmdCarryOnGuiBorderEnable(TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBorderEnabled = true;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderEnabled = true;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error enabling CarryOn anchor border: " + ex);
                return TextCommandResult.Error("Failed to enable CarryOn anchor border due to an error.");
            }
            return TextCommandResult.Success("CarryOn anchor border: enabled");
        }

        protected TextCommandResult CmdCarryOnGuiBorderDisable(TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBorderEnabled = false;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderEnabled = false;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error disabling CarryOn anchor border: " + ex);
                return TextCommandResult.Error("Failed to disable CarryOn anchor border due to an error.");
            }
            return TextCommandResult.Success("CarryOn anchor border: disabled");
        }

        protected TextCommandResult CmdCarryOnGuiBorderColor(TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error("Usage: .carryon gui border color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out hex))
            {
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            }

            try
            {
                HudCarried.AnchorBorderColor = hex;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderColor = hex;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn anchor border color: " + ex);
                return TextCommandResult.Error("Failed to set CarryOn anchor border color due to an error.");
            }

            return TextCommandResult.Success($"CarryOn anchor border color set to {hex}");
        }

        protected TextCommandResult CmdCarryOnGuiBorderAlpha(TextCommandCallingArgs args)
        {
            float a = 0f;
            try { a = (float)args[0]; }
            catch { return TextCommandResult.Error("Usage: .carryon gui border alpha 0.0-1.0"); }

            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            try
            {
                HudCarried.AnchorBorderAlpha = a;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderAlpha = a;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn anchor border alpha: " + ex);
                return TextCommandResult.Error("Failed to set CarryOn anchor border alpha due to an error.");
            }

            return TextCommandResult.Success($"CarryOn anchor border alpha set to {a:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiBorderReset(TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBorderEnabled = true;
                HudCarried.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
                HudCarried.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderEnabled = HudCarried.AnchorBorderEnabled;
                    this.carrySystem.ClientConfig.Config.AnchorBorderColor = HudCarried.AnchorBorderColor;
                    this.carrySystem.ClientConfig.Config.AnchorBorderAlpha = HudCarried.AnchorBorderAlpha;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error resetting CarryOn anchor border: " + ex);
                return TextCommandResult.Error("Failed to reset CarryOn anchor border due to an error.");
            }

            return TextCommandResult.Success($"CarryOn anchor border reset to defaults: enabled={HudCarried.AnchorBorderEnabled}, color={HudCarried.AnchorBorderColor}, alpha={HudCarried.AnchorBorderAlpha:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiBorderShow(TextCommandCallingArgs args)
        {
            string runtime = $"Runtime: enabled={HudCarried.AnchorBorderEnabled}, color={HudCarried.AnchorBorderColor}, alpha={HudCarried.AnchorBorderAlpha:0.##}";
            string saved = "Saved: (none)";
            try
            {
                var cfg = this.carrySystem?.ClientConfig?.Config;
                if (cfg != null)
                {
                    saved = $"Saved: enabled={cfg.AnchorBorderEnabled}, color={cfg.AnchorBorderColor}, alpha={cfg.AnchorBorderAlpha:0.##}";
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error showing CarryOn GUI anchor border settings: " + ex);
            }

            return TextCommandResult.Success("CarryOn anchor border — " + runtime + " | " + saved);
        }

        // === Highlight subcommand handlers ===
        protected TextCommandResult CmdCarryOnGuiHighlightEnable(TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.IconHighlightEnabled = true;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightEnabled = true;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error enabling CarryOn icon highlight: " + ex);
                return TextCommandResult.Error("Failed to enable CarryOn icon highlight due to an error.");
            }
            return TextCommandResult.Success("CarryOn icon highlight: enabled");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightDisable(TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.IconHighlightEnabled = false;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightEnabled = false;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error disabling CarryOn icon highlight: " + ex);
                return TextCommandResult.Error("Failed to disable CarryOn icon highlight due to an error.");
            }
            return TextCommandResult.Success("CarryOn icon highlight: disabled");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightColor(TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return TextCommandResult.Error("Usage: .carryon gui highlight color #rrggbb");
            if (!ColorHelper.TryNormalizeHex(hex, out hex))
            {
                return TextCommandResult.Error("Invalid hex color. Expected formats: #RRGGBB, RRGGBB, #RGB, or RGB");
            }

            try
            {
                HudCarried.IconHighlightColor = hex;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightColor = hex;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn icon highlight color: " + ex);
                return TextCommandResult.Error("Failed to set CarryOn icon highlight color due to an error.");
            }

            return TextCommandResult.Success($"CarryOn icon highlight color set to {hex}");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightAlpha(TextCommandCallingArgs args)
        {
            float a = 0f;
            try { a = (float)args[0]; }
            catch { return TextCommandResult.Error("Usage: .carryon gui highlight alpha 0.0-1.0"); }
            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            try
            {
                HudCarried.IconHighlightAlpha = a;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightAlpha = a;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn icon highlight alpha: " + ex);
                return TextCommandResult.Error("Failed to set CarryOn icon highlight alpha due to an error.");
            }

            return TextCommandResult.Success($"CarryOn icon highlight alpha set to {a:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightReset(TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.IconHighlightEnabled = true;
                HudCarried.IconHighlightColor = HudCarried.IconHighlightColorDefault;
                HudCarried.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;
                HudCarried.UpdateParsedColors();

                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightEnabled = HudCarried.IconHighlightEnabled;
                    this.carrySystem.ClientConfig.Config.IconHighlightColor = HudCarried.IconHighlightColor;
                    this.carrySystem.ClientConfig.Config.IconHighlightAlpha = HudCarried.IconHighlightAlpha;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                // Handle any errors that occur during the reset process
                this.api.Logger.Error("Error resetting CarryOn icon highlight to defaults: " + ex);
                return TextCommandResult.Error("Failed to reset CarryOn icon highlight to defaults due to an error.");
            }

            return TextCommandResult.Success($"CarryOn icon highlight reset to defaults: enabled={HudCarried.IconHighlightEnabled}, color={HudCarried.IconHighlightColor}, alpha={HudCarried.IconHighlightAlpha:0.##}");
        }

        protected TextCommandResult CmdCarryOnGuiHighlightShow(TextCommandCallingArgs args)
        {
            string runtime = $"Runtime: enabled={HudCarried.IconHighlightEnabled}, color={HudCarried.IconHighlightColor}, alpha={HudCarried.IconHighlightAlpha:0.##}";
            string saved = "Saved: (none)";
            try
            {
                var cfg = this.carrySystem?.ClientConfig?.Config;
                if (cfg != null)
                {
                    saved = $"Saved: enabled={cfg.IconHighlightEnabled}, color={cfg.IconHighlightColor}, alpha={cfg.IconHighlightAlpha:0.##}";
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error showing CarryOn GUI icon highlight settings: " + ex);
            }

            return TextCommandResult.Success("CarryOn icon highlight — " + runtime + " | " + saved);
        }
    }
}

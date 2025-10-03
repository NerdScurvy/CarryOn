namespace CarryOn.Client
{
    public class Commands
    {
        private readonly CarrySystem carrySystem;
        private readonly Vintagestory.API.Client.ICoreClientAPI api;

        public Commands(CarrySystem carrySystem)
        {
            if (carrySystem == null)
                throw new System.ArgumentNullException(nameof(carrySystem));
            this.carrySystem = carrySystem;
            this.api = carrySystem.ClientAPI;
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
                        //  .carryon gui debug
                        .BeginSubCommand("debug")
                            .WithDescription("Toggle CarryOn GUI debug icons (alias)")
                            .HandleWith(this.CmdCarryOnGuiToggle)
                        .EndSubCommand()
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
                            .WithDescription("Reset CarryOn GUI anchors to defaults (Back -> R1, Hands -> empty)")
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

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiToggle(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            HudCarried.ShowDebugIcons = !HudCarried.ShowDebugIcons;
            string state = HudCarried.ShowDebugIcons ? "enabled" : "disabled";
            string msg = $"CarryOn GUI debug icons {state}";
            // Command framework will display the returned message; avoid double-printing
            // Attempt to persist client config (no-op if not present)

            try
            {
                // No fields currently for this toggle, but save to ensure config folder exists
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch { }

            return Vintagestory.API.Common.TextCommandResult.Success(msg);
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiSet(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string anchorStr = ((string)args[0])?.ToUpperInvariant();
            string slotStr = ((string)args[1])?.ToLowerInvariant();

            if (string.IsNullOrEmpty(anchorStr) || string.IsNullOrEmpty(slotStr))
            {
                return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui set L1 hands | R2 back | R1 clear");
            }

            // Parse anchor
            if (!System.Enum.TryParse<HudCarried.Anchor>(anchorStr, true, out var anchor))
            {
                return Vintagestory.API.Common.TextCommandResult.Error("Invalid anchor. Use L3,L2,L1,R1,R2,R3");
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
                        return Vintagestory.API.Common.TextCommandResult.Success($"Hands already at {anchor}");
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
                    return Vintagestory.API.Common.TextCommandResult.Error("Failed to move Hands anchor due to an error.");
                }

                return Vintagestory.API.Common.TextCommandResult.Success($"Hands moved to {anchor}");
            }

            if (slotStr == "back")
            {
                if (HudCarried.BackAnchor != HudCarried.Anchor.None)
                {
                    if (HudCarried.BackAnchor == anchor)
                    {
                        return Vintagestory.API.Common.TextCommandResult.Success($"Back already at {anchor}");
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
                    return Vintagestory.API.Common.TextCommandResult.Error("Failed to move Back anchor due to an error.");
                }

                return Vintagestory.API.Common.TextCommandResult.Success($"Back moved to {anchor}");
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
                        return Vintagestory.API.Common.TextCommandResult.Error("Failed to clear anchor due to an error.");
                    }

                    return Vintagestory.API.Common.TextCommandResult.Success($"Cleared anchor {anchor}");
                }

                return Vintagestory.API.Common.TextCommandResult.Error($"Anchor {anchor} was already empty");
            }

            return Vintagestory.API.Common.TextCommandResult.Error("Invalid slot. Use 'hands', 'back', or 'clear'");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            // Reset to defaults: Hands -> L1, Back -> R1
            HudCarried.HandsAnchor = HudCarried.HandsAnchorDefault;
            HudCarried.BackAnchor = HudCarried.BackAnchorDefault;

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
                this.api.Logger.Error("Error resetting CarryOn GUI anchors: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to reset CarryOn GUI anchors due to an error.");
            }

            string msg = $"CarryOn GUI anchors reset to defaults (Hands -> {HudCarried.HandsAnchor}, Back -> {HudCarried.BackAnchor})";
            return Vintagestory.API.Common.TextCommandResult.Success(msg);
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiShow(Vintagestory.API.Common.TextCommandCallingArgs args)
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
            catch { }

            string msg = $"CarryOn GUI anchors — Runtime: Hands={hands}, Back={back}" + (saved != null ? " | " + saved : "");
            return Vintagestory.API.Common.TextCommandResult.Success(msg);
        }

        // === Background subcommand handlers ===
        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgEnable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                // Update runtime and client config
                HudCarried.AnchorBackgroundEnabled = true;
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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to enable CarryOn anchor background due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor background: enabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgDisable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBackgroundEnabled = false;
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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to disable CarryOn anchor background due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor background: disabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgColor(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui bg color #rrggbb");

            // Normalize: ensure leading '#' and uppercase hex digits
            hex = "#" + hex.TrimStart('#').ToUpperInvariant();

            // Strict validation: must be exactly 7 characters (# + 6 hex digits)
            if (hex.Length != 7)
            {
                return Vintagestory.API.Common.TextCommandResult.Error("Invalid hex color. Expected format: #rrggbb (6 hex digits)");
            }

            // Validate each char is a hex digit
            for (int i = 1; i < 7; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok)
                {
                    return Vintagestory.API.Common.TextCommandResult.Error("Invalid hex color. Expected format: #rrggbb (6 hex digits)");
                }
            }

            try
            {
                // Apply to runtime and persist
                HudCarried.AnchorBackgroundColor = hex;
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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to set CarryOn anchor background color due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn anchor background color set to {hex}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgAlpha(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            float a = 0f;
            try
            {
                a = (float)args[0];
            }
            catch
            {
                return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui bg alpha 0.0-1.0");
            }

            if (a < 0f || a > 1f) return Vintagestory.API.Common.TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            try
            {
                HudCarried.AnchorBackgroundAlpha = a;
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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to set CarryOn anchor background alpha due to an error.");
            }   

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn anchor background alpha set to {a:0.##}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgShow(Vintagestory.API.Common.TextCommandCallingArgs args)
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
            catch { }

            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor background — " + runtime + " | " + saved);
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {

            try
            {
                HudCarried.AnchorBackgroundEnabled = true;
                HudCarried.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
                HudCarried.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;

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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to reset CarryOn anchor background due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn anchor background reset to defaults: enabled={HudCarried.AnchorBackgroundEnabled}, color={HudCarried.AnchorBackgroundColor}, alpha={HudCarried.AnchorBackgroundAlpha:0.##}");
        }

        // === Border subcommand handlers ===
        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBorderEnable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBorderEnabled = true;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderEnabled = true;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error enabling CarryOn anchor border: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to enable CarryOn anchor border due to an error.");
            }
            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor border: enabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBorderDisable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBorderEnabled = false;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderEnabled = false;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error disabling CarryOn anchor border: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to disable CarryOn anchor border due to an error.");
            }
            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor border: disabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBorderColor(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui border color #rrggbb");

            // Normalize to uppercase with '#'
            hex = "#" + hex.TrimStart('#').ToUpperInvariant();

            // Validate
            if (hex.Length != 7) return Vintagestory.API.Common.TextCommandResult.Error("Invalid hex color. Expected format: #rrggbb");
            for (int i = 1; i < 7; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
                if (!ok) return Vintagestory.API.Common.TextCommandResult.Error("Invalid hex color. Expected format: #rrggbb");
            }

            try
            {
                HudCarried.AnchorBorderColor = hex;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderColor = hex;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn anchor border color: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to set CarryOn anchor border color due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn anchor border color set to {hex}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBorderAlpha(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            float a = 0f;
            try { a = (float)args[0]; }
            catch { return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui border alpha 0.0-1.0"); }

            if (a < 0f || a > 1f) return Vintagestory.API.Common.TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            try
            {
                HudCarried.AnchorBorderAlpha = a;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.AnchorBorderAlpha = a;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn anchor border alpha: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to set CarryOn anchor border alpha due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn anchor border alpha set to {a:0.##}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBorderReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.AnchorBorderEnabled = true;
                HudCarried.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
                HudCarried.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;
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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to reset CarryOn anchor border due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn anchor border reset to defaults: enabled={HudCarried.AnchorBorderEnabled}, color={HudCarried.AnchorBorderColor}, alpha={HudCarried.AnchorBorderAlpha:0.##}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBorderShow(Vintagestory.API.Common.TextCommandCallingArgs args)
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
            catch { }

            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor border — " + runtime + " | " + saved);
        }

        // === Highlight subcommand handlers ===
        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiHighlightEnable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.IconHighlightEnabled = true;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightEnabled = true;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error enabling CarryOn icon highlight: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to enable CarryOn icon highlight due to an error.");
            }
            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn icon highlight: enabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiHighlightDisable(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.IconHighlightEnabled = false;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightEnabled = false;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error disabling CarryOn icon highlight: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to disable CarryOn icon highlight due to an error.");
            }
            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn icon highlight: disabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiHighlightColor(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui highlight color #rrggbb");

            hex = "#" + hex.TrimStart('#').ToUpperInvariant();
            if (hex.Length != 7) return Vintagestory.API.Common.TextCommandResult.Error("Invalid hex color. Expected format: #rrggbb");
            for (int i = 1; i < 7; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
                if (!ok) return Vintagestory.API.Common.TextCommandResult.Error("Invalid hex color. Expected format: #rrggbb");
            }

            try
            {
                HudCarried.IconHighlightColor = hex;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightColor = hex;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn icon highlight color: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to set CarryOn icon highlight color due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn icon highlight color set to {hex}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiHighlightAlpha(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            float a = 0f;
            try { a = (float)args[0]; }
            catch { return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui highlight alpha 0.0-1.0"); }
            if (a < 0f || a > 1f) return Vintagestory.API.Common.TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            try
            {
                HudCarried.IconHighlightAlpha = a;
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.IconHighlightAlpha = a;
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch (System.Exception ex)
            {
                this.api.Logger.Error("Error setting CarryOn icon highlight alpha: " + ex);
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to set CarryOn icon highlight alpha due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn icon highlight alpha set to {a:0.##}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiHighlightReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            try
            {
                HudCarried.IconHighlightEnabled = true;
                HudCarried.IconHighlightColor = HudCarried.IconHighlightColorDefault;
                HudCarried.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;
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
                return Vintagestory.API.Common.TextCommandResult.Error("Failed to reset CarryOn icon highlight to defaults due to an error.");
            }

            return Vintagestory.API.Common.TextCommandResult.Success($"CarryOn icon highlight reset to defaults: enabled={HudCarried.IconHighlightEnabled}, color={HudCarried.IconHighlightColor}, alpha={HudCarried.IconHighlightAlpha:0.##}");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiHighlightShow(Vintagestory.API.Common.TextCommandCallingArgs args)
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
            catch { }

            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn icon highlight — " + runtime + " | " + saved);
        }
    }
}


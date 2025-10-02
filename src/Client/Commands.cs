namespace CarryOn.Client
{
    public class Commands
    {
        private readonly CarrySystem carrySystem;
        private readonly Vintagestory.API.Client.ICoreClientAPI api;

        public Commands(CarrySystem carrySystem)
        {
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
                if (this.carrySystem?.ClientConfig != null)
                {
                    // No fields currently for this toggle, but save to ensure config folder exists
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
                return Vintagestory.API.Common.TextCommandResult.Error("Invalid anchor. Use L1,L2,L3,R1,R2,R3");
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
                catch { }

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
                catch { }

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
                    catch { }

                    return Vintagestory.API.Common.TextCommandResult.Success($"Cleared anchor {anchor}");
                }

                return Vintagestory.API.Common.TextCommandResult.Error($"Anchor {anchor} was already empty");
            }

            return Vintagestory.API.Common.TextCommandResult.Error("Invalid slot. Use 'hands', 'back', or 'clear'");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiReset(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            // Reset to defaults: Hands -> None, Back -> R1
            HudCarried.HandsAnchor = HudCarried.Anchor.None;
            HudCarried.BackAnchor = HudCarried.Anchor.R1;

            try
            {
                if (this.carrySystem?.ClientConfig != null)
                {
                    this.carrySystem.ClientConfig.Config.HandsAnchor = HudCarried.HandsAnchor.ToString();
                    this.carrySystem.ClientConfig.Config.BackAnchor = HudCarried.BackAnchor.ToString();
                    this.carrySystem.ClientConfig.Save(this.api);
                }
            }
            catch { }

            string msg = "CarryOn GUI anchors reset to defaults (Hands cleared, Back -> R1)";
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
            catch { }

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
            catch { }

            return Vintagestory.API.Common.TextCommandResult.Success("CarryOn anchor background: disabled");
        }

        protected Vintagestory.API.Common.TextCommandResult CmdCarryOnGuiBgColor(Vintagestory.API.Common.TextCommandCallingArgs args)
        {
            string hex = ((string)args[0])?.Trim();
            if (string.IsNullOrEmpty(hex)) return Vintagestory.API.Common.TextCommandResult.Error("Usage: .carryon gui bg color #rrggbb");

            // Normalize: ensure leading '#'
            if (!hex.StartsWith("#")) hex = "#" + hex;

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
            catch { }

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
            catch { }

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
    }
}


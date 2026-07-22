using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Commands
{
    public partial class ClientCommands
    {
        protected TextCommandResult CmdCarryOnGuiSet(TextCommandCallingArgs args)
        {
            string anchorStr = (args[0] as string)?.ToUpperInvariant() ?? "";
            string slotStr = (args[1] as string)?.ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(anchorStr) || string.IsNullOrEmpty(slotStr))
                return TextCommandResult.Error("Usage: .carryon gui set L1 hands | R2 back | R1 clear");

            if (!System.Enum.TryParse<HudCarried.Anchor>(anchorStr, true, out var anchor))
                return TextCommandResult.Error("Invalid anchor. Use L1,L2,L3,R1,R2,R3");

            var cfg = this.clientModConfig.Config;
            if (cfg == null)
                return TextCommandResult.Error("Client config not available.");

            if (slotStr == "hands")
            {
                return MoveAnchor(anchor,
                    c => c.HandsAnchor,
                    (c, v) => c.HandsAnchor = v,
                    c => c.BackAnchor,
                    (c, v) => c.BackAnchor = v,
                    "Hands");
            }

            if (slotStr == "back")
            {
                return MoveAnchor(anchor,
                    c => c.BackAnchor,
                    (c, v) => c.BackAnchor = v,
                    c => c.HandsAnchor,
                    (c, v) => c.HandsAnchor = v,
                    "Back");
            }

            if (slotStr == "clear")
            {
                bool cleared = false;
                if (cfg.HandsAnchor == anchor) { cfg.HandsAnchor = HudCarried.Anchor.None; cleared = true; }
                if (cfg.BackAnchor == anchor) { cfg.BackAnchor = HudCarried.Anchor.None; cleared = true; }

                if (!cleared)
                    return TextCommandResult.Error($"Anchor {anchor} was already empty");

                try
                {
                    this.clientModConfig.Save(this.api);
                }
                catch (System.Exception ex)
                {
                    this.api.Logger.Error("Error clearing anchor: " + ex);
                    return TextCommandResult.Error("Failed to clear anchor due to an error.");
                }

                return TextCommandResult.Success($"Cleared anchor {anchor}");
            }

            return TextCommandResult.Error("Invalid slot. Use 'hands', 'back', or 'clear'");
        }

        protected TextCommandResult CmdCarryOnGuiReset(TextCommandCallingArgs args)
        {
            return ResetAndPersist(cfg =>
            {
                cfg.HandsAnchor = HudCarried.HandsAnchorDefault;
                cfg.BackAnchor = HudCarried.BackAnchorDefault;
                cfg.AnchorBackgroundEnabled = true;
                cfg.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
                cfg.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;
                cfg.AnchorBorderEnabled = true;
                cfg.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
                cfg.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;
                cfg.IconHighlightEnabled = true;
                cfg.IconHighlightColor = HudCarried.IconHighlightColorDefault;
                cfg.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;
            }, "CarryOn GUI anchors and visuals");
        }

        protected TextCommandResult CmdCarryOnGuiShow(TextCommandCallingArgs args)
        {
            var cfg = this.clientModConfig.Config;
            if (cfg == null)
                return TextCommandResult.Success("CarryOn GUI anchors - Config not available");

            string msg = $"CarryOn GUI anchors - Hands={cfg.HandsAnchor}, Back={cfg.BackAnchor}";
            return TextCommandResult.Success(msg);
        }
    }
}

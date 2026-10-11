using CarryOn.API.Common.Models;
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

            if (!TryGetConfig(out var cfg, out var configError))
                return configError!;

            if (slotStr == "clear")
            {
                bool cleared = false;
                if (cfg.HandsAnchor == anchor) { cfg.HandsAnchor = HudCarried.Anchor.None; cleared = true; }
                if (cfg.BackAnchor == anchor) { cfg.BackAnchor = HudCarried.Anchor.None; cleared = true; }

                if (!cleared)
                    return TextCommandResult.Error($"Anchor {anchor} was already empty");

                return SaveWithErrorHandling("Error clearing anchor", "Failed to clear anchor due to an error.")
                    ?? TextCommandResult.Success($"Cleared anchor {anchor}");
            }

            if (!System.Enum.TryParse<CarrySlot>(slotStr, true, out var slot) || slot == CarrySlot.Attached)
                return TextCommandResult.Error("Invalid slot. Use 'hands', 'back', or 'clear'");

            if (slot == CarrySlot.Hands)
            {
                return MoveAnchor(anchor,
                    c => c.HandsAnchor,
                    (c, v) => c.HandsAnchor = v,
                    c => c.BackAnchor,
                    (c, v) => c.BackAnchor = v,
                    "Hands");
            }

            return MoveAnchor(anchor,
                c => c.BackAnchor,
                (c, v) => c.BackAnchor = v,
                c => c.HandsAnchor,
                (c, v) => c.HandsAnchor = v,
                "Back");
        }

        protected TextCommandResult CmdCarryOnGuiReset(TextCommandCallingArgs args)
        {
            return ResetAndPersist(cfg =>
            {
                cfg.HandsAnchor = HudCarried.HandsAnchorDefault;
                cfg.BackAnchor = HudCarried.BackAnchorDefault;
                ResetCategory(cfg, GuiCategory.Background);
                ResetCategory(cfg, GuiCategory.Border);
                ResetCategory(cfg, GuiCategory.Highlight);
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

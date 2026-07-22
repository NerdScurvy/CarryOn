using System;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Commands
{
    public partial class ClientCommands
    {
        private enum GuiCategory { Background, Border, Highlight }

        private TextCommandResult HandleGuiEnable(GuiCategory cat, TextCommandCallingArgs _)
        {
            return ApplySetting(
                cfg => SetEnabled(cfg, cat, true),
                $"enabling CarryOn {cat.ToString().ToLowerInvariant()}",
                $"CarryOn {cat.ToString().ToLowerInvariant()}: enabled");
        }

        private TextCommandResult HandleGuiDisable(GuiCategory cat, TextCommandCallingArgs _)
        {
            return ApplySetting(
                cfg => SetEnabled(cfg, cat, false),
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

            return ApplySetting(
                cfg => SetColor(cfg, cat, hex),
                $"setting CarryOn {cat.ToString().ToLowerInvariant()} color",
                $"CarryOn {cat.ToString().ToLowerInvariant()} color set to {hex}");
        }

        private TextCommandResult HandleGuiAlpha(GuiCategory cat, TextCommandCallingArgs args)
        {
            float a = args[0] is float floatVal ? floatVal : 0f;
            if (a < 0f || a > 1f) return TextCommandResult.Error("Alpha must be between 0.0 and 1.0");

            return ApplySetting(
                cfg => SetAlpha(cfg, cat, a),
                $"setting CarryOn {cat.ToString().ToLowerInvariant()} alpha",
                $"CarryOn {cat.ToString().ToLowerInvariant()} alpha set to {a:0.##}");
        }

        private TextCommandResult HandleGuiShow(GuiCategory cat, TextCommandCallingArgs _)
        {
            var cfg = this.clientModConfig.Config;
            if (cfg == null)
                return TextCommandResult.Error("Client config not available.");

            string name;
            bool enabled;
            string color;
            float alpha;

            switch (cat)
            {
                case GuiCategory.Background:
                    name = "anchor background";
                    enabled = cfg.AnchorBackgroundEnabled;
                    color = cfg.AnchorBackgroundColor;
                    alpha = cfg.AnchorBackgroundAlpha;
                    break;
                case GuiCategory.Border:
                    name = "anchor border";
                    enabled = cfg.AnchorBorderEnabled;
                    color = cfg.AnchorBorderColor;
                    alpha = cfg.AnchorBorderAlpha;
                    break;
                case GuiCategory.Highlight:
                    name = "icon highlight";
                    enabled = cfg.IconHighlightEnabled;
                    color = cfg.IconHighlightColor;
                    alpha = cfg.IconHighlightAlpha;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cat));
            }

            return ShowSetting(
                $"enabled={enabled}, color={color}, alpha={alpha:0.##}",
                name);
        }

        private TextCommandResult HandleGuiReset(GuiCategory cat, TextCommandCallingArgs _)
        {
            return ApplySetting(
                cfg => ResetCategory(cfg, cat),
                $"resetting CarryOn {GetCategoryName(cat)}",
                $"CarryOn {GetCategoryName(cat)} reset to defaults");
        }

        private static void SetEnabled(CarryOnClientConfig cfg, GuiCategory cat, bool value)
        {
            switch (cat)
            {
                case GuiCategory.Background: cfg.AnchorBackgroundEnabled = value; break;
                case GuiCategory.Border: cfg.AnchorBorderEnabled = value; break;
                case GuiCategory.Highlight: cfg.IconHighlightEnabled = value; break;
            }
        }

        private static void SetColor(CarryOnClientConfig cfg, GuiCategory cat, string value)
        {
            switch (cat)
            {
                case GuiCategory.Background: cfg.AnchorBackgroundColor = value; break;
                case GuiCategory.Border: cfg.AnchorBorderColor = value; break;
                case GuiCategory.Highlight: cfg.IconHighlightColor = value; break;
            }
        }

        private static void SetAlpha(CarryOnClientConfig cfg, GuiCategory cat, float value)
        {
            switch (cat)
            {
                case GuiCategory.Background: cfg.AnchorBackgroundAlpha = value; break;
                case GuiCategory.Border: cfg.AnchorBorderAlpha = value; break;
                case GuiCategory.Highlight: cfg.IconHighlightAlpha = value; break;
            }
        }

        private static void ResetCategory(CarryOnClientConfig cfg, GuiCategory cat)
        {
            switch (cat)
            {
                case GuiCategory.Background:
                    cfg.AnchorBackgroundEnabled = true;
                    cfg.AnchorBackgroundColor = HudCarried.AnchorBackgroundColorDefault;
                    cfg.AnchorBackgroundAlpha = HudCarried.AnchorBackgroundAlphaDefault;
                    break;
                case GuiCategory.Border:
                    cfg.AnchorBorderEnabled = true;
                    cfg.AnchorBorderColor = HudCarried.AnchorBorderColorDefault;
                    cfg.AnchorBorderAlpha = HudCarried.AnchorBorderAlphaDefault;
                    break;
                case GuiCategory.Highlight:
                    cfg.IconHighlightEnabled = true;
                    cfg.IconHighlightColor = HudCarried.IconHighlightColorDefault;
                    cfg.IconHighlightAlpha = HudCarried.IconHighlightAlphaDefault;
                    break;
            }
        }

        private static string GetCategoryName(GuiCategory cat) => cat switch
        {
            GuiCategory.Background => "anchor background",
            GuiCategory.Border => "anchor border",
            GuiCategory.Highlight => "icon highlight",
            _ => throw new ArgumentOutOfRangeException(nameof(cat))
        };

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

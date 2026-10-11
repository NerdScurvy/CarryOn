using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.Client.Models;
using Vintagestory.API.Client;

namespace CarryOn.Client.Logic
{
    public class HudCarried : IDisposable
    {
        private readonly ICoreClientAPI api;
        private HudCarriedRenderer? renderer;

        // Anchor positions available for GUI placement
        public enum Anchor
        {
            None = 0,
            L1,
            L2,
            L3,
            R1,
            R2,
            R3
        }

        public static readonly Anchor HandsAnchorDefault = Anchor.L1;
        public static readonly Anchor BackAnchorDefault = Anchor.R1;

        public static readonly float AnchorBackgroundAlphaDefault = 0.4f;
        public static readonly string AnchorBackgroundColorDefault = "#E4C4A6";

        public static readonly float AnchorBorderAlphaDefault = 0.75f;
        public static readonly string AnchorBorderColorDefault = "#45372D";

        public static readonly string IconHighlightColorDefault = "#FFFFFF";
        public static readonly float IconHighlightAlphaDefault = 0.8f;

        // Animation constants
        public const float DefaultHighlightDuration = 1.0f;
        public const float HighlightFadeExtra = 0.4f;

        // Layout constants (unscaled pixels)
        public const float BaseSlotSizePixels = 32.0f;
        public const float GroupMarginPixels = 16.0f;
        public const float IconSpacingPixels = 16.0f;
        public const float HotbarWidthPixels = 850.0f;
        public const float VerticalOffsetPixels = 36.0f;
        public const float BackgroundSizePixels = 64.0f;

        public void TriggerHandsHighlight(float seconds = DefaultHighlightDuration + HighlightFadeExtra)
            => this.renderer?.TriggerHandsHighlight(seconds);

        public void TriggerBackHighlight(float seconds = DefaultHighlightDuration + HighlightFadeExtra)
            => this.renderer?.TriggerBackHighlight(seconds);

        public HudCarried(ICoreClientAPI api, ICarryManager carryManager, CarryOnClientConfig config)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.renderer = new HudCarriedRenderer(api, carryManager, config);
            api.Event.RegisterRenderer(this.renderer, EnumRenderStage.Ortho);
        }

        public void Dispose()
        {
            if (this.renderer != null)
            {
                api.Event.UnregisterRenderer(this.renderer, EnumRenderStage.Ortho);
                this.renderer.Dispose();
                this.renderer = null;
            }
        }
    }
}

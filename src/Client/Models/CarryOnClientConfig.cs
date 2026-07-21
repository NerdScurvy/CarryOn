using System.ComponentModel;
using CarryOn.Client.Logic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CarryOn.Client.Models
{
    /// <summary>
    /// Client-side config that persists only client preferences such as HUD anchor placements.
    /// Stored using the Vintage Story mod config helpers (LoadModConfig / StoreModConfig) in the client-side modconfig folder.
    /// </summary>
    public class CarryOnClientConfig
    {
        /// <summary> Placeholder for future client config versioning and migration. </summary>
        [Browsable(false)]
        public int? ConfigVersion { get; set; }

        // Runtime toggle for CarryOn behavior (client-side only - always enabled on game start)
        [JsonIgnore]
        [Browsable(false)]
        public bool CarryOnEnabled { get; set; } = true;

        [Category("HUD Anchor")]
        [DisplayName("Hands Anchor")]
        [Description("HUD anchor position for hands carry slot (L1, L2, L3, R1, R2, R3, None)")]
        [JsonConverter(typeof(StringEnumConverter))]
        public HudCarried.Anchor HandsAnchor { get; set; } = HudCarried.HandsAnchorDefault;

        [Category("HUD Anchor")]
        [DisplayName("Back Anchor")]
        [Description("HUD anchor position for back carry slot (L1, L2, L3, R1, R2, R3, None)")]
        [JsonConverter(typeof(StringEnumConverter))]
        public HudCarried.Anchor BackAnchor { get; set; } = HudCarried.BackAnchorDefault;

        [Category("HUD Anchor")]
        [DisplayName("Background Enabled")]
        [Description("Show a background behind the carried item HUD")]
        [DefaultValue(false)]
        public bool AnchorBackgroundEnabled { get; set; } = false;

        [Category("HUD Anchor")]
        [DisplayName("Background Color")]
        [Description("Hex color for the HUD background (e.g. #E4C4A6)")]
        public string AnchorBackgroundColor { get; set; } = HudCarried.AnchorBackgroundColorDefault;

        [Category("HUD Anchor")]
        [DisplayName("Background Alpha")]
        [Description("Transparency of the HUD background (0.0 = fully transparent, 1.0 = fully opaque)")]
        [DefaultValue(0.4f)]
        public float AnchorBackgroundAlpha { get; set; } = HudCarried.AnchorBackgroundAlphaDefault;

        [Category("HUD Anchor")]
        [DisplayName("Border Enabled")]
        [Description("Show a border/outline around the carried item HUD")]
        [DefaultValue(false)]
        public bool AnchorBorderEnabled { get; set; } = false;

        [Category("HUD Anchor")]
        [DisplayName("Border Color")]
        [Description("Hex color for the HUD border (e.g. #45372D)")]
        public string AnchorBorderColor { get; set; } = HudCarried.AnchorBorderColorDefault;

        [Category("HUD Anchor")]
        [DisplayName("Border Alpha")]
        [Description("Transparency of the HUD border (0.0 = fully transparent, 1.0 = fully opaque)")]
        [DefaultValue(0.75f)]
        public float AnchorBorderAlpha { get; set; } = HudCarried.AnchorBorderAlphaDefault;

        [Category("HUD Anchor")]
        [DisplayName("Icon Highlight Enabled")]
        [Description("Show a highlight effect when picking up or placing carried items")]
        [DefaultValue(false)]
        public bool IconHighlightEnabled { get; set; } = false;

        [Category("HUD Anchor")]
        [DisplayName("Icon Highlight Color")]
        [Description("Hex color for the icon highlight effect (e.g. #FFFFFF)")]
        public string IconHighlightColor { get; set; } = HudCarried.IconHighlightColorDefault;

        [Category("HUD Anchor")]
        [DisplayName("Icon Highlight Alpha")]
        [Description("Transparency of the icon highlight effect (0.0 = fully transparent, 1.0 = fully opaque)")]
        [DefaultValue(0.8f)]
        public float IconHighlightAlpha { get; set; } = HudCarried.IconHighlightAlphaDefault;

        [Category("Render")]
        [DisplayName("Render Attached Blocks")]
        [Description("Render attached blocks (e.g. torches, signs) on carried blocks")]
        [DefaultValue(true)]
        public bool RenderAttachedBlocks { get; set; } = true;

        [Category("Render")]
        [DisplayName("Capture Attached Wall Signs")]
        [Description("Include attached wall signs when picking up blocks")]
        [DefaultValue(true)]
        public bool CaptureAttachedWallSigns { get; set; } = true;

        [Category("Render")]
        [DisplayName("Icon Texture Mode")]
        [Description("How icon textures are acquired: Standalone (best quality, auto-fallback), Atlas (UV coordinates), StandaloneFallback (force legacy path), Disabled (no icons)")]
        [DefaultValue(IconTextureMode.Standalone)]
        [JsonConverter(typeof(StringEnumConverter))]
        public IconTextureMode IconTextureMode { get; set; } = IconTextureMode.Standalone;

        public CarryOnClientConfig() { }

        public CarryOnClientConfig(int version)
        {
            ConfigVersion = version;
        }
    }
}

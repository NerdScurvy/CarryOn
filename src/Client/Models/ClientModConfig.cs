using System;
using System.Text.Json.Serialization;
using CarryOn.Client.Logic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Models
{
    /// <summary>
    /// Client-side config that persists only client preferences such as HUD anchor placements.
    /// Stored using the Vintage Story mod config helpers (LoadModConfig / StoreModConfig) in the client-side modconfig folder.
    /// </summary>
    public class CarryOnClientConfig
    {
        public int? ConfigVersion { get; set; }

        // Runtime toggle for CarryOn behavior (client-side only - always enabled on game start)
        [JsonIgnore]
        public bool CarryOnEnabled { get; set; } = true;

        // Stored as the enum name (L1,L2,...). "None" indicates not assigned.
        public string HandsAnchor { get; set; } = HudCarried.HandsAnchorDefault.ToString();
        public string BackAnchor { get; set; } = HudCarried.BackAnchorDefault.ToString();

        // Anchor background preferences (client-side persistence)
        public bool AnchorBackgroundEnabled { get; set; } = false;
        public string AnchorBackgroundColor { get; set; } = HudCarried.AnchorBackgroundColorDefault;
        public float AnchorBackgroundAlpha { get; set; } = HudCarried.AnchorBackgroundAlphaDefault;
        // Anchor border (outline) preferences
        public bool AnchorBorderEnabled { get; set; } = false;
        public string AnchorBorderColor { get; set; } = HudCarried.AnchorBorderColorDefault;
        public float AnchorBorderAlpha { get; set; } = HudCarried.AnchorBorderAlphaDefault;
        // Icon highlight preferences
        public bool IconHighlightEnabled { get; set; } = false;
        public string IconHighlightColor { get; set; } = HudCarried.IconHighlightColorDefault;
        public float IconHighlightAlpha { get; set; } = HudCarried.IconHighlightAlphaDefault;

        // Cluster carry render preferences (client-side only)
        public bool RenderAttachedBlocks { get; set; } = true;

        public CarryOnClientConfig() { }

        public CarryOnClientConfig(int version)
        {
            ConfigVersion = version;
        }

        internal void ApplyTo()
        {
            if (!string.IsNullOrEmpty(HandsAnchor) && Enum.TryParse<HudCarried.Anchor>(HandsAnchor, true, out var handsAnchor))
            {
                HudCarried.HandsAnchor = handsAnchor;
            }

            if (!string.IsNullOrEmpty(BackAnchor) && Enum.TryParse<HudCarried.Anchor>(BackAnchor, true, out var backAnchor))
            {
                HudCarried.BackAnchor = backAnchor;
            }

            HudCarried.AnchorBackgroundEnabled = AnchorBackgroundEnabled;
            if (!string.IsNullOrEmpty(AnchorBackgroundColor))
            {
                HudCarried.AnchorBackgroundColor = AnchorBackgroundColor;
            }
            HudCarried.AnchorBackgroundAlpha = AnchorBackgroundAlpha;

            HudCarried.AnchorBorderEnabled = AnchorBorderEnabled;
            if (!string.IsNullOrEmpty(AnchorBorderColor))
            {
                HudCarried.AnchorBorderColor = AnchorBorderColor;
            }
            HudCarried.AnchorBorderAlpha = AnchorBorderAlpha;

            HudCarried.IconHighlightEnabled = IconHighlightEnabled;
            if (!string.IsNullOrEmpty(IconHighlightColor))
            {
                HudCarried.IconHighlightColor = IconHighlightColor;
            }
            HudCarried.IconHighlightAlpha = IconHighlightAlpha;
        }
    }

    public class ClientModConfig
    {
        private const int CurrentVersion = 1;
        private const string ConfigFileName = "CarryOnClientConfig.json";

        public CarryOnClientConfig? Config { get; private set; }

        public void Load(ICoreClientAPI api)
        {
            if (api == null || api.Side != EnumAppSide.Client) return;

            try
            {
                var loaded = api.LoadModConfig<CarryOnClientConfig>(ConfigFileName);
                if (loaded == null)
                {
                    loaded = new CarryOnClientConfig(CurrentVersion);
                }

                // Apply defaults if necessary
                if (loaded.ConfigVersion == null) loaded.ConfigVersion = CurrentVersion;

                Config = loaded;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("CarryOn: Failed to load client config: " + ex.Message);
                Config = new CarryOnClientConfig(CurrentVersion);
            }
        }

        public void Save(ICoreClientAPI api)
        {
            if (api == null || api.Side != EnumAppSide.Client || Config == null) return;

            try
            {
                api.StoreModConfig(Config, ConfigFileName);
            }
            catch (Exception ex)
            {
                api.Logger.Warning("CarryOn: Failed to save client config: " + ex.Message);
            }
        }
    }
}

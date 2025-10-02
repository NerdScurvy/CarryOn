using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic
{
    /// <summary>
    /// Client-side config that persists only client preferences such as HUD anchor placements.
    /// Stored using the Vintage Story mod config helpers (LoadModConfig / StoreModConfig) in the client-side modconfig folder.
    /// </summary>
    public class CarryOnClientConfig
    {
        public int? ConfigVersion { get; set; }

        // Stored as the enum name (L1,L2,...). "None" indicates not assigned.
        public string HandsAnchor { get; set; } = HudCarried.Anchor.None.ToString();
        public string BackAnchor { get; set; } = HudCarried.Anchor.R1.ToString();
        
        // Anchor background preferences (client-side persistence)
        public bool AnchorBackgroundEnabled { get; set; } = true;
        public string AnchorBackgroundColor { get; set; } = "#e4c4a6";
        public float AnchorBackgroundAlpha { get; set; } = 0.6f;

        public CarryOnClientConfig() { }

        public CarryOnClientConfig(int version)
        {
            ConfigVersion = version;
        }
    }

    public class ClientModConfig
    {
        private const int CurrentVersion = 1;
        private const string ConfigFileName = "CarryOnClientConfig.json";

        public CarryOnClientConfig Config { get; private set; }

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

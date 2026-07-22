using System;
using CarryOn.Client.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic
{
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

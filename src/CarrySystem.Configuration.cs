using System;
using CarryOn.Common.Network;
using CarryOn.Common.Entities;
using CarryOn.Common.Models;
using Newtonsoft.Json;
using Vintagestory.API.Datastructures;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn
{
    public partial class CarrySystem
    {
        private Action<CarryOnConfig>? onConfigChangedInvalidateBackpackCache;
        private Action<CarryOnConfig>? onConfigChangedRefreshCarryHandler;
        private Action<CarryOnConfig>? onConfigChangedUpdateEntityConfig;
        private Action<CarryOnConfig>? onConfigChangedPersistToDisk;
        private Action<CarryOnConfig>? onConfigChangedBroadcastToClients;
        private Action<CarryOnConfig>? onConfigChangedRegenerateReport;

        private void WireConfigChangeHandlers()
        {
            onConfigChangedInvalidateBackpackCache = _ => Config.InvalidateBackpackCache();
            onConfigChangedRefreshCarryHandler = _ => this.CarryHandler?.RefreshConfigCache();
            onConfigChangedUpdateEntityConfig = _ => EntityCarriedBlock.Config = Config.CarriedBlockEntity;
            onConfigChangedPersistToDisk = _ =>
            {
                if (ServerApi == null) return;
                if (ServerApi.World.Config is ITreeAttribute worldTree)
                    worldTree[ModId] = Config.ToTreeAttribute();
                ServerApi.Logger.Debug("CarryOn: Persisting config to disk via StoreModConfig");
                ServerApi.StoreModConfig(Config, ConfigFile);
            };
            onConfigChangedBroadcastToClients = _ =>
                {
                    if (ServerChannel == null || ServerApi == null) return;
                    var json = JsonConvert.SerializeObject(Config);
                    ServerApi.Logger.Debug("CarryOn: Broadcasting config sync to clients");
                    ServerChannel.BroadcastPacket(new ConfigSyncMessage(json));
                };
            onConfigChangedRegenerateReport = _ =>
                {
                    carryableReportService?.Generate();
                };

            ConfigService.OnConfigChanged += onConfigChangedInvalidateBackpackCache;
            ConfigService.OnConfigChanged += onConfigChangedRefreshCarryHandler;
            ConfigService.OnConfigChanged += onConfigChangedUpdateEntityConfig;
            ConfigService.OnConfigChanged += onConfigChangedPersistToDisk;
            ConfigService.OnConfigChanged += onConfigChangedBroadcastToClients;
            ConfigService.OnConfigChanged += onConfigChangedRegenerateReport;
        }

        private void UnwireConfigChangeHandlers()
        {
            if (onConfigChangedInvalidateBackpackCache != null)
                ConfigService.OnConfigChanged -= onConfigChangedInvalidateBackpackCache;
            if (onConfigChangedRefreshCarryHandler != null)
                ConfigService.OnConfigChanged -= onConfigChangedRefreshCarryHandler;
            if (onConfigChangedUpdateEntityConfig != null)
                ConfigService.OnConfigChanged -= onConfigChangedUpdateEntityConfig;
            if (onConfigChangedPersistToDisk != null)
                ConfigService.OnConfigChanged -= onConfigChangedPersistToDisk;
            if (onConfigChangedBroadcastToClients != null)
                ConfigService.OnConfigChanged -= onConfigChangedBroadcastToClients;
            if (onConfigChangedRegenerateReport != null)
                ConfigService.OnConfigChanged -= onConfigChangedRegenerateReport;

            onConfigChangedInvalidateBackpackCache = null;
            onConfigChangedRefreshCarryHandler = null;
            onConfigChangedUpdateEntityConfig = null;
            onConfigChangedPersistToDisk = null;
            onConfigChangedBroadcastToClients = null;
            onConfigChangedRegenerateReport = null;
        }

        private void OnClientConfigSync(ConfigSyncMessage message)
        {
            if (message.ConfigJson == null || ClientApi == null) return;

            try
            {
                var reloaded = JsonConvert.DeserializeObject<CarryOnConfig>(message.ConfigJson);
                if (reloaded != null)
                {
                    reloaded.UpgradeVersion();
                    ConfigService.Replace(reloaded);
                    ClientApi.Logger.Notification("CarryOn: Config synced from server and applied");
                }
            }
            catch (Exception ex)
            {
                ClientApi.Logger.Warning("CarryOn: Failed to apply synced config: " + ex.Message);
            }
        }
    }
}

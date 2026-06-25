using System;
using System.IO;
using System.Threading;
using CarryOn.API.Common.Models;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Logic
{
    public sealed class CarryOnConfigService
    {
        private FileSystemWatcher? configWatcher;
        private DateTime lastConfigFileChange = DateTime.MinValue;
        private readonly object configWatcherLock = new();
        private ICoreServerAPI? serverApi;

        public CarryOnConfig Config { get; private set; }

        public event Action<CarryOnConfig>? OnConfigChanged;

        public CarryOnConfigService(CarryOnConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Replace(CarryOnConfig newConfig)
        {
            ArgumentNullException.ThrowIfNull(newConfig);

            Config = newConfig;
            OnConfigChanged?.Invoke(newConfig);
        }

        public void NotifyChanged()
        {
            OnConfigChanged?.Invoke(Config);
        }

        public void SetupFileWatcher(ICoreServerAPI api)
        {
            serverApi = api;

            try
            {
                var configPath = Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);
                if (!File.Exists(configPath))
                {
                    api.Logger.Warning("CarryOn: Config file not found at " + configPath);
                    return;
                }

                var configDir = Path.GetDirectoryName(configPath);
                if (configDir == null) return;

                configWatcher = new FileSystemWatcher(configDir, ConfigFile)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                configWatcher.Changed += OnConfigFileChanged;
                configWatcher.Created += OnConfigFileChanged;

                api.Logger.Notification("CarryOn: Watching " + configPath + " for config changes");
            }
            catch (Exception ex)
            {
                api.Logger?.Warning("CarryOn: Failed to set up config file watcher: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (configWatcher != null)
            {
                configWatcher.EnableRaisingEvents = false;
                configWatcher.Dispose();
                configWatcher = null;
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (configWatcherLock)
            {
                var now = DateTime.UtcNow;
                if ((now - lastConfigFileChange).TotalMilliseconds < 500) return;
                lastConfigFileChange = now;
            }

            Thread.Sleep(100);

            serverApi?.Event.EnqueueMainThreadTask(ReloadFromFile, "carryon-config-reload");
        }

        private void ReloadFromFile()
        {
            if (serverApi == null) return;

            try
            {
                var reloaded = serverApi.LoadModConfig<CarryOnConfig>(ConfigFile);
                if (reloaded == null) return;

                reloaded.UpgradeVersion();
                Replace(reloaded);

                serverApi.Logger.Notification("CarryOn: Config reloaded and applied");
            }
            catch (Exception ex)
            {
                serverApi.Logger?.Warning("CarryOn: Failed to reload config from file: " + ex.Message);
            }
        }
    }
}

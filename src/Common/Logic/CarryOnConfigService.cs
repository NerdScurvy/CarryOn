using System;
using System.IO;
using CarryOn.Common.Models;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    /// <summary>Manages CarryOn configuration loading, hot-reload via file watcher, and change notifications.</summary>
    public sealed class CarryOnConfigService
    {
        private FileSystemWatcher? configWatcher;
        private DateTime lastConfigFileChange = DateTime.MinValue;
        private readonly object configWatcherLock = new();
        private ICoreServerAPI? serverApi;
        private volatile bool suppressWatcher;

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
            suppressWatcher = true;
            try
            {
                serverApi?.Logger.Debug("CarryOn: Config replaced, firing OnConfigChanged");
                OnConfigChanged?.Invoke(newConfig);
            }
            finally
            {
                suppressWatcher = false;
            }
        }

        public void NotifyChanged()
        {
            OnConfigChanged?.Invoke(Config);
        }

        public void Reload()
        {
            ReloadFromFile();
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
            if (suppressWatcher)
            {
                serverApi?.Logger.Debug("CarryOn: Config file change suppressed (originated from Replace)");
                return;
            }

            lock (configWatcherLock)
            {
                var now = DateTime.UtcNow;
                if ((now - lastConfigFileChange).TotalMilliseconds < 500)
                {
                    serverApi?.Logger.Debug("CarryOn: Config file change debounced");
                    return;
                }
                lastConfigFileChange = now;
            }

            serverApi?.Logger.Debug("CarryOn: Config file change detected, enqueueing reload");
            serverApi?.Event.EnqueueMainThreadTask(ReloadFromFile, "carryon-config-reload");
        }

        private void ReloadFromFile()
        {
            if (serverApi == null) return;

            try
            {
                serverApi.Logger.Debug("CarryOn: ReloadFromFile — reading config from disk");
                var reloaded = serverApi.LoadModConfig<CarryOnConfig>(ConfigFile);
                if (reloaded == null)
                {
                    serverApi.Logger.Warning("CarryOn: ReloadFromFile — LoadModConfig returned null");
                    return;
                }

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

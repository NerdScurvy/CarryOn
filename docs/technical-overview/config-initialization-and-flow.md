# Config Initialization and Flow

This document traces how `CarryOnConfig` is created, distributed, and hot-reloaded
through the system — from disk to every consumer.

---

## 1. Interface: `IConfigProvider`

**File:** `src/Common/Interfaces/IConfigProvider.cs`

```csharp
public interface IConfigProvider
{
    CarryOnConfig Config { get; }
}
```

Minimal interface. Two implementers:

| Implementer | File | Behavior |
|---|---|---|
| `CarrySystem` | `src/CarrySystem.cs:42` | Returns `ConfigService.Config` |
| `CarryManager` | `src/Common/Services/CarryManager.cs:21` | Delegates to its stored `IConfigProvider` |

---

## 2. Initialization Sequence

### 2a. Config File Loading (Server side only)

```
CarrySystem.StartPre()
  └─ ICoreServerAPI only:
       new ModConfig().Load(api)
         ├─ api.LoadModConfig<CarryOnConfig>("CarryOnConfig.json")
         │    └─ reads from <VintagestoryData>/ModConfig/CarryOnConfig.json
         ├─ if null ⇒ new CarryOnConfig(CurrentConfigVersion)  // defaults
         ├─ UpgradeVersion()  // migrates v1→2→3→4
         ├─ api.StoreModConfig(config, "CarryOnConfig.json")
         └─ world config tree:
              api.World.Config
                .GetOrAddTreeAttribute("carryon")
                .MergeTree(config.ToTreeAttribute())
```

`ModConfig.Load()` serializes the config into the world's `ITreeAttribute` storage.
This is how the config survives world reloads and syncs to clients.

### 2b. Config Deserialization (Both Sides)

After `ModConfig.Load()` has written the world config tree, `CarrySystem.StartPre()`
reads it back:

```
CarrySystem.StartPre()
  ├─ Api.Side == server:
  │    ModConfig.Load(api)          // loads file → world config tree
  │
  ├─ Api.Side == client:
  │    (world config tree already synced by VS network layer)
  │
  ├─ config = api.World.Config["carryon"] is ITreeAttribute tree
  │    ? CarryOnConfig.FromTreeAttribute(tree)   // deserialize from tree
  │    : new CarryOnConfig()                     // fallback defaults
  │
  └─ ConfigService = new CarryOnConfigService(config)
```

The **client** never reads `CarryOnConfig.json` from disk. It receives the config
via the server's world config sync or via the `ConfigSyncMessage` network packet.

### 2c. Config Distribution

In `CarrySystem.Start()`:

```
CarrySystem.Start()
  ├─ CarryManager = new CarryManager(api, this, CarryEvents)
  │    └─ this (CarrySystem) is passed as IConfigProvider
  │    └─ CarryManager.Config ⇒ ConfigProvider.Config ⇒ CarrySystem.Config ⇒ ConfigService.Config
  │
  ├─ CarryHandler = new CarryHandler(carryManager, this.Config, ...)
  │    └─ stores private copy of config (mutable, updated via UpdateConfig())
  │
  ├─ (client only) EntityCarryRenderer = new(api, carryManager, this.Config, ...)
  │    └─ stores private copy, passes to CacheManager & Dispatcher
  │
  └─ WireConfigChangeHandlers()
       └─ subscribes to ConfigService.OnConfigChanged
```

---

## 3. Config Consumer Chain

```
CarrySystem (IConfigProvider)
  │
  ├── Config property ⇒ ConfigService.Config
  │
  ├── AS IConfigProvider → CarryManager constructor
  │    └── CarryManager.Config (delegates to IConfigProvider.Config)
  │
  ├── AS CarryOnConfig (value copy) → CarryHandler
  │    └── → CarryInteractionController (via InitClient)
  │         └── → CarryInteractionValidator
  │              └── Invalidates parsed arrays (PreventSwapFromBackOnTarget)
  │
  ├── AS CarryOnConfig (value copy) → EntityCarryRenderer
  │    └── → CarryRenderCacheManager
  │    └── → CarryRenderDispatcher
  │
  └── AS CarryOnConfig (value copy) → BehavioralConditioning.Init(api, config)
       └── One-shot at AssetsFinalize (not wired to reload)
```

Every consumer stores a **private mutable field** for config, then relies on
`UpdateConfig()` to receive new instances on change.

---

## 4. Config Hotloading

### 4a. Triggers

| Trigger | Initiator | Path |
|---|---|---|
| File change (disk) | `CarryOnConfigService.SetupFileWatcher()` | `OnConfigFileChanged()` → `ReloadFromFile()` → `Replace()` |
| Network sync | Server broadcasts `ConfigSyncMessage` | Client `CarrySystem.OnClientConfigSync()` → `ConfigService.Replace()` |
| Console command | `/carryon reload` | Calls `ConfigService.Reload()` |
| Programmatic | Any code | Calls `ConfigService.Replace(newConfig)` |

### 4b. File Watcher (Server Only)

`CarryOnConfigService.SetupFileWatcher(ICoreServerAPI)` creates a
`FileSystemWatcher` on `<VintagestoryData>/ModConfig/CarryOnConfig.json`:

```
FileSystemWatcher.Changed / .Created
  └─ OnConfigFileChanged()
       ├─ debounce 500ms (prevents double-fire)
       ├─ Thread.Sleep(100)  (let file IO settle)
       └─ api.Event.EnqueueMainThreadTask(ReloadFromFile)
            ├─ api.LoadModConfig<CarryOnConfig>(ConfigFile)
            ├─ reloaded.UpgradeVersion()
            └─ Replace(reloaded)
                 ├─ Config = newConfig
                 └─ OnConfigChanged?.Invoke(newConfig)
```

Disabled when `DebuggingOptions.DisableConfigWatcher` is `true`.

### 4c. `Replace()` — The Central Mutation Point

`CarryOnConfigService.Replace(CarryOnConfig newConfig)` is the single entry
point for all config changes. It swaps the in-memory config and fires the event:

```csharp
public void Replace(CarryOnConfig newConfig)
{
    Config = newConfig;
    OnConfigChanged?.Invoke(newConfig);
}
```

### 4d. Subscriber Chain (`WireConfigChangeHandlers()`)

All in `CarrySystem.Start()`:

| Order | Subscriber | Effect |
|---|---|---|
| 1 | `Config.InvalidateBackpackCache()` | Clears lazy-parsed `BackpackMapping` dictionary |
| 2 | `CarryHandler?.UpdateConfig(cfg)` | Updates private config; forwards to `InteractionController` → `Validator` → `InvalidateConfigCache()` (re-parses `PreventSwapFromBackOnTarget` filters) |
| 3 | `EntityCarryRenderer?.UpdateConfig(cfg)` | Updates private config; forwards to `CacheManager.UpdateConfig()`, `Dispatcher.UpdateConfig()` |
| 4 | `EntityCarriedBlock.Config = Config.CarriedBlockEntity` | Carried block entity behavior settings |
| 5 | (server) persist to world config + disk | `ServerApi.StoreModConfig(Config, ConfigFile)` |
| 6 | (server) broadcast to clients | `ServerChannel.BroadcastPacket(new ConfigSyncMessage(json))` |

### 4e. Network Sync

```
Server (OnConfigChanged):
  ├─ json = JsonConvert.SerializeObject(Config)
  └─ ServerChannel.BroadcastPacket(new ConfigSyncMessage(json))

Client (CarrySystem.OnClientConfigSync):
  ├─ var reloaded = JsonConvert.DeserializeObject<CarryOnConfig>(message.ConfigJson)
  ├─ reloaded.UpgradeVersion()
  └─ ConfigService.Replace(reloaded)
       └─ fires OnConfigChanged (subscribers 1-4 above run client-side)
```

---

## 5. Flow Diagram

```
                          ┌──────────────────────────────┐
                          │       CarryOnConfig.json      │
                          │  <VintagestoryData>/ModConfig │
                          └──────────────┬───────────────┘
                                         │
                          ┌──────────────▼───────────────┐
                          │  ModConfig.Load(api)         │
                          │  (server StartPre)           │
                          │  • LoadModConfig             │
                          │  • UpgradeVersion            │
                          │  • StoreModConfig            │
                          │  • worldConfig.MergeTree()   │
                          └──────────────┬───────────────┘
                                         │ (via world config ITreeAttribute)
                          ┌──────────────▼───────────────┐
                          │  CarryOnConfig.FromTreeAttr()│
                          │  (both sides, StartPre)      │
                          └──────────────┬───────────────┘
                                         │
                          ┌──────────────▼───────────────┐
                          │  CarryOnConfigService(config) │
                          │  • Config property            │
                          │  • OnConfigChanged event      │
                          │  • Replace() / Reload()       │
                          │  • SetupFileWatcher()         │
                          └──────┬───────────────────────┘
                                 │
                    ┌────────────┼────────────────────────────┐
                    │            │                            │
         ┌──────────▼────┐ ┌────▼──────────┐     ┌───────────▼──────────┐
         │  CarrySystem   │ │ CarryHandler  │     │ EntityCarryRenderer  │
         │  (IConfigPro-  │ │ (private copy)│     │ (private copy)       │
         │   vider)       │ │              │     │                      │
         │  Config ⇒      │ │ UpdateConfig │     │ UpdateConfig         │
         │  ConfigService │ │   ↓           │     │   ↓                  │
         │                │ │ Interaction   │     ├─ CacheManager        │
         │  Passed as     │ │ Controller    │     └─ Dispatcher         │
         │  IConfigProv.  │ │   ↓           │                          │
         │  → CarryManager│ │ Validator     │                          │
         └────────────────┘ └──────────────┘     └──────────────────────┘

                     HOT RELOAD FLOW (server):

  Disk change ──► FileWatcher ──► ReloadFromFile()
                                    │
                                    ▼
  /carryon reload ─────────────► Reload() ──► Replace(newConfig)
                                    │
                                    ▼
                            OnConfigChanged
                                    │
              ┌─────────────────────┼──────────────────────┐
              ▼                     ▼                      ▼
      InvalidateBackpack     Update Consumers        Broadcast to
              │              (Handler, Renderer,     Clients via
              │               EntityCarriedBlock)    ConfigSyncMessage
              │                                         │
              ▼                                         ▼
      BackpackMapping                              Client Dispatches
      recalculated on                              OnClientConfigSync
      next access                                         │
                                                          ▼
                                                   ConfigService.Replace()
                                                          │
                                                          ▼
                                                   OnConfigChanged
                                                   (client-side subscribers)
```

---

## 6. Key Design Points

- **ConfigService is the single source of truth.** `CarrySystem.Config` delegates
  to it. `CarryManager.Config` delegates through `CarrySystem` via `IConfigProvider`.
- **Private mutable copies.** Most consumers take a config snapshot at construction
  and rely on `UpdateConfig()` for hot-reload. This avoids coupling to the service
  lifecycle and makes testing simpler.
- **Server-authoritative.** Only the server can trigger hot-reload from disk.
  Clients receive the config as a serialized JSON message.
- **Two serializations at startup.** `ModConfig.Load()` writes POCO → `ITreeAttribute`
  (world config), then `StartPre()` reads `ITreeAttribute` → POCO. This ensures
  the config is always consistent with world storage.
- **`BehavioralConditioning` is NOT wired to hot-reload.** It runs once at
  `AssetsFinalize`. Carryables, interactables, and filter changes require restart.

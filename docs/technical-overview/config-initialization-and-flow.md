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
| `CarryManager` | `src/Common/Services/CarryManager.cs:21` | Delegates to its stored `IConfigProvider` (`CarrySystem`) |

### Config Access Pattern

`ICarryManager` (in CarryOnLib) cannot expose `CarryOnConfig` directly —
the config type is too volatile for the API library. Consumers that need
config hold `ICarryManager` and cast to `IConfigProvider` once in their
constructor:

```csharp
this.configProvider = (IConfigProvider)carryManager
    ?? throw new ArgumentException("carryManager must implement IConfigProvider", ...);
```

This cast is safe: `CarryManager` is the only `ICarryManager` implementation
and always implements both interfaces. The cast fires once per instance and
has zero hot-path cost.

**4 consumers use this pattern:**
- `CarryHandler` (`src/Common/Handlers/CarryHandler.cs`)
- `CarryInteractionValidator` (`src/Client/Logic/Interaction/`)
- `CarryRenderCacheManager` (`src/Client/Logic/CarryRenderer/`)
- `CarryRenderDispatcher` (`src/Client/Logic/CarryRenderer/`)

---

## 2. Initialization Sequence

### 2a. Server Startup (`StartPre`)

```mermaid
flowchart TD
    disk["CarryOnConfig.json<br>&lt;VintagestoryData&gt;/ModConfig"]
    load["ModConfig.Load(ICoreServerAPI)"]
    upgrade["UpgradeVersion()"]
    worldTree["World Config Tree<br>api.World.Config['carryon']"]
    store["api.StoreModConfig()"]
    configService["CarryOnConfigService(config)"]
    clientSync["VS network syncs world tree<br>to connected clients"]

    disk -->|LoadModConfig| load
    load -->|null? use defaults| upgrade
    upgrade --> configService
    upgrade --> store
    upgrade --> worldTree
    worldTree --> clientSync
```

**`ModConfig.Load(ICoreServerAPI)`** returns `CarryOnConfig?`:

```csharp
// src/Server/Logic/ModConfig.cs
public CarryOnConfig? Load(ICoreServerAPI api)
{
    var config = api.LoadModConfig<CarryOnConfig>(ConfigFile)
                 ?? new CarryOnConfig(CurrentConfigVersion);
    config.UpgradeVersion();
    api.StoreModConfig(config, ConfigFile);
    // write to world config tree (needed for client sync)
    var worldTree = api.World.Config.GetOrAddTreeAttribute("carryon");
    worldTree.MergeTree(config.ToTreeAttribute());
    return config;   // caller uses this directly
}
```

### 2b. Client Startup (`StartPre`)

```mermaid
flowchart TD
    worldTree["World Config Tree<br>(synced by VS network layer)"]
    fromTree["CarryOnConfig.FromTreeAttribute(tree)"]
    defaults["new CarryOnConfig()"]
    configService["CarryOnConfigService(config)"]

    worldTree -->|tree exists| fromTree
    worldTree -->|no tree| defaults
    fromTree --> configService
    defaults --> configService
```

The **client** never reads `CarryOnConfig.json` from disk. It receives the config
via the server's world config sync (VS built-in) or via the `ConfigSyncMessage`
network packet (for hot-reload).

### 2c. Server + Client Combined (`StartPre`)

```csharp
// src/CarrySystem.cs
CarryOnConfig config;

if (api is ICoreServerAPI sapi)
{
    config = new ModConfig().Load(sapi) ?? new CarryOnConfig();
}
else
{
    var tree = api.World.Config?.GetTreeAttribute(ModId);
    config = tree != null
        ? CarryOnConfig.FromTreeAttribute(tree)
        : new CarryOnConfig();
}

ConfigService = new CarryOnConfigService(config);
```

**Key improvement over the old architecture:** The server no longer performs a
redundant deserialization round-trip. `ModConfig.Load()` returns the loaded
config directly. The world config tree is still written (for client sync), but
the server doesn't read it back.

### 2d. Distribution (`Start`)

```mermaid
flowchart TD
    cfgService["CarryOnConfigService"]
    cs["CarrySystem (IConfigProvider)<br>Config ⇒ ConfigService.Config"]
    cm["CarryManager<br>(receives CarrySystem as IConfigProvider)"]
    ch["CarryHandler<br>reads via configProvider.Config"]
    er["EntityCarryRenderer<br>reads via child renderers"]
    wired["WireConfigChangeHandlers()"]

    cs -->|"new CarryManager(api, this, ...)"| cm
    cs -->|"new CarryHandler(carryManager, ...)"| ch
    cs -->|"new EntityCarryRenderer(api, carryManager, ...)"| er
    cs --> wired

    cm -.->|cast to IConfigProvider| ch
    cm -.->|cast to IConfigProvider| er
```

No consumer stores a private `CarryOnConfig` copy. All read live from
`carryManager` via the cast-to-`IConfigProvider` pattern.

---

## 3. Config Consumer Chain

```mermaid
flowchart LR
    cs["CarrySystem<br>(IConfigProvider)"]
    cs -->|Config| csc["ConfigService.Config"]
    cs -->|as IConfigProvider| cm["CarryManager.Config"]
    cm -->|cast to IConfigProvider| ch["CarryHandler"]
    cm -->|cast to IConfigProvider| cv["CarryInteractionValidator"]
    cm -->|cast to IConfigProvider| rcm["CarryRenderCacheManager"]
    cm -->|cast to IConfigProvider| rd["CarryRenderDispatcher"]
    cs -->|direct| bc["BehavioralConditioning<br>(one-shot, not wired to reload)"]
    cs -->|direct| ecb["EntityCarriedBlock.Config<br>(static, pushed on change)"]
```

| Consumer | Access Path | Reacts to Change? |
|---|---|---|
| `CarryHandler` | `configProvider.Config.X` | Yes — relays to `RefreshConfigCache()` |
| `CarryInteractionValidator` | `configProvider.Config.X` | Yes — re-parses `PreventSwapFromBackOnTarget` |
| `CarryRenderCacheManager` | `configProvider.Config.X` | No — reads live each render tick |
| `CarryRenderDispatcher` | `configProvider.Config.X` | No — reads live each render tick |
| `EntityCarriedBlock` (static) | Pushed via subscriber | Yes — `Config = Config.CarriedBlockEntity` |
| `BehavioralConditioning` | Direct `CarryOnConfig` param | No — one-shot at `AssetsFinalize` |

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

```mermaid
flowchart TD
    fs["FileSystemWatcher<br>.Changed / .Created"]
    debounce["Debounce 500ms<br>Thread.Sleep(100)"]
    mainThread["EnqueueMainThreadTask<br>(ReloadFromFile)"]
    load["api.LoadModConfig<br>&lt;CarryOnConfig&gt;"]
    upgrade["UpgradeVersion()"]
    replace["Replace(reloaded)<br>Config = newConfig<br>OnConfigChanged?.Invoke(newConfig)"]

    fs --> debounce
    debounce --> mainThread
    mainThread --> load
    load --> upgrade
    upgrade --> replace
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
| 2 | `CarryHandler?.RefreshConfigCache()` | Forwards to `InteractionController` → `Validator` → re-parses `PreventSwapFromBackOnTarget` filters |
| 3 | `EntityCarriedBlock.Config = Config.CarriedBlockEntity` | Carried block entity behavior settings |
| 4 | (server) persist to world config + disk | `ServerApi.StoreModConfig(Config, ConfigFile)` |
| 5 | (server) broadcast to clients | `ServerChannel.BroadcastPacket(new ConfigSyncMessage(json))` |

No more forwarding to 6+ individual `UpdateConfig()` methods. Consumers
read config live from `carryManager` — they don't need to be pushed a copy.

### 4e. Network Sync

```mermaid
sequenceDiagram
    participant Server
    participant Clients
    Server->>Server: ConfigService.Replace(newConfig)
    Server->>Server: OnConfigChanged fired
    Server->>Server: JsonConvert.SerializeObject(Config)
    Server->>Clients: BroadcastPacket(ConfigSyncMessage(json))
    Clients->>Clients: OnClientConfigSync(message)
    Clients->>Clients: Deserialize + UpgradeVersion
    Clients->>Clients: ConfigService.Replace(reloaded)
    Clients->>Clients: OnConfigChanged fired (client subscribers)
```

---

## 5. Full Flow Diagram

```mermaid
flowchart TB
    subgraph Startup["Startup Sequence"]
        disk["CarryOnConfig.json"]
        sapi["ICoreServerAPI"]
        modcfg["ModConfig.Load(sapi)"]
        upgrade["UpgradeVersion()"]
        worldtree["World Config Tree"]
        store["StoreModConfig"]
        csc["CarryOnConfigService(config)"]
    end

    subgraph Distribution["Config Distribution"]
        cs["CarrySystem<br>(IConfigProvider)"]
        cm["CarryManager<br>(delegates to IConfigProvider)"]
        ch["CarryHandler<br>(reads via IConfigProvider cast)"]
        cv["Validator<br>(reads via IConfigProvider cast)"]
        rcm["CacheManager<br>(reads via IConfigProvider cast)"]
        rd["Dispatcher<br>(reads via IConfigProvider cast)"]
        ecb["EntityCarriedBlock.Config<br>(static push)"]
    end

    subgraph HotReload["Hot Reload"]
        fw["FileSystemWatcher"]
        reload["ReloadFromFile()"]
        replace["Replace(newConfig)"]
        evt["OnConfigChanged"]
        inv_bp["InvalidateBackpackCache"]
        inv_v["RefreshConfigCache"]
        push_ecb["EntityCarriedBlock.Config = ..."]
        persist["Persist world tree + disk"]
        broadcast["Broadcast ConfigSyncMessage"]
    end

    disk --> modcfg
    sapi --> modcfg
    modcfg --> upgrade
    upgrade --> worldtree
    upgrade --> store
    upgrade --> csc

    csc --> cs
    cs --> cm
    cm --> ch
    cm --> cv
    cm --> rcm
    cm --> rd
    cs --> ecb

    fw --> reload --> replace --> evt
    evt --> inv_bp
    evt --> inv_v
    evt --> push_ecb
    evt --> persist
    evt --> broadcast
```

---

## 6. Key Design Points

- **`ConfigService` is the single source of truth.** `CarrySystem.Config` delegates
  to it. `CarryManager.Config` delegates through `CarrySystem` via `IConfigProvider`.
- **No private copies.** All consumers read config live from `carryManager` via the
  `IConfigProvider` cast. `UpdateConfig()` no longer exists.
- **Server-authoritative.** Only the server can trigger hot-reload from disk.
  Clients receive the config as a serialized JSON message.
- **One serialization at startup.** `ModConfig.Load()` returns the loaded config
  directly. The server no longer reads it back from the world config tree. The
  world tree is still written (for client sync), but only the client deserializes
  from it.
- **`BehavioralConditioning` is NOT wired to hot-reload.** It runs once at
  `AssetsFinalize`. Carryables, interactables, and filter changes require restart.
- **The cast pattern is a deliberate trade-off.** `ICarryManager` (CarryOnLib)
  can't reference the volatile `CarryOnConfig` type. The runtime cast to
  `IConfigProvider` is safe (`CarryManager` always implements both) and has
  zero hot-path cost. Four consumers use it.

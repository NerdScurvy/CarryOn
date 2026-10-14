# Entity Handler System in CarryOn

This document explains the entity handler system that allows entity types to integrate with the carry pickup and placement pipelines.

The system is split into two independent directions, each with its own interface and registry:

- **Pickup direction** (entity to CarriedBlock): `ICarryableEntityPickupHandler` — resolves per-entity, either via behavior scan or singleton registry.
- **Placement/drop direction** (CarriedBlock to entity): `ICarryEntitySpawnerFactory` — always resolved via singleton registry using `CarriedBlock.SpawnerFactoryCode`.

This split allows external mods to control entity spawning while referencing only **CarryOnLib** — they do not need to depend on CarryOn.

## 0. Scope

This pipeline covers:

- [src/API/Common/Interfaces/ICarryableEntityPickupHandler.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Interfaces/ICarryableEntityPickupHandler.cs)
- [src/API/Common/Interfaces/ICarryEntitySpawnerFactory.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Interfaces/ICarryEntitySpawnerFactory.cs)
- [src/API/Common/Interfaces/ICarryManager.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Interfaces/ICarryManager.cs)
- [src/API/Common/Models/CarriedBlock.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Models/CarriedBlock.cs)
- [src/API/Common/Models/EntitySpawnContext.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Models/EntitySpawnContext.cs)
- [src/API/Common/Models/CarryConstants.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Models/CarryConstants.cs)
- [src/Utility/CarriedBlockTreeSerializer.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/Utility/CarriedBlockTreeSerializer.cs)
- `src/Common/Behaviors/EntityBehaviorCarryable.cs`
- `src/Common/Handlers/CarryHandlers/CarriedBlockEntityHandler.cs`
- `src/Common/Handlers/CarryHandlers/CarriedBlockEntityFactory.cs`
- `src/Utility/EntityHandlerSpawner.cs`
- `src/Common/Services/CarryPlacementService.cs`
- `src/Common/Services/CarryDropService.cs`
- `src/CarrySystem.cs`
- `resources/assets/carryon/entities/carriedblock.json`

---

## 1. `ICarryableEntityPickupHandler` — Pickup Direction

**File:** [src/API/Common/Interfaces/ICarryableEntityPickupHandler.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Interfaces/ICarryableEntityPickupHandler.cs)

Handles the entity-to-CarriedBlock conversion: access control, serialization, and post-pickup cleanup.

```csharp
public interface ICarryableEntityPickupHandler
{
    float GetInteractDelay(Entity entity, ICoreAPI api);

    bool CanPickup(
        EnumGameMode gameMode,
        string? playerUid,
        Entity entity,
        ICoreAPI api);

    CarriedBlock? SerializeToCarriedBlock(
        Entity entity,
        CarrySlot slot,
        ICoreAPI api);

    void OnPickedUp(
        Entity entity,
        IPlayer player,
        CarriedBlock carriedBlock,
        ICoreAPI api);
}
```

The player does NOT carry the entity itself — they carry a `CarriedBlock` (a block representation). The handler bridges the two forms.

### Method Details

| Method | Side | Purpose |
|--------|------|---------|
| `GetInteractDelay(entity, api)` | Client | Interaction delay in seconds for the progress circle. May be customised per entity; falls back to `CarryCodes.Defaults.PickUpSpeed`. |
| `CanPickup(...)` | Both | Access control check. Called on client (for interaction help) and server (for validation). |
| `SerializeToCarriedBlock(...)` | Server | Converts the entity's in-world data into a `CarriedBlock`. Should set `SpawnerFactoryCode` on the returned `CarriedBlock` so the placement pipeline can look up the correct factory. Returns null if conversion fails. |
| `OnPickedUp(...)` | Server | Called after successful pickup. Responsible for entity cleanup/despawn (e.g., `entity.Die(PickedUp)`). |

### Handler Responsibility for OnPickedUp

The handler is responsible for removing or despawning the entity from the world if that is the desired behavior. The default `CarriedBlockEntityHandler` calls `entity.Die(EnumDespawnReason.PickedUp, null)` and plays a sound. A modder handler could instead keep the entity alive and drain inventory slots.

---

## 2. `ICarryEntitySpawnerFactory` — Placement/Spawn Direction

**File:** [src/API/Common/Interfaces/ICarryEntitySpawnerFactory.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Interfaces/ICarryEntitySpawnerFactory.cs)

Handles the CarriedBlock-to-entity conversion: spawn decision, entity creation, and post-spawn setup. Registered as an instance to support dependency injection.

```csharp
public interface ICarryEntitySpawnerFactory
{
    bool ShouldSpawnAsEntity(
        CarriedBlock carriedBlock,
        bool dropped,
        ICoreAPI api);

    Entity? SpawnFromCarriedBlock(EntitySpawnContext context);

    void OnSpawned(
        Entity entity,
        CarriedBlock carriedBlock,
        ICoreAPI api);
}
```

### Method Details

| Method | Side | Purpose |
|--------|------|---------|
| `ShouldSpawnAsEntity(...)` | Server | Determines whether the carried block should spawn an entity instead of being placed as a block. `dropped` is true when the block was dropped (damage, death, quick-drop) rather than deliberately placed. |
| `SpawnFromCarriedBlock(context)` | Server | Spawns the entity. Receives an `EntitySpawnContext` with the carried block, owner UID, precise position, block position, and API reference. Returns the spawned entity, or null on failure. |
| `OnSpawned(...)` | Server | Called after successful entity spawn. Post-spawn setup (e.g., play sound). |

### `EntitySpawnContext`

**File:** [src/API/Common/Models/EntitySpawnContext.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Models/EntitySpawnContext.cs)

Context object passed to `SpawnFromCarriedBlock`:

| Property | Type | Description |
|----------|------|-------------|
| `CarriedBlock` | `CarriedBlock` | The carried block being spawned as an entity. |
| `OwnerUid` | `string` | UID of the player who placed/dropped this block. |
| `Position` | `Vec3d` | Precise world position (floating-point). For placement: block position + hit offset. For drop: player entity position. |
| `BlockPosition` | `BlockPos` | Integer block coordinates of the targeted cell. |
| `HitPosition` | `Vec3d?` | Exact hit point within the targeted block (0-1). Null for drops. |
| `Face` | `BlockFacing?` | The block face that was hit. Null for drops. |
| `Dropped` | `bool` | True when spawned from a drop (damage, death, quick-drop) vs. deliberate placement. |
| `Api` | `ICoreAPI` | The API instance. The pipeline verifies this is `ICoreServerAPI` before calling the factory. |

---

## 3. The Handler Registries

**File:** [src/API/Common/Interfaces/ICarryManager.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Interfaces/ICarryManager.cs)

`ICarryManager` exposes two independent registries — one for pickup handlers, one for spawner factories.

### Pickup Handler Registry

```csharp
void RegisterEntityPickupHandler(string handlerCode, ICarryableEntityPickupHandler handler);
bool TryGetEntityPickupHandler(string handlerCode, out ICarryableEntityPickupHandler? handler);
bool IsEntityPickupHandlerRegistered(string handlerCode);
IEnumerable<string> EnumerateEntityPickupHandlers();
```

### Spawner Factory Registry

```csharp
void RegisterEntitySpawnerFactory(string factoryCode, ICarryEntitySpawnerFactory factory);
bool TryGetEntitySpawnerFactory(string factoryCode, out ICarryEntitySpawnerFactory? factory);
bool IsEntitySpawnerFactoryRegistered(string factoryCode);
IEnumerable<string> EnumerateEntitySpawnerFactories();
```

Codes are namespaced during registration via `CarryCodes.GetCarryCode()` (prepends `"carryon:"`). Both registries support bare-code lookup with automatic `carryon:` prefix fallback.

---

## 4. `EntityBehaviorCarryable` — The Behavior

**File:** `src/Common/Behaviors/EntityBehaviorCarryable.cs`

This entity behavior marks an entity as carry-interactable. When a player right-clicks an entity with this behavior, the interaction pipeline activates.

### Behavior Name

The behavior property name is `carryon:carryable`. In the entity JSON, behaviors reference it with `"code": "carryon:carryable"`.

### Handler Resolution

The behavior resolves its handler lazily via `EnsureHandler()` (`:136`). The `handlerCode` attribute from the entity's JSON drives resolution:

```csharp
private bool EnsureHandler()
{
    if (Handler != null) return true;
    if (carryManager == null) return false;

    if (string.IsNullOrEmpty(handlerCode))
    {
        if (string.IsNullOrEmpty(slotHandlerCode))
        {
            entity?.World?.Logger?.Warning(
                "EntityBehaviorCarryable on {0} has handlerCode not set. Disabling carry behavior.",
                entity?.Code);
        }
        return false;
    }

    // "auto" -> scan entity behaviors for ICarryableEntityPickupHandler
    if (handlerCode == CarryCodes.ReservedCodes.Auto)
    {
        if (entity == null) return false;

        foreach (var behavior in entity.SidedProperties?.Behaviors ?? [])
        {
            if (behavior is ICarryableEntityPickupHandler pickupBehavior)
            {
                Handler = pickupBehavior;
                return true;
            }
        }

        entity?.World?.Logger?.Warning(
            "EntityBehaviorCarryable: No ICarryableEntityPickupHandler behavior found on {0}. " +
            "Disabling carry behavior.",
            entity?.Code);
        return false;
    }

    // Named code -> registry lookup
    if (carryManager.TryGetEntityPickupHandler(handlerCode, out var instance))
    {
        Handler = instance;
        return true;
    }

    entity?.World?.Logger?.Warning(
        "EntityBehaviorCarryable: No handler registered for code '{0}'. Disabling carry behavior.",
        handlerCode);
    return false;
}
```

Three resolution paths:

| `handlerCode` value | Behavior |
|---------------------|----------|
| *(empty/null)* | No pickup available. Entity cannot be picked up. If `slotHandlerCode` is also empty, a warning is logged. |
| `"auto"` | Scans `entity.SidedProperties.Behaviors` for the first behavior implementing `ICarryableEntityPickupHandler`. The behavior IS the handler — no singleton registration needed. |
| `"<code>"` | Registry lookup via `TryGetEntityPickupHandler(code)`. Returns a singleton handler registered at startup. |

### JSON Configuration Attributes

The behavior reads attributes from the entity definition JSON:

```json
{
    "code": "carryon:carryable",
    "handlerCode": "carriedblock",
    "showHelp": true,
    "interactDelay": 0.8,
    "slotInteractDelay": 1.2,
    "pickupHelpLangCode": "carryon:help-pickup",
    "pickupHelpIcon": "carryon:icon-handsfree",
    "pickupMode": "interact"
}
```

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `handlerCode` | string | *(required)* | Drives handler resolution. `"auto"` scans behaviors; a named code looks up the registry; empty disables pickup. |
| `showHelp` | bool | `true` | Whether to show the world interaction help text. |
| `interactDelay` | float | `0.8` (via handler) | Pickup interaction hold time in seconds. |
| `slotInteractDelay` | float | `0.8` (via handler) | Attach/detach interaction hold time for slot-handler mode. |
| `pickupHelpLangCode` | string | `"carryon:help-pickup"` | Lang code for the interaction help action text. |
| `pickupHelpIcon` | string | `"carryon:icon-handsfree"` | Item code for the interaction help icon. |
| `pickupMode` | string | `"interact"` | `"interact"` = right mouse only; `"pickup-hotkey"` = hold Pick Up/Put Down hotkey + right mouse. |

### Initialization

`EntityBehaviorCarryable.Init(ICarryManager)` must be called during mod startup on both client and server to inject the carry manager reference:

```csharp
EntityBehaviorCarryable.Init(CarryManager!);
```

---

## 5. `CarriedBlock` Model — `SpawnerFactoryCode`

**File:** [src/API/Common/Models/CarriedBlock.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Models/CarriedBlock.cs)

The `CarriedBlock` model carries a `SpawnerFactoryCode` property that routes the placement pipeline:

```csharp
public class CarriedBlock
{
    public CarrySlot Slot { get; }
    public ItemStack ItemStack { get; }
    public Block Block => ItemStack.Block;
    public ITreeAttribute? BlockEntityData { get; }
    public IReadOnlyList<AttachedCarriedBlock>? AttachedBlocks { get; }
    public bool HasAttachedBlocks => AttachedBlocks is { Count: > 0 };
    public AssetLocation? OriginalBlockCode { get; }
    public float? OriginalMeshAngle { get; }
    public string? SpawnerFactoryCode { get; }
    // ...
}
```

`SpawnerFactoryCode` is set during entity pickup by the pickup handler's `SerializeToCarriedBlock()`. During placement/drop, `EntityHandlerSpawner` reads this code to look up the spawner factory. If `SpawnerFactoryCode` is null, the block is placed normally — no entity spawn is attempted.

This is a clean binary: either the carried block was created from an entity (and has a factory code), or it was picked up from the world (and places as a block).

### Serialization Constant

**File:** [src/API/Common/Models/CarryConstants.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/API/Common/Models/CarryConstants.cs)

```csharp
public static class CarryConstants
{
    public static class AttributeKeys
    {
        public static class CarriedBlockData
        {
            public const string SpawnerFactoryCode = "SpawnerFactoryCode";
            // ...
        }
    }
}
```

---

## 6. SpawnerFactoryCode Serialization

**File:** [src/Utility/CarriedBlockTreeSerializer.cs](https://github.com/NerdScurvy/CarryOnLib/blob/main/src/Utility/CarriedBlockTreeSerializer.cs)

The `SpawnerFactoryCode` is serialized into the `CarriedBlock`'s tree attribute and survives save/load cycles:

```csharp
// Serialize
if (carriedBlock.SpawnerFactoryCode != null)
    tree.SetString(AttributeKeys.CarriedBlockData.SpawnerFactoryCode, carriedBlock.SpawnerFactoryCode);

// Deserialize
var spawnerFactoryCodeStr = tree.GetString(AttributeKeys.CarriedBlockData.SpawnerFactoryCode, null);
var carriedBlock = new CarriedBlock(slot, stack, blockEntityData, attachedBlocks, originalCode, originalMeshAngle, spawnerFactoryCodeStr);
```

---

## 7. `CarriedBlockEntityHandler` — Default Pickup Handler

**File:** `src/Common/Handlers/CarryHandlers/CarriedBlockEntityHandler.cs`

The default pickup handler for `EntityCarriedBlock` — the built-in "dropped block" entity type. Implements `ICarryableEntityPickupHandler`.

### Handler Code

```csharp
public const string HandlerCode = "carriedblock";
```

Registered as `"carryon:carriedblock"` via `GetCarryCode(HandlerCode)`.

### Configuration

The handler reads from `CarriedBlockEntity` config section:

```json
{
    "CarriedBlockEntity": {
        "DropMode": "EntityOnFailedPlacement",
        "Scale": 0.6,
        "PickupAccess": "Owner",
        "GracePeriodSeconds": 30,
        "RandomDropRotation": false
    }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DropMode` | enum | `EntityOnFailedPlacement` | `EntityAlways`, `EntityOnFailedPlacement`, or `Items` |
| `Scale` | float | `0.6` | Visual and collision scale of dropped entity |
| `PickupAccess` | enum | `Owner` | Who can pick up: `Owner`, `Everyone`, or `OwnerOrOp` |
| `GracePeriodSeconds` | int | `30` | Seconds only the owner can pick up |
| `RandomDropRotation` | bool | `false` | Random yaw on drop |

### Pickup Lifecycle

| Method | Behavior |
|--------|----------|
| `GetInteractDelay` | Returns `EntityBehaviorCarryable.InteractDelay` if set, otherwise `CarryCodes.Defaults.PickUpSpeed`. |
| `CanPickup` | Checks `CarriedBlockAccessPolicy.CanPickup()` against `PickupAccess`, owner UID, grace period, and game mode. |
| `SerializeToCarriedBlock` | Extracts `ITreeAttribute` from `EntityCarriedBlock.CarriedBlockTree`, deserializes via `CarriedBlockTreeSerializer`, then rebuilds the `CarriedBlock` with `SpawnerFactoryCode = HandlerCode` injected via the constructor. |
| `OnPickedUp` | Plays the block's place sound, calls `entity.Die(EnumDespawnReason.PickedUp, null)`. |

---

## 8. `CarriedBlockEntityFactory` — Default Spawner Factory

**File:** `src/Common/Handlers/CarryHandlers/CarriedBlockEntityFactory.cs`

The default spawner factory registered alongside the pickup handler. Implements `ICarryEntitySpawnerFactory`.

### Factory Code

```csharp
public static string FactoryCode { get; } = CarriedBlockEntityHandler.HandlerCode;
```

Uses the same code as the pickup handler: `"carryon:carriedblock"`.

### Spawn Lifecycle

| Method | Behavior |
|--------|----------|
| `ShouldSpawnAsEntity` | Always returns `true`. The drop mode check (EntityAlways, EntityOnFailedPlacement, Items) is handled upstream by `CarryDropService` and `CarryPlacementService`, not here. |
| `SpawnFromCarriedBlock` | Resolves `CarrySystem` from the API, gets `CarriedBlockEntityService`, and calls `SpawnCarriedBlockEntityWithGravity()` with scale and random yaw from config. |
| `OnSpawned` | No-op. The default dropped block entity does not play a placement sound. |

---

## 9. `EntityHandlerSpawner` — The Placement Pipeline

**File:** `src/Utility/EntityHandlerSpawner.cs`

A static utility that bridges the carried block to the spawner factory. Both `CarryPlacementService` and `CarryDropService` delegate to this class.

```csharp
public static class EntityHandlerSpawner
{
    public static bool TrySpawn(
        ICarryManager carryManager,
        Entity entity,
        EntitySpawnContext context)
    {
        var carriedBlock = context.CarriedBlock;

        if (carriedBlock.SpawnerFactoryCode == null ||
            !carryManager.TryGetEntitySpawnerFactory(carriedBlock.SpawnerFactoryCode, out var factory) ||
            factory == null)
        {
            return false;
        }

        if (!factory.ShouldSpawnAsEntity(carriedBlock, context.Dropped, context.Api))
            return false;

        if (context.Api is not ICoreServerAPI) return false;

        var spawnedEntity = factory.SpawnFromCarriedBlock(context);
        if (spawnedEntity == null) return false;

        factory.OnSpawned(spawnedEntity, carriedBlock, context.Api);
        carryManager.RemoveCarried(entity, carriedBlock.Slot);
        return true;
    }

    public static bool IsEntitySpawnApplicable(
        ICoreAPI api, ICarryManager carryManager,
        CarriedBlock carriedBlock, bool dropped)
    {
        if (carriedBlock.SpawnerFactoryCode == null ||
            !carryManager.TryGetEntitySpawnerFactory(carriedBlock.SpawnerFactoryCode, out var factory) ||
            factory == null)
        {
            return false;
        }

        return factory.ShouldSpawnAsEntity(carriedBlock, dropped, api);
    }
}
```

### Resolution chain

```
CarriedBlock.SpawnerFactoryCode
    |
    v
carryManager.TryGetEntitySpawnerFactory(code)
    |
    v
factory.ShouldSpawnAsEntity(carriedBlock, dropped, api)
    |
    v
factory.SpawnFromCarriedBlock(context)
    |
    v
factory.OnSpawned(entity, carriedBlock, api)
```

No fallback chains, no dual registries. If `SpawnerFactoryCode` is null, the method returns `false` immediately — the block is placed normally.

---

## 10. Registration

### Default Handler and Factory

In `CarrySystem.Start(ICoreAPI api)`:

```csharp
carryManager.RegisterEntityPickupHandler(
    CarriedBlockEntityHandler.HandlerCode,
    new CarriedBlockEntityHandler(this));
carryManager.RegisterEntitySpawnerFactory(
    CarriedBlockEntityHandler.HandlerCode,
    new CarriedBlockEntityFactory(api, this));
```

Both registrations use the same code (`"carryon:carriedblock"`), but they are in separate registries targeting separate interfaces.

### Behavior Registration

```csharp
api.Register<EntityBehaviorCarryable>();
```

This registers the behavior with the code `carryon:carryable`, matching the `"code": "carryon:carryable"` reference in entity JSON.

### Entity Class Registration

```csharp
api.RegisterEntity("EntityCarriedBlock", typeof(EntityCarriedBlock));
```

### External Mod Registration

Other mods register handlers and factories by accessing the carry manager:

```csharp
var carrySystem = api.ModLoader.GetModSystem<CarrySystem>();
var carryManager = carrySystem?.CarryManager;

// Pickup handler (if implementing a custom pickup handler)
carryManager?.RegisterEntityPickupHandler("mymod:myentity", new MyPickupHandler());

// Spawner factory (for custom entity spawning on placement/drop)
carryManager?.RegisterEntitySpawnerFactory("mymod:myentity", new MySpawnerFactory(api));
```

---

## 11. Pickup Pipeline

### Server-Side

**File:** `src/Common/Handlers/CarryHandlers/CarryServerHandler.cs`

```
1. Server receives PickupEntityMessage from client
2. Gets target entity by ID
3. Reads EntityBehaviorCarryable from entity
4. Calls behavior.IsCarryInteractable
   -> behavior.EnsureHandler() resolves handler via:
      - "auto" -> behavior scan for ICarryableEntityPickupHandler
      - named code -> carryManager.TryGetEntityPickupHandler(handlerCode)
5. CarryServerHandler.TryPickupFromEntityGeneric:
   a. handler.SerializeToCarriedBlock(entity, CarrySlot.Hands, api)
      -> converts entity to CarriedBlock, stamps SpawnerFactoryCode
   b. Check player is not already carrying in hands
   c. handler.CanPickup(gameMode, playerUid, entity, api)
      -> access control check
   d. carryManager.SetCarried(player.Entity, carriedBlock, CarrySlot.Hands)
      -> stores CarriedBlock on player
   e. handler.OnPickedUp(entity, player, carriedBlock, api)
      -> despawn/sound cleanup
```

### Client-Side Validation

The `CarryInteractionStateMachine` calculates the required interaction time using `handler.GetInteractDelay(entity, api)`. The `CarryInteractionValidator` checks for the `EntityBehaviorCarryable` behavior before sending the `PickupEntityMessage`.

---

## 12. Place/Drop Pipeline

### Placement

**File:** `src/Common/Services/CarryPlacementService.cs`

```
1. Player right-clicks to place a CarriedBlock
2. Check carriedBlock.SpawnerFactoryCode != null
3. carryManager.TryGetEntitySpawnerFactory(spawnerFactoryCode, out factory)
4. factory.ShouldSpawnAsEntity(carriedBlock, dropped: false, api)
5. If true:
   a. factory.SpawnFromCarriedBlock(context) -> spawn entity
   b. factory.OnSpawned(entity, ...) -> post-spawn setup
   c. carryManager.RemoveCarried(entity, slot)
   d. return true (block placement prevented)
6. If false or spawn failed -> fall through to normal block placement
```

### Drop

**File:** `src/Common/Services/CarryDropService.cs`

```
1. System drops a CarriedBlock
2. Check carriedBlock.SpawnerFactoryCode != null
3. carryManager.TryGetEntitySpawnerFactory(spawnerFactoryCode, out factory)
4. factory.ShouldSpawnAsEntity(carriedBlock, dropped: true, api)
5. If true:
   a. factory.SpawnFromCarriedBlock(context) with randomYaw from config
   b. factory.OnSpawned(entity, ...)
   c. carryManager.RemoveCarried(entity, slot)
   d. return true
6. If false or spawn failed -> fall through to default entity spawn or item drops
```

---

## 13. Entity Definition JSON

**File:** `resources/assets/carryon/entities/carriedblock.json`

The entity must be defined in the asset system with the `"carryon:carryable"` behavior and a `handlerCode` attribute:

```json
{
    "code": "carriedblock",
    "class": "EntityCarriedBlock",
    "hitboxSize": { "x": 1.0, "y": 1.0 },
    "client": {
        "renderer": "Shape",
        "shape": { "base": "game:block/wood/crate/normal-closed" },
        "behaviors": [
            { "code": "carryon:carryable", "handlerCode": "carriedblock" }
        ]
    },
    "server": {
        "behaviors": [
            { "code": "carryon:carryable", "handlerCode": "carriedblock" }
        ]
    }
}
```

Both client and server sections must include the behavior with `handlerCode`. If `handlerCode` is missing, the behavior logs a warning and carry functionality is disabled.

---

## 14. Creating a Custom Entity Integration

### Pattern A: Behavior-based pickup + registered spawner factory (recommended for new mods)

The cleanest decoupled pattern — pickup lives on the entity as a behavior, spawning is a separate registered factory. No singletons, no CarryOn dependency.

**Pickup behavior on the entity:**

```csharp
public class MyEntityCarryBehavior : EntityBehavior, ICarryableEntityPickupHandler
{
    public override string PropertyName() => "mymod:myentitycarry";

    public float GetInteractDelay(Entity entity, ICoreAPI api) => 1.0f;

    public bool CanPickup(EnumGameMode gameMode, string? playerUid, Entity entity, ICoreAPI api)
        => gameMode == EnumGameMode.Creative;

    public CarriedBlock? SerializeToCarriedBlock(Entity entity, CarrySlot slot, ICoreAPI api)
    {
        var block = new ItemStack(api.World.GetBlock(new AssetLocation("mymod", "myentityblock")));
        return new CarriedBlock(slot, block, null, null, null, null,
            spawnerFactoryCode: "mymod:myentity");
    }

    public void OnPickedUp(Entity entity, IPlayer player, CarriedBlock carriedBlock, ICoreAPI api)
    {
        entity.Die(EnumDespawnReason.PickedUp, null);
    }
}
```

**Separate spawner factory:**

```csharp
public class MySpawnerFactory : ICarryEntitySpawnerFactory
{
    private readonly ICoreAPI api;

    public MySpawnerFactory(ICoreAPI api) => this.api = api;

    public bool ShouldSpawnAsEntity(CarriedBlock cb, bool dropped, ICoreAPI api) => true;

    public Entity? SpawnFromCarriedBlock(EntitySpawnContext context)
    {
        var entityType = context.Api.World.GetEntityType(new AssetLocation("mymod", "myentity"));
        if (entityType == null) return null;
        var entity = context.Api.World.ClassRegistry.CreateEntity(entityType);
        if (entity == null) return null;
        entity.Pos.X = context.Position.X;
        entity.Pos.Y = context.Position.Y;
        entity.Pos.Z = context.Position.Z;
        context.Api.World.SpawnEntity(entity);
        return entity;
    }

    public void OnSpawned(Entity e, CarriedBlock cb, ICoreAPI api)
    {
        var sound = cb.Block?.Sounds?.Place.Location
            ?? new AssetLocation("game:sounds/block/place");
        api.World.PlaySoundAt(sound, e.Pos.X, e.Pos.Y, e.Pos.Z);
    }
}
```

**Mod startup (CarryOnLib only):**

```csharp
public class MyMod : ModSystem
{
    public override void StartServerSide(ICoreServerAPI api)
    {
        var carryManager = api.ModLoader.GetModSystem<CarryOnLibSystem>()?.CarryManager;
        carryManager?.RegisterEntitySpawnerFactory(
            "mymod:myentity", new MySpawnerFactory(api));
    }
}
```

**Entity JSON:**

```json
{
    "code": "mymod:myentity",
    "class": "MyMod.MyEntityClass",
    "client": {
        "behaviors": [
            { "code": "carryon:carryable", "handlerCode": "auto" },
            { "code": "mymod:myentitycarry" }
        ]
    },
    "server": {
        "behaviors": [
            { "code": "carryon:carryable", "handlerCode": "auto" },
            { "code": "mymod:myentitycarry" }
        ]
    }
}
```

`handlerCode: "auto"` tells `EntityBehaviorCarryable` to scan the entity's behaviors for an `ICarryableEntityPickupHandler`. The custom behavior `mymod:myentitycarry` implements that interface. No singleton registration needed for pickup.

### Pattern B: Named singleton handler (backward compat)

For existing mods that already implement a full handler singleton. The handler code references a registered singleton:

```csharp
public class MyEntityHandler : ICarryableEntityPickupHandler
{
    public const string HandlerCode = "myentity";

    public float GetInteractDelay(Entity entity, ICoreAPI api) => 0.5f;

    public bool CanPickup(EnumGameMode gameMode, string? playerUid, Entity entity, ICoreAPI api)
        => true;

    public CarriedBlock? SerializeToCarriedBlock(Entity entity, CarrySlot slot, ICoreAPI api)
    {
        var block = new ItemStack(api.World.GetBlock(new AssetLocation("game:someblock")));
        return new CarriedBlock(slot, block, null, null, null, null, HandlerCode);
    }

    public void OnPickedUp(Entity entity, IPlayer player, CarriedBlock carriedBlock, ICoreAPI api)
    {
        entity.Die(EnumDespawnReason.PickedUp, null);
    }
}

// Registration in mod startup:
var carryManager = api.ModLoader.GetModSystem<CarrySystem>()?.CarryManager;
carryManager?.RegisterEntityPickupHandler("mymod:myentity", new MyEntityHandler());
```

**Entity JSON:**

```json
{
    "code": "mymod:myentity",
    "class": "MyMod.MyEntityClass",
    "client": {
        "behaviors": [
            { "code": "carryon:carryable", "handlerCode": "myentity" }
        ]
    },
    "server": {
        "behaviors": [
            { "code": "carryon:carryable", "handlerCode": "myentity" }
        ]
    }
}
```

A named `handlerCode` triggers registry lookup for a singleton `ICarryableEntityPickupHandler`. The handler stamps its code as the `SpawnerFactoryCode` during `SerializeToCarriedBlock`, and the placement pipeline resolves the spawner factory separately.

### Pattern C: Existing CarriedBlockEntityHandler (built-in)

No changes needed. The built-in `CarriedBlockEntityHandler` handles pickup for `EntityCarriedBlock` entities. It stamps `SpawnerFactoryCode = "carryon:carriedblock"` during pickup. The default `CarriedBlockEntityFactory` handles placement.

---

## 15. Key Files Reference

| File | Purpose |
|------|---------|
| `CarryOnLib/.../ICarryableEntityPickupHandler.cs` | Pickup direction interface (4 methods) |
| `CarryOnLib/.../ICarryEntitySpawnerFactory.cs` | Spawner factory interface (3 methods) |
| `CarryOnLib/.../ICarryManager.cs` | Two registries: pickup handlers + spawner factories |
| `CarryOnLib/.../Models/CarriedBlock.cs` | Data model with `SpawnerFactoryCode` |
| `CarryOnLib/.../Models/EntitySpawnContext.cs` | Context passed to spawner factory |
| `CarryOnLib/.../CarryConstants.cs` | Attribute key constants |
| `CarryOnLib/.../CarriedBlockTreeSerializer.cs` | Serialize/deserialize `SpawnerFactoryCode` |
| `CarryOn/.../EntityBehaviorCarryable.cs` | Entity behavior with `handlerCode` resolution (`"auto"` / named) |
| `CarryOn/.../CarriedBlockEntityHandler.cs` | Default pickup handler for `EntityCarriedBlock` |
| `CarryOn/.../CarriedBlockEntityFactory.cs` | Default spawner factory for dropped block entities |
| `CarryOn/.../EntityHandlerSpawner.cs` | Factory-lookup bridge for placement/drop pipelines |
| `CarryOn/.../CarryServerHandler.cs` | Server-side pickup pipeline |
| `CarryOn/.../CarryPlacementService.cs` | Placement pipeline with entity spawn check |
| `CarryOn/.../CarryDropService.cs` | Drop pipeline with entity spawn check |
| `CarryOn/.../CarrySystem.cs` | Behavior, handler, and factory registration |
| `CarryOn/entities/carriedblock.json` | Entity definition with `handlerCode` attribute |

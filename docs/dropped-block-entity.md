# Dropped Block Entity

When a carried block is dropped — from damage, death or quick drop — and there is no suitable location to place it in the world, CarryOn can spawn a persistent **block entity** instead of dropping loose item entities. The owner (or anyone, depending on config) can later retrieve the block by interacting with the entity.

## How It Works

### Drop Behavior

When a carried block cannot be placed (no valid surface, blocked, or permission denied), the `DropMode` setting determines what happens:

| Mode | Behavior |
| --- | --- |
| `Items` | Always drop as item entities (default vanilla behavior). |
| `EntityOnFailedPlacement` | Try to place the block in the world first. If placement fails, spawn a block entity instead of items. |
| `EntityAlways` | Always spawn a block entity, never try placement. |

The default mode is `EntityOnFailedPlacement`.

### Gravity and Floor Finding

When a block entity is spawned, it does not appear at a fixed position. CarryOn applies gravity — it searches downward from the drop position to find the nearest solid surface. The entity lands on top of the first solid block it finds. If the entity is above liquid, it floats partially submerged at the liquid surface.

### Picking Up Dropped Blocks

To pick up a dropped block entity, hold the carry action modifier (`Shift` by default) and right-click on the entity. A progress circle appears at the cursor. When the progress completes, the block enters your hands carry slot.

Your hands slot must be empty to pick up a dropped block.

### Owner and Access Control

Each dropped block entity records who dropped it. The `PickupAccess` setting controls who can retrieve it:

| Access | Behavior |
| --- | --- |
| `Anyone` | Any player can pick up the dropped block immediately. |
| `OwnerOnly` | Only the player who dropped it can pick it up. No time limit. |
| `OwnerFirst` | Only the owner can pick it up for `GracePeriodSeconds` (default: 300 seconds / 5 minutes). After the grace period expires, anyone can pick it up. |

Players in **Creative mode** can always pick up any dropped block entity regardless of access settings.

### Entity Info

Looking at a dropped block entity shows:

- **Block name** — the type of block that was dropped.
- **Dropped by** — the name of the player who dropped it (or UID if offline and not in creative mode).
- **Dropped ago** — time elapsed since the block was dropped.
- **Owner pickup** — remaining grace period time (only when `PickupAccess` is `OwnerFirst` and the grace period is active).
- **Despawns in** — remaining in-game days before the entity despawns (only when `DespawnAfterDays` is greater than 0).

### Visual Appearance

Dropped block entities render as a small scaled-down block model. The entity displays glowing pickup particles when `ShowParticles` is enabled (default: true). The entity can spawn with a random facing rotation when `RandomDropRotation` is enabled (default: true).

### Despawn

By default, dropped block entities despawn after 30 in-game days. Set `DespawnAfterDays` to `0` or a negative number to prevent despawning.

## Configuration

All settings are under `CarriedBlockEntity` in `CarryOnConfig.json`:

```json
{
  "CarriedBlockEntity": {
    "DropMode": "EntityOnFailedPlacement",
    "RandomDropRotation": true,
    "ShowParticles": true,
    "DespawnAfterDays": 30,
    "PickupAccess": "OwnerFirst",
    "GracePeriodSeconds": 300,
    "Scale": 0.6
  }
}
```

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `DropMode` | `string` | `EntityOnFailedPlacement` | How carried blocks are dropped: `Items`, `EntityOnFailedPlacement`, or `EntityAlways`. |
| `RandomDropRotation` | `bool` | `true` | Spawn dropped block entities with a random facing rotation. |
| `ShowParticles` | `bool` | `true` | Display glowing pickup particles on dropped block entities. |
| `DespawnAfterDays` | `float` | `30` | In-game days before the entity despawns. `0` or negative to never despawn. |
| `PickupAccess` | `string` | `OwnerFirst` | Who can pick up the entity: `Anyone`, `OwnerOnly`, or `OwnerFirst`. |
| `GracePeriodSeconds` | `float` | `300` | Real-time seconds the owner has exclusive pickup access. Only used when `PickupAccess` is `OwnerFirst`. |
| `Scale` | `float` | `0.6` | Uniform scale for the entity visual size and collision hitbox (0.1 to 10.0). |

See [carryon-config.md](carryon-config.md) for full config documentation.

## Drop Triggers

Dropped block entities can be spawned from any of these events:

- **Damage drop** — when the player takes damage above the configured threshold.
- **Death drop** — when the player dies while carrying a block.
- **Quick drop** — when the player uses the quick-drop hotkey (`K` by default).

## Limitations

- Dropped block entities use a generic placeholder model, not the actual block model. The block type is identified through the info text, not visually.
- The entity does not collide with players (no physics behavior), so it cannot be used as a platform or obstacle.
- Entity persistence across server restarts is handled automatically by the Vintage Story chunk system.
- The `GracePeriodSeconds` uses real-time seconds (wall clock), while `DespawnAfterDays` uses in-game game calendar days. These are independent time systems.

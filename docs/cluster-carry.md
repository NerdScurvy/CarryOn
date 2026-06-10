# Cluster Carry

The cluster carry feature lets you pick up wall signs together with the block they are attached to. For example, a chest with a "Tools" sign above it can be picked up, carried, and placed back as a single unit — signs and all.

## Enabling the Feature

Cluster carry is **disabled by default**. To enable it, set `CarryAttachedWallSigns` to `true` in `CarryOnConfig.json`:

```json
{
  "CarryOptions": {
    "CarryAttachedWallSigns": true
  }
}
```

See the [carryon-config document](carryon-config.md) for full config details.

## How It Works

### Pickup

When you pick up a carryable block that has wall signs attached to it, CarryOn scans the block's footprint and captures any wall-mounted signs. Each sign's text, color, font size, position, and facing are preserved alongside the parent block.

Cluster carry is currently limited to **wall-mounted signs** (`sign-wall-*` variants). Signs on the ground, hanging signs, and other sign types are not captured.

Only signs whose backing block is part of the carryable's footprint are captured — signs attached to an adjacent unrelated block are left in place.

### Carrying

While carried, the captured signs are stored as part of the carry data. They survive:

- Slot swaps between Hands and Back
- Game saves and reloads
- Player disconnects and reconnects

If client-side rendering is enabled (default: on), the signs are visually displayed on top of the carried block. See [Client-Side Toggle](#client-side-toggle) below.

### Placement

When you place the carried block, the signs are placed back at their correct relative positions. If the parent block rotates during placement (based on the player's facing direction), the signs' positions and facing directions rotate to match.

Placement can fail with a specific error if:

- **Not enough clearance** — one or more sign positions would be inside a solid block or outside the world.
- **No support** — the block a sign would attach to does not exist at the target position (e.g. placing the cluster against air).

If placement fails, the entire cluster stays in your carry slot — the parent block and signs are not lost.

### Dropping

If you drop a carried block that has attached signs (via the drop shortcut, death, or other means), CarryOn searches for a valid cluster placement position. If found, the entire cluster is placed as a block. If no valid cluster position exists, the parent and all attached signs are dropped as item entities.

### Attaching to Entities

A block with attached signs **cannot** be attached to an entity (e.g. as a mounted container on a boat or cart). This prevents data loss, since entity-attached blocks do not support cluster rendering.

## Client-Side Toggle

### Rendering

Rendering of attached signs on the carried block is controlled by `RenderAttachedBlocks` in `CarryOnClientConfig.json`:

```json
{
  "RenderAttachedBlocks": false
}
```

Default is `true`. When disabled, the signs are still carried and will be placed correctly — they just are not visible on the carried model. This can help performance on lower-end systems.

### Pickup Preference

Whether attached signs are captured when you pick up a block is controlled by `CaptureAttachedWallSigns` in `CarryOnClientConfig.json`:

```json
{
  "CaptureAttachedWallSigns": false
}
```

Default is `true`. When disabled, signs attached to the block are left in place during pickup — only the parent block is carried. This also requires the server-side `CarryAttachedWallSigns` option to be enabled; if the server has it disabled, this client preference has no effect and signs are never captured.

### Chat Commands

Two chat commands provide quick access to these settings at runtime:

| Command | Effect |
| --- | --- |
| `.carryon attachedRender [true\|false]` | Toggle or set rendering of attached signs on the carried block model. |
| `.carryon attachedPickup [true\|false]` | Toggle or set whether attached signs are captured on pickup. |

Without an argument, each command toggles the current value. Pass `true` or `false` to set explicitly. Changes are saved immediately to `CarryOnClientConfig.json` and take effect right away (no reconnect needed).

## Configuration Reference

### `CarryOnConfig.json` (server)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `CarryOptions.CarryAttachedWallSigns` | `bool` | `false` | Enables capturing wall signs when picking up carryable blocks. |

See [carryon-config.md](carryon-config.md) for full config documentation.

### `CarryOnClientConfig.json` (client)

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `RenderAttachedBlocks` | `bool` | `true` | Draws attached wall signs on the carried block model. |
| `CaptureAttachedWallSigns` | `bool` | `true` | Captures wall signs when picking up a block (requires server `CarryAttachedWallSigns`). |

## Limitations

- Only **wall-mounted signs** (`sign-wall-*`) are captured (this does include plaques). Ground signs, hanging signs, and other sign types are not supported.
- Signs are captured only from the horizontal faces of the carryable block's footprint. Signs attached above, below, or diagonally are not captured.
- A block with attached signs cannot be attached to an entity.
- The cluster must fit at the placement location — all sign positions must have clearance and support.

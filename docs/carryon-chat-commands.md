# CarryOn Chat Commands

Carry On exposes a client-side `.carryon` chat command tree for gameplay toggles and HUD debug settings, and two server-side `/carryon-*` commands for server administration.

## Availability

- Client commands are registered on the client.
- They are intended for the local player and local client configuration.
- GUI-related subcommands update both the current runtime HUD state and the saved client config when possible.
- Server commands are registered on the server, require the `controlserver` privilege, and affect server/world state.

## Top-Level Commands

### `.carryon attachedRender [true|false]`

Toggles or sets rendering of attached wall signs on carried blocks (cluster carry visual).

If called without an argument, the current state is toggled.

Examples:
```text
.carryon attachedRender true
.carryon attachedRender false
.carryon attachedRender
```

### `.carryon attachedPickup [true|false]`

Toggles or sets whether attached wall signs are captured when picking up a block (cluster carry pickup).

If called without an argument, the current state is toggled.

Examples:
```text
.carryon attachedPickup true
.carryon attachedPickup false
.carryon attachedPickup
```

## GUI Debug Commands

The `.carryon gui` branch manages the on-screen carry HUD anchors and visual styling.

### `.carryon gui show`

Shows the current carry HUD anchor assignments.

### `.carryon gui reset`

Resets HUD anchors and visuals to their defaults.

### `.carryon gui set <anchor> <hands|back|clear>`

Sets or clears carry slot anchors.

Valid anchors:

- `L1`
- `L2`
- `L3`
- `R1`
- `R2`
- `R3`

Valid slot values:

- `hands` assigns the chosen anchor to the hands carry slot.
- `back` assigns the chosen anchor to the back carry slot.
- `clear` removes whatever slot is assigned at that anchor.

Examples:

```text
.carryon gui set L1 hands
.carryon gui set R2 back
.carryon gui set R1 clear
```

### `.carryon gui bg ...`

Configures the anchor background fill.

Available subcommands:

- `.carryon gui bg enable`
- `.carryon gui bg disable`
- `.carryon gui bg color <hex>`
- `.carryon gui bg alpha <0.0-1.0>`
- `.carryon gui bg show`
- `.carryon gui bg reset`

Examples:

```text
.carryon gui bg color #e4c4a6
.carryon gui bg alpha 0.5
```

### `.carryon gui border ...`

Configures the anchor border outline.

Available subcommands:

- `.carryon gui border enable`
- `.carryon gui border disable`
- `.carryon gui border color <hex>`
- `.carryon gui border alpha <0.0-1.0>`
- `.carryon gui border show`
- `.carryon gui border reset`

Examples:

```text
.carryon gui border color #45372D
.carryon gui border alpha 0.75
```

### `.carryon gui highlight ...`

Configures the icon highlight used for carry HUD cues.

Available subcommands:

- `.carryon gui highlight enable`
- `.carryon gui highlight disable`
- `.carryon gui highlight color <hex>`
- `.carryon gui highlight alpha <0.0-1.0>`
- `.carryon gui highlight show`
- `.carryon gui highlight reset`

Examples:

```text
.carryon gui highlight color #FFFFFF
.carryon gui highlight alpha 0.6
```

### `.carryon iconmode [mode]`

Toggles or sets the icon texture mode used by carried label rendering.

If called without an argument, cycles through modes: `standalone` → `atlas` → `standalone-fallback` → `disabled` → `standalone`.

Valid modes:

- `standalone` — extract icons to standalone textures
- `atlas` — use atlas textures directly
- `standalone-fallback` — standalone with atlas fallback
- `disabled` — skip icon rendering

Examples:

```text
.carryon iconmode atlas
.carryon iconmode disabled
.carryon iconmode
```

## Server Commands

These commands are registered on the server side and are invoked with a leading `/`. Both require the `controlserver` privilege.

### `/carryon-reload`

Reloads `CarryOnConfig.json` from disk without restarting the server. Useful after editing the config file while the world is running.

- **Privilege:** `controlserver`
- **Usage:** `/carryon-reload`

The command reports what the reload actually did, in the invoking player's chat:

- A rejected reload (invalid JSON, an empty or skeleton file) returns a **command error** with the reason, not a success line — the running config is kept either way.
- A successful reload appends each warning after the success message: dropped sections that reset toward defaults, behavior-surface sections that changed while `ReconditionOnConfigChange` is off (they take effect on the **next server start**), and a file value of `ReconditionOnConfigChange` that disagrees with what the server started with.

Watcher-triggered reloads (file edits while the world runs) deliver the same warnings to every online player holding the `controlserver` privilege — or to every online player on a single-player world. Each distinct warning reaches chat once per session; the `Config reloaded and applied` confirmation is sent on every watcher reload.

### `/carryon-recover [radius]`

Recovery command for attachable entities (boats, packs, mounts) whose rendering was corrupted by a broken attachment — for example a collapsed chest or trunk attached to a slot before CarryOn's attach gates existed. Entities affected this way can become invisible until the bad attachment is removed.

- **Privilege:** `controlserver`
- **Usage:** `/carryon-recover [radius]`
  - `radius` *(optional, default `15`)* — scan radius in blocks around the invoking player. Pass `0` to scan **all** loaded entities.

What it does:

1. Scans every entity with an attachable inventory within the radius.
2. Checks each non-empty attachment slot against the same attach gates used when attaching (the vanilla `IsAttachable` check, plus the collapsed-chest `retrieveOnly` check).
3. Clears any slot that fails a gate, so the entity re-renders immediately and after save/load.
4. Restores the removed block near the invoking player:
   - **Carryable** blocks spawn as carried block entities with their contents preserved.
   - **Non-carryable** blocks drop as loose items near the player, along with any stored contents.

The command prints a summary of entities checked, slots cleaned, carried blocks restored, and items dropped.

> **Note:** a radius of `0` iterates every loaded entity, so on a large or busy world it is
> heavier than a radius-limited sweep. It is an admin command run on demand, so this is
> acceptable; prefer a bounded radius unless you specifically need to scan everything.

Example:

```text
/carryon-recover
/carryon-recover 25
/carryon-recover 0
```

## Notes

- Hex color values accept `#RRGGBB`, `RRGGBB`, `#RGB`, or `RGB`.
- Alpha values must be between `0.0` and `1.0`.
- When a command changes GUI state, the mod tries to update the runtime HUD immediately and persist the matching client config value.
- The command handlers are defensive: if the client config is unavailable, the command returns an error instead of silently failing.

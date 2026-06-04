# CarryOn Chat Commands

Carry On exposes a client-side `.carryon` chat command tree for gameplay toggles and HUD debug settings.

## Availability

- These commands are registered on the client.
- They are intended for the local player and local client configuration.
- GUI-related subcommands update both the current runtime HUD state and the saved client config when possible.

## Top-Level Commands

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

## Notes

- Hex color values accept `#RRGGBB`, `RRGGBB`, `#RGB`, or `RGB`.
- Alpha values must be between `0.0` and `1.0`.
- When a command changes GUI state, the mod tries to update the runtime HUD immediately and persist the matching client config value.
- The command handlers are defensive: if the client config is unavailable, the command returns an error instead of silently failing.

# Carry On 2.0

Carry On is a [*Vintage Story*][VS] mod for moving placed blocks without emptying them first. It is especially useful for storage, containers, and workshop blocks that would otherwise need to be unpacked and rebuilt.

Supported blocks can be carried in your hands and, for some block families, swapped onto your back for transport.

Originally developed by copygirl, it was inspired by copygirl's Minecraft mod [*Wearable Backpacks*][WBs] as well as *Charset*, *CarryOn*, and similar mods.

The mod is now maintained by Nerdscurvy.

![Screenshot](docs/screenshot.jpg)

## Overview

Carry On is built around a simple interaction loop:

- Pick up supported placed blocks while both hands are empty.
- Carry them in your hands to reposition them.
- Swap eligible blocks onto your back when you are not targeting a block.
- Place them back into the world with their carried data intact.
- Transfer carryable blocks in and out of supported block entities without fully breaking/replacing the parent block.
- Attach and detach carryable blocks on supported entities (for example seats or attachment slots).
- Quick-drop what you are carrying when you need immediate control in combat or hazards.

This makes it much easier to relocate containers, tidy a base, move workshop stations, or transport loot while exploring.

## Download And Installation

Downloads are available through [ModDB][DL].

Carry On 2.0 requires [CarryOnLib][COL], which must also be installed.

To install manually:

1. Download Carry On from [ModDB][DL].
2. Download [CarryOnLib][COL].
3. Open the Vintage Story mod folder from the in-game *Mod Manager*.
4. Copy both `.zip` files into the mod folder **without extracting them**.

Alternatively, use the [Vintage Story Launcher][VSL] to manage game installations and mods.

## Gameplay

The default interaction is to sneak and hold right click while your selected hotbar slot and offhand are both empty. When the action starts, a progress circle appears at the cursor.

- Pick up a supported block.
- Place it back down against a block while carrying it in your hands.
- Move it onto your back by aiming at open space instead of a block or by holding the swap-back key combination.
- Pull it back off your back into your hands the same way.
- If a carryable block supports transfer, use the same carry interaction to take it out of, or put it into, compatible block entities.
- If an entity has attachable carry slots, use the carry interaction to attach what you are holding or detach a carryable from that entity.

Carrying something in your hands still blocks normal item use. Sprinting while carrying is disabled by default.

Some world interactions are still allowed while carrying in your hands through `CarryableInteract`. This includes common targets such as doors, trapdoors, fence gates, barrels, chests, reed chests, and storage vessels.

## Default Hotkeys

Carry On uses two interaction modifiers and four function hotkeys by default. All of these can be changed in the in-game controls menu.

### Carry Interaction Modifiers

These are used together with **right click** for carry actions.

| Action | Default |
| --- | --- |
| Carry action modifier (pick up / place / carry interactions) | `Shift` (Left Shift) |
| Swap-back modifier (used with carry action modifier) | `Ctrl` (Left Ctrl) |

Note that the game may show warnings of conflicting hotkeys since these are also the default sneak and sprint hotkeys. These conflicts can be safely ignored.

### Function Hotkeys

These are direct hotkeys and do not require right click.

| Action | Default |
| --- | --- |
| Toggle Carry On enabled/disabled | `Alt` + `K` |
| Quick drop from hands | `K` |
| Quick drop from hands + back | `Alt` + `Ctrl` + `K` |
| Toggle double-tap dismount behavior | `Ctrl` + `K` |

## Default Carry Behavior

Movement speed and interaction time are configured per block behavior, so exact values can vary by block and by world config. The default Carry On timing and movement values are:

| Setting | Default |
| --- | :---: |
| Hands walk speed modifier | `-0.25` (75% move speed) |
| Back walk speed modifier | `-0.15` (85% move speed) |
| Pickup interaction time | `0.8 s` |
| Place interaction time | `0.6 s` (`pickup * 0.75`) |
| Swap hands/back time | `1.2 s` (`pickup * 1.5`) |

Many block families override these defaults, including anvils, chest-trunks, crates, and resin logs. For current vanilla block values, see [Vanilla Block Inventory](docs/vanilla-block-inventory.md).

## Configuration

Server and singleplayer world behavior is configured through `CarryOnConfig.json` in the `ModConfig` folder.

That config controls:

- Which vanilla block families can be carried.
- Which ones can be placed on the back.
- Interaction behavior while carrying.
- Carry speed and interaction modifiers.
- Automatic matching rules for modded blocks.
- Debug and development options.

For the full current config schema and defaults, see [CarryOn Config Reference](docs/carryon-config.md).

## Mod Compatibility

Carry On support for non-vanilla content is handled through three paths:

- **Automatic behavior assignment** for modded blocks that match known carryable patterns.
- **CarryOnMore compatibility patches** for supported mods with custom behavior, transforms, or interaction needs.
- **Mod-author patches** that add or override `Carryable` behavior directly in the mod's own assets.

For a full list of supported vanilla blocks, see [Vanilla Block Inventory](docs/vanilla-block-inventory.md).

For details on how auto-assignment works for modded blocks, see [Automatic Behavior Assignment](docs/automatic-behavior-assignment.md).

For a full compatibility list of supported mods, see [Mod Support Inventory](docs/mod-support-inventory.md).

## Documentation

Additional project documentation:

- [Vanilla Block Inventory](docs/vanilla-block-inventory.md)
- [CarryOn Config Reference](docs/carryon-config.md)
- [CarryOn Chat Commands](docs/carryon-chat-commands.md)
- [Dropped Block Entity](docs/dropped-block-entity.md)
- [Automatic Behavior Assignment](docs/automatic-behavior-assignment.md)
- [Mod Support Inventory](docs/mod-support-inventory.md)
- [Modding Guide](docs/modding-guide.md)

## Modding

Carry On adds `Carryable` behavior to vanilla blocks through built-in patches, and can also auto-map that behavior to similar modded blocks.

If you are developing a mod and want explicit support, use a JSON patch to add your own `Carryable` behavior. Current field names, valid examples, patch priority rules, and transform template guidance are documented in [Modding Guide](docs/modding-guide.md).

If your block family is not being auto-detected as expected, check [Automatic Behavior Assignment](docs/automatic-behavior-assignment.md) for matching rules and limitations.

[VS]: https://www.vintagestory.at/
[WBs]: https://github.com/copygirl/WearableBackpacks
[DL]: https://mods.vintagestory.at/carryon
[COL]: https://mods.vintagestory.at/carryonlib
[VSL]: https://mods.vintagestory.at/vslauncher

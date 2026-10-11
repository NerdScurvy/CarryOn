# Technical Overview

Internals documentation: how CarryOn works, and the reasoning behind it.

**Start with [Design Decisions](design-decisions.md)** if you want to know *why* something behaves
the way it does — each entry states the decision, the constraint that forced it, and what is
deliberately not handled. The documents below are the *mechanism* those decisions produce.

For player-facing documentation (config, commands, supported blocks, modding), see the
[Documentation list in the repository README](../../README.md#documentation). For problems and
limitations, see [Known Issues](../known-issues.md).

---

## Start here

| Document | Read it for |
|---|---|
| [Design Decisions](design-decisions.md) | The decisions behind the behavior, and the constraint behind each |
| [Config Initialization and Flow](config-initialization-and-flow.md) | Load order, config subscribers, the reload gate, and the two network wires |
| [Behavioral Conditioning](behavioral-conditioning.md) | The nine-pass pipeline that decides which blocks are carryable, and why it is reversible |
| [Entity Handler System](entity-handler-system.md) | The handler chain every carried entity runs through |

## Carryability

| Document | Read it for |
|---|---|
| [Behavioral Conditioning](behavioral-conditioning.md) | Pass order, the per-world baseline, multiple-carryable resolution, auto-mapping |
| [CarryManager and Handler Pipeline](carry-manager-and-handlers.md) | Pickup and place-down paths, claim gates, footprint handling, reconciliation |
| [Carryability State Sync](carryability-sync.md) | How the server's decided carryability surface reaches the client's block registry |

## Rendering and transforms

| Document | Read it for |
|---|---|
| [Entity Carry Renderer Pipeline](entity-carry-renderer-pipeline.md) | Transform templates, placeholders, and the render cache |
| [Transform Template System](transform-template-system.md) | How a transform template is built and resolved |
| [Transform Group Value Propagation](transform-group-value-propagation.md) | How a value set on a group reaches its children |
| [Hint Selection Policy](hint-selection-policy.md) | Which interaction hint wins when several apply |

## Block-type-specific rendering

| Document | Read it for |
|---|---|
| [Carried Chest-Trunk and Chest Rendering](carried-chest-trunk-rendering.md) | Chest, trunk, and multiblock rendering specifics |
| [Carried Plant Container Rendering](carried-plant-container-rendering.md) | Planter and container rendering specifics |

---

## Related

- [Modding Guide](../modding-guide.md) — adding or overriding `Carryable` behavior in your own mod
- [Automatic Behavior Assignment](../automatic-behavior-assignment.md) — how `CarryablesFilters`
  shapes which modded blocks become carryable
- [CarryOn Config Reference](../carryon-config.md) — every field, default, and upgrade step
- [Known Issues](../known-issues.md) — known limitations, for players and mod authors

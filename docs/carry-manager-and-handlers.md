# CarryManager and Handler Pipeline in CarryOn

This document explains the carry management and interaction pipeline centered on `CarryManager` and `CarryHandler`.

It reflects the current implementation:
- `CarryManager` is the core authority for carried state, pickup/place/drop operations, permission checks, and carry event hooks.
- `CarryHandler` is the orchestration layer that wires input, network messages, server validation, and interaction state progression.
- `InteractionLogic` drives client-side carry action state (`PickUp`, `PlaceDown`, `SwapBack`, `Attach`, `Detach`, `Put`, `Take`, `Interact`).
- `TransferLogic` handles carryable transfer into and out of block entities via pluggable transfer handlers.
- `DeathHandler` enforces carried-item drop behavior on player death based on vanilla keep-contents rules.

## 0. Scope

This pipeline covers:
- `src/Common/CarryManager.cs`
- `src/Common/Handlers/CarryHandler.cs`
- `src/Common/Handlers/DeathHandler.cs`
- `src/Common/Logic/InteractionLogic.cs`
- `src/Common/Logic/TransferLogic.cs`
- `src/Common/Models/CarryInteraction.cs`
- `src/CarrySystem.cs`
- `../CarryOnLib/src/API/Common/Interfaces/ICarryManager.cs`

---

## 1. High-Level Responsibilities

### 1A. `CarryManager`

Core responsibilities:
- Stores and resolves carried blocks in entity watched attributes per carry slot.
- Sets/removes carried state, including side effects:
  - carry animation start/stop
  - movement speed modifier
  - hand hotbar slot locking synchronization
- Performs world-to-carried and carried-to-world transitions (`TryPickUp`, `TryPlaceDown`, `TryPlaceDownAt`).
- Handles drop fallback (`DropCarried`, `DropBlockAsItem`) when placement is not possible.
- Validates build/reinforcement/claim permissions before pickup on server side.
- Publishes and consumes carry event delegate hooks around pickup/remove/restore/drop and permission checks.
- Maintains transform group resolver registry used by carried render planning.

### 1B. `CarryHandler`

Orchestration responsibilities:
- Initializes client and server carry pipelines from `CarrySystem`.
- Registers carry network message types and per-message handlers.
- Wires client input events and game tick loop into `InteractionLogic`.
- Wires server message handlers into `CarryManager` and `TransferLogic` with authority checks.
- Prevents active hotbar slot switching while carrying in hands.
- Reapplies carry state effects on entity/player spawn so animations/stats/locks are restored.

### 1C. `InteractionLogic`

Client interaction state machine responsibilities:
- Tracks active interaction context in `CarryInteraction`.
- Detects and begins valid interaction intents based on right-click + carry key context.
- Progresses timed interactions each tick and dispatches corresponding client packets.
- Performs client-side prechecks and local transfer calls (`Put`/`Take`) before server packet send.
- Cancels/completes interaction state and updates carry HUD progress/refresh behavior.

### 1D. `TransferLogic`

Transfer subsystem responsibilities:
- Configures transfer handlers for carryable blocks by scanning mod assemblies for `ICarryableTransfer` implementors.
- Validates transfer feasibility (`CanPutCarryable`, `CanTakeCarryable`).
- Executes transfer operations (`TryPutCarryable`, `TryTakeCarryable`) and applies carry state changes through `CarryManager`.

### 1E. `DeathHandler`

Death flow responsibility:
- On player death, drops all carried blocks unless vanilla keep-contents behavior is active (`keepContents == true`).

---

## 2. System Lifecycle Integration

`CarrySystem.Start`:
- Registers carry behaviors (`BlockBehaviorCarryable`, `BlockBehaviorCarryableInteract`, `EntityBehaviorAttachableCarryable`).
- Creates `CarryHandler` and `CarryEvents`.
- Retrieves CarryOnLib mod system and installs `CarryManager` implementation.

`CarrySystem.StartClientSide`:
- Registers client channel.
- Creates renderer/HUD systems.
- Calls `CarryHandler.InitClient`.
- Calls `CarryManager.InitEvents`.
- Registers transform group resolvers (`plant-container`, `display-case`, `mold-rack`).

`CarrySystem.StartServerSide`:
- Registers `EntityBehaviorDropCarriedOnDamage`.
- Registers server channel.
- Creates `DeathHandler`.
- Calls `CarryHandler.InitServer`.
- Calls `CarryManager.InitEvents`.

---

## 3. Carried State Storage Model

`CarryManager.GetCarried` / `SetCarried` / `RemoveCarried` use watched attributes under:
- `AttributeKey.Watched.EntityCarried`
- slot key (`Hands`, `Back`, `Shoulder`, etc.)
- child keys:
  - `Stack` for block item stack
  - `Data` for serialized block entity data

Important implementation behavior:
- `GetCarried` resolves `ItemStack.Block` if missing after tree deserialization.
- Server-side set/remove marks watched tree paths dirty so client state stays synchronized.
- Server-side set/remove updates hand lock state and sends `LockSlotsMessage`.

---

## 4. Pickup and Place-Down Pipeline

`TryPickUp` flow (`CarryManager`):
1. Server: permission check (`HasPermissionToCarry`) against claims/reinforcement and external delegates.
2. Reject if target carry slot already occupied.
3. Build `CarriedBlock` from world position (`GetCarriedFromWorld`) and invoke pre-remove delegates.
4. Remove world block (+ clear reinforcement + neighbor update).
5. Set carried state on entity and play placement sound profile.
6. Audit log on server.

`TryPlaceDownAt` + `TryPlaceDown` flow (`CarryManager`):
1. Resolve actual placement position (replace selected block or offset by face).
2. For player placements, route through block `TryPlaceBlock` with temporary active-slot phantom stack.
3. Force Shift=true and Ctrl=false during placement attempt (compatibility workaround), then restore controls.
4. For dropped placements, directly exchange block/spawn block entity and call `OnBlockPlaced`.
5. Restore serialized block entity data at final position.
6. Mark block dirty, trigger neighbor update, remove carried state, play sound, raise drop event when applicable.

---

## 5. Drop Pipeline and Failure Fallback

`DropCarried` flow:
- For each requested carried slot, finds candidate placement near entity using `BlockPlacer`.
- If a placement is found, attempts dropped placement (`TryPlaceDown(..., dropped: true)`).
- If no placement or placement fails, falls back to `DropBlockAsItem`.

`DropBlockAsItem` behavior:
- If carried block contains serialized inventory, spills contents as item entities and drops block stack.
- Otherwise uses block drop table; if drops differ from carried stack, block is treated as destroyed.
- Plays break sound, removes carried state, logs audit, and triggers block-dropped carry events.

---

## 6. Permission, Rules, and Event Hooks

Permission checks (`HasPermissionToCarry`):
- Invokes `CheckPermissionToCarry` delegates first (can explicitly allow/deny).
- Applies reinforcement + claim checks for player entities.
- Non-player entities are restricted only by reinforcement state.

Carry event delegate integration points (`CarryManager`):
- Before pickup: `BeforePickUpBlock`
- Before world remove: `BeforeRemoveBlockFromWorld`
- Before block entity restore: `BeforeRestoreBlockEntityData`
- Permission override: `CheckPermissionToCarry`
- Drop notification: `TriggerBlockDropped`

`InitEvents` discovers and initializes `ICarryEvent` implementations from non-vanilla mod assemblies.

---

## 7. CarryHandler Client Pipeline

`InitClient` registers:
- message types and lock-slot message handler
- carry hotkeys
- in-world action hook (`OnEntityAction`)
- game tick listener (`OnGameTick`)
- active-slot-change prevention hook
- player spawn and player-ready hooks

Runtime behavior:
- `OnEntityAction` starts/cancels interactions through `InteractionLogic`.
- `OnGameTick` advances interaction timer/progress and refreshes interaction help when carry capability state changes.
- `OnBeforeActiveSlotChanged` prevents hotbar active slot changes while hands slot is carrying.

---

## 8. CarryHandler Server Pipeline

`InitServer` registers:
- all carry message types
- authoritative handlers for interaction, pickup, placement, swapping, attach/detach, transfer put/take, and dismount
- spawn/player-now-playing hooks to reapply carry state side effects
- active-slot-change prevention hook
- transfer behavior initialization when CarryOn is enabled

Validation examples in server handlers:
- pickup/place: slot constraints, can-interact checks, failure-code based user feedback
- attach/detach: entity existence, range checks, slot validity, occupancy, compatibility, and inventory-open guards
- put/take: delegates to `TransferLogic` and propagates on-screen errors

---

## 9. Interaction State Machine

`CarryInteraction` stores transient action context:
- `CarryAction`
- elapsed hold time
- source/target carry slot
- target block position
- target entity and slot index
- optional transfer delay override

`InteractionLogic.TryBeginInteraction` picks action in priority order, including:
- entity attach/detach
- swap-back
- block interact behavior
- transfer put/take
- block pickup/place-down

`InteractionLogic.TryContinueInteraction`:
- validates that preconditions still hold while button is held
- computes required hold duration from behavior settings + config multipliers
- drives HUD progress
- executes action locally and sends corresponding network packet when successful
- emits localized failure messaging for rejected operations

---

## 10. TransferLogic Integration

Transfer behavior model:
- A carryable block can expose `TransferEnabled` and a `TransferHandler`.
- Handler methods gate and execute transfer (`CanPut/CanTake`, `TryPut/TryTake`).

`TryPutCarryable`:
- requires carried block in hands
- validates target block entity + carryable transfer handler
- invokes transfer handler
- on success removes carried hands block via `CarryManager.RemoveCarried`

`TryTakeCarryable`:
- requires empty hands carry slot
- validates target block entity + carryable transfer handler
- invokes transfer handler to materialize `ItemStack` + block entity data
- on success installs carried hands block via `CarryManager.SetCarried`

---

## 11. Death Handling

`DeathHandler` subscribes to server `PlayerDeath`:
- If player server attributes do not indicate `keepContents == true`, it drops all carried blocks from the dead player entity.
- This aligns carry drop behavior with vanilla keep-inventory semantics.

---

## 12. Summary Flowchart

```mermaid
graph TD
  A[CarrySystem startup] --> B[Create CarryManager and CarryHandler]
  B --> C{Client or Server init}
  C -- Client --> D[InitClient: input hooks, tick loop, client message handlers]
  C -- Server --> E[InitServer: message handlers, spawn hooks, transfer init]

  D --> F[Player input right click and carry key]
  F --> G[InteractionLogic begins action]
  G --> H[Hold-to-complete timing and HUD progress]
  H --> I{Action type}

  I -- PickUp/PlaceDown --> J[Local try via CarryManager]
  J --> K[Send PickUp/PlaceDown message]
  K --> L[Server validates and re-runs authoritative operation]

  I -- Put/Take --> M[Local transfer precheck/attempt]
  M --> N[Send Put/Take message]
  N --> O[Server TransferLogic authoritative execution]

  I -- Attach/Detach --> P[Send Attach/Detach message]
  P --> Q[Server validates target and applies inventory/entity changes]

  L --> R[Set/Remove carried state, sync watched attributes, lock slots]
  O --> R
  Q --> R

  R --> S[On death or forced drop path]
  S --> T[DropCarried: place nearby or spill as items]
```

---

## 13. References

- `src/Common/CarryManager.cs`
- `src/Common/Handlers/CarryHandler.cs`
- `src/Common/Handlers/DeathHandler.cs`
- `src/Common/Logic/InteractionLogic.cs`
- `src/Common/Logic/TransferLogic.cs`
- `src/Common/Models/CarryInteraction.cs`
- `src/CarrySystem.cs`
- `../CarryOnLib/src/API/Common/Interfaces/ICarryManager.cs`

---

## See Also

- [Transform Template System](transform-template-system.md) — How carryable transform groups are resolved before rendering.
- [Entity Carry Renderer Pipeline](entity-carry-renderer-pipeline.md) — Client rendering pipeline that consumes carried state and resolved transform groups.
- [Carried Plant Container Rendering](carried-plant-container-rendering.md) — Example of resolver-driven carried rendering behavior.
- [Carried Chest-Trunk and Chest Rendering](carried-chest-trunk-rendering.md) — Example of template + group mapping behavior for storage blocks.

---

This document is intended as a technical reference for understanding and debugging carry state, interaction orchestration, and transfer/death handling in CarryOn.

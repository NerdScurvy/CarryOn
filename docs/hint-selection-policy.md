# Hint Selection Policy in CarryOn

This document explains how CarryOn and transfer behaviors can dynamically control which interaction hints are shown.

It reflects the current implementation:
- CarryOn computes default pickup hints in `BlockBehaviorCarryable`.
- Transfer behaviors can optionally implement `ICarryableHintPolicy` to filter those defaults.
- The same policy model is used for transfer hints (`Put`/`Take`) in transfer behaviors.
- Policies only remove hints from the default set; they cannot add hints that are not currently possible.

## 0. Scope

- `../CarryOnLib/src/API/Common/Interfaces/ICarryableHintPolicy.cs`
- `../CarryOnLib/src/API/Common/Models/CarryHintType.cs`
- `../CarryOnLib/src/API/Common/Models/CarryHintContext.cs`
- `src/Common/Behaviors/BlockBehaviorCarryable.cs`
- `../RackEmUp/src/BlockBehaviorMoldRackTransfer.cs`

---

## 1. Feature Goal

Hint selection lets each carryable target decide which hints are helpful for the current context.

Typical use cases:
- Show only base pickup when a target is directly pickable.
- Show only force pickup when replacement/swap behavior is intended.
- Hide pickup hints while hands are occupied.
- Show transfer put/take hints only when the selected slot supports the action.

---

## 2. Core Contract

### 2A. Hint Flags

`CarryHintType` is a flags enum:

```csharp
[Flags]
public enum CarryHintType
{
    None = 0,
    BasePickup = 1,
    ForcePickup = 2,
    TransferPut = 4,
    TransferTake = 8,
}
```

This allows policies to mask any combination of hints with bitwise operations.

### 2B. Context Payload

`CarryHintContext` carries decision inputs such as:
- `Player`
- `Selection` and `SelectionBoxIndex`
- `BlockEntity`
- `IsTargetCarryable`
- `IsForcePickupEnabled`
- `CanDoCarryAction`
- `IsCarryingInHands`
- optional transfer-specific values (`IsTargetSlotEmpty`, `CanTransferPut`, `CanTransferTake`)

### 2C. Policy Interface

```csharp
public interface ICarryableHintPolicy
{
    CarryHintType GetAllowedHints(CarryHintContext context, CarryHintType defaultHints);
}
```

Behavior notes:
- The interface is optional.
- Existing transfer handlers that do not implement it remain fully compatible.
- Returning `defaultHints` means "no filtering".

---

## 3. How Core CarryOn Uses It

In `BlockBehaviorCarryable.GetPlacedBlockInteractionHelp`:

1. CarryOn computes `defaultHints` for pickup interactions.
2. It builds `CarryHintContext`.
3. If the transfer handler behavior implements `ICarryableHintPolicy`, CarryOn calls it.
4. CarryOn applies a safety mask:

```csharp
allowedHints = policy.GetAllowedHints(context, defaultHints) & defaultHints;
```

This ensures policy code can never introduce a hint that is impossible in that state.

---

## 4. Usage Pattern for Mods

Implement `ICarryableHintPolicy` on your transfer behavior and filter hints by criteria.

Example skeleton:

```csharp
public CarryHintType GetAllowedHints(CarryHintContext context, CarryHintType defaultHints)
{
    var allowed = defaultHints;

    // Remove hints that are not valid in current state.
    // Example: allowed &= ~CarryHintType.BasePickup;

    return allowed;
}
```

Recommended filtering style:
- Start with `defaultHints`.
- Remove disallowed hints via bit masks.
- Keep logic side-effect free (policy controls UI hints only).

---

## 5. RackEmUp Criteria Example

The Mold Rack transfer behavior currently applies criteria-based pickup filtering:

- If carrying in hands: hide both pickup hints.
- If selected slot has an item: show force pickup and hide base pickup.
- If selected slot is empty: show base pickup and hide force pickup.
- If target is not carryable: hide both pickup hints.

Representative logic:

```csharp
if (isCarryingInHands)
{
    allowedHints &= ~(CarryHintType.BasePickup | CarryHintType.ForcePickup);
}
else if (!slot.Empty)
{
    // Occupied slot: prefer swap-back pickup.
    allowedHints &= ~CarryHintType.BasePickup;
}
else
{
    // Empty slot: normal pickup.
    allowedHints &= ~CarryHintType.ForcePickup;
}
```

This directly supports the UX goal:
- Hover occupied slot -> force pickup hint only.
- Hover empty slot -> base pickup hint only.

---

## 6. Non-Breaking Behavior Summary

The hint policy feature is additive:
- No changes to `ICarryableTransfer` are required.
- Existing mods continue to work without implementing any new interface.
- Hint filtering is opt-in and per-behavior.
- Core carry/transfer mechanics are unaffected; only hint visibility is controlled.

---

## 7. Testing Checklist

When adding a new policy, verify:

1. Base pickup appears only in intended states.
2. Force pickup appears only in intended states.
3. Put/take hints match slot and carried-state rules.
4. No hints appear for impossible actions.
5. Behavior remains correct when not carrying, carrying, and after slot content changes.

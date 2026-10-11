# Known Issues

Known bugs, limitations, and common problems with CarryOn, along with workarounds where one
exists.

If something here has no workaround and matters to you, please
[open an issue](https://github.com/Nerdscurvy/CarryOn/issues).

---

## For players

### 1. Rebinding carry keys can prevent putting blocks down

**Affects:** All versions
**Status:** Open
**Symptoms:** After rebinding the carry keys, picking up a block works but putting it down fails.

**Workaround:**
- Use the Quickdrop hotkey `[CTRL]+[ALT]+K` to force-drop
- Toggle carry interactions with `[ALT]+K`
- In *Settings → Controls → Key Controls*, uncheck and re-check
  *"Mouse click modifiers locked to Sneak/Sprint keys"*

---

### 2. Carried block contents lost after a version upgrade

**Affects:** Version upgrades between incompatible CarryOn / Vintage Story versions
**Status:** Open, high priority
**Symptoms:** A chest or container carried on your back appears empty after loading a save in a
different game version.

**Workaround (prevention):**
- Before updating the game or the mod, place every carried block back down
- Back up your save folder before any version change
- If contents are already lost, check for world backups (`.bak` / `.vwdb` files) in your save
  directory

---

### 3. Multi-block carryables are not fully supported

**Affects:** All versions
**Status:** Known limitation
**Symptoms:** Multi-block structures (for example trunks assembled from parts) can delete other
blocks when dropped.
**Note:** Trunks specifically are handled correctly and are covered by automated tests. Carryable
multi-blocks in general are not.

---

### 4. Picking up a reinforced block showed a generic permission error (fixed)

**Affects:** Versions with reinforcement-gated block interaction
**Status:** Fixed. Reinforced pickups are denied with their own message naming reinforcement,
rather than the generic *"no permission"* wording.

**What changed:** the permission gate now reports *why* it refused, instead of collapsing every
refusal into one code. A reinforced cell answers `reinforced`, which the handler turns into a
message that says the area is reinforced. Claim refusals answer `already-reported`, because the
game has already told the player why, so CarryOn no longer adds a second message naming the same
refusal — you get one toast instead of two.

**Impact:** the pickup is still denied either way. This is about which message you see, and for
claims about how many you see.

---

### 5. Block interactions are not range-checked against your position (fixed)

**Affects:** All versions
**Status:** Fixed. Block and entity pickup, place-down, carryable-interact, attach, and ground-storage
lift now all verify the target is within reach, using the same server-side measurement.

**What is checked now:** Distance is measured on the server from the actor's **eye** position to the
**centre of the target cell** (or to the entity's own position for entity targets), and compared
against `CarryOptions.MaxInteractionDistance`. Place-down checks the target cell plus the two cells
it can rebuild into - the faced neighbour and the cell above - so a build that starts in reach cannot
step out of it. The claim and footprint gates still apply in full, so you cannot pick up or rebuild
blocks you have no claim to, and multiblock footprints are checked as a unit.

**Creative is exempt.** A creative player is not distance-checked on these paths, matching how the
claim and reinforcement gates already treat creative. See
[carryon-config.md](carryon-config.md#carryoptions) for the setting.

**What this is for:** it is an anti-abuse measure against a modified client, not a precision
mechanic. It stops a client naming a target far away in a carry message; it is not a substitute for
server-side permission plugins, and it does not try to model what the crosshair is pointing at.

**The residual exposure is unchanged and is a different issue.** The claim gate is identity-keyed, so
a *non-player* actor - a dropped item, a projectile, a mod-owned entity - is not claim-checked, and
reach validation does not change that. A tamed creature that detaches into a carried item and
reattaches elsewhere can land inside a claim you do not own and rebuild the block there.
Player-initiated actions are not affected in this way. **If you play on a shared or claim-protected
server:** treat this as a reason to keep valuable blocks reinforced rather than merely claimed, and
to avoid carrying tamed creatures across claim boundaries.

---

### 6. `CoreCLR version ... is not supported` crash on world load

**Affects:** Various versions
**Status:** Known — runtime/environment issue, not a CarryOn bug
**Symptoms:** The game refuses to load the world with
`System.PlatformNotSupportedException: CoreCLR version ... is not supported`. Affects CarryOn and
other Harmony-using mods.

**Cause:** Vintage Story's bundled .NET runtime does not support the installed CoreCLR version in
some configurations.

**Workaround:** See
[Vintage Story Mod Troubleshooting](https://wiki.vintagestory.at/Troubleshooting_Mods) and make
sure your .NET installation matches the version Vintage Story expects.

---

### 7. `MissingMethodException` or "don't know how to instantiate" on startup

**Affects:** Version mismatch between CarryOn and Vintage Story
**Symptoms:** Errors such as:
- `Method not found: 'Vintagestory.GameContent.BlockEntityOpenableContainer.get_CloseSound()'`
- `Don't know how to instantiate collectible behavior of class 'Carryable'`

**Cause:** This CarryOn version was compiled against a different Vintage Story API than the
installed game version.

**Workaround:** Use a CarryOn version built for your Vintage Story version. Update CarryOn — or
downgrade the game — so the two match.

---

## For mod authors

### A. An overlay without `patchPriority` merges silently at the wrong precedence

**Affects:** Blocks patched with `overrideExistingProperties: true`
**Status:** Known authoring trap — warned about at load
**Symptoms:** Your overlay merges onto the base behavior, but the documented ascending-priority
merge ordering never engages, because an omitted `patchPriority` resolves to an effective priority
of `0`.

**Two traps in one:** `patchPriority` is the *only* recognized spelling. `"priority"` is not an
alias and is not read at all, so a key spelled that way is both unparsed and unwarned.

**What CarryOn does:** The resolver emits an authoring warning once per offending behavior per
conditioning run, before resolving. The boundary is **missing vs declared** — an explicit
`patchPriority: 0` is a declared value and stays silent, and only pristine overlays are judged
(never a merge mount or an automap clone, which inherit keys from merged properties).

**Not warned:** a *declared* `patchPriority` that is simply lower than the primary's is still
dropped from the merge by the precedence gate, silently. If your overlay seems to do nothing,
check its declared priority against the base first.

---

## Resolved

- **Wattle and bamboo fence gates not interactable while carrying** — fixed by adding
  `carryon:carryableinteract` patch entries for `game:blocktypes/wood/wattlegate` and
  `game:blocktypes/wood/fence-bamboogate`.
- **Client crash carrying chiseled blocks** — fixed in 2.0.0-pre.6
- **Container contents desync** (crates and barrels mysteriously gaining or losing contents) —
  fixed in 1.10.7
- **Trunks and collapsed chests attached to boats losing storage slots** — fixed in 1.14.3
- **Planter and pot transform ordering** — fixed in 2.0.0-pre.8

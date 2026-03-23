# Player Spy Overlay (F5) — Design Spec

## Overview

A combat-only F5 overlay that shows another player's hand, draw pile, and discard pile in real-time during multiplayer games. Cycles through non-local players with F6.

## Behavior

- **F5** toggles the "Player Spy" panel on/off (combat only)
- **F6** cycles to the next non-local player (wraps around). No-op when panel is hidden.
- Panel shows one non-local player at a time
- **Never shows the local player**
- **Singleplayer**: F5/F6 are no-ops (no other players to show)
- Three sections: Hand, Draw Pile, Discard Pile
- Cards displayed as grouped, alphabetically sorted names with upgrades (e.g., "Defend+1 x3, Strike x2, Bash")
- Empty piles show "(empty)"
- Updates every 0.5s via Godot timer
- Auto-hides on combat end (CombatReset / CombatRoomExit)
- Does not auto-show on combat start — user opts in with F5
- **Combat detection**: use a static `_inCombat` flag set by `CombatPatch` (true) and `CombatResetPatch`/`CombatRoomExitPatch` (false). F5/F6 check this flag.

## UI Layout

- **PanelContainer**, top-right of screen
- Dark semi-transparent background: rgba(0.08, 0.08, 0.12, 0.88)
- ~300px wide, max height with `ScrollContainer` if content overflows
- Header: character name + player indicator, e.g., "Ironclad (2/3)" (orange, 18pt)
- Section labels: "Hand", "Draw Pile", "Discard Pile" (grey, 14pt)
- Card lists: white, 12pt, grouped and sorted
- `MouseFilter.Ignore` — no input blocking
- ZIndex 100

## Components

### `PlayerSpyPanel` (new static class)

Same pattern as `CombatOverlay`. Static class, no instance.

- `Show(Player player, int playerIndex, int nonLocalCount)` — builds panel, adds to SceneTree, starts 0.5s `Timer` node (added as child of panel, auto-cleaned on `QueueFree`). Extra params enable the "Ironclad (1/2)" header.
- `Hide()` — removes panel from SceneTree (idempotent, safe to call when already hidden)
- `Refresh()` — called by timer, reads player's hand/draw/discard via reflection, updates existing `Label.Text` values (full node rebuild only on `Show`)
- `IsVisible` — property to check current state

Tracks the currently viewed player reference internally.

### `InputPatch` (modify existing)

- Add F5 handler: toggle `PlayerSpyPanel` visibility. Only responds during combat.
- Add F6 handler: cycle `_currentSpyIndex` through non-local players in `RunState.Players`, call `PlayerSpyPanel.Show(nextPlayer)`.
- Uses existing `GetLocalPlayer()` to filter out local player.

### `CombatResetPatch` / `CombatRoomExitPatch` (modify existing)

- Add `PlayerSpyPanel.Hide()` alongside existing `CombatOverlay.Hide()` calls.

### No changes to:

- `DataLoader`, `OverlayData`, `OverlayFactory`, `MapIntelPanel`
- No new data files or networking

## Data Access

Card pile state during combat needs to be read from the game's combat state via reflection. `Player.Deck.Cards` is the full deck, but the combat-time split into hand/draw/discard is likely on the combat state or a per-player combat entity.

**Prerequisite exploration spike** (must complete before writing UI code):
1. Inspect `CombatManager.DebugOnlyGetState()` for per-player card zones
2. Check `Player` object for hand/draw/discard properties
3. Fall back to `Traverse` reflection if fields are private
4. If card zones are not accessible for non-local players, the feature is not feasible as designed — escalate before proceeding

## Non-Goals

- No networking — reads in-process game state only
- No persistence — no saving/loading spy state
- No analytics — just raw card lists
- No card overlays/ratings on spy panel cards

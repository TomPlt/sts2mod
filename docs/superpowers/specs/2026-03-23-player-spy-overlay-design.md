# Player Spy Overlay (F5) — Design Spec

## Overview

A combat-only F5 overlay that shows another player's hand, draw pile, and discard pile in real-time during multiplayer games. Cycles through non-local players with F6.

## Behavior

- **F5** toggles the "Player Spy" panel on/off (combat only)
- **F6** cycles to the next non-local player (wraps around)
- Panel shows one non-local player at a time
- **Never shows the local player**
- Three sections: Hand, Draw Pile, Discard Pile
- Cards displayed as grouped, alphabetically sorted names (e.g., "Defend x3, Strike x2, Bash")
- Empty piles show "(empty)"
- Updates every 0.5s via Godot timer
- Auto-hides on combat end (CombatReset / CombatRoomExit)
- Does not auto-show on combat start — user opts in with F5

## UI Layout

- **PanelContainer**, top-right of screen
- Dark semi-transparent background: rgba(0.08, 0.08, 0.12, 0.88)
- ~300px wide, height auto-fits
- Header: character name + player indicator (orange, 18pt)
- Section labels: "Hand", "Draw Pile", "Discard Pile" (grey, 14pt)
- Card lists: white, 12pt, grouped and sorted
- `MouseFilter.Ignore` — no input blocking
- ZIndex 100

## Components

### `PlayerSpyPanel` (new static class)

Same pattern as `CombatOverlay`. Static class, no instance.

- `Show(Player player)` — builds panel, adds to SceneTree, starts 0.5s timer
- `Hide()` — removes panel from SceneTree, stops timer
- `Refresh()` — called by timer, reads player's hand/draw/discard via reflection, rebuilds card list labels
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

Card pile state during combat needs to be read from the game's combat state via reflection. The exact API for hand/draw/discard split is TBD — `Player.Deck.Cards` is the full deck, but combat-time piles are likely on the combat state or a per-player combat entity.

Discovery during implementation:
1. Inspect `CombatManager.DebugOnlyGetState()` for per-player card zones
2. Check `Player` object for hand/draw/discard properties
3. Fall back to `Traverse` reflection if fields are private

## Non-Goals

- No networking — reads in-process game state only
- No persistence — no saving/loading spy state
- No analytics — just raw card lists
- No card overlays/ratings on spy panel cards

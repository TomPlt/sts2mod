# SpireOracle Mod — Design Spec

## Overview

An STS2 mod that overlays card analytics (Elo ratings, pick rates, win rates, recommendations) on the card reward screen. Reads pre-computed data from a JSON file exported by the CLI tool.

## Mod Structure

```
mods/SpireOracle/
├── SpireOracle.dll          # Compiled mod
├── mod_manifest.json        # Mod metadata
└── data.json                # Analytics export (from CLI)
```

No `.pck` required — UI is created programmatically via Godot nodes, no custom assets.

## Data Source

The CLI `export --mod` command outputs a slimmed-down JSON file:

```json
{
  "skipElo": 1710.0,
  "cards": [
    {
      "cardId": "CARD.OFFERING",
      "elo": 1672.0,
      "pickRate": 0.895,
      "winRatePicked": 0.682,
      "winRateSkipped": 0.314,
      "delta": 0.368
    }
  ]
}
```

**Data models:**
```csharp
record CardStats(string CardId, double Elo, double PickRate,
    double WinRatePicked, double WinRateSkipped, double Delta);
record OverlayData(double SkipElo, List<CardStats> Cards);
```

**Lookup:** Cards matched by game ID (e.g., `CARD.OFFERING`). Cards not in the data get no overlay.

## Entry Point

`[ModInitializer]` class with `ModLoaded()`:
1. Determine mod directory path (where the DLL lives)
2. Read and deserialize `data.json` into `OverlayData`
3. Build a `Dictionary<string, CardStats>` for O(1) lookup
4. Apply Harmony patches
5. Register F2 hotkey listener
6. If `data.json` is missing or invalid, log a warning and disable overlays (don't crash)

## Card Reward Overlay

### Always Visible (when overlay is on)

**Elo badge** — top-right corner of each reward card:
- Ember background (#d4552a): Elo >= 1650 (strong pick)
- Grey background (#243044): Elo 1500-1649 (decent)
- Dark red background (#2a1a1a): Elo < 1500 (below average)
- Shows Elo as integer (e.g., "1672")
- Font: monospace, 11px equivalent, bold

**Recommendation pill** — bottom-center of each card:
- `▲ PICK` (green #4ead6a): card Elo > Skip Elo + 50
- `— OK` (gold #d4a86a): card Elo within ±50 of Skip Elo
- `▼ SKIP` (red #d44040): card Elo < Skip Elo - 50
- Font: monospace, 9px equivalent, bold, letter-spaced

**Skip line reference** — small text at bottom of reward screen:
- Shows "SKIP LINE: {skipElo}" in ember color
- Centered, subtle

### On Hover/Focus (detail panel)

When hovering a card, show a panel below/beside it with:
- Elo rating
- Pick Rate (%)
- Win Rate when picked (%)
- Win Rate when skipped (%)
- Win Rate Delta (+/-%)

Panel style: dark background (rgba black 0.95), ember border, monospace grid layout.

## F2 Toggle

F2 hotkey cycles:
- **On** (default): badges + pills + hover details + skip line
- **Off**: no overlay, vanilla experience

State persists for the session (not saved to disk).

## Harmony Patches

**Research required:** Before implementation, decompile `sts2.dll` with ILSpy to identify:
1. Card reward screen class name and namespace
2. Method that populates/displays the 3 card reward choices
3. Card UI node type and structure (Godot Control hierarchy)
4. How card hover/focus state is detected
5. Input handling system (for F2 key binding)

**Patch strategy:**
- Postfix patch on the card reward display method to inject overlay UI nodes
- Each card node gets child nodes added: Elo badge (Label in a Panel), recommendation pill (Label in a Panel)
- Hover detail panel added as a sibling or child that toggles visibility on focus
- Skip line reference added to the reward screen container

**Safety:** All patches check for null/missing data. If a card ID isn't in the analytics data, that card gets no overlay. If the reward screen structure is unexpected, patches fail silently.

## Project Setup

```
src/Sts2Analytics.Mod/
├── Sts2Analytics.Mod.csproj    # Godot.NET.Sdk, Harmony, targets net9.0
├── ModEntry.cs                 # [ModInitializer] entry point
├── Data/
│   ├── OverlayData.cs          # Data models (CardStats, OverlayData)
│   └── DataLoader.cs           # JSON loading + dictionary building
├── Patches/
│   ├── CardRewardPatch.cs      # Harmony patch for reward screen
│   └── InputPatch.cs           # F2 hotkey handling
└── UI/
    ├── EloBadge.cs             # Creates Elo badge Godot node
    ├── RecommendationPill.cs   # Creates pick/skip pill node
    └── DetailPanel.cs          # Creates hover detail panel node
```

**Does NOT reference Sts2Analytics.Core** — the mod is standalone. Only dependencies are:
- `sts2.dll` (game reference)
- `Lib.Harmony` (2.4.2)
- `Godot.NET.Sdk`
- `System.Text.Json` (built-in)

## CLI Changes

Add `--mod` flag to the existing `export` command:

```bash
sts2analytics export --mod --output <game>/mods/SpireOracle/data.json
```

Outputs the slimmed-down `OverlayData` JSON instead of the full analytics export. Uses the "overall" context Elo ratings. Includes the Skip Elo baseline.

## Workflow

1. Play runs normally
2. Run `sts2analytics import` to parse new .run files
3. Run `sts2analytics export --mod --output <game>/mods/SpireOracle/data.json`
4. Launch STS2 — mod loads data, shows overlays on card rewards
5. Press F2 to toggle overlay on/off
6. Repeat after more runs to update data

## Implementation Phases

**Phase A: Decompilation research** — use ILSpy on `sts2.dll` to map out the card reward screen internals. Document findings. This unblocks all other work.

**Phase B: Mod scaffold + data loading** — create the mod project, entry point, JSON loading. Verify the mod loads in-game (log a message).

**Phase C: Overlay UI** — implement the Elo badge, recommendation pill, and detail panel as Godot nodes. Apply Harmony patches to inject them.

**Phase D: Polish** — F2 toggle, skip line reference, error handling, edge cases.

# Ancient Choice Glicko-2 Ratings — Design Spec

## Overview

Add Glicko-2 ratings for ancient choices (Neow rewards and post-act ancient rewards). Each ancient floor presents a set of options — the chosen option is rated against the unchosen options using the same matchup system as card ratings, but in separate DB tables.

## Architecture

New `AncientRatingEngine` following the same pattern as the existing `Glicko2Engine`. Separate `AncientGlicko2Ratings` and `AncientGlicko2History` tables. Integrated into CLI, mod overlay, and dashboard.

## 1. Ancient Rating Engine

### Matchup Logic

Each ancient floor is a rating period. The chosen option scores 1.0 vs each unchosen option (which scores 0.0). This is the same matchup logic as card choices — we rate the decision, not the outcome.

Data source: `AncientChoices` table joined to `Floors`. Each row has `TextKey` (the choice identity, e.g., `BOOMING_CONCH`) and `WasChosen` (1 or 0).

### Entity Identity

Each choice is identified by its `TextKey` from the `AncientChoices` table (e.g., `BOOMING_CONCH`, `NEOWS_TORMENT`, `LEAFY_POULTICE`). No SKIP entity — ancient choices always require picking exactly one.

### Contexts

Each choice is rated in 3 contexts simultaneously based on the ancient floor's position:

**Timing context** — determined by which act boundary the ancient floor sits at:
- Ancient floor at start of run (ActIndex 0) → `"neow"` context
- Ancient floor at start of Act 2 (ActIndex 1) → `"post_act1"` context
- Ancient floor at start of Act 3 (ActIndex 2) → `"post_act2"` context

**Character context** — the run's character (e.g., `"IRONCLAD"`)

**Overall** — `"overall"`

So each matchup routes to: timing + character + overall (3 contexts).

### Temporal Decay

Same formula as card ratings: `φ' = √(φ² + σ²)` applied for runs where a choice was absent.

### DB Schema

**`AncientGlicko2Ratings` table:**
| Column | Type | Description |
|--------|------|-------------|
| Id | INTEGER PK | |
| ChoiceKey | TEXT | TextKey from AncientChoices |
| Character | TEXT | "ALL" for overall, character name otherwise |
| Context | TEXT | "overall", "neow", "post_act1", "post_act2", character name |
| Rating | REAL | Default 1500.0 |
| RatingDeviation | REAL | Default 350.0 |
| Volatility | REAL | Default 0.06 |
| GamesPlayed | INTEGER | Default 0 |
| LastUpdatedRunId | INTEGER FK | |

**UNIQUE constraint:** (ChoiceKey, Character, Context)

**`AncientGlicko2History` table:**
| Column | Type | Description |
|--------|------|-------------|
| Id | INTEGER PK | |
| AncientGlicko2RatingId | INTEGER FK | References AncientGlicko2Ratings(Id) |
| RunId | INTEGER FK | References Runs(Id) |
| RatingBefore | REAL | |
| RatingAfter | REAL | |
| RdBefore | REAL | |
| RdAfter | REAL | |
| VolatilityBefore | REAL | |
| VolatilityAfter | REAL | |
| Timestamp | TEXT | ISO timestamp |

### Processing

Runs processed chronologically. For each run:
1. Query all ancient floors: `Floors WHERE RunId = @RunId` joined with `AncientChoices` grouped by FloorId
2. For each ancient floor, identify which act boundary it represents from `ActIndex`
3. Build matchups: chosen option beats each unchosen option
4. Route to 3 contexts: timing + character + overall
5. Update ratings via `Glicko2Calculator.UpdateRating`
6. Record history snapshots

Idempotent: uses same unprocessed-run detection pattern as `Glicko2Engine`.

## 2. CLI Integration

### Rating Command

Add `--ancient` flag to the existing `rating` command. When set, queries `AncientGlicko2Ratings` and displays a table:

```
  Ancient Choice Ratings
  ───────────────────────────────────────────────
  Choice              Rating    ±RD   Games
  ───────────────────────────────────────────────
  BOOMING_CONCH         1620     85      12
  NEOWS_TORMENT         1580    110       8
  ...
```

Respects existing `--character` and `--act` filters (act maps to timing context).

### Export Command

`export --mod` includes ancient ratings in the overlay JSON. Ancient rating engine runs as part of the export pipeline (after card Glicko-2, alongside player ratings and blind spots).

## 3. Overlay JSON

Add `ancientChoices` array to the existing v3 `ModOverlayData`:

```json
{
  "version": 3,
  "ancientChoices": [{
    "choiceKey": "BOOMING_CONCH",
    "elo": 1620,
    "rd": 110,
    "eloNeow": 1580,
    "rdNeow": 130,
    "eloPostAct1": 1650,
    "rdPostAct1": 100,
    "eloPostAct2": 1600,
    "rdPostAct2": 150
  }],
  ...existing fields unchanged...
}
```

The `elo`/`rd` fields are from the `"overall"` context. Per-timing fields use the timing context ratings.

## 4. Mod Overlay

Harmony patch on the ancient choice screen (same pattern as `CardRewardPatch`). Each option gets:
- A rating badge showing its Glicko-2 rating, colored by strength relative to the other options presented
- Hover detail panel showing rating ± RD, per-timing breakdown

The mod reads `ancientChoices` from `overlay_data.json` via `DataLoader`.

## 5. Dashboard

New "Ancient Ratings" tab in the web dashboard:
- Leaderboard table sorted by rating descending
- Columns: Choice, Rating, ±RD, Neow, Post-Act1, Post-Act2, Games
- Character filter bar (same pattern as Rating Leaderboard)
- Same dark Spire Oracle aesthetic

## 6. Export Data for Dashboard

Add ancient ratings to the dashboard JSON export:

```json
{
  "ancientRatings": [{
    "choiceKey": "BOOMING_CONCH",
    "character": "ALL",
    "context": "overall",
    "rating": 1620,
    "ratingDeviation": 110,
    "gamesPlayed": 12
  }]
}
```

## 7. Processing Pipeline

```
sts2analytics import → Parse .run files → Store runs, floors, ancient choices

sts2analytics rating (or implicit during export)
  → Card Glicko-2 engine (existing)
  → Ancient Glicko-2 engine (NEW)
  → Player Glicko-2 engine (existing)
  → Blind spot analyzer (existing)

sts2analytics export --mod
  → Read card ratings + blind spots + ancient ratings
  → Write overlay_data.json v3
```

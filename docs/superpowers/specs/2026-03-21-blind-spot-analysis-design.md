# Blind Spot Analysis & Personal Rating — Design Spec

## Overview

Three new features layered on top of the existing Glicko-2 card rating system:

1. **Player Rating** — Glicko-2 rating per character tracking the player's skill over time, with ascension-scaled opponents
2. **Blind Spot Detection** — dual-signal analysis identifying cards the player consistently over-picks or under-picks relative to ratings and outcomes
3. **In-Game Blind Spot Flags** — overlay badges on the card reward screen flagging blind spot cards

## Architecture

Layered pipeline — each system has its own responsibility and DB tables:

```
Card Glicko-2 (existing) ──┐
                            ├──→ BlindSpotAnalyzer
Player Glicko-2 (new) ─────┘         ↓
                              BlindSpot scores
```

Processing order:
1. Card Glicko-2 ratings (existing, unchanged)
2. Player Glicko-2 ratings (new, independent of card ratings)
3. Blind spot analysis (new, depends on card ratings + pick/win data)

## 1. Player Rating System

### Concept

The player is a Glicko-2 rated entity. Each run is a match: the player vs. "the game at ascension X." Win/loss is the outcome.

### Opponent Rating

Each ascension level has a fixed rating acting as opponent strength. These are static anchors — they don't update via Glicko-2.

- A0 = 1200, scaling linearly to A20 = 2400
- Formula: `opponentRating = 1200 + (ascension * 60)`
- Opponent RD is fixed at a low value (e.g., 50) to represent certainty about ascension difficulty

### Player Rating

Standard Glicko-2 triplet (μ, φ, σ) per context:
- Per character: Ironclad, Silent, Defect, Regent, Necrobinder
- Overall (all characters combined)

Processing:
- Runs processed chronologically
- Win → score 1.0 against ascension opponent rating
- Loss → score 0.0 against ascension opponent rating
- Rating deviation decays between runs using the same temporal decay formula as card ratings: `φ' = √(φ² + σ²)`

### DB Schema

**`PlayerRatings` table:**
| Column | Type | Description |
|--------|------|-------------|
| Context | TEXT PK | "overall", "IRONCLAD", "SILENT", etc. |
| Rating | REAL | Current μ |
| RatingDeviation | REAL | Current φ |
| Volatility | REAL | Current σ |
| GamesPlayed | INTEGER | Total runs in this context |
| LastUpdatedRunId | INTEGER FK | |

**`PlayerRatingHistory` table:**
| Column | Type | Description |
|--------|------|-------------|
| Id | INTEGER PK | |
| RunId | INTEGER FK | |
| Context | TEXT | |
| RatingBefore | REAL | |
| RatingAfter | REAL | |
| RdBefore | REAL | |
| RdAfter | REAL | |
| VolatilityBefore | REAL | |
| VolatilityAfter | REAL | |
| Opponent | TEXT | e.g., "A18" |
| OpponentRating | REAL | |
| Outcome | REAL | 1.0 or 0.0 |

## 2. Blind Spot Detection

### Dual-Signal Approach

A card is flagged as a blind spot only when BOTH conditions are true:

1. **Pick-rating mismatch** — pick rate significantly diverges from what the rating suggests
2. **Outcome penalty** — win rate when picking differs meaningfully from win rate when skipping

### Blind Spot Types

**Over-pick:** Card rated below SKIP threshold + high pick rate + negative win rate delta (win rate drops when picked). The player drafts a card that hurts their runs.

**Under-pick:** Card rated above SKIP threshold + low pick rate + positive win rate delta (win rate improves when picked). The player skips a card that helps their runs.

### Expected Pick Rate

A card's expected pick rate is derived from its Glicko-2 rating relative to SKIP using a logistic function:

```
expectedPickRate = 1 / (1 + e^(-(cardRating - skipRating) / 200))
```

This maps the rating difference to a 0-1 probability curve:
- Card rated equal to SKIP → 50% expected pick rate
- Card rated 200 above SKIP → ~73% expected
- Card rated 200 below SKIP → ~27% expected
- The 200 divisor controls curve steepness (tunable)

### Scoring Formula

Blind spot score is the product of pick deviation magnitude and outcome signal, dampened by uncertainty:

```
pickRateDeviation = actualPickRate - expectedPickRate
winRateDelta = winRateWhenPicked - winRateWhenSkipped
confidenceWeight = max(0, 1 - (cardRD / 350))  // 0 at default RD, 1 at RD=0

blindSpotScore = |pickRateDeviation| * |winRateDelta| * confidenceWeight
```

**Over-pick** when: `pickRateDeviation > 0` AND `winRateDelta < 0` (picks too much, hurts win rate)
**Under-pick** when: `pickRateDeviation < 0` AND `winRateDelta > 0` (skips too much, helps when picked)

A card is only flagged when:
- `blindSpotScore >= 0.02` (tunable threshold, starting default)
- Minimum sample size met (offered at least 5 times)
- `confidenceWeight > 0.3` (card RD below ~245, system is reasonably confident)

### Per-Context Analysis

Blind spots computed per character and per act, matching existing card rating contexts. A card may be a blind spot in one context but not others.

### DB Schema

**`BlindSpots` table:**
| Column | Type | Description |
|--------|------|-------------|
| CardId | TEXT | |
| Context | TEXT | e.g., "IRONCLAD", "IRONCLAD_ACT1" |
| BlindSpotType | TEXT | "over_pick" or "under_pick" |
| Score | REAL | Combined blind spot score |
| PickRate | REAL | Player's actual pick rate |
| ExpectedPickRate | REAL | Expected pick rate based on rating |
| WinRateDelta | REAL | Win rate when picked minus when skipped |
| GamesAnalyzed | INTEGER | Times card was offered |
| LastUpdated | TEXT | ISO timestamp |

**Composite PK:** (CardId, Context)

## 3. In-Game Overlay Integration

### Overlay Changes

Extend existing SpireOracle overlay with blind spot badges on the card reward screen.

**Badge types:**
- Red `⚠ OVER-PICK` — top-right corner of card, shown for over-pick blind spots
- Amber `⚠ UNDER-PICK` — top-right corner of card, shown for under-pick blind spots
- No badge for normal cards

Existing rating badges, recommendation pills, and hover detail panels remain unchanged. Blind spot badges are additive.

### Export Data v3

Bump `ModOverlayData` to version 3. New fields per card:

```json
{
  "version": 3,
  "exportedAt": "2026-03-21T08:49:49Z",
  "skipElo": 1704.16,
  "skipEloByAct": { "act1": 1404.23, "act2": 1734.09, "act3": 1818.40 },
  "cards": [{
    "cardId": "CARD.INFLAME",
    "elo": 1280,
    "rd": 85,
    "pickRate": 0.85,
    "winRatePicked": 0.72,
    "winRateSkipped": 0.31,
    "delta": 0.41,
    "eloAct1": 1540,
    "rdAct1": 90,
    "eloAct2": 1650,
    "rdAct2": 110,
    "eloAct3": 1580,
    "rdAct3": 120,
    "blindSpot": "over_pick",
    "blindSpotScore": 0.04,
    "blindSpotPickRate": 0.78,
    "blindSpotWinRateDelta": -0.12
  }]
}
```

All existing `CardStats` fields (`elo`, `rd`, `pickRate`, `winRatePicked`, `winRateSkipped`, `delta`, `eloAct1`-`3`, `rdAct1`-`3`) are retained. New fields added: `blindSpot` (null when no flag), `blindSpotScore`, `blindSpotPickRate`, `blindSpotWinRateDelta`. The overlay reads `blindSpot` and conditionally renders the badge.

Player rating is NOT included in the mod export — it's dashboard-only.

## 4. Dashboard Integration

### My Rating Tab

New tab in the existing Blazor WASM dashboard:

- **Rating summary cards** — per-character current rating (μ ± φ)
- **Rating trajectory chart** — line chart of rating over time per character, sourced from `PlayerRatingHistory`
- **Recent runs list** — last N runs with character, ascension, outcome, and rating change

### Blind Spots Tab

New tab in the dashboard:

- **Character filter bar** — filter by character or view all
- **Over-picks table** (red) — sorted by blind spot score descending. Columns: Card, Rating, Pick Rate, Win Δ, Score
- **Under-picks table** (amber) — same columns, sorted by score descending

Both tables link to the existing card detail view for deeper analysis.

## 5. CLI Integration

### Existing Commands Modified

- `rating --player` flag — shows personal ratings per character instead of card ratings
- `export --mod` — includes blind spot data in overlay JSON, version bumped to 3

### No New Commands

Blind spots and player ratings are computed as part of the standard processing pipeline. Viewable via dashboard or the `rating --player` flag.

## 6. Processing Pipeline

### Full Pipeline Order

```
sts2analytics import
  → Parse .run files
  → Store runs, floors, card choices, etc.

sts2analytics rating (or implicit during export)
  → Card Glicko-2 engine (existing)
  → Player Glicko-2 engine (new)
  → Blind spot analyzer (new)

sts2analytics export --mod
  → Read card ratings + blind spots
  → Write overlay_data.json v3
```

### Idempotency

All three systems are idempotent — reprocessing all runs produces the same result. Player rating engine processes runs in chronological order. Blind spot analyzer reads current state and recomputes from scratch.

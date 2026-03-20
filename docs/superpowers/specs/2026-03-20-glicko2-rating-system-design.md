# Glicko-2 Rating System Design

Replace the current Elo rating system with Glicko-2 to add built-in confidence intervals and temporal decay.

## Problem

The current Elo system has two pain points:
1. No confidence signal — a rating based on 3 appearances looks the same as one based on 50
2. Ratings feel static — early learning runs weigh equally with recent informed play

## Solution: Glicko-2

Glicko-2 solves both natively:
- **Rating Deviation (φ)** provides confidence — shrinks as data accumulates, increases when a card isn't seen
- **Volatility (σ)** tracks how erratic a card's results are
- **Temporal decay** is built in — φ grows between rating periods, so stale ratings automatically show wider uncertainty

## Rating Model

Each card gets a triplet per context:
- **μ (mu)** — rating, default 1500
- **φ (phi)** — rating deviation, default 350 (very uncertain), converges toward ~50
- **σ (sigma)** — volatility, default 0.06

K-factor tiers (40/20/10) are eliminated — φ and σ handle what they approximated.

## Context Dimensions

**Per-character (5):** IRONCLAD, SILENT, DEFECT, REGENT, NECROBINDER
**Per-character-per-act (15):** e.g., IRONCLAD_ACT1, IRONCLAD_ACT2, IRONCLAD_ACT3
**Overall (1):** All characters pooled (character = "ALL", context = "overall")

Total: **21 contexts**. Each card has a separate (μ, φ, σ) in each context where it appears.

SKIP remains a rated entity in every context.

No ascension-specific contexts for now — 100 runs across 5 characters and 20 ascension levels is too thin. Can be added later.

## Matchup Logic

### Picked always beats skipped

The current system inverts matchup direction on run losses. This is removed. A pick is a pick — the player chose it over alternatives. Over many runs, bad cards sink because they get skipped more. Run outcome is captured separately in win-rate analytics.

### Act context routing

Each card choice floor has `ActIndex` (0-indexed: 0 = Act 1, 1 = Act 2, 2 = Act 3). Route matchups to the correct context:
- ActIndex 0 → CHARACTER_ACT1 + CHARACTER + overall
- ActIndex 1 → CHARACTER_ACT2 + CHARACTER + overall
- ActIndex 2 → CHARACTER_ACT3 + CHARACTER + overall

### Upgraded cards as separate entities

Cards can appear upgraded at reward screens. When reading from the `CardChoices` table, if `UpgradeLevel > 0`, construct the entity ID as `"{CardId}+{UpgradeLevel}"` (e.g., `"CARD.INFLAME+1"`, `"CARD.INFLAME+2"`). When `UpgradeLevel == 0`, use `CardId` as-is. STS2 supports multiple upgrade levels, so the format handles any value.

### No enchantment filtering needed

Cards at reward screens are never enchanted — enchantments come from events/relics after acquisition. No special handling required.

### Matchup generation (unchanged from current)

- All skipped → SKIP wins vs each card
- Some picked → each picked card wins vs each skipped card + vs SKIP

### Rating period

One run = one rating period. All matchups across all floors within a run are **batched** per card per context, then the Glicko-2 update is applied once. This differs from the current Elo code which applies updates sequentially per floor — Glicko-2 requires collecting all results in a period before computing the update.

### Temporal decay between rating periods

When processing run N for a given card, apply inactivity decay for each run between the card's `LastUpdatedRunId` and N where the card was absent. For each skipped run: `φ' = √(φ² + σ²)`. This means a card absent for 5 consecutive runs has its φ grown 5 times before the next actual update.

During full reprocessing (all runs sequentially), the engine must track which runs each card appeared in to apply the correct number of decay steps.

## Data Model

### Glicko2Ratings table (replaces EloRatings)

| Column | Type | Default | Notes |
|--------|------|---------|-------|
| Id | INTEGER PK | auto | |
| CardId | TEXT | | e.g., CARD.INFLAME or CARD.INFLAME+1 |
| Character | TEXT | | "ALL" for overall |
| Context | TEXT | "overall" | "overall", "IRONCLAD", "IRONCLAD_ACT1", etc. |
| Rating | REAL | 1500.0 | μ |
| RatingDeviation | REAL | 350.0 | φ |
| Volatility | REAL | 0.06 | σ |
| GamesPlayed | INTEGER | 0 | Count of rating periods (runs) where the card appeared, not individual matchups |
| LastUpdatedRunId | INTEGER | | For temporal decay calculation |

Unique constraint: (CardId, Character, Context). Index on CardId.

### Glicko2History table (replaces EloHistory)

| Column | Type | Notes |
|--------|------|-------|
| Id | INTEGER PK | |
| Glicko2RatingId | INTEGER FK | |
| RunId | INTEGER FK | |
| RatingBefore | REAL | μ before |
| RatingAfter | REAL | μ after |
| RdBefore | REAL | φ before |
| RdAfter | REAL | φ after |
| VolatilityBefore | REAL | σ before |
| VolatilityAfter | REAL | σ after |
| Timestamp | TEXT | |

### Migration

Drop old EloRatings and EloHistory tables. Reprocess all runs — takes seconds with 100 runs. No migration path needed.

## Display

### CLI (`elo` command)

```
Rank | Card              | Rating | ±    | Games | Trend
   1 | Inflame           |   1620 |   45 |    32 | ▲
   2 | Noxious Fumes     |   1580 |  180 |     5 | ─
   3 | SKIP              |   1540 |   30 |    87 | ▼
```

- **±** — confidence interval from φ
- **Trend** — direction of last 3 rating changes within the displayed context (▲ ▼ ─), queried as last 3 Glicko2History rows for the card's RatingId ordered by RunId descending
- **`--act N`** — filter by act context
- **`--min-games N`** — client-side filter, hides cards below threshold from display

### Dashboard (Blazor)

- Add ± column, sortable
- Add act filter dropdown alongside character filter
- Muted/faded text for low-confidence ratings (φ > 200)
- Tooltip showing volatility σ

### JSON export

Add `ratingDeviation` and `volatility` fields alongside `rating`.

## Implementation Scope

### New files
- `Glicko2Calculator.cs` — pure Glicko-2 math
- `Glicko2Engine.cs` — run processing, matchup generation, context routing
- `Glicko2Analytics.cs` — query layer for ratings and history

### Modified files
- `Schema.cs` — new tables, drop old
- `Entities.cs` — new entity records
- `AnalyticsResults.cs` — new DTOs with RD and volatility
- `EloCommand.cs` → rename to `RatingCommand.cs` — updated display with confidence and trend, CLI verb stays `elo` for familiarity
- `EloLeaderboard.razor` → rename to `RatingLeaderboard.razor` — new columns and act filter
- `ExportCommand.cs` — export new fields
- `DataService.cs` — consume new fields

### Removed files
- `EloCalculator.cs` — replaced by Glicko2Calculator
- `EloEngine.cs` — replaced by Glicko2Engine
- `EloAnalytics.cs` — replaced by Glicko2Analytics

### Tests
- `Glicko2CalculatorTests.cs` — verify math against Glicko-2 reference values
- `Glicko2EngineTests.cs` — matchup routing, act contexts, upgrade handling, temporal decay

## Glicko-2 Algorithm Reference

The Glicko-2 algorithm (Mark Glickman, 2013) processes in steps:

1. Convert to Glicko-2 scale: μ' = (μ - 1500) / 173.7178, φ' = φ / 173.7178
2. Compute quantity v (estimated variance of rating based on game outcomes)
3. Compute quantity Δ (estimated improvement)
4. Determine new volatility σ' via iterative convergence (Illinois algorithm)
5. Update φ' using new σ' and v
6. Update μ' using game results
7. Convert back to original scale

System constant τ (constrains volatility change) set to 0.5 (reasonable default, hardcoded as a const — can be tuned later if ratings feel too volatile or sluggish).

Between rating periods for inactive cards: φ' = √(φ² + σ²) — deviation grows, reflecting increasing uncertainty.

## Expectations with Current Data

With ~100 runs across 5 characters, act-specific contexts will have thin data for most cards. φ will naturally remain high (~200-350) for these, which is correct — the confidence interval honestly reflects the uncertainty. Per-character overall ratings will be more informative. Act-specific ratings become actionable as more runs are played.

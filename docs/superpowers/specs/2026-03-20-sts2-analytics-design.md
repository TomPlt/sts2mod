# STS2 Analytics — Design Spec

## Overview

A C# analytics platform for Slay the Spire 2 run data. Parses the game's native `.run` JSON history files into a SQLite database, computes card/relic/path analytics including an Elo rating system, and presents results via CLI and Blazor WASM dashboard.

**Phase 1** (this spec): Standalone parser + analytics from existing run history files.
**Phase 2** (future): In-game mod that captures per-card-play combat data not present in run files.

## Architecture

### Solution Structure

```
Sts2Analytics.sln
├── src/
│   ├── Sts2Analytics.Core/        # Class library (.NET 9.0)
│   │   ├── Models/                # Run, Card, Relic, Floor, Combat, etc.
│   │   ├── Parsing/               # JSON run file parser
│   │   ├── Database/              # SQLite schema, repository, migrations
│   │   ├── Analytics/             # Query/aggregation services
│   │   └── Elo/                   # Elo rating engine
│   ├── Sts2Analytics.Cli/         # Console app (.NET 9.0)
│   │   └── Commands/              # Import, stats, export, elo commands
│   └── Sts2Analytics.Web/         # Blazor WASM (.NET 9.0)
│       ├── Pages/                 # Dashboard pages
│       ├── Components/            # Charts, filters, tables
│       └── Services/              # Bridge to Core analytics
└── tests/
    └── Sts2Analytics.Core.Tests/  # Unit tests for parsing + analytics
```

### Dependencies

- `Microsoft.Data.Sqlite` + `Dapper` — lightweight SQLite access
- `System.CommandLine` — CLI argument parsing
- `System.Text.Json` — JSON deserialization (built-in)
- `Radzen.Blazor` or `BlazorChart.js` — chart components for dashboard

## Data Source

Run history files are located at:
```
%AppData%/Roaming/SlayTheSpire2/steam/<steam_id>/<profile>/saves/history/*.run
```

Each `.run` file is JSON (schema version 8) containing:
- Run metadata: character, ascension, seed, win/loss, build version, run time, acts
- Floor-by-floor map history with card/relic/potion choices, combat stats, event choices
- Final deck, relics, and potions with floor-acquired tracking

Files are named by Unix timestamp (e.g., `1772736706.run`).

## Data Model

> **Assumption:** Phase 1 assumes single-player runs. Character is extracted from `players[0].character`. If STS2 adds co-op run history, a Players table can be introduced.

### Runs
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | Auto-increment |
| FileName | TEXT UNIQUE | Source file name (dedup key) |
| Seed | TEXT | Run seed |
| Character | TEXT | From players[0].character, e.g. CHARACTER.IRONCLAD |
| Ascension | INTEGER | 0-20 |
| GameMode | TEXT | standard, etc. |
| BuildVersion | TEXT | e.g. v0.98.0 |
| Win | BOOLEAN | |
| WasAbandoned | BOOLEAN | |
| KilledByEncounter | TEXT | NONE.NONE if won |
| KilledByEvent | TEXT | NONE.NONE if won |
| StartTime | INTEGER | Unix timestamp |
| RunTime | INTEGER | Seconds |
| Acts | TEXT | Comma-separated act IDs |
| SchemaVersion | INTEGER | |
| PlatformType | TEXT | |
| Modifiers | TEXT | JSON array of gameplay modifiers (nullable) |
| MaxPotionSlots | INTEGER | From players[0].max_potion_slot_count |

### Floors
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| RunId | INTEGER FK | |
| ActIndex | INTEGER | 0-based act number |
| FloorIndex | INTEGER | Position within act |
| MapPointType | TEXT | monster/shop/rest_site/boss/elite/treasure/unknown/ancient |
| EncounterId | TEXT | e.g. ENCOUNTER.NIBBITS_WEAK |
| RoomType | TEXT | |
| TurnsTaken | INTEGER | |
| PlayerId | INTEGER | |
| CurrentHp | INTEGER | |
| MaxHp | INTEGER | |
| DamageTaken | INTEGER | |
| HpHealed | INTEGER | |
| MaxHpGained | INTEGER | |
| MaxHpLost | INTEGER | |
| CurrentGold | INTEGER | |
| GoldGained | INTEGER | |
| GoldSpent | INTEGER | |
| GoldLost | INTEGER | |
| GoldStolen | INTEGER | |

### CardChoices
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| CardId | TEXT | e.g. CARD.INFLAME |
| WasPicked | BOOLEAN | |
| WasBought | BOOLEAN | True if purchased at shop |
| UpgradeLevel | INTEGER | 0 if not upgraded (tracked for all cards, picked and skipped) |
| EnchantmentId | TEXT | nullable |
| EnchantmentAmount | INTEGER | |
| Source | TEXT | reward/shop/event |

### RelicChoices
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| RelicId | TEXT | |
| WasPicked | BOOLEAN | |
| WasBought | BOOLEAN | True if purchased at shop (from bought_relics) |
| Source | TEXT | elite/treasure/shop/boss/event/ancient |

### PotionChoices
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| PotionId | TEXT | |
| WasPicked | BOOLEAN | |

### PotionEvents
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| PotionId | TEXT | |
| Action | TEXT | used/discarded (parsed from separate potion_used and potion_discarded arrays) |

### EventChoices
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| EventId | TEXT | e.g. EVENT.BYRDONIS_NEST |
| ChoiceKey | TEXT | Localization key (title.key) |
| ChoiceTable | TEXT | Localization table (title.table) |
| Variables | TEXT | JSON blob |

### RestSiteChoices
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| Choice | TEXT | SMITH/REST/HATCH |

### RestSiteUpgrades
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| RestSiteChoiceId | INTEGER FK | |
| CardId | TEXT | Card that was upgraded |

### CardTransforms
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| OriginalCardId | TEXT | |
| FinalCardId | TEXT | |

### Monsters
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| MonsterId | TEXT | |

### FinalDecks
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| RunId | INTEGER FK | |
| CardId | TEXT | |
| UpgradeLevel | INTEGER | |
| FloorAdded | INTEGER | |
| EnchantmentId | TEXT | nullable |

### CardsGained
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| CardId | TEXT | |
| UpgradeLevel | INTEGER | |
| EnchantmentId | TEXT | nullable |
| Source | TEXT | reward/shop/relic/event/transform |

### CardRemovals
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| CardId | TEXT | |
| FloorAddedToDeck | INTEGER | nullable, from original card data |

### CardEnchantments
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| CardId | TEXT | |
| EnchantmentId | TEXT | |
| EnchantmentAmount | INTEGER | |

### FinalRelics
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| RunId | INTEGER FK | |
| RelicId | TEXT | |
| FloorAdded | INTEGER | |
| Props | TEXT | nullable JSON for relic-specific data |

### FinalPotions
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| RunId | INTEGER FK | |
| PotionId | TEXT | |
| SlotIndex | INTEGER | |

### AncientChoices
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| FloorId | INTEGER FK | |
| TextKey | TEXT | |
| WasChosen | BOOLEAN | |

### EloRatings
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| CardId | TEXT | Card ID or "SKIP" |
| Character | TEXT | |
| Context | TEXT | overall/act1/act2/act3/asc0-10/asc11-20 |
| Rating | REAL | Current Elo (starts 1500) |
| GamesPlayed | INTEGER | |

### EloHistory
| Column | Type | Description |
|---|---|---|
| Id | INTEGER PK | |
| EloRatingId | INTEGER FK | |
| RunId | INTEGER FK | |
| RatingBefore | REAL | |
| RatingAfter | REAL | |
| Timestamp | INTEGER | |

## Elo Rating System

### Core Mechanic

Each card reward screen is a matchup. All offered cards and the implicit "Skip" option compete:

- **Pick card A, skip B and C**: A wins vs B, A wins vs C, A wins vs Skip
- **Skip all (A, B, C offered)**: Skip wins vs A, Skip wins vs B, Skip wins vs C

The outcome (win/loss) of the run determines the direction:
- In a **winning run**: the picked card gains Elo from the skipped cards/Skip
- In a **losing run**: the skipped cards gain Elo from the picked card (or Skip gains if all were skipped)

### Parameters

- **Starting Elo**: 1500
- **K-factor**: Starts at 40 (< 10 games), drops to 20 (10-30 games), settles at 10 (30+ games)
- **Expected score**: Standard Elo formula: `E = 1 / (1 + 10^((Rb - Ra) / 400))`

### Context Segmentation

Start with two contexts to avoid thin data:
- **Per-character** (Ironclad, etc.)
- **Overall** (all characters combined)

Add these when sufficient data exists (100+ runs):
- Ascension tier (0, 1-10, 11-20)
- Act (when the card was offered: act 1, 2, or 3)

### Skip as a Player

"Skip" has its own Elo rating per context. This enables:
- "Is this card better than skipping?" → compare card Elo vs Skip Elo
- "When does skipping become optimal?" → Skip Elo by act
- "Which cards are trap picks?" → high pick rate but Elo below Skip

### Matchup Table

Head-to-head records tracked: when card A and card B are both offered, how often does picking A lead to a win vs picking B?

## Analytics Services

All methods accept an optional `AnalyticsFilter`:
```csharp
public record AnalyticsFilter(
    string? Character = null,
    int? AscensionMin = null,
    int? AscensionMax = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? GameMode = null,
    int? ActIndex = null
);
```

### CardAnalytics
- `GetCardWinRates(filter?)` — win% when picked vs skipped
- `GetCardPickRates(filter?)` — pick rate when offered
- `GetCardPicksByAct()` — pick rate by act
- `GetCardSynergies()` — cards frequently co-occurring in winning decks
- `GetCardImpactScore()` — composite: pick rate weighted by win rate delta

### RelicAnalytics
- `GetRelicWinRates()` — win% with vs without
- `GetRelicPickRates()` — pick rate when offered
- `GetRelicTimingImpact()` — floor acquired vs win rate

### PotionAnalytics
- `GetPotionPickRates()` — pick rate when offered
- `GetPotionUsageTiming()` — floors/room types where used
- `GetPotionWasteRate()` — discard rate

### PathAnalytics
- `GetPathPatternWinRates()` — win% by path signature
- `GetEliteCountCorrelation()` — elites fought vs win rate
- `GetShopTimingImpact()` — shop visit timing vs win rate

### EconomyAnalytics
- `GetGoldEfficiency()` — gold spent vs win rate
- `GetShopPurchasePatterns()` — category breakdown vs wins
- `GetCardRemovalImpact()` — which removals correlate with wins

### CombatAnalytics
- `GetDamageTakenByEncounter()` — avg damage per encounter type
- `GetTurnsByEncounter()` — fight length
- `GetDeathFloorDistribution()` — where runs end
- `GetHpThresholdAnalysis()` — HP at floor X vs win probability

### RunAnalytics
- `GetOverallWinRate(filter?)` — by character, ascension, mode
- `GetRunLengthDistribution()` — floors reached
- `GetArchetypeDetection()` — cluster by card type ratios
- `GetWinStreaks()` — streak tracking

### EloAnalytics
- `GetCardEloRatings(filter?)` — current rankings
- `GetCardEloHistory(cardId)` — rating over time
- `GetCardMatchups(cardA, cardB)` — head-to-head when both offered
- `GetSkipEloByContext()` — Skip rating across contexts

## CLI Commands

```
sts2analytics import <path>       # Parse .run files into SQLite
  --db <path>                     # DB location (default: ~/.sts2analytics/data.db)
  --watch                         # Watch for new .run files

sts2analytics stats               # Overall stats summary
  --character <name>
  --ascension <n>

sts2analytics cards               # Card pick/win rate table
  --sort winrate|pickrate|impact
  --top <n>
  --character <name>

sts2analytics relics              # Relic pick/win rate table
  --sort winrate|pickrate

sts2analytics elo                 # Card Elo leaderboard
  --character <name>
  --ascension <range>
  --history                       # Rating over time
  --matchup <card> <card>         # Head-to-head

sts2analytics run <id|seed>       # Single run breakdown

sts2analytics export              # Export to JSON
  --output <path>
```

Auto-detects default save path: `%AppData%/Roaming/SlayTheSpire2/steam/*/profile*/saves/history/`

## Web Dashboard Pages

### Overview (`/`)
- Win rate over time (line chart)
- Runs by character (pie chart)
- Recent runs table
- Win/loss streak

### Card Explorer (`/cards`)
- Sortable table: name, Elo, pick rate, win rate, impact score
- Filters: character, ascension, act
- Click → detail: Elo history, matchups, pick context

### Card Matchups (`/cards/matchups`)
- Head-to-head grid for any two cards
- Skip Elo baseline comparison

### Relic Explorer (`/relics`)
- Elo/win rate/pick rate table with drill-down

### Run History (`/runs`)
- Filterable run list
- Click → floor-by-floor timeline: choices, HP curve, gold curve

### Path Analysis (`/paths`)
- Map point sequence heatmap vs win rate
- Elite count scatter plot

### Economy (`/economy`)
- Gold over time (overlaid per run)
- Shop spending breakdown

### Combat (`/combat`)
- Damage by encounter (bar chart)
- Death floor histogram
- HP threshold analysis

### Elo Leaderboard (`/elo`)
- Rankings with sparkline history
- Tabs: per-character, per-ascension, per-act
- Skip Elo reference line

## Future: Phase 2 Mod

The Core library is designed to be referenced by a future STS2 mod (C#/.NET 9.0, Harmony patching) that captures real-time combat data not in run files:
- Per-card-play tracking (which card played, target, damage dealt)
- Turn-by-turn state snapshots
- Status effect tracking

The mod would write events to the same SQLite DB, extending the schema with combat-level detail.

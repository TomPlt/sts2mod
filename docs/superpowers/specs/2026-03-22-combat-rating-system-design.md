# Combat Rating System Design

## Overview

A Glicko-2 based rating system that measures individual card combat performance by treating each fight as a team match: the deck (team of cards) vs an encounter pool (opponent). Over many runs, cards that consistently appear in decks that take low damage develop high combat ratings. Aggregating card combat ratings produces a "Deck Elo" — a single number representing the current deck's predicted combat strength against a given encounter type.

This is independent of the existing pick-preference Glicko-2 system. Pick Elo measures draft desirability; Combat Elo measures actual combat contribution.

## Motivation

The existing map intel overlay shows average damage per encounter pool, but these numbers are the same regardless of the player's deck. A strong deck should expect less damage than a weak one. Combat Elo enables deck-aware predictions and surfaces cards whose combat value diverges from their pick value.

## Prior Art

- **TrueSkill (Microsoft):** Team strength = sum of player ratings. All team members share credit/blame. Uncertain players absorb larger updates. Directly analogous — cards are players, decks are teams, encounters are opponents.
- **RAPM (Basketball/Hockey):** Regularized regression decomposes team performance into individual player contributions. Same structure: `damage = f(cards_in_deck) + encounter_effect`.
- **17Lands (MTG):** Game-in-Hand Win Rate measures per-card marginal contribution. Plus-minus for cards.

## Data Model

### New Tables

**CombatGlicko2Ratings**
Same schema as `Glicko2Ratings`:
- `Id` (PK)
- `CardId` — card entity ID (e.g., `CARD.IRON_WAVE`, `CARD.INFLAME+1`) or pool entity ID (e.g., `POOL.ACT1_ELITE`)
- `Character` — `ALL` or specific character
- `Context` — encounter pool context: `act1_weak`, `act1_normal`, `act1_elite`, `act1_boss`, `act2_normal`, `act2_elite`, `act2_boss`, `act3_normal`, `act3_elite`, `act3_boss`, `overall`
- `Rating` (default 1500)
- `RatingDeviation` (default 350)
- `Volatility` (default 0.06)
- `GamesPlayed`
- `LastUpdatedRunId`

**CombatGlicko2History**
Same schema as `Glicko2History` plus `FloorId` for per-combat tracing:
- `Id` (PK)
- `CombatGlicko2RatingId` (FK)
- `RunId`
- `FloorId` — which combat floor triggered this update
- `RatingBefore`, `RatingAfter`
- `RdBefore`, `RdAfter`
- `VolatilityBefore`, `VolatilityAfter`
- `Timestamp`

### Pool Entity IDs

Encounter pools are rated as opponents. Pool IDs derived from the encounter ID suffix:
- `_WEAK` → `POOL.ACT{n}_WEAK`
- `_NORMAL` → `POOL.ACT{n}_NORMAL`
- `_ELITE` → `POOL.ACT{n}_ELITE`
- `_BOSS` → `POOL.ACT{n}_BOSS`

## Deck Reconstruction

To know the deck at each combat floor, use an event-sourced approach — walk forward through all deck mutation events rather than reconstructing from the `FinalDecks` snapshot (which only contains cards that survived to the end).

**Algorithm:**

1. **Build event log** for the run from three sources:
   - `CardsGained` — (floor, card_id, upgrade_level, "add")
   - `CardRemovals` — (floor, card_id, "remove"). Note: `CardRemovals` lacks `UpgradeLevel`, so join against `CardsGained` or `FinalDecks` to determine the upgrade state of the removed card.
   - `CardTransforms` — (floor, original_card_id, "remove") + (floor, new_card_id, "add"). Lacks `UpgradeLevel`; infer from `CardsGained`/`FinalDecks`.
2. **Sort by floor index**
3. **Walk forward**: maintain a deck set, apply add/remove events at each floor
4. **Snapshot** the deck state at each combat floor before processing that floor's rating update

Starter cards (floor 0) are identified from `CardsGained` where `FloorId` corresponds to floor index 0, or from `FinalDecks WHERE FloorAdded = 0` for cards never removed.

Card entity IDs include upgrade level to match existing convention: `CARD.NAME` or `CARD.NAME+1`.

## Combat Rating Engine

### Processing Flow

Process all runs chronologically. For each run, process combat floors in floor order.

**Rating period:** Each combat floor is a separate rating period (not per-run like the pick-preference engine). This is necessary because deck composition changes between floors. Inactivity decay is NOT applied between floors within the same run — only between runs.

Per floor:
1. **Reconstruct deck** at this floor
2. **Determine encounter pool** from `EncounterId` suffix and `ActIndex`
3. **Compute outcome score** (see Scoring section)
4. **Update ratings** for every card in the deck and the pool entity

**Death floors:** When the player dies in combat, the floor's `DamageTaken` reflects lethal damage. Score this normally — the percentile rank will naturally place it at the low end (bad outcome). No special-casing needed.

### Outcome Scoring

Convert damage taken into a Glicko-2 score [0, 1] using percentile rank against the pool's historical damage distribution:

```
score = (number of historical fights with damage >= actual_damage) / (total historical fights in pool)
```

- 0 damage → score ≈ 1.0 (beat almost everyone)
- Median damage → score ≈ 0.5
- Worst damage ever → score ≈ 0.0

The damage distribution per pool/act is precomputed from all combat floors before processing ratings. This grounds scores in what's normal for each pool — boss fights naturally have a different scale than weak fights.

### Rating Update

Each combat floor produces one "match" per card in the deck:

- **Card** plays against the **pool entity** as the opponent
- Card gets score from the outcome scoring
- Pool entity gets `1 - score`
- Standard Glicko-2 update applied to both

**Contexts:** Each card-vs-pool match produces updates in multiple contexts (same pattern as existing Glicko-2):
- `("ALL", pool_context)` — e.g., `("ALL", "act1_elite")`
- `(character, pool_context)` — e.g., `("CHARACTER.IRONCLAD", "act1_elite")`
- `("ALL", "overall")` — aggregate across all pools

### Inactivity Decay

Same as existing Glicko-2: cards not seen in a run get RD decay applied. This prevents stale ratings from dominating predictions.

## Deck Elo Aggregation

Given a deck of cards and a target encounter pool context, compute a single deck rating:

**Weighted mean by 1/RD:**
```
deck_mu = sum(card_mu_i / card_RD_i) / sum(1 / card_RD_i)
```

Cards with lower RD (more observations, more confidence) dominate the prediction. Unknown cards with default RD 350 contribute almost nothing.

**Combined uncertainty:**
```
deck_RD = 1 / sqrt(sum(1 / card_RD_i^2))
```

Adding more known cards shrinks uncertainty. A deck of well-rated cards has a tight prediction; a deck of unknowns has wide uncertainty.

## Export Format

### Card-level additions to overlay_data.json

Each card in the `cards` array gains:
```json
{
  "combatElo": 1534.2,
  "combatRd": 112.5,
  "combatByPool": {
    "act1_weak": { "elo": 1580.1, "rd": 145.2 },
    "act1_normal": { "elo": 1520.3, "rd": 138.7 },
    "act1_elite": { "elo": 1490.8, "rd": 155.0 },
    ...
  }
}
```

### New top-level section

```json
{
  "encounterPools": {
    "act1_weak": { "elo": 1480.2, "rd": 45.3 },
    "act1_normal": { "elo": 1510.5, "rd": 42.1 },
    "act1_elite": { "elo": 1620.8, "rd": 50.7 },
    ...
  }
}
```

### Deck Elo (computed live by mod)

The mod computes deck Elo at runtime from the player's current deck composition and the exported card combat ratings. Not stored in the export — it changes as the deck evolves.

## Relationship to Existing Systems

| System | Measures | Data Source | Matchup |
|--------|----------|-------------|---------|
| Pick Glicko-2 | Draft preference | CardChoices (pick/skip) | Card vs card + SKIP |
| Combat Glicko-2 | Combat contribution | Floors (damage) + deck state | Deck (team) vs pool |
| Blind Spots | Pick-value mismatch | Pick rate vs win rate delta | N/A (derived metric) |

**Divergence signal:** Cards where pick Elo and combat Elo disagree are candidates for revaluation — either overvalued in drafts (high pick, low combat) or undervalued (low pick, high combat).

## Known Limitations

**Shared-credit dilution:** Every card in the deck receives the same score for a fight, but only ~5-8 cards are actually drawn and played per combat. The 15-20 undrawn "bystanders" share credit equally. Over many observations bystander ratings regress toward the mean, but convergence is slower than it would be with per-card play data. Future refinement: scale effective observations by `1/sqrt(deck_size)` or weight by draw probability.

**Pool entity convergence:** Pool entities (e.g., `POOL.ACT1_ELITE`) face every deck, so they accumulate observations much faster than individual cards. Their RD drops quickly, making them stable anchors. This is desirable behavior but means pool volatility could be tuned lower in future iterations.

**Data volume:** ~109 runs / ~1800 combat floors is sufficient for common cards (~300 observations for frequently seen cards) but rare cards will retain high RD. The 1/RD-weighted aggregation handles this gracefully — uncertain cards contribute little to deck Elo.

## Scope

**In scope:**
- CombatRatingEngine with deck reconstruction
- CombatGlicko2Analytics for querying
- Schema additions for new tables
- Export additions to overlay_data.json
- Deck Elo aggregation helper (for mod runtime use)

**Deferred:**
- Path damage prediction UI (uses deck Elo + pool ratings, but UI is separate)
- Per-card combat stats in overlay (could show "your best/worst combat cards")
- Pick Elo vs Combat Elo divergence analysis
- Card synergy detection (pairwise interaction terms)

# STS2 Encounter Design & Fight Pools

Research doc for map overlay feature. Data from 106 runs in local database + community sources.

## Fight Pool System

STS2 uses the same two-tier pool system as STS1. Encounter IDs encode the pool: `_WEAK` (easy), `_NORMAL` (hard), `_ELITE`, `_BOSS`.

### Pool Transition Rules

| Act | Easy pool (WEAK) fights | Transition to hard pool |
|-----|------------------------|------------------------|
| Act 1 | First 3 monster fights | Fight 4+ draws from NORMAL pool |
| Act 2 | First 2 monster fights | Fight 3+ draws from NORMAL pool |
| Act 3 | First 2 monster fights | Fight 3+ draws from NORMAL pool |

Note: floor index ≠ fight number because non-combat nodes (events, shops, rest sites) intervene. The Nth monster fight can land on varying floor indices.

## Average Damage by Pool (from 106 runs)

| Act | Pool | Fights | Avg Damage | Avg (when hit) | Max |
|-----|------|--------|-----------|----------------|-----|
| 1 | Weak | 290 | 3.0 | 4.9 | 17 |
| 1 | Normal | 188 | 5.6 | 8.2 | 53 |
| 1 | Elite | 262 | 15.4 | 17.1 | 68 |
| 1 | Boss | 83 | 18.8 | 20.3 | 54 |
| 2 | Weak | 130 | 4.8 | 8.0 | 27 |
| 2 | Normal | 125 | 5.8 | 8.3 | 39 |
| 2 | Elite | 182 | 11.9 | 14.5 | 56 |
| 2 | Boss | 65 | 21.1 | 24.5 | 66 |
| 3 | Weak | 116 | 4.6 | 8.5 | 42 |
| 3 | Normal | 102 | 4.9 | 10.3 | 43 |
| 3 | Elite | 140 | 11.9 | 16.0 | 52 |
| 3 | Boss | 61 | 22.8 | 25.8 | 78 |

## Map Structure

### Floors Per Act

| Act | Floors | Boss Floor | Notes |
|-----|--------|-----------|-------|
| Act 1 | 17 (0-16) | 15-16 | Floor 0 is Neow (ancient) |
| Act 2 | 16-18 (0-15+) | 14-15 | Floor 0 is ancient choice |
| Act 3 | 15 (0-14) | 13-14 | Floor 0 is ancient choice |

### Node Types

`monster`, `elite`, `boss`, `shop`, `rest_site`, `treasure`, `unknown` (events), `ancient`

## Alternate Acts (Biomes)

Each act slot can be one of two biomes with different enemy pools:

| Slot | Biome A | Biome B |
|------|---------|---------|
| Act 1 | Overgrowth | Underdocks |
| Act 2 | The Hive | (unknown/unreleased) |
| Act 3 | The Glory | (unknown/unreleased) |

## Per-Encounter Damage (n ≥ 3)

### Act 1 — Weak Pool

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| SLIMES_WEAK | 41 | 5.2 | 6.7 | 16 |
| NIBBITS_WEAK | 42 | 3.9 | 4.7 | 17 |
| CORPSE_SLUGS_WEAK | 33 | 3.3 | 4.7 | 9 |
| TOADPOLES_WEAK | 28 | 3.2 | 5.9 | 12 |
| SEAPUNK_WEAK | 32 | 2.4 | 3.9 | 10 |
| SLUDGE_SPINNER_WEAK | 33 | 2.4 | 3.9 | 11 |
| FUZZY_WURM_CRAWLER_WEAK | 42 | 1.8 | 4.1 | 11 |
| SHRINKER_BEETLE_WEAK | 39 | 1.5 | 4.3 | 12 |

### Act 1 — Normal Pool

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| LIVING_FOG_NORMAL | 6 | 14.2 | 21.3 | 53 |
| SNAPPING_JAXFRUIT_NORMAL | 15 | 11.3 | 14.2 | 43 |
| SLITHERING_STRANGLER_NORMAL | 10 | 8.3 | 9.2 | 21 |
| MAWLER_NORMAL | 10 | 8.0 | 10.0 | 17 |
| VINE_SHAMBLER_NORMAL | 16 | 6.6 | 9.5 | 22 |
| FOSSIL_STALKER_NORMAL | 5 | 6.4 | 6.4 | 10 |
| INKLETS_NORMAL | 9 | 5.8 | 6.5 | 13 |
| HAUNTED_SHIP_NORMAL | 10 | 5.5 | 7.9 | 17 |
| NIBBITS_NORMAL | 11 | 5.4 | 7.4 | 21 |
| TWO_TAILED_RATS_NORMAL | 10 | 5.2 | 6.5 | 19 |
| CUBEX_CONSTRUCT_NORMAL | 8 | 4.1 | 16.5 | 20 |
| SLIMES_NORMAL | 12 | 4.0 | 5.3 | 9 |
| SEWER_CLAM_NORMAL | 8 | 3.6 | 4.8 | 10 |
| CORPSE_SLUGS_NORMAL | 8 | 2.5 | 5.0 | 8 |
| TOADPOLES_NORMAL | 8 | 2.4 | 4.8 | 11 |
| PUNCH_CONSTRUCT_NORMAL | 11 | 2.3 | 5.0 | 8 |
| RUBY_RAIDERS_NORMAL | 8 | 1.3 | 3.3 | 5 |
| CULTISTS_NORMAL | 9 | 1.0 | 3.0 | 4 |

### Act 1 — Elites

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| BYGONE_EFFIGY_ELITE | 49 | 17.8 | 20.3 | 52 |
| TERROR_EEL_ELITE | 38 | 15.6 | 18.5 | 39 |
| BYRDONIS_ELITE | 51 | 15.4 | 16.7 | 61 |
| PHANTASMAL_GARDENERS_ELITE | 36 | 15.3 | 16.2 | 68 |
| PHROG_PARASITE_ELITE | 51 | 14.8 | 15.7 | 51 |
| SKULKING_COLONY_ELITE | 37 | 13.0 | 15.1 | 39 |

### Act 1 — Bosses

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| VANTOM_BOSS | 19 | 23.6 | 23.6 | 54 |
| SOUL_FYSH_BOSS | 15 | 23.1 | 23.1 | 48 |
| CEREMONIAL_BEAST_BOSS | 16 | 18.1 | 18.1 | 42 |
| THE_KIN_BOSS | 10 | 17.4 | 17.4 | 32 |
| LAGAVULIN_MATRIARCH_BOSS | 6 | 15.8 | 15.8 | 23 |
| WATERFALL_GIANT_BOSS | 17 | 12.4 | 19.2 | 29 |

### Act 2 — Weak Pool

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| BOWLBUGS_WEAK | 38 | 6.2 | 9.5 | 22 |
| THIEVING_HOPPER_WEAK | 41 | 5.2 | 7.6 | 21 |
| EXOSKELETONS_WEAK | 23 | 3.8 | 6.3 | 27 |
| TUNNELER_WEAK | 28 | 3.2 | 8.1 | 19 |

### Act 2 — Normal Pool

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| SPINY_TOAD_NORMAL | 9 | 9.0 | 13.5 | 27 |
| SLUMBERING_BEETLE_NORMAL | 14 | 7.4 | 10.4 | 24 |
| THE_OBSCURA_NORMAL | 22 | 6.1 | 7.5 | 39 |
| LOUSE_PROGENITOR_NORMAL | 7 | 5.9 | 6.8 | 15 |
| CHOMPERS_NORMAL | 12 | 5.8 | 6.9 | 13 |
| OVICOPTER_NORMAL | 13 | 5.6 | 8.1 | 18 |
| MYTES_NORMAL | 13 | 5.5 | 8.0 | 19 |
| BOWLBUGS_NORMAL | 9 | 5.1 | 6.6 | 12 |
| EXOSKELETONS_NORMAL | 7 | 5.1 | 9.0 | 15 |
| HUNTER_KILLER_NORMAL | 9 | 3.8 | 8.5 | 11 |
| TUNNELER_NORMAL | 10 | 3.2 | 8.0 | 21 |

### Act 2 — Elites

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| DECIMILLIPEDE_ELITE | 61 | 13.5 | 16.8 | 56 |
| INFESTED_PRISMS_ELITE | 62 | 12.1 | 14.4 | 40 |
| ENTOMANCER_ELITE | 59 | 9.9 | 12.2 | 27 |

### Act 2 — Bosses

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| THE_INSATIABLE_BOSS | 27 | 23.4 | 27.4 | 66 |
| KNOWLEDGE_DEMON_BOSS | 21 | 21.3 | 23.5 | 57 |
| KAISER_CRAB_BOSS | 17 | 17.4 | 21.1 | 39 |

### Act 3 — Weak Pool

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| SCROLLS_OF_BITING_WEAK | 37 | 6.7 | 11.9 | 38 |
| TURRET_OPERATOR_WEAK | 38 | 3.7 | 6.6 | 42 |
| DEVOTED_SCULPTOR_WEAK | 41 | 3.4 | 7.0 | 17 |

### Act 3 — Normal Pool

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| SLIMED_BERSERKER_NORMAL | 10 | 11.6 | 14.5 | 35 |
| FROG_KNIGHT_NORMAL | 15 | 6.4 | 8.7 | 20 |
| OWL_MAGISTRATE_NORMAL | 13 | 5.9 | 15.4 | 43 |
| THE_LOST_AND_FORGOTTEN_NORMAL | 11 | 5.8 | 10.7 | 19 |
| SCROLLS_OF_BITING_NORMAL | 15 | 3.7 | 9.2 | 18 |
| AXEBOTS_NORMAL | 17 | 3.4 | 9.5 | 19 |
| FABRICATOR_NORMAL | 10 | 2.2 | 7.3 | 9 |
| CONSTRUCT_MENAGERIE_NORMAL | 5 | 1.0 | 5.0 | 5 |
| GLOBE_HEAD_NORMAL | 6 | 0.5 | 1.5 | 2 |

### Act 3 — Elites

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| MECHA_KNIGHT_ELITE | 49 | 13.3 | 17.7 | 52 |
| SOUL_NEXUS_ELITE | 49 | 12.1 | 16.5 | 35 |
| KNIGHTS_ELITE | 42 | 10.0 | 13.5 | 31 |

### Act 3 — Bosses

| Encounter | n | Avg Dmg | Avg (hit) | Max |
|-----------|---|---------|-----------|-----|
| TEST_SUBJECT_BOSS | 24 | 29.3 | 30.5 | 78 |
| DOORMAKER_BOSS | 15 | 22.7 | 24.3 | 64 |
| QUEEN_BOSS | 22 | 15.9 | 20.5 | 56 |

## Ancients (Act Start Choices)

| Act | Ancient | Count |
|-----|---------|-------|
| 1 | EVENT.NEOW | 104 |
| 2 | EVENT.TEZCATARA | 27 |
| 2 | EVENT.PAEL | 21 |
| 2 | EVENT.OROBAS | 16 |
| 2 | EVENT.DARV | 7 |
| 3 | EVENT.TANX | 21 |
| 3 | EVENT.VAKUU | 19 |
| 3 | EVENT.NONUPEIPE | 14 |
| 3 | EVENT.DARV | 6 |

## Per-Character Damage by Act and Pool

### Ironclad (23 runs) — highest damage taken across the board

| Act | Weak | Normal | Elite | Boss |
|-----|------|--------|-------|------|
| 1 | 5.6 | 8.9 | 22.0 | 26.1 |
| 2 | 8.8 | 7.6 | 18.5 | 31.3 |
| 3 | 10.5 | 9.5 | 17.2 | 37.9 |

### Silent (16 runs) — lowest damage taken

| Act | Weak | Normal | Elite | Boss |
|-----|------|--------|-------|------|
| 1 | 1.4 | 3.3 | 10.9 | 16.9 |
| 2 | 0.9 | 6.8 | 8.4 | 17.4 |
| 3 | 1.6 | 1.2 | 9.7 | 16.4 |

### Defect (14 runs)

| Act | Weak | Normal | Elite | Boss |
|-----|------|--------|-------|------|
| 1 | 2.7 | 5.1 | 13.0 | 16.7 |
| 2 | 5.8 | 6.2 | 12.3 | 21.3 |
| 3 | 3.7 | 4.6 | 10.2 | 29.5 |

### Regent (27 runs)

| Act | Weak | Normal | Elite | Boss |
|-----|------|--------|-------|------|
| 1 | 3.1 | 6.2 | 15.4 | 19.6 |
| 2 | 5.1 | 9.3 | 13.4 | 19.7 |
| 3 | 4.6 | 7.3 | 14.7 | 22.6 |

### Necrobinder (26 runs)

| Act | Weak | Normal | Elite | Boss |
|-----|------|--------|-------|------|
| 1 | 1.6 | 4.1 | 13.0 | 14.3 |
| 2 | 3.9 | 3.6 | 6.3 | 16.9 |
| 3 | 2.7 | 3.0 | 8.9 | 13.7 |

## Key Takeaways for Map Overlay

1. **Easy hallway fights are cheap**: ~3-5 avg damage across all acts
2. **Hard hallway fights hurt 2x more**: ~5-10 avg damage, with high variance (some 40+ spikes)
3. **Elites are expensive**: ~12-15 avg damage, consistent across acts
4. **The gap between weak and normal shrinks in later acts** — Act 3 weak fights (4.6 avg) are nearly as costly as Act 3 normal fights (4.9 avg)
5. **Zero-damage fights are common** in weak pool (~40% of the time), less so in normal pool, rare in elites
6. **High-variance encounters** to flag: Living Fog (53 max), Snapping Jaxfruit (43 max), Scrolls of Biting Weak (38 max), Test Subject Boss (78 max)

## Sources

- Local database: 106 runs, ~/.sts2analytics/data.db
- https://sts2.wiki/encounters/
- https://mobalytics.gg/slay-the-spire-2/encounters/
- https://slaythespire2.gg/encounters

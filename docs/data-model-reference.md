# STS2 Analytics Data Model Reference

Quick reference for all data captured from `.run` files into SQLite.

## Run-Level Data (Runs table)

| Field | Type | Notes |
|-------|------|-------|
| Character | string | e.g. IRONCLAD, SILENT, DEFECT, REGENT, NECROBINDER |
| Ascension | int | 0-20 |
| Win | bool | Run victory |
| Abandoned | bool | Run abandoned |
| Seed | string | |
| GameMode | string | e.g. standard |
| BuildVersion | string | |
| StartTime | string | Unix timestamp |
| Duration | double | Seconds |
| ActsCompleted | int | |
| PlatformType | string | e.g. steam |
| Modifiers | string | JSON array |
| MaxPotionSlots | int | |
| KilledBy | string | Encounter/event that killed player |

## Floor-Level Data (Floors table)

| Field | Type | Notes |
|-------|------|-------|
| RunId | FK | |
| ActIndex | int | 0-2 for acts 1-3 |
| FloorIndex | int | Position within act |
| MapPointType | string | monster, shop, rest_site, boss, elite, treasure, unknown, ancient |
| EncounterId | string | e.g. ENCOUNTER.NIBBITS_WEAK |
| RoomType | string | |
| TurnsTaken | int | |
| CurrentHp / MaxHp | int | HP state at floor |
| DamageTaken / HpHealed | int | |
| MaxHpGained / MaxHpLost | int | |
| CurrentGold / GoldGained / GoldSpent / GoldLost / GoldStolen | int | |

## Card Choices (CardChoices table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | Links to floor (→ act, floor index) |
| CardId | string | e.g. CARD.INFLAME |
| WasPicked | bool | |
| WasBought | bool | Shop purchase |
| CurrentUpgradeLevel | int | 0 if not upgraded |
| EnchantmentId | string | e.g. ENCHANTMENT.GLAM |
| EnchantmentAmount | int | Numeric value |
| Source | string | reward, shop, event |

## Cards Gained (CardsGained table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| CardId | string | |
| UpgradeLevel | int | |
| EnchantmentId | string | |
| EnchantmentAmount | int | |
| Source | string | reward, shop, relic, event, transform |

## Card Transforms (CardTransforms table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| OriginalCardId | string | |
| FinalCardId | string | |

## Card Removals (CardRemovals table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| CardId | string | |
| FloorAddedToDeck | int | |

## Card Enchantments (CardEnchantments table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| CardId | string | |
| EnchantmentId | string | |
| EnchantmentAmount | int | |

## Final Deck (FinalDecks table)

| Field | Type | Notes |
|-------|------|-------|
| RunId | FK | |
| CardId | string | |
| FinalUpgradeLevel | int | |
| FloorAddedToDeck | int | |
| FinalEnchantmentId | string | |

## Rest Site Upgrades (RestSiteUpgrades table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| CardId | string | |

## Relic Choices (RelicChoices table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| RelicId | string | |
| WasPicked | bool | |
| WasBought | bool | |
| Source | string | elite, treasure, shop, boss, event, ancient |

## Final Relics (FinalRelics table)

| Field | Type | Notes |
|-------|------|-------|
| RunId | FK | |
| RelicId | string | |
| FloorAcquired | int | |
| Props | string | JSON blob |

## Potion Choices / Events / Final (PotionChoices, PotionEvents, FinalPotions)

Tracks potions offered, picked, used, discarded, and final state with slot indices.

## Event Choices (EventChoices table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| EventId | string | |
| ChoiceKey / ChoiceTable | string | Localization keys |
| Variables | string | JSON blob |

## Ancient Choices (AncientChoices table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| TextKey | string | |
| WasChosen | bool | |

## Monsters (Monsters table)

| Field | Type | Notes |
|-------|------|-------|
| FloorId | FK | |
| MonsterId | string | |

---

## Data NOT Directly Tracked

- **Card rarity/type** — Must be looked up externally via card ID
- **Deck state at choice time** — Reconstructible from CardsGained + CardRemovals + CardTransforms ordered by floor
- **Relics held at choice time** — Reconstructible from FinalRelics.FloorAcquired
- **Co-op vs solo** — Schema only captures players[0]

## Existing Analytics Filters (AnalyticsFilter)

Filterable dimensions: Character, Ascension, DateRange, GameMode, ActIndex

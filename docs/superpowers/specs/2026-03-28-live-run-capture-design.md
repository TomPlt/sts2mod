# Live Run Capture — Design Spec

## Goal

Capture every meaningful in-game action in real-time to a local SQLite database so that any run can be fully reconstructed action-by-action after the fact. This includes combat actions (card plays, damage, block, buffs), event decisions, map choices, and reward picks.

## Approach

A separate SQLite database (`spireoracle_live.db`) in the mod folder, dedicated to real-time event capture. Separate from the existing analytics DB which is populated from `.run` files after runs end.

Harmony patches hook into the game's Hook system and concrete action classes, enqueuing lightweight structs onto a concurrent queue. A background thread drains the queue and writes to SQLite in batches, avoiding frame drops on the game thread.

## Schema

Database location: `mods/SpireOracle/spireoracle_live.db`

```sql
CREATE TABLE IF NOT EXISTS LiveRuns (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Seed TEXT NOT NULL,
    Character TEXT NOT NULL,
    Ascension INTEGER NOT NULL DEFAULT 0,
    StartedAt TEXT NOT NULL,
    EndedAt TEXT,
    Win INTEGER,
    RunFileName TEXT
);

CREATE TABLE IF NOT EXISTS Combats (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
    EncounterId TEXT NOT NULL,
    ActIndex INTEGER NOT NULL,
    FloorIndex INTEGER NOT NULL,
    StartedAt TEXT NOT NULL,
    EndedAt TEXT,
    Won INTEGER
);

CREATE TABLE IF NOT EXISTS Turns (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CombatId INTEGER NOT NULL REFERENCES Combats(Id),
    TurnNumber INTEGER NOT NULL,
    StartingEnergy INTEGER NOT NULL DEFAULT 0,
    StartingHp INTEGER NOT NULL DEFAULT 0,
    StartingBlock INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS CombatActions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TurnId INTEGER NOT NULL REFERENCES Turns(Id),
    Seq INTEGER NOT NULL,
    ActionType TEXT NOT NULL,
    SourceId TEXT,
    TargetId TEXT,
    Amount INTEGER NOT NULL DEFAULT 0,
    Detail TEXT
);

CREATE TABLE IF NOT EXISTS EventDecisions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
    ActIndex INTEGER NOT NULL,
    FloorIndex INTEGER NOT NULL,
    EventId TEXT,
    OptionChosen TEXT,
    Timestamp TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MapChoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
    ActIndex INTEGER NOT NULL,
    FloorIndex INTEGER NOT NULL,
    MapPointType TEXT NOT NULL,
    Timestamp TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS RewardDecisions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
    ActIndex INTEGER NOT NULL,
    FloorIndex INTEGER NOT NULL,
    DecisionType TEXT NOT NULL,
    ItemId TEXT,
    WasPicked INTEGER NOT NULL DEFAULT 0,
    Timestamp TEXT NOT NULL
);
```

Indexes:

```sql
CREATE INDEX IF NOT EXISTS IX_Combats_RunId ON Combats(RunId);
CREATE INDEX IF NOT EXISTS IX_Turns_CombatId ON Turns(CombatId);
CREATE INDEX IF NOT EXISTS IX_CombatActions_TurnId ON CombatActions(TurnId);
CREATE INDEX IF NOT EXISTS IX_EventDecisions_RunId ON EventDecisions(RunId);
CREATE INDEX IF NOT EXISTS IX_MapChoices_RunId ON MapChoices(RunId);
CREATE INDEX IF NOT EXISTS IX_RewardDecisions_RunId ON RewardDecisions(RunId);
```

### CombatActions ActionType Values

| ActionType | SourceId | TargetId | Amount | Detail |
|---|---|---|---|---|
| CARD_PLAYED | card ID | target creature | 0 | `{"upgrade":1}` if upgraded |
| DAMAGE_DEALT | source creature/card | target creature | damage amount | null |
| DAMAGE_TAKEN | source creature/card | target creature | damage amount | null |
| BLOCK_GAINED | source creature/card | creature gaining | block amount | null |
| POWER_APPLIED | source | target creature | stack count | `{"powerId":"..."}` |
| CARD_DRAWN | card ID | null | 0 | null |
| CARD_EXHAUSTED | card ID | null | 0 | null |
| CARD_DISCARDED | card ID | null | 0 | null |
| POTION_USED | potion ID | target creature | 0 | null |
| ENERGY_SPENT | card ID | null | energy cost | null |
| ENEMY_MOVE | enemy ID | null | 0 | `{"intent":"..."}` |

### RewardDecisions DecisionType Values

- `CARD_PICK` — picked a card from reward screen
- `CARD_SKIP` — skipped the card reward
- `RELIC_PICK` — picked a relic
- `ANCIENT_CHOICE` — chose an ancient option
- `REST_SITE` — rest site decision (heal/smith/etc.)

## Harmony Hooks

### Combat Lifecycle

| Game Hook / Method | Captures |
|---|---|
| `Hook.BeforeCombatStart` | Insert `Combats` row |
| `Hook.AfterCombatEnd` / `Hook.AfterCombatVictory` | Mark combat ended, set Won |
| `Hook.AfterPlayerTurnStart` | Insert `Turns` row |
| `Hook.BeforeTurnEnd` | Finalize turn |

### Card Actions

| Game Hook | ActionType |
|---|---|
| `Hook.AfterCardPlayed` | CARD_PLAYED |
| `Hook.AfterCardDrawn` | CARD_DRAWN |
| `Hook.AfterCardDiscarded` | CARD_DISCARDED |
| `Hook.AfterCardExhausted` | CARD_EXHAUSTED |

### Combat Mechanics

| Game Hook | ActionType |
|---|---|
| `Hook.AfterDamageGiven` | DAMAGE_DEALT |
| `Hook.AfterDamageReceived` | DAMAGE_TAKEN |
| `Hook.AfterBlockGained` | BLOCK_GAINED |
| `Hook.AfterPowerAmountChanged` | POWER_APPLIED |
| `Hook.AfterEnergySpent` | ENERGY_SPENT |

### Potions

| Game Hook | ActionType |
|---|---|
| `Hook.AfterPotionUsed` | POTION_USED |

### Out-of-Combat

| Target | Table |
|---|---|
| `EventOption.Chosen` (Harmony patch) | EventDecisions |
| `MoveToMapCoordAction.ExecuteAction` (Harmony patch) | MapChoices |
| Existing `CardRewardPatch` (add enqueue) | RewardDecisions |
| Existing `AncientChoicePatch` (add enqueue) | RewardDecisions |

### Not Hooked (Selective Exclusions)

- Card animations / VFX triggers
- UI transitions
- Orb passive ticks
- Modifier calculation internals
- Block cleared / broken (derived from damage events)

## Background Writer

### Architecture

- `LiveRunDb` static class holds the SQLite connection and background thread
- `ConcurrentQueue<DbAction>` receives events from patches on the game thread
- Dedicated background `Thread` drains the queue and batches writes in transactions
- Flush interval: every 100ms or every 50 queued actions, whichever comes first

### DbAction Struct

```csharp
enum DbActionKind
{
    StartRun, EndRun,
    StartCombat, EndCombat,
    StartTurn,
    CombatAction,
    EventDecision,
    MapChoice,
    RewardDecision
}

readonly record struct DbAction(
    DbActionKind Kind,
    string? Id1,        // contextual string (card ID, event ID, etc.)
    string? Id2,        // second string (target, option, etc.)
    int Amount,         // damage/block/stacks
    int ActIndex,       // current act (for non-combat events)
    int FloorIndex,     // current floor (for non-combat events)
    string? Detail      // optional JSON for extra context
);
```

### Lifecycle

1. `ModEntry.Initialize()` calls `LiveRunDb.Initialize(modPath)` — creates DB, starts writer thread
2. Harmony patches enqueue `DbAction` structs (game thread cost: one queue push)
3. Background thread wakes every 100ms, drains queue, writes batch in a transaction
4. On run end: enqueue `EndRun`, flush
5. On game exit: signal thread to stop, flush remaining, close connection
6. Crash resilience: data loss limited to ~100ms of unflushed actions

### State Tracking

`LiveRunDb` maintains current IDs to avoid round-trips:

- `_currentRunId` — set on StartRun, cleared on EndRun
- `_currentCombatId` — set on StartCombat, cleared on EndCombat
- `_currentTurnId` — set on StartTurn, cleared on EndCombat
- `_actionSeq` — incremented per CombatAction, reset on StartTurn

These are only accessed on the writer thread, so no synchronization needed.

## New Files

| File | Purpose |
|---|---|
| `Data/LiveRunDb.cs` | DB connection, schema init, background writer, enqueue API |
| `Data/LiveRunSchema.cs` | CREATE TABLE SQL for the live DB |
| `Patches/LiveCapture/CombatLifecyclePatch.cs` | Combat start/end, turn start/end |
| `Patches/LiveCapture/CardActionPatch.cs` | Card played/drawn/discarded/exhausted |
| `Patches/LiveCapture/DamageBlockPatch.cs` | Damage dealt/taken, block gained |
| `Patches/LiveCapture/PowerPatch.cs` | Power applied/changed |
| `Patches/LiveCapture/EventPatch.cs` | Event option chosen |
| `Patches/LiveCapture/MapMovePatch.cs` | Map coord selection |

## Modified Files

| File | Change |
|---|---|
| `ModEntry.cs` | Add `LiveRunDb.Initialize()` call in `Initialize()` |
| `Patches/CardRewardPatch.cs` | Add one-liner enqueue for RewardDecisions |
| `Patches/AncientChoicePatch.cs` | Add one-liner enqueue for RewardDecisions |
| `Patches/CombatPatch.cs` | Add one-liner enqueue for combat start context |

## Integration Points

- **DB location:** `mods/SpireOracle/spireoracle_live.db` — alongside existing mod files
- **Cross-reference:** `LiveRuns.RunFileName` links to the `.run` file that the analytics pipeline parses
- **No changes to:** analytics pipeline, overlay system, cloud sync, dashboard
- **Queryable with:** any SQLite tool (DB Browser, sqlite3 CLI, DBeaver, etc.)

## Out of Scope

- CLI commands for querying the live DB
- Dashboard integration
- Live DB upload/sync
- Run replay UI
- Pruning/retention policy for old runs

## Example Queries

Replay a full combat:

```sql
SELECT t.TurnNumber, a.Seq, a.ActionType, a.SourceId, a.TargetId, a.Amount
FROM CombatActions a
JOIN Turns t ON a.TurnId = t.Id
JOIN Combats c ON t.CombatId = c.Id
WHERE c.RunId = ? AND c.EncounterId = 'JAW_WORM'
ORDER BY t.TurnNumber, a.Seq;
```

Full run timeline:

```sql
SELECT 'combat' AS phase, c.FloorIndex, c.EncounterId, c.Won, NULL AS choice
FROM Combats c WHERE c.RunId = ?
UNION ALL
SELECT 'event', e.FloorIndex, e.EventId, NULL, e.OptionChosen
FROM EventDecisions e WHERE e.RunId = ?
UNION ALL
SELECT 'map', m.FloorIndex, m.MapPointType, NULL, NULL
FROM MapChoices m WHERE m.RunId = ?
UNION ALL
SELECT 'reward', r.FloorIndex, r.ItemId, r.WasPicked, r.DecisionType
FROM RewardDecisions r WHERE r.RunId = ?
ORDER BY FloorIndex;
```

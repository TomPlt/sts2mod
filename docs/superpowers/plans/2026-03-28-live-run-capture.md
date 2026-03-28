# Live Run Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture every meaningful in-game action to a local SQLite database so any run is fully reconstructable action-by-action.

**Architecture:** Separate `spireoracle_live.db` written by a background thread. Harmony patches enqueue lightweight structs to a `ConcurrentQueue`; a dedicated writer thread batches them into SQLite transactions. New patches live under `Patches/LiveCapture/`, existing patches get one-liner enqueue calls.

**Tech Stack:** C#/.NET 9.0, Harmony, Microsoft.Data.Sqlite (bundled with mod), SQLite

**Spec:** `docs/superpowers/specs/2026-03-28-live-run-capture-design.md`

---

## File Structure

| File | Responsibility |
|---|---|
| **Create:** `Data/LiveRunSchema.cs` | Schema SQL constants for the live DB |
| **Create:** `Data/LiveRunDb.cs` | Static class: SQLite connection, background writer thread, enqueue API, state tracking |
| **Create:** `Patches/LiveCapture/RunLifecyclePatch.cs` | Hooks for run start/end (detects new run, marks completion) |
| **Create:** `Patches/LiveCapture/CombatLifecyclePatch.cs` | Hooks for combat start/end, turn start |
| **Create:** `Patches/LiveCapture/CardActionPatch.cs` | Hooks for card played/drawn/discarded/exhausted |
| **Create:** `Patches/LiveCapture/DamageBlockPatch.cs` | Hooks for damage dealt/taken, block gained |
| **Create:** `Patches/LiveCapture/PowerPatch.cs` | Hooks for power amount changed |
| **Create:** `Patches/LiveCapture/PotionPatch.cs` | Hooks for potion used |
| **Create:** `Patches/LiveCapture/EventPatch.cs` | Hooks for event option chosen |
| **Create:** `Patches/LiveCapture/MapMovePatch.cs` | Hooks for map coord selection |
| **Modify:** `Sts2Analytics.Mod.csproj` | Add Microsoft.Data.Sqlite dependency |
| **Modify:** `ModEntry.cs` | Initialize LiveRunDb on startup |
| **Modify:** `Patches/CardRewardPatch.cs` | Add enqueue for reward decisions |
| **Modify:** `Patches/AncientChoicePatch.cs` | Add enqueue for ancient choices |
| **Modify:** `.claude/skills/deploy/SKILL.md` | Copy SQLite native libs + DB to game dir |

---

### Task 1: Add SQLite dependency to the mod project

**Files:**
- Modify: `src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj`

- [ ] **Step 1: Add Microsoft.Data.Sqlite package reference**

In `src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj`, add inside the existing `<ItemGroup>` with the DLL references:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.5" />
  </ItemGroup>
```

This must be a separate `<ItemGroup>` from the `<Reference>` elements. The `Private` default is `true` for PackageReferences, so the native SQLite binary (`e_sqlite3.dll`) will be included in the build output.

- [ ] **Step 2: Verify it builds**

Run: `cd /home/tom/projects/sts2mod && dotnet build src/Sts2Analytics.Mod -c Release`
Expected: Build succeeds. Check that `src/Sts2Analytics.Mod/bin/Release/net9.0/` contains `Microsoft.Data.Sqlite.dll` and a `runtimes/` folder with native SQLite libs.

- [ ] **Step 3: Verify native libs exist**

Run: `find src/Sts2Analytics.Mod/bin/Release/net9.0/runtimes -name "*sqlite*" -o -name "*e_sqlite3*" | head -10`
Expected: At least one native library file (e.g., `e_sqlite3.dll` or `libe_sqlite3.so`).

If native libs are NOT found, the `SQLitePCLRaw.bundle_e_sqlite3` transitive dependency may need to be explicit. Add it:
```xml
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.11" />
```

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj
git commit -m "feat: add Microsoft.Data.Sqlite dependency to mod project"
```

---

### Task 2: Create LiveRunSchema.cs

**Files:**
- Create: `src/Sts2Analytics.Mod/Data/LiveRunSchema.cs`

- [ ] **Step 1: Create the schema file**

Create `src/Sts2Analytics.Mod/Data/LiveRunSchema.cs`:

```csharp
namespace SpireOracle.Data;

public static class LiveRunSchema
{
    public const string Sql = """
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

        CREATE INDEX IF NOT EXISTS IX_Combats_RunId ON Combats(RunId);
        CREATE INDEX IF NOT EXISTS IX_Turns_CombatId ON Turns(CombatId);
        CREATE INDEX IF NOT EXISTS IX_CombatActions_TurnId ON CombatActions(TurnId);
        CREATE INDEX IF NOT EXISTS IX_EventDecisions_RunId ON EventDecisions(RunId);
        CREATE INDEX IF NOT EXISTS IX_MapChoices_RunId ON MapChoices(RunId);
        CREATE INDEX IF NOT EXISTS IX_RewardDecisions_RunId ON RewardDecisions(RunId);
        """;
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/Data/LiveRunSchema.cs
git commit -m "feat: add live run capture SQLite schema"
```

---

### Task 3: Create LiveRunDb.cs — enqueue API and background writer

**Files:**
- Create: `src/Sts2Analytics.Mod/Data/LiveRunDb.cs`

- [ ] **Step 1: Create the LiveRunDb static class**

Create `src/Sts2Analytics.Mod/Data/LiveRunDb.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Data.Sqlite;
using SpireOracle.UI;

namespace SpireOracle.Data;

public enum DbActionKind
{
    StartRun, EndRun,
    StartCombat, EndCombat,
    StartTurn,
    CombatAction,
    EventDecision,
    MapChoice,
    RewardDecision
}

public readonly record struct DbAction(
    DbActionKind Kind,
    string? Id1,
    string? Id2,
    int Amount,
    int ActIndex,
    int FloorIndex,
    string? Detail
);

public static class LiveRunDb
{
    private static SqliteConnection? _conn;
    private static readonly ConcurrentQueue<DbAction> _queue = new();
    private static Thread? _writerThread;
    private static volatile bool _running;
    private static readonly ManualResetEventSlim _signal = new(false);

    // State tracking — only accessed on writer thread
    private static long _currentRunId;
    private static long _currentCombatId;
    private static long _currentTurnId;
    private static int _actionSeq;

    public static bool IsInitialized => _conn != null;

    public static void Initialize(string modPath)
    {
        try
        {
            var dbPath = System.IO.Path.Combine(modPath, "spireoracle_live.db");
            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();

            using var schemaCmd = _conn.CreateCommand();
            schemaCmd.CommandText = LiveRunSchema.Sql;
            schemaCmd.ExecuteNonQuery();

            _running = true;
            _writerThread = new Thread(WriterLoop)
            {
                Name = "SpireOracle-LiveDb",
                IsBackground = true
            };
            _writerThread.Start();

            DebugLogOverlay.Log("[SpireOracle] Live capture DB initialized");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Live capture DB init failed: {ex.Message}");
            _conn = null;
        }
    }

    public static void Enqueue(DbAction action)
    {
        if (!_running) return;
        _queue.Enqueue(action);
        _signal.Set();
    }

    public static void Shutdown()
    {
        _running = false;
        _signal.Set();
        _writerThread?.Join(timeout: TimeSpan.FromSeconds(5));
        _conn?.Close();
        _conn?.Dispose();
        _conn = null;
    }

    private static void WriterLoop()
    {
        while (_running)
        {
            _signal.Wait(TimeSpan.FromMilliseconds(100));
            _signal.Reset();
            DrainQueue();
        }
        // Final drain on shutdown
        DrainQueue();
    }

    private static void DrainQueue()
    {
        if (_conn == null || _queue.IsEmpty) return;

        try
        {
            using var tx = _conn.BeginTransaction();
            int count = 0;
            while (_queue.TryDequeue(out var action) && count < 200)
            {
                ProcessAction(action, tx);
                count++;
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Live DB write error: {ex.Message}");
        }
    }

    private static void ProcessAction(DbAction a, SqliteTransaction tx)
    {
        switch (a.Kind)
        {
            case DbActionKind.StartRun:
                ExecuteInsert(tx,
                    "INSERT INTO LiveRuns (Seed, Character, Ascension, StartedAt) VALUES (@p1, @p2, @p3, @p4)",
                    a.Id1 ?? "", a.Id2 ?? "", a.Amount, Now());
                _currentRunId = LastInsertRowId(tx);
                _currentCombatId = 0;
                _currentTurnId = 0;
                break;

            case DbActionKind.EndRun:
                if (_currentRunId > 0)
                {
                    ExecuteUpdate(tx,
                        "UPDATE LiveRuns SET EndedAt = @p1, Win = @p2, RunFileName = @p3 WHERE Id = @p4",
                        Now(), a.Amount, a.Id1, _currentRunId);
                    _currentRunId = 0;
                }
                break;

            case DbActionKind.StartCombat:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO Combats (RunId, EncounterId, ActIndex, FloorIndex, StartedAt) VALUES (@p1, @p2, @p3, @p4, @p5)",
                        _currentRunId, a.Id1 ?? "", a.ActIndex, a.FloorIndex, Now());
                    _currentCombatId = LastInsertRowId(tx);
                    _currentTurnId = 0;
                    _actionSeq = 0;
                }
                break;

            case DbActionKind.EndCombat:
                if (_currentCombatId > 0)
                {
                    ExecuteUpdate(tx,
                        "UPDATE Combats SET EndedAt = @p1, Won = @p2 WHERE Id = @p3",
                        Now(), a.Amount, _currentCombatId);
                    _currentCombatId = 0;
                    _currentTurnId = 0;
                }
                break;

            case DbActionKind.StartTurn:
                if (_currentCombatId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO Turns (CombatId, TurnNumber, StartingEnergy, StartingHp, StartingBlock) VALUES (@p1, @p2, @p3, @p4, @p5)",
                        _currentCombatId, a.Amount, a.ActIndex, a.FloorIndex, 0);
                    _currentTurnId = LastInsertRowId(tx);
                    _actionSeq = 0;
                }
                break;

            case DbActionKind.CombatAction:
                if (_currentTurnId > 0)
                {
                    _actionSeq++;
                    ExecuteInsert(tx,
                        "INSERT INTO CombatActions (TurnId, Seq, ActionType, SourceId, TargetId, Amount, Detail) VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                        _currentTurnId, _actionSeq, a.Detail ?? "", a.Id1, a.Id2, a.Amount, null);
                }
                break;

            case DbActionKind.EventDecision:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO EventDecisions (RunId, ActIndex, FloorIndex, EventId, OptionChosen, Timestamp) VALUES (@p1, @p2, @p3, @p4, @p5, @p6)",
                        _currentRunId, a.ActIndex, a.FloorIndex, a.Id1, a.Id2, Now());
                }
                break;

            case DbActionKind.MapChoice:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO MapChoices (RunId, ActIndex, FloorIndex, MapPointType, Timestamp) VALUES (@p1, @p2, @p3, @p4, @p5)",
                        _currentRunId, a.ActIndex, a.FloorIndex, a.Id1 ?? "", Now());
                }
                break;

            case DbActionKind.RewardDecision:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO RewardDecisions (RunId, ActIndex, FloorIndex, DecisionType, ItemId, WasPicked, Timestamp) VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                        _currentRunId, a.ActIndex, a.FloorIndex, a.Detail ?? "", a.Id1, a.Amount, Now());
                }
                break;
        }
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    private static void ExecuteInsert(SqliteTransaction tx, string sql, params object?[] args)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i + 1}", args[i] ?? DBNull.Value);
        }
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteUpdate(SqliteTransaction tx, string sql, params object?[] args)
    {
        ExecuteInsert(tx, sql, args); // Same implementation, different name for clarity
    }

    private static long LastInsertRowId(SqliteTransaction tx)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }
}
```

**Key design notes:**
- `CombatAction` uses `Detail` field for the `ActionType` string (CARD_PLAYED, DAMAGE_DEALT, etc.) to avoid adding another field to the struct. The struct's `Id1`/`Id2`/`Amount` carry the card/target/value.
- `StartTurn` repurposes `Amount` for turn number, `ActIndex` for starting energy, `FloorIndex` for starting HP. These are packed into the existing struct fields to avoid allocations.
- WAL mode + NORMAL sync for best write performance without corruption risk.

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/Data/LiveRunDb.cs
git commit -m "feat: add LiveRunDb background writer with concurrent queue"
```

---

### Task 4: Wire LiveRunDb into ModEntry

**Files:**
- Modify: `src/Sts2Analytics.Mod/ModEntry.cs`

- [ ] **Step 1: Add LiveRunDb.Initialize call**

In `ModEntry.cs`, after the line `DebugLogOverlay.Log($"[SpireOracle] Mod path: {_modPath}");` (line 65), add:

```csharp
        // Initialize live run capture DB
        LiveRunDb.Initialize(_modPath);
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/ModEntry.cs
git commit -m "feat: initialize live capture DB on mod startup"
```

---

### Task 5: Run lifecycle patches (run start/end)

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/RunLifecyclePatch.cs`

The game's `RunManager` controls run lifecycle. We need to detect when a new run starts and when it ends.

- [ ] **Step 1: Create RunLifecyclePatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/RunLifecyclePatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Detects new run start by patching RunManager.InitializeNewRun.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.InitializeNewRun))]
public static class RunStartPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return;

            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            if (state == null) return;

            var seed = state.Seed?.ToString() ?? "";
            var player = state.Players?.Count > 0 ? state.Players[0] : null;
            var character = player?.Character?.ToString() ?? "";
            var spaceIdx = character.IndexOf(' ');
            if (spaceIdx > 0) character = character.Substring(0, spaceIdx);

            var ascension = state.Ascension;

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.StartRun,
                Id1: seed,
                Id2: character,
                Amount: ascension,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] Live capture: run started ({character} A{ascension})");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunStartPatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Detects run end by patching RunManager.EndRun.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EndRun))]
public static class RunEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var runManager = RunManager.Instance;
            var state = runManager != null
                ? Traverse.Create(runManager).Property("State").GetValue<RunState>()
                : null;

            var win = state?.Win == true ? 1 : 0;

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.EndRun,
                Id1: null, // RunFileName set later if available
                Id2: null,
                Amount: win,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] Live capture: run ended (win={win})");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunEndPatch error: {ex.Message}");
        }
    }
}
```

**Note:** The exact method names (`InitializeNewRun`, `EndRun`) need to be verified against the decompiled types. If they don't exist as public methods, we'll need to find the actual method names. Check `all_types.txt` for `RunManager` methods:

Run: `grep "RunManager\." all_types.txt | head -20`

If `InitializeNewRun` doesn't exist, look for alternatives like `StartRun`, `BeginRun`, or patch `RunState` constructor instead.

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS (or compile errors if method names are wrong — fix them based on the grep results)

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/LiveCapture/RunLifecyclePatch.cs
git commit -m "feat: add run start/end live capture patches"
```

---

### Task 6: Combat lifecycle patches (combat start/end, turn start)

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/CombatLifecyclePatch.cs`

- [ ] **Step 1: Create CombatLifecyclePatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/CombatLifecyclePatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures combat start. Piggybacks on the same method as CombatPatch.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.AfterCombatRoomLoaded))]
public static class LiveCombatStartPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;

            var state = cm.DebugOnlyGetState();
            if (state == null) return;

            var encounter = state.Encounter;
            if (encounter == null) return;

            var encounterId = encounter.ToString() ?? "";
            var spaceIdx = encounterId.IndexOf(' ');
            if (spaceIdx > 0) encounterId = encounterId.Substring(0, spaceIdx);

            var actIndex = state.RunState?.CurrentActIndex ?? 0;
            var floorIndex = state.RunState?.CurrentFloorIndex ?? 0;

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.StartCombat,
                Id1: encounterId,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] Live capture: combat started ({encounterId})");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveCombatStartPatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Captures combat end via CombatManager.Reset.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public static class LiveCombatEndPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            // Check if we won before the state is cleared
            var cm = CombatManager.Instance;
            var state = cm?.DebugOnlyGetState();
            var won = state?.IsVictory == true ? 1 : 0;

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.EndCombat,
                Id1: null,
                Id2: null,
                Amount: won,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveCombatEndPatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Captures turn start. Patches CombatManager.SetupPlayerTurn or StartTurn.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.StartTurn))]
public static class LiveTurnStartPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;

            var state = cm.DebugOnlyGetState();
            if (state == null) return;

            var turnNumber = state.TurnNumber;

            // Get player HP/energy from the local player
            var runManager = RunManager.Instance;
            var runState = state.RunState as RunState;
            var player = InputPatch.GetLocalPlayer(runManager, runState);

            var hp = player?.CurrentHp ?? 0;
            var energy = player?.CurrentEnergy ?? 0;

            // StartTurn repurposes: Amount=turnNumber, ActIndex=energy, FloorIndex=hp
            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.StartTurn,
                Id1: null,
                Id2: null,
                Amount: turnNumber,
                ActIndex: energy,
                FloorIndex: hp,
                Detail: null
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveTurnStartPatch error: {ex.Message}");
        }
    }
}
```

**Note:** Some property names (`TurnNumber`, `IsVictory`, `CurrentFloorIndex`, `CurrentHp`, `CurrentEnergy`) need verification against the decompiled game types. Use `Traverse` if properties are not directly accessible. Check:

Run: `grep "CombatState\." all_types.txt | head -20` and `grep "Player\." all_types.txt | grep -i "hp\|energy\|turn" | head -10`

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS (fix property names if needed based on actual types)

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/LiveCapture/CombatLifecyclePatch.cs
git commit -m "feat: add combat/turn lifecycle live capture patches"
```

---

### Task 7: Card action patches (played/drawn/discarded/exhausted)

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/CardActionPatch.cs`

These hook into the Hook system's async dispatchers. The game calls `Hook.AfterCardPlayed(card)` etc. We patch the concrete method that dispatches these hooks.

- [ ] **Step 1: Identify the correct patch targets**

The Hook system uses async methods. We need to find where they're called from. The most reliable approach is to patch `CardModel.OnPlayWrapper` (called when any card is played) and the card pile management for draw/discard/exhaust.

Run: `grep "CardModel\.\|CardPile\.\|Hook\.After" all_types.txt | grep -i "play\|draw\|discard\|exhaust" | head -20`

This will identify the exact method signatures to patch.

- [ ] **Step 2: Create CardActionPatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/CardActionPatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures card played events via CardModel.OnPlayWrapper.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
public static class LiveCardPlayedPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardModel __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var cardId = __instance.Id.ToString() ?? "";
            var spaceIdx = cardId.IndexOf(' ');
            if (spaceIdx > 0) cardId = cardId.Substring(0, spaceIdx);

            var upgrade = __instance.CurrentUpgradeLevel;
            var fullCardId = upgrade > 0 ? $"{cardId}+{upgrade}" : cardId;

            // Try to get target info
            string? targetId = null;
            try
            {
                var target = __instance.CurrentTarget;
                if (target != null)
                {
                    targetId = target.ToString() ?? "";
                    var tSpace = targetId.IndexOf(' ');
                    if (tSpace > 0) targetId = targetId.Substring(0, tSpace);
                }
            }
            catch { }

            var detail = upgrade > 0 ? $"{{\"upgrade\":{upgrade}}}" : null;

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.CombatAction,
                Id1: fullCardId,
                Id2: targetId,
                Amount: 0,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: "CARD_PLAYED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveCardPlayedPatch error: {ex.Message}");
        }
    }
}
```

**Note on CARD_DRAWN, CARD_DISCARDED, CARD_EXHAUSTED:** These require finding the right methods to patch. The card pile transitions are managed by `CardPile` or similar types. After identifying the correct methods in Step 1, add additional `[HarmonyPatch]` classes following the same pattern:

```csharp
// Add these once the correct method names are identified:
// LiveCardDrawnPatch — patches the method that adds a card to the hand
// LiveCardDiscardedPatch — patches the method that moves a card to discard pile
// LiveCardExhaustedPatch — patches the method that moves a card to exhaust pile
```

Each follows the same pattern: extract card ID, enqueue a `DbAction` with `Kind: DbActionKind.CombatAction` and `Detail: "CARD_DRAWN"` (or DISCARDED/EXHAUSTED).

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/LiveCapture/CardActionPatch.cs
git commit -m "feat: add card played live capture patch"
```

---

### Task 8: Damage and block patches

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/DamageBlockPatch.cs`

- [ ] **Step 1: Identify patch targets for damage/block**

Run: `grep "CreatureCmd\.\|Creature\." all_types.txt | grep -i "damage\|block\|hp\|heal" | head -20`

The damage pipeline likely goes through `CreatureCmd.TakeDamage` or similar. Block goes through `CreatureCmd.GainBlock`.

- [ ] **Step 2: Create DamageBlockPatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/DamageBlockPatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures block gained events via CreatureCmd.GainBlock.
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock))]
public static class LiveBlockGainedPatch
{
    [HarmonyPostfix]
    public static void Postfix(object __instance, int amount)
    {
        if (!LiveRunDb.IsInitialized || amount <= 0) return;

        try
        {
            var creatureId = __instance?.ToString() ?? "";
            var spaceIdx = creatureId.IndexOf(' ');
            if (spaceIdx > 0) creatureId = creatureId.Substring(0, spaceIdx);

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.CombatAction,
                Id1: null,
                Id2: creatureId,
                Amount: amount,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: "BLOCK_GAINED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveBlockGainedPatch error: {ex.Message}");
        }
    }
}
```

**Note:** Damage patches need more investigation into the game's damage pipeline. The exact method signature for `TakeDamage` / `DealDamage` depends on what `CreatureCmd` exposes. After identifying the correct methods (Step 1), add:

```csharp
// LiveDamageDealtPatch — patches the method where damage is dealt to a creature
// Capture: source creature, target creature, damage amount after modifiers
// Use Detail: "DAMAGE_DEALT" for player dealing, "DAMAGE_TAKEN" for player receiving
```

The distinction between DAMAGE_DEALT and DAMAGE_TAKEN can be determined by checking if the target is a player or enemy.

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/LiveCapture/DamageBlockPatch.cs
git commit -m "feat: add damage/block live capture patches"
```

---

### Task 9: Power and potion patches

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/PowerPatch.cs`
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/PotionPatch.cs`

- [ ] **Step 1: Identify patch targets**

Run: `grep "PowerModel\.\|Power\." all_types.txt | grep -i "apply\|add\|change\|amount" | head -15`
Run: `grep "Potion\.\|PotionModel\." all_types.txt | grep -i "use\|drink\|apply" | head -10`

- [ ] **Step 2: Create PowerPatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/PowerPatch.cs`:

```csharp
using System;
using HarmonyLib;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

// Patch target TBD based on Step 1 results.
// Expected pattern: patch the method that applies/changes power stacks on a creature.
//
// Template:
// [HarmonyPatch(typeof(TargetType), nameof(TargetType.MethodName))]
// public static class LivePowerAppliedPatch
// {
//     [HarmonyPostfix]
//     public static void Postfix(/* params from method signature */)
//     {
//         if (!LiveRunDb.IsInitialized) return;
//         try
//         {
//             var powerId = /* extract power ID */;
//             var targetId = /* extract target creature ID */;
//             var amount = /* extract stack count */;
//             LiveRunDb.Enqueue(new DbAction(
//                 DbActionKind.CombatAction,
//                 Id1: powerId,
//                 Id2: targetId,
//                 Amount: amount,
//                 ActIndex: 0,
//                 FloorIndex: 0,
//                 Detail: "POWER_APPLIED"
//             ));
//         }
//         catch (Exception ex)
//         {
//             DebugLogOverlay.LogErr($"[SpireOracle] LivePowerAppliedPatch error: {ex.Message}");
//         }
//     }
// }
```

**Important:** This is a template. The actual patch target must be determined from the decompiled types. The power system in STS2 likely uses a method like `Creature.ApplyPower(PowerModel, int amount)` or similar. The patch should capture: power ID, target creature, stack amount.

- [ ] **Step 3: Create PotionPatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/PotionPatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures potion use via the UsePotionAction or NetUsePotionAction.
/// </summary>
[HarmonyPatch(typeof(NetUsePotionAction), nameof(NetUsePotionAction.ExecuteAction))]
public static class LivePotionUsedPatch
{
    [HarmonyPostfix]
    public static void Postfix(NetUsePotionAction __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            // Extract potion ID from the action instance using Traverse
            var potionId = Traverse.Create(__instance).Field("_potionId").GetValue<string>() ?? "";
            var spaceIdx = potionId.IndexOf(' ');
            if (spaceIdx > 0) potionId = potionId.Substring(0, spaceIdx);

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.CombatAction,
                Id1: potionId,
                Id2: null,
                Amount: 0,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: "POTION_USED"
            ));

            DebugLogOverlay.Log($"[SpireOracle] Live capture: potion used ({potionId})");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LivePotionUsedPatch error: {ex.Message}");
        }
    }
}
```

**Note:** The field name `_potionId` and the exact action type need verification. Check decompiled types for the actual fields on `NetUsePotionAction`.

- [ ] **Step 4: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS (commented-out power patch won't cause issues)

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/LiveCapture/PowerPatch.cs src/Sts2Analytics.Mod/Patches/LiveCapture/PotionPatch.cs
git commit -m "feat: add power and potion live capture patches"
```

---

### Task 10: Event decision and map move patches

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/EventPatch.cs`
- Create: `src/Sts2Analytics.Mod/Patches/LiveCapture/MapMovePatch.cs`

- [ ] **Step 1: Create EventPatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/EventPatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures event option chosen via EventOption.Chosen.
/// </summary>
[HarmonyPatch(typeof(EventOption), nameof(EventOption.Chosen))]
public static class LiveEventChosenPatch
{
    [HarmonyPostfix]
    public static void Postfix(EventOption __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var textKey = __instance.TextKey ?? "";
            var optionChosen = textKey;
            if (optionChosen.Contains('.'))
                optionChosen = optionChosen.Substring(optionChosen.LastIndexOf('.') + 1);

            // Get event ID from the parent event model if available
            string? eventId = null;
            try
            {
                var eventModel = Traverse.Create(__instance).Property("Event").GetValue<object>();
                eventId = eventModel?.ToString() ?? "";
                var spaceIdx = eventId.IndexOf(' ');
                if (spaceIdx > 0) eventId = eventId.Substring(0, spaceIdx);
            }
            catch { }

            // Get act/floor from run state
            var (actIndex, floorIndex) = GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.EventDecision,
                Id1: eventId,
                Id2: optionChosen,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] Live capture: event choice ({eventId} -> {optionChosen})");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveEventChosenPatch error: {ex.Message}");
        }
    }

    internal static (int actIndex, int floorIndex) GetRunPosition()
    {
        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return (0, 0);
            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            if (state == null) return (0, 0);
            return (state.CurrentActIndex, state.CurrentFloorIndex);
        }
        catch
        {
            return (0, 0);
        }
    }
}
```

**Note:** `EventOption.Chosen` is an async method. Harmony can patch async methods but the postfix runs when the method *starts*, not when the awaited task completes. This is fine for our use case — we just need to know the option was chosen, not what happened after.

If `EventOption.Chosen` is not directly patchable (e.g., it's an interface method), fall back to patching `NEventRoom.BeforeOptionChosen` instead.

- [ ] **Step 2: Create MapMovePatch.cs**

Create `src/Sts2Analytics.Mod/Patches/LiveCapture/MapMovePatch.cs`:

```csharp
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures map movement via MoveToMapCoordAction.
/// </summary>
[HarmonyPatch(typeof(MoveToMapCoordAction), nameof(MoveToMapCoordAction.GoToMapCoord))]
public static class LiveMapMovePatch
{
    [HarmonyPostfix]
    public static void Postfix(MoveToMapCoordAction __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            // Extract map point type from the action
            string mapPointType = "";
            try
            {
                var coord = Traverse.Create(__instance).Field("_coord").GetValue<object>();
                mapPointType = coord?.ToString() ?? "";
            }
            catch { }

            var (actIndex, floorIndex) = LiveEventChosenPatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                DbActionKind.MapChoice,
                Id1: mapPointType,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: null
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] LiveMapMovePatch error: {ex.Message}");
        }
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/LiveCapture/EventPatch.cs src/Sts2Analytics.Mod/Patches/LiveCapture/MapMovePatch.cs
git commit -m "feat: add event decision and map move live capture patches"
```

---

### Task 11: Add reward decision enqueues to existing patches

**Files:**
- Modify: `src/Sts2Analytics.Mod/Patches/CardRewardPatch.cs`
- Modify: `src/Sts2Analytics.Mod/Patches/AncientChoicePatch.cs`

- [ ] **Step 1: Add enqueue to CardRewardPatch**

In `CardRewardPatch.cs`, inside the `foreach` loop where cards are processed, after the line `OverlayFactory.AddOverlay(holder, stats, DataLoader.SkipElo);` (line 54), add:

```csharp
                    // Live capture: reward decision
                    if (LiveRunDb.IsInitialized)
                    {
                        var (_, character2, actIdx) = ReadLiveDeck();
                        var floorIdx = 0;
                        try
                        {
                            var rm = RunManager.Instance;
                            var rs = rm != null ? Traverse.Create(rm).Property("State").GetValue<RunState>() : null;
                            floorIdx = rs?.CurrentFloorIndex ?? 0;
                        }
                        catch { }

                        LiveRunDb.Enqueue(new DbAction(
                            DbActionKind.RewardDecision,
                            Id1: fullCardId,
                            Id2: null,
                            Amount: 0, // WasPicked — we don't know yet at offer time
                            ActIndex: actIdx,
                            FloorIndex: floorIdx,
                            Detail: "CARD_OFFER"
                        ));
                    }
```

Also add `using SpireOracle.Data;` at the top if not already present.

**Actually — wait.** The reward patch fires when cards are *displayed*, not when one is *picked*. To capture the pick decision, we need to patch the card selection action instead. The better approach is to patch `NCardRewardSelectionScreen` when a card is actually selected. This needs investigation:

Run: `grep "NCardRewardSelectionScreen\.\|CardRewardSelection" all_types.txt | head -10`

For now, the patch at display time captures the *offer*. The actual *pick* can be captured by patching the selection confirmation method in a follow-up.

- [ ] **Step 2: Add enqueue to AncientChoicePatch**

In `AncientChoicePatch.cs`, this patch fires when options are *displayed*. To capture the actual *choice*, we'd need to hook into the selection. However, the existing `EventOption.Chosen` patch from Task 10 already captures ancient choices (since ancients use the event option system).

No changes needed to `AncientChoicePatch.cs` — the `LiveEventChosenPatch` from Task 10 handles this.

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/CardRewardPatch.cs
git commit -m "feat: add card offer live capture to reward patch"
```

---

### Task 12: Update deploy skill to copy SQLite native libs

**Files:**
- Modify: `.claude/skills/deploy/SKILL.md`

- [ ] **Step 1: Update the deploy script**

In `.claude/skills/deploy/SKILL.md`, after the line that copies `SpireOracle.dll`:

```
cp src/Sts2Analytics.Mod/bin/Release/net9.0/SpireOracle.dll mods/SpireOracle/
```

Add lines to copy the SQLite dependencies:

```bash
# Copy SQLite dependencies
for dll in Microsoft.Data.Sqlite.dll SQLitePCLRaw.core.dll SQLitePCLRaw.batteries_v2.dll SQLitePCLRaw.provider.e_sqlite3.dll; do
    [ -f "src/Sts2Analytics.Mod/bin/Release/net9.0/$dll" ] && cp "src/Sts2Analytics.Mod/bin/Release/net9.0/$dll" mods/SpireOracle/
done

# Copy native SQLite library for Windows
NATIVE_DIR="src/Sts2Analytics.Mod/bin/Release/net9.0/runtimes/win-x64/native"
if [ -d "$NATIVE_DIR" ]; then
    mkdir -p mods/SpireOracle/runtimes/win-x64/native
    cp "$NATIVE_DIR"/* mods/SpireOracle/runtimes/win-x64/native/
fi
```

And update the final cp line to also copy these to the game dir:

```bash
# Copy all mod files to game directory
cp mods/SpireOracle/SpireOracle.dll mods/SpireOracle/overlay_data.json mods/SpireOracle/mod_manifest.json mods/SpireOracle/sts2_reference.json mods/SpireOracle/config.json "$GAME_DIR/"
for dll in Microsoft.Data.Sqlite.dll SQLitePCLRaw.core.dll SQLitePCLRaw.batteries_v2.dll SQLitePCLRaw.provider.e_sqlite3.dll; do
    [ -f "mods/SpireOracle/$dll" ] && cp "mods/SpireOracle/$dll" "$GAME_DIR/"
done
if [ -d "mods/SpireOracle/runtimes" ]; then
    cp -r mods/SpireOracle/runtimes "$GAME_DIR/"
fi
```

- [ ] **Step 2: Commit**

```bash
git add .claude/skills/deploy/SKILL.md
git commit -m "feat: deploy SQLite native libs with mod"
```

---

### Task 13: Smoke test — deploy and verify DB creation

This is a manual verification task.

- [ ] **Step 1: Build the mod**

Run: `cd /home/tom/projects/sts2mod && dotnet build src/Sts2Analytics.Mod -c Release`
Expected: PASS with no errors.

- [ ] **Step 2: Deploy to game**

Run the deploy skill (`/deploy`) to copy everything to the game directory.

- [ ] **Step 3: Launch STS2 and start a run**

Start the game, begin a new run. Check the Godot log (`godot.log`) for:
- `[SpireOracle] Live capture DB initialized`
- `[SpireOracle] Live capture: run started`
- `[SpireOracle] Live capture: combat started`

- [ ] **Step 4: Verify the database**

After playing a few floors, check the DB exists:
```bash
ls -la "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle/spireoracle_live.db"
```

Query it:
```bash
sqlite3 "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle/spireoracle_live.db" \
  "SELECT * FROM LiveRuns; SELECT * FROM Combats; SELECT COUNT(*) FROM CombatActions;"
```

Expected: At least one run, combats, and combat actions recorded.

- [ ] **Step 5: Commit any fixes**

If any patches needed adjustment during smoke testing, commit the fixes.

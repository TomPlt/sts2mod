# Combat Rating System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Glicko-2 combat rating system that rates individual cards on their combat performance by treating each fight as a team match (deck vs encounter pool), then aggregating card ratings into a Deck Elo.

**Architecture:** New `CombatRatingEngine` processes all combat floors chronologically, reconstructing the deck at each floor and updating per-card combat Glicko-2 ratings against encounter pool entities. Uses percentile-based scoring (damage vs historical pool distribution). Deck Elo computed via 1/RD-weighted aggregation of card combat ratings. Reuses existing `Glicko2Calculator` for all Glicko-2 math.

**Tech Stack:** C# / .NET 9.0, SQLite via Dapper, xUnit for tests, existing Glicko2Calculator

**Spec:** `docs/superpowers/specs/2026-03-22-combat-rating-system-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/Sts2Analytics.Core/Database/Schema.cs` | Add CombatGlicko2Ratings + CombatGlicko2History tables |
| Create | `src/Sts2Analytics.Core/Elo/DeckReconstructor.cs` | Event-sourced deck state reconstruction per floor |
| Create | `src/Sts2Analytics.Core/Elo/CombatRatingEngine.cs` | Process combat floors, update card + pool ratings |
| Create | `src/Sts2Analytics.Core/Elo/CombatGlicko2Analytics.cs` | Query combat ratings, deck Elo aggregation |
| Modify | `src/Sts2Analytics.Core/Models/AnalyticsResults.cs` | Add combat rating records, extend ModCardStats + ModOverlayData |
| Modify | `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | Export combat ratings to overlay_data.json |
| Create | `tests/Sts2Analytics.Core.Tests/Elo/DeckReconstructorTests.cs` | Test deck reconstruction logic |
| Create | `tests/Sts2Analytics.Core.Tests/Elo/CombatRatingEngineTests.cs` | Test combat rating processing |
| Create | `tests/Sts2Analytics.Core.Tests/Elo/CombatGlicko2AnalyticsTests.cs` | Test deck Elo aggregation |

---

### Task 1: Schema — Add Combat Rating Tables

**Files:**
- Modify: `src/Sts2Analytics.Core/Database/Schema.cs:277` (before closing `""";`)
- Test: `tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// In SchemaTests.cs — add new test
[Fact]
public void Initialize_CreatesCombatGlicko2Tables()
{
    using var conn = new SqliteConnection("Data Source=:memory:");
    conn.Open();
    Schema.Initialize(conn);
    var combatRatingCount = conn.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CombatGlicko2Ratings'");
    var combatHistoryCount = conn.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CombatGlicko2History'");
    Assert.Equal(1, combatRatingCount);
    Assert.Equal(1, combatHistoryCount);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesCombatGlicko2Tables" -v n`
Expected: FAIL — tables don't exist yet

- [ ] **Step 3: Add tables to Schema.cs**

Insert before the closing `""";` at line 285 of Schema.cs (after the AncientGlicko2 indexes):

```csharp
        CREATE TABLE IF NOT EXISTS CombatGlicko2Ratings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CardId TEXT NOT NULL,
            Character TEXT NOT NULL,
            Context TEXT NOT NULL DEFAULT 'overall',
            Rating REAL NOT NULL DEFAULT 1500.0,
            RatingDeviation REAL NOT NULL DEFAULT 350.0,
            Volatility REAL NOT NULL DEFAULT 0.06,
            GamesPlayed INTEGER NOT NULL DEFAULT 0,
            LastUpdatedRunId INTEGER,
            UNIQUE(CardId, Character, Context)
        );

        CREATE TABLE IF NOT EXISTS CombatGlicko2History (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CombatGlicko2RatingId INTEGER NOT NULL REFERENCES CombatGlicko2Ratings(Id),
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            RatingBefore REAL NOT NULL DEFAULT 0,
            RatingAfter REAL NOT NULL DEFAULT 0,
            RdBefore REAL NOT NULL DEFAULT 0,
            RdAfter REAL NOT NULL DEFAULT 0,
            VolatilityBefore REAL NOT NULL DEFAULT 0,
            VolatilityAfter REAL NOT NULL DEFAULT 0,
            Timestamp TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS IX_CombatGlicko2Ratings_CardId ON CombatGlicko2Ratings(CardId);
        CREATE INDEX IF NOT EXISTS IX_CombatGlicko2History_RatingId ON CombatGlicko2History(CombatGlicko2RatingId);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesCombatGlicko2Tables" -v n`
Expected: PASS

- [ ] **Step 5: Run all existing tests to verify no regressions**

Run: `dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/Sts2Analytics.Core/Database/Schema.cs tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs
git commit -m "feat: add CombatGlicko2Ratings and CombatGlicko2History tables"
```

---

### Task 2: Deck Reconstruction

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/DeckReconstructor.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Elo/DeckReconstructorTests.cs`

This builds the deck state at any combat floor by walking through card gain/removal/transform events.

- [ ] **Step 1: Write failing test — starter deck only**

```csharp
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;

namespace Sts2Analytics.Core.Tests.Elo;

public class DeckReconstructorTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    [Fact]
    public void GetDeckAtFloor_ReturnsStarterDeck()
    {
        using var conn = CreateDb();
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('r1', 's1', 'IRONCLAD', 1, '2026-01-01')");
        var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

        // Floor 0: starter cards gained
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 0, 'monster')", new { RunId = runId });
        var floor0Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

        // Floor 1: combat floor
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId, DamageTaken) VALUES (@RunId, 0, 1, 'monster', 'ENCOUNTER.NIBBITS_WEAK', 5)", new { RunId = runId });
        var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

        // Starter cards
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.STRIKE', 0, 'starter')", new { FloorId = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.DEFEND', 0, 'starter')", new { FloorId = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.BASH', 0, 'starter')", new { FloorId = floor0Id });

        // Also in FinalDecks for cross-reference
        conn.Execute("INSERT INTO FinalDecks (RunId, CardId, UpgradeLevel, FloorAdded) VALUES (@RunId, 'CARD.STRIKE', 0, 0)", new { RunId = runId });
        conn.Execute("INSERT INTO FinalDecks (RunId, CardId, UpgradeLevel, FloorAdded) VALUES (@RunId, 'CARD.DEFEND', 0, 0)", new { RunId = runId });
        conn.Execute("INSERT INTO FinalDecks (RunId, CardId, UpgradeLevel, FloorAdded) VALUES (@RunId, 'CARD.BASH', 0, 0)", new { RunId = runId });

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, floor1Id);

        Assert.Equal(3, deck.Count);
        Assert.Contains("CARD.STRIKE", deck);
        Assert.Contains("CARD.DEFEND", deck);
        Assert.Contains("CARD.BASH", deck);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetDeckAtFloor_ReturnsStarterDeck" -v n`
Expected: FAIL — DeckReconstructor does not exist

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class DeckReconstructor
{
    private readonly IDbConnection _connection;

    public DeckReconstructor(IDbConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// Returns the list of card entity IDs (e.g., "CARD.STRIKE", "CARD.INFLAME+1") in the deck
    /// at the given combat floor. Uses event-sourced reconstruction: walk forward through
    /// CardsGained, CardRemovals, CardTransforms, and RestSiteUpgrades floor by floor.
    /// </summary>
    public List<string> GetDeckAtFloor(long runId, long floorId)
    {
        var targetFloorIndex = _connection.QueryFirst<int>(
            "SELECT FloorIndex FROM Floors WHERE Id = @FloorId", new { FloorId = floorId });

        // Build unified event log sorted by floor index
        var events = new List<DeckEvent>();

        // Card gains
        var gains = _connection.Query<(int FloorIndex, string CardId, int UpgradeLevel)>("""
            SELECT f.FloorIndex, cg.CardId, cg.UpgradeLevel
            FROM CardsGained cg
            JOIN Floors f ON cg.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.FloorIndex <= @TargetFloor
            ORDER BY f.FloorIndex
            """, new { RunId = runId, TargetFloor = targetFloorIndex });
        foreach (var (floorIndex, cardId, upgradeLevel) in gains)
            events.Add(new DeckEvent(floorIndex, "add", MakeEntityId(cardId, upgradeLevel)));

        // Card removals — use FloorAddedToDeck to find the upgrade level from CardsGained
        var removals = _connection.Query<(int FloorIndex, string CardId, int? FloorAddedToDeck)>("""
            SELECT f.FloorIndex, cr.CardId, cr.FloorAddedToDeck
            FROM CardRemovals cr
            JOIN Floors f ON cr.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.FloorIndex <= @TargetFloor
            ORDER BY f.FloorIndex
            """, new { RunId = runId, TargetFloor = targetFloorIndex });
        foreach (var (floorIndex, cardId, floorAddedToDeck) in removals)
        {
            // Try to determine upgrade level from CardsGained using FloorAddedToDeck
            var entityId = cardId;
            if (floorAddedToDeck is not null)
            {
                var upgradeLevel = _connection.QueryFirstOrDefault<int?>("""
                    SELECT cg.UpgradeLevel FROM CardsGained cg
                    JOIN Floors f ON cg.FloorId = f.Id
                    WHERE f.RunId = @RunId AND f.FloorIndex = @FloorAdded AND cg.CardId = @CardId
                    LIMIT 1
                    """, new { RunId = runId, FloorAdded = floorAddedToDeck, CardId = cardId });
                if (upgradeLevel is not null)
                    entityId = MakeEntityId(cardId, upgradeLevel.Value);
            }
            events.Add(new DeckEvent(floorIndex, "remove", entityId));
        }

        // Card transforms
        var transforms = _connection.Query<(int FloorIndex, string OriginalCardId, string FinalCardId)>("""
            SELECT f.FloorIndex, ct.OriginalCardId, ct.FinalCardId
            FROM CardTransforms ct
            JOIN Floors f ON ct.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.FloorIndex <= @TargetFloor
            ORDER BY f.FloorIndex
            """, new { RunId = runId, TargetFloor = targetFloorIndex });
        foreach (var (floorIndex, originalCardId, finalCardId) in transforms)
        {
            events.Add(new DeckEvent(floorIndex, "remove", originalCardId));
            events.Add(new DeckEvent(floorIndex, "add", finalCardId));
        }

        // Rest site upgrades — swap CARD.X to CARD.X+1
        var upgrades = _connection.Query<(int FloorIndex, string CardId)>("""
            SELECT f.FloorIndex, rsu.CardId
            FROM RestSiteUpgrades rsu
            JOIN RestSiteChoices rsc ON rsu.RestSiteChoiceId = rsc.Id
            JOIN Floors f ON rsc.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.FloorIndex <= @TargetFloor
            ORDER BY f.FloorIndex
            """, new { RunId = runId, TargetFloor = targetFloorIndex });
        foreach (var (floorIndex, cardId) in upgrades)
        {
            events.Add(new DeckEvent(floorIndex, "remove", cardId));
            events.Add(new DeckEvent(floorIndex, "add", $"{cardId}+1"));
        }

        // Sort by floor index, apply events in order
        events.Sort((a, b) => a.FloorIndex.CompareTo(b.FloorIndex));

        var deck = new List<string>();
        foreach (var evt in events)
        {
            if (evt.Type == "add")
                deck.Add(evt.CardEntityId);
            else // "remove"
                deck.Remove(evt.CardEntityId);
        }

        return deck;
    }

    private static string MakeEntityId(string cardId, int upgradeLevel)
        => upgradeLevel > 0 ? $"{cardId}+{upgradeLevel}" : cardId;

    private record DeckEvent(int FloorIndex, string Type, string CardEntityId);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetDeckAtFloor_ReturnsStarterDeck" -v n`
Expected: PASS

- [ ] **Step 5: Write test — deck with card reward added**

```csharp
[Fact]
public void GetDeckAtFloor_IncludesCardRewards()
{
    using var conn = CreateDb();
    conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('r1', 's1', 'IRONCLAD', 1, '2026-01-01')");
    var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    // Floor 0: starter
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 0, 'monster')", new { RunId = runId });
    var floor0Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.STRIKE', 0, 'starter')", new { FloorId = floor0Id });

    // Floor 1: combat + reward
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId, DamageTaken) VALUES (@RunId, 0, 1, 'monster', 'ENCOUNTER.NIBBITS_WEAK', 5)", new { RunId = runId });
    var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.INFLAME', 1, 'reward')", new { FloorId = floor1Id });

    // Floor 2: next combat
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId, DamageTaken) VALUES (@RunId, 0, 2, 'monster', 'ENCOUNTER.SLIMES_WEAK', 3)", new { RunId = runId });
    var floor2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    var reconstructor = new DeckReconstructor(conn);

    // At floor 1: only starter
    var deckFloor1 = reconstructor.GetDeckAtFloor(runId, floor1Id);
    Assert.Single(deckFloor1);
    Assert.Contains("CARD.STRIKE", deckFloor1);

    // At floor 2: starter + reward from floor 1
    var deckFloor2 = reconstructor.GetDeckAtFloor(runId, floor2Id);
    Assert.Equal(2, deckFloor2.Count);
    Assert.Contains("CARD.STRIKE", deckFloor2);
    Assert.Contains("CARD.INFLAME+1", deckFloor2);
}
```

- [ ] **Step 6: Run test — should pass with current implementation**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetDeckAtFloor_IncludesCardRewards" -v n`
Expected: PASS (cards gained at floor 1 with FloorIndex <= 2 are included)

- [ ] **Step 7: Write test — card removal**

```csharp
[Fact]
public void GetDeckAtFloor_ExcludesRemovedCards()
{
    using var conn = CreateDb();
    conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('r1', 's1', 'IRONCLAD', 1, '2026-01-01')");
    var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 0, 'monster')", new { RunId = runId });
    var floor0Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.STRIKE', 0, 'starter')", new { FloorId = floor0Id });
    conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.DEFEND', 0, 'starter')", new { FloorId = floor0Id });

    // Floor 1: removal happens
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'shop')", new { RunId = runId });
    var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardRemovals (FloorId, CardId, FloorAddedToDeck) VALUES (@FloorId, 'CARD.STRIKE', 0)", new { FloorId = floor1Id });

    // Floor 2: combat
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId, DamageTaken) VALUES (@RunId, 0, 2, 'monster', 'ENCOUNTER.X_WEAK', 5)", new { RunId = runId });
    var floor2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    var reconstructor = new DeckReconstructor(conn);
    var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

    Assert.Single(deck);
    Assert.Contains("CARD.DEFEND", deck);
    Assert.DoesNotContain("CARD.STRIKE", deck);
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetDeckAtFloor_ExcludesRemovedCards" -v n`
Expected: PASS

- [ ] **Step 9: Write test — card transform**

```csharp
[Fact]
public void GetDeckAtFloor_AppliesTransforms()
{
    using var conn = CreateDb();
    conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('r1', 's1', 'IRONCLAD', 1, '2026-01-01')");
    var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 0, 'monster')", new { RunId = runId });
    var floor0Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.EGG', 0, 'event')", new { FloorId = floor0Id });

    // Floor 1: transform
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'unknown')", new { RunId = runId });
    var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardTransforms (FloorId, OriginalCardId, FinalCardId) VALUES (@FloorId, 'CARD.EGG', 'CARD.BIRD')", new { FloorId = floor1Id });

    // Floor 2: combat
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId, DamageTaken) VALUES (@RunId, 0, 2, 'monster', 'ENCOUNTER.X_WEAK', 3)", new { RunId = runId });
    var floor2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    var reconstructor = new DeckReconstructor(conn);
    var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

    Assert.Single(deck);
    Assert.Contains("CARD.BIRD", deck);
    Assert.DoesNotContain("CARD.EGG", deck);
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetDeckAtFloor_AppliesTransforms" -v n`
Expected: PASS

- [ ] **Step 11: Write test — rest site upgrade changes card identity**

```csharp
[Fact]
public void GetDeckAtFloor_AppliesRestSiteUpgrades()
{
    using var conn = CreateDb();
    conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('r1', 's1', 'IRONCLAD', 1, '2026-01-01')");
    var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    // Floor 0: starter
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 0, 'monster')", new { RunId = runId });
    var floor0Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@FloorId, 'CARD.INFLAME', 0, 'reward')", new { FloorId = floor0Id });

    // Floor 1: rest site with upgrade
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'rest_site')", new { RunId = runId });
    var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO RestSiteChoices (FloorId, Choice) VALUES (@FloorId, 'SMITH')", new { FloorId = floor1Id });
    var restChoiceId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    conn.Execute("INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId) VALUES (@Id, 'CARD.INFLAME')", new { Id = restChoiceId });

    // Floor 2: combat
    conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId, DamageTaken) VALUES (@RunId, 0, 2, 'monster', 'ENCOUNTER.X_WEAK', 3)", new { RunId = runId });
    var floor2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

    var reconstructor = new DeckReconstructor(conn);
    var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

    Assert.Single(deck);
    Assert.Contains("CARD.INFLAME+1", deck);
    Assert.DoesNotContain("CARD.INFLAME", deck);
}
```

- [ ] **Step 12: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetDeckAtFloor_AppliesRestSiteUpgrades" -v n`
Expected: PASS

- [ ] **Step 13: Write test with real sample run data**
(renumbered from here — step 14/15/16 below)

```csharp
[Fact]
public void GetDeckAtFloor_WithSampleRun_ReturnsReasonableDeck()
{
    using var conn = CreateDb();
    var repo = new RunRepository(conn);
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
    var runFile = RunFileParser.Parse(path);
    var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
    var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

    var reconstructor = new DeckReconstructor(conn);

    // Get first combat floor
    var firstCombatFloor = conn.QueryFirst<(long Id, int FloorIndex)>(
        "SELECT Id, FloorIndex FROM Floors WHERE RunId = @RunId AND MapPointType IN ('monster', 'elite', 'boss') ORDER BY FloorIndex LIMIT 1",
        new { RunId = runId });

    var deck = reconstructor.GetDeckAtFloor(runId, firstCombatFloor.Id);

    // Should have at least starter cards (5+ cards for any character)
    Assert.True(deck.Count >= 5, $"Deck at first combat floor should have at least 5 cards, got {deck.Count}");
}
```

- [ ] **Step 12: Run all deck reconstruction tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "DeckReconstructorTests" -v n`
Expected: All PASS

- [ ] **Step 13: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/DeckReconstructor.cs tests/Sts2Analytics.Core.Tests/Elo/DeckReconstructorTests.cs
git commit -m "feat: add DeckReconstructor for event-sourced deck state per floor"
```

---

### Task 3: Combat Rating Engine — Core Processing

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/CombatRatingEngine.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Elo/CombatRatingEngineTests.cs`

- [ ] **Step 1: Write failing test — creates ratings**

```csharp
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class CombatRatingEngineTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportSampleRun(SqliteConnection conn)
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        return repo.ImportRun(run, floors, floorData, runFile.Players[0]);
    }

    [Fact]
    public void ProcessAllRuns_CreatesCombatRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2Ratings");
        Assert.True(count > 0, "Should create combat ratings");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesCombatRatings" -v n`
Expected: FAIL — CombatRatingEngine does not exist

- [ ] **Step 3: Write CombatRatingEngine**

```csharp
using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class CombatRatingEngine
{
    private readonly IDbConnection _connection;

    public CombatRatingEngine(IDbConnection connection)
    {
        _connection = connection;
    }

    public void ProcessAllRuns()
    {
        // Find runs not yet processed for combat ratings
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM CombatGlicko2History ch
                JOIN CombatGlicko2Ratings cr ON ch.CombatGlicko2RatingId = cr.Id
                WHERE ch.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        // Precompute damage distributions per pool context
        var damageDistributions = PrecomputeDamageDistributions();

        var deckReconstructor = new DeckReconstructor(_connection);

        foreach (var runId in unprocessedRunIds)
        {
            ProcessRun(runId, deckReconstructor, damageDistributions);
        }
    }

    private void ProcessRun(long runId, DeckReconstructor deckReconstructor,
        Dictionary<string, List<int>> damageDistributions)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character, Win, StartTime FROM Runs WHERE Id = @RunId",
            new { RunId = runId });
        if (run is null) return;

        // Get all combat floors for this run
        var combatFloors = _connection.Query<CombatFloor>("""
            SELECT Id, ActIndex, FloorIndex, EncounterId, DamageTaken
            FROM Floors
            WHERE RunId = @RunId
              AND MapPointType IN ('monster', 'elite', 'boss')
              AND EncounterId IS NOT NULL
            ORDER BY FloorIndex ASC
            """, new { RunId = runId }).ToList();

        if (combatFloors.Count == 0) return;

        // One transaction per run for performance
        using var transaction = _connection.BeginTransaction();
        try
        {
            // Apply inactivity decay for cards not seen since their last update
            // (only between runs, not between floors within a run)
            ApplyInterRunDecay(run.Id, transaction);

            foreach (var floor in combatFloors)
            {
                var poolContext = DerivePoolContext(floor.EncounterId, floor.ActIndex);
                if (poolContext is null) continue;

                var deck = deckReconstructor.GetDeckAtFloor(runId, floor.Id);
                if (deck.Count == 0) continue;

                var score = ComputePercentileScore(floor.DamageTaken, poolContext, damageDistributions);

                var poolEntityId = $"POOL.{poolContext.ToUpper()}";
                var contexts = GetContexts(run.Character, poolContext);

                foreach (var (character, context) in contexts)
                {
                    // Get pool entity rating as the opponent
                    var poolRating = GetOrCreateRating(poolEntityId, character, context, transaction);
                    var poolGlicko = new Glicko2Calculator.Glicko2Rating(
                        poolRating.Rating, poolRating.RatingDeviation, poolRating.Volatility);

                    // Store pre-update card ratings for pool opponent calculation
                    var preUpdateCards = new List<Glicko2Calculator.Glicko2Rating>();

                    // Update each card in the deck against the pool
                    foreach (var cardId in deck)
                    {
                        var cardRating = GetOrCreateRating(cardId, character, context, transaction);
                        var cardGlicko = new Glicko2Calculator.Glicko2Rating(
                            cardRating.Rating, cardRating.RatingDeviation, cardRating.Volatility);
                        preUpdateCards.Add(cardGlicko);

                        var newCardRating = Glicko2Calculator.UpdateRating(cardGlicko,
                            [(poolGlicko, score)]);

                        UpdateRating(cardRating, newCardRating, runId, floor.Id, run.StartTime, transaction);
                    }

                    // Update pool entity using pre-update deck average as opponent
                    var deckAvgMu = preUpdateCards.Average(c => c.Rating);
                    var deckAvgRd = Math.Sqrt(preUpdateCards.Average(c => c.RatingDeviation * c.RatingDeviation));
                    var deckAvgVol = preUpdateCards.Average(c => c.Volatility);
                    var deckOpponent = new Glicko2Calculator.Glicko2Rating(deckAvgMu, deckAvgRd, deckAvgVol);

                    var newPoolRating = Glicko2Calculator.UpdateRating(poolGlicko,
                        [(deckOpponent, 1.0 - score)]);

                    UpdateRating(poolRating, newPoolRating, runId, floor.Id, run.StartTime, transaction);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Apply inactivity decay: for each card rating that was last updated in a prior run,
    /// grow RD for each run missed (same pattern as Glicko2Engine).
    /// </summary>
    private void ApplyInterRunDecay(long currentRunId, IDbTransaction transaction)
    {
        var ratingsToDecay = _connection.Query<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM CombatGlicko2Ratings
            WHERE LastUpdatedRunId IS NOT NULL AND LastUpdatedRunId < @CurrentRunId
            """, new { CurrentRunId = currentRunId }, transaction).ToList();

        foreach (var rating in ratingsToDecay)
        {
            var missedRuns = _connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Runs WHERE Id > @From AND Id < @To",
                new { From = rating.LastUpdatedRunId!.Value, To = currentRunId },
                transaction);

            if (missedRuns == 0) continue;

            var current = new Glicko2Calculator.Glicko2Rating(
                rating.Rating, rating.RatingDeviation, rating.Volatility);

            for (int i = 0; i < missedRuns; i++)
                current = Glicko2Calculator.ApplyInactivityDecay(current);

            _connection.Execute("""
                UPDATE CombatGlicko2Ratings
                SET RatingDeviation = @Rd
                WHERE Id = @Id
                """,
                new { Rd = current.RatingDeviation, rating.Id },
                transaction);
        }
    }

    /// <summary>
    /// Precompute sorted damage values per pool context for percentile scoring.
    /// </summary>
    private Dictionary<string, List<int>> PrecomputeDamageDistributions()
    {
        var rows = _connection.Query<(string EncounterId, int ActIndex, int DamageTaken)>("""
            SELECT EncounterId, ActIndex, DamageTaken
            FROM Floors
            WHERE MapPointType IN ('monster', 'elite', 'boss')
              AND EncounterId IS NOT NULL
            """).ToList();

        var distributions = new Dictionary<string, List<int>>();
        foreach (var (encounterId, actIndex, damageTaken) in rows)
        {
            var poolContext = DerivePoolContext(encounterId, actIndex);
            if (poolContext is null) continue;

            if (!distributions.ContainsKey(poolContext))
                distributions[poolContext] = [];
            distributions[poolContext].Add(damageTaken);
        }

        // Sort each distribution for percentile lookups
        foreach (var key in distributions.Keys)
            distributions[key].Sort();

        return distributions;
    }

    /// <summary>
    /// Score = fraction of historical fights where damage was >= actual damage.
    /// Low damage → high score (good). High damage → low score (bad).
    /// </summary>
    private static double ComputePercentileScore(int actualDamage, string poolContext,
        Dictionary<string, List<int>> distributions)
    {
        if (!distributions.TryGetValue(poolContext, out var sorted) || sorted.Count == 0)
            return 0.5; // No data — neutral score

        int countGe = sorted.Count - LowerBound(sorted, actualDamage);
        return (double)countGe / sorted.Count;
    }

    /// <summary>
    /// Binary search: returns index of first element >= value.
    /// </summary>
    private static int LowerBound(List<int> sorted, int value)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Derive pool context from encounter ID suffix and act index.
    /// e.g., "ENCOUNTER.NIBBITS_WEAK" + actIndex=0 → "act1_weak"
    /// </summary>
    internal static string? DerivePoolContext(string encounterId, int actIndex)
    {
        var actNum = actIndex + 1;
        if (encounterId.EndsWith("_WEAK")) return $"act{actNum}_weak";
        if (encounterId.EndsWith("_NORMAL")) return $"act{actNum}_normal";
        if (encounterId.EndsWith("_ELITE")) return $"act{actNum}_elite";
        if (encounterId.EndsWith("_BOSS")) return $"act{actNum}_boss";
        return null;
    }

    private static List<(string Character, string Context)> GetContexts(string character, string poolContext)
    {
        return
        [
            ("ALL", poolContext),
            (character, poolContext),
            ("ALL", "overall"),
            (character, "overall"),
        ];
    }

    private RatingInfo GetOrCreateRating(string cardId, string character, string context,
        IDbTransaction transaction)
    {
        _connection.Execute(
            "INSERT OR IGNORE INTO CombatGlicko2Ratings (CardId, Character, Context) VALUES (@CardId, @Character, @Context)",
            new { CardId = cardId, Character = character, Context = context },
            transaction);

        return _connection.QueryFirst<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM CombatGlicko2Ratings
            WHERE CardId = @CardId AND Character = @Character AND Context = @Context
            """,
            new { CardId = cardId, Character = character, Context = context },
            transaction);
    }

    private void UpdateRating(RatingInfo current, Glicko2Calculator.Glicko2Rating newRating,
        long runId, long floorId, string timestamp, IDbTransaction transaction)
    {
        _connection.Execute("""
            UPDATE CombatGlicko2Ratings
            SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                GamesPlayed = @Games, LastUpdatedRunId = @RunId
            WHERE Id = @Id
            """,
            new
            {
                Rating = newRating.Rating,
                Rd = newRating.RatingDeviation,
                Vol = newRating.Volatility,
                Games = current.GamesPlayed + 1,
                RunId = runId,
                current.Id
            },
            transaction);

        _connection.Execute("""
            INSERT INTO CombatGlicko2History
                (CombatGlicko2RatingId, RunId, FloorId, RatingBefore, RatingAfter, RdBefore, RdAfter,
                 VolatilityBefore, VolatilityAfter, Timestamp)
            VALUES (@RatingId, @RunId, @FloorId, @RatingBefore, @RatingAfter, @RdBefore, @RdAfter,
                    @VolBefore, @VolAfter, @Timestamp)
            """,
            new
            {
                RatingId = current.Id,
                RunId = runId,
                FloorId = floorId,
                RatingBefore = current.Rating,
                RatingAfter = newRating.Rating,
                RdBefore = current.RatingDeviation,
                RdAfter = newRating.RatingDeviation,
                VolBefore = current.Volatility,
                VolAfter = newRating.Volatility,
                Timestamp = timestamp
            },
            transaction);
    }

    private record RunInfo(long Id, string Character, long Win, string StartTime);
    private record CombatFloor(long Id, int ActIndex, int FloorIndex, string EncounterId, int DamageTaken);

    private record RatingInfo
    {
        public long Id { get; init; }
        public double Rating { get; init; }
        public double RatingDeviation { get; init; }
        public double Volatility { get; init; }
        public int GamesPlayed { get; init; }
        public long? LastUpdatedRunId { get; init; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesCombatRatings" -v n`
Expected: PASS

- [ ] **Step 5: Write test — pool entities get rated**

```csharp
[Fact]
public void ProcessAllRuns_CreatesPoolEntityRatings()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);
    var engine = new CombatRatingEngine(conn);
    engine.ProcessAllRuns();
    var poolCount = conn.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM CombatGlicko2Ratings WHERE CardId LIKE 'POOL.%'");
    Assert.True(poolCount > 0, "Should create pool entity ratings");
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesPoolEntityRatings" -v n`
Expected: PASS

- [ ] **Step 7: Write test — ratings differ from default**

```csharp
[Fact]
public void ProcessAllRuns_RatingsDeviateFromDefault()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);
    var engine = new CombatRatingEngine(conn);
    engine.ProcessAllRuns();

    var ratings = conn.Query<double>(
        "SELECT Rating FROM CombatGlicko2Ratings WHERE CardId NOT LIKE 'POOL.%' AND Context = 'overall' AND GamesPlayed > 0")
        .ToList();

    Assert.True(ratings.Count > 0);
    // At least some ratings should differ from 1500
    Assert.True(ratings.Any(r => Math.Abs(r - 1500) > 1.0),
        "At least some card combat ratings should differ from the default 1500");
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_RatingsDeviateFromDefault" -v n`
Expected: PASS

- [ ] **Step 9: Write test — combat history records include FloorId**

```csharp
[Fact]
public void ProcessAllRuns_HistoryIncludesFloorId()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);
    var engine = new CombatRatingEngine(conn);
    engine.ProcessAllRuns();
    var floorIds = conn.Query<long>(
        "SELECT DISTINCT FloorId FROM CombatGlicko2History").ToList();
    Assert.True(floorIds.Count > 0, "History should reference floor IDs");
    // All referenced floors should be combat floors
    foreach (var floorId in floorIds)
    {
        var mapPointType = conn.QueryFirst<string>(
            "SELECT MapPointType FROM Floors WHERE Id = @Id", new { Id = floorId });
        Assert.Contains(mapPointType, new[] { "monster", "elite", "boss" });
    }
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_HistoryIncludesFloorId" -v n`
Expected: PASS

- [ ] **Step 11: Write test — idempotent**

```csharp
[Fact]
public void ProcessAllRuns_Idempotent()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);
    var engine = new CombatRatingEngine(conn);
    engine.ProcessAllRuns();
    var countBefore = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2History");
    engine.ProcessAllRuns();
    var countAfter = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2History");
    Assert.Equal(countBefore, countAfter);
}
```

- [ ] **Step 12: Run all combat rating tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "CombatRatingEngineTests" -v n`
Expected: All PASS

- [ ] **Step 13: Write test — DerivePoolContext unit tests**

```csharp
[Theory]
[InlineData("ENCOUNTER.NIBBITS_WEAK", 0, "act1_weak")]
[InlineData("ENCOUNTER.SLIMES_NORMAL", 0, "act1_normal")]
[InlineData("ENCOUNTER.KNIGHTS_ELITE", 1, "act2_elite")]
[InlineData("ENCOUNTER.BEAST_BOSS", 2, "act3_boss")]
[InlineData("EVENT.SOMETHING", 0, null)]
public void DerivePoolContext_ReturnsCorrectContext(string encounterId, int actIndex, string? expected)
{
    var result = CombatRatingEngine.DerivePoolContext(encounterId, actIndex);
    Assert.Equal(expected, result);
}
```

- [ ] **Step 14: Run all tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: All PASS

- [ ] **Step 15: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/CombatRatingEngine.cs tests/Sts2Analytics.Core.Tests/Elo/CombatRatingEngineTests.cs
git commit -m "feat: add CombatRatingEngine with deck-vs-pool Glicko-2 ratings"
```

---

### Task 4: Combat Analytics and Deck Elo Aggregation

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/CombatGlicko2Analytics.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Elo/CombatGlicko2AnalyticsTests.cs`

- [ ] **Step 1: Write failing test — get combat ratings**

```csharp
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class CombatGlicko2AnalyticsTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportAndProcess(SqliteConnection conn)
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();
        return runId;
    }

    [Fact]
    public void GetRatings_ReturnsCardCombatRatings()
    {
        using var conn = CreateDb();
        ImportAndProcess(conn);
        var analytics = new CombatGlicko2Analytics(conn);
        var ratings = analytics.GetRatings();
        Assert.True(ratings.Count > 0);
        // Should have card ratings (not just pool entities)
        Assert.True(ratings.Any(r => r.CardId.StartsWith("CARD.")));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetRatings_ReturnsCardCombatRatings" -v n`
Expected: FAIL — CombatGlicko2Analytics does not exist

- [ ] **Step 3: Write CombatGlicko2Analytics**

```csharp
using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Elo;

public class CombatGlicko2Analytics
{
    private readonly IDbConnection _connection;

    public CombatGlicko2Analytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<Glicko2RatingResult> GetRatings(string? character = null)
    {
        var where = character is not null ? "WHERE Character = @Character" : "";
        var sql = $"""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM CombatGlicko2Ratings
            {where}
            ORDER BY Rating DESC
            """;
        return _connection.Query<Glicko2RatingResult>(sql, new { Character = character }).ToList();
    }

    public List<Glicko2RatingResult> GetPoolRatings()
    {
        return _connection.Query<Glicko2RatingResult>("""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM CombatGlicko2Ratings
            WHERE CardId LIKE 'POOL.%'
            ORDER BY Context, Character
            """).ToList();
    }

    /// <summary>
    /// Compute deck Elo as 1/RD-weighted mean of card combat ratings for a given context.
    /// Returns (deckMu, deckRd).
    /// </summary>
    public static (double Mu, double Rd) ComputeDeckElo(
        IEnumerable<(double Rating, double Rd)> cardRatings)
    {
        double sumWeightedMu = 0;
        double sumWeight = 0;
        double sumPrecision = 0;

        foreach (var (rating, rd) in cardRatings)
        {
            if (rd <= 0) continue;
            var weight = 1.0 / rd;
            sumWeightedMu += rating * weight;
            sumWeight += weight;
            sumPrecision += 1.0 / (rd * rd);
        }

        if (sumWeight == 0) return (1500.0, 350.0);

        var deckMu = sumWeightedMu / sumWeight;
        var deckRd = 1.0 / Math.Sqrt(sumPrecision);

        return (deckMu, deckRd);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "GetRatings_ReturnsCardCombatRatings" -v n`
Expected: PASS

- [ ] **Step 5: Write test — deck Elo aggregation**

```csharp
[Fact]
public void ComputeDeckElo_WeightsByRd()
{
    // Card A: rating 1600, RD 100 (confident)
    // Card B: rating 1400, RD 300 (uncertain)
    // Weighted mean should be closer to 1600 (card A dominates)
    var cards = new (double Rating, double Rd)[]
    {
        (1600, 100),
        (1400, 300),
    };

    var (mu, rd) = CombatGlicko2Analytics.ComputeDeckElo(cards);

    // Weights: 1/100 = 0.01, 1/300 = 0.0033
    // Weighted mean: (1600*0.01 + 1400*0.0033) / (0.01 + 0.0033) = 16 + 4.62 / 0.0133 ≈ 1550
    Assert.True(mu > 1500, $"Deck Elo {mu} should be above midpoint (confident high card dominates)");
    Assert.True(mu < 1600, $"Deck Elo {mu} should be below the high card (uncertain card pulls down)");
    Assert.True(rd < 100, $"Deck RD {rd} should be less than any individual card RD");
}

[Fact]
public void ComputeDeckElo_DefaultForEmptyDeck()
{
    var (mu, rd) = CombatGlicko2Analytics.ComputeDeckElo([]);
    Assert.Equal(1500.0, mu);
    Assert.Equal(350.0, rd);
}

[Fact]
public void ComputeDeckElo_HighRdCardsContributeLittle()
{
    // One confident card + many uncertain cards
    var cards = new (double Rating, double Rd)[]
    {
        (1600, 100),
        (1200, 350),
        (1200, 350),
        (1200, 350),
        (1200, 350),
    };

    var (mu, _) = CombatGlicko2Analytics.ComputeDeckElo(cards);
    // The confident 1600 card should dominate despite 4 uncertain low cards
    Assert.True(mu > 1450, $"Deck Elo {mu} should be pulled toward the confident card");
}
```

- [ ] **Step 6: Run all analytics tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "CombatGlicko2AnalyticsTests" -v n`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/CombatGlicko2Analytics.cs tests/Sts2Analytics.Core.Tests/Elo/CombatGlicko2AnalyticsTests.cs
git commit -m "feat: add CombatGlicko2Analytics with deck Elo aggregation"
```

---

### Task 5: Model Records and Export Integration

**Files:**
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs:50-68`
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs:190-466`

- [ ] **Step 1: Add combat rating fields to ModCardStats**

In `AnalyticsResults.cs`, replace the ModCardStats record:

```csharp
public record ModCardStats(
    string CardId, double Elo, double Rd, double PickRate,
    double WinRatePicked, double WinRateSkipped, double Delta,
    double EloAct1, double RdAct1, double EloAct2, double RdAct2, double EloAct3, double RdAct3,
    string? BlindSpot = null, double BlindSpotScore = 0,
    double BlindSpotPickRate = 0, double BlindSpotWinRateDelta = 0,
    double CombatElo = 0, double CombatRd = 350,
    Dictionary<string, PoolRating>? CombatByPool = null);

public record PoolRating(double Elo, double Rd);
```

- [ ] **Step 2: Add encounter pool section to ModOverlayData**

```csharp
public record ModOverlayData(
    int Version, string ExportedAt, double SkipElo,
    Dictionary<string, double> SkipEloByAct,
    List<ModCardStats> Cards,
    List<ModAncientStats>? AncientChoices = null,
    List<MapIntelCharacter>? MapIntel = null,
    Dictionary<string, PoolRating>? EncounterPools = null);
```

- [ ] **Step 3: Build and verify compilation**

Run: `dotnet build src/Sts2Analytics.Core -c Release`
Expected: Build succeeded (ExportCommand will have warnings about unused fields but should compile)

- [ ] **Step 4: Add combat rating export to ExportCommand.ExportMod**

In `ExportCommand.cs`, after the ancient rating processing (~line 216), add:

```csharp
// Process combat ratings
var combatEngine = new CombatRatingEngine(conn);
combatEngine.ProcessAllRuns();
var combatAnalytics = new CombatGlicko2Analytics(conn);
var allCombatRatings = combatAnalytics.GetRatings();
var combatOverall = allCombatRatings
    .Where(r => r.Context == "overall" && r.Character == "ALL" && !r.CardId.StartsWith("POOL."))
    .ToDictionary(r => r.CardId);
var combatByPool = allCombatRatings
    .Where(r => r.Context != "overall" && r.Character == "ALL" && !r.CardId.StartsWith("POOL."))
    .ToLookup(r => r.CardId);

// Pool entity ratings
var poolRatings = combatAnalytics.GetPoolRatings()
    .Where(r => r.Character == "ALL")
    .ToDictionary(r => r.Context, r => new PoolRating(r.Rating, r.RatingDeviation));
```

Then in the card stats builder (~line 264-295), add combat fields:

```csharp
// Inside the .Select lambda, after blindspot fields:
var combatElo = combatOverall.TryGetValue(id, out var ce) ? ce.Rating : 0.0;
var combatRdVal = ce?.RatingDeviation ?? 350.0;
var cardCombatPools = combatByPool[id]
    .ToDictionary(r => r.Context, r => new PoolRating(r.Rating, r.RatingDeviation));
```

And pass these to the ModCardStats constructor.

Finally, add `EncounterPools: poolRatings` to the ModOverlayData constructor (~line 446).

- [ ] **Step 5: Build full solution**

Run: `dotnet build src/Sts2Analytics.Cli -c Release`
Expected: Build succeeded

- [ ] **Step 6: Run the export against real data and verify output**

Run: `dotnet run --project src/Sts2Analytics.Cli -- export --mod --output /tmp/test_combat_overlay.json`

Then verify:
```bash
python3 -c "
import json
with open('/tmp/test_combat_overlay.json') as f:
    data = json.load(f)
pools = data.get('encounterPools', {})
print(f'Encounter pools: {len(pools)}')
for k, v in sorted(pools.items()):
    print(f'  {k}: elo={v[\"elo\"]:.0f} rd={v[\"rd\"]:.0f}')
cards_with_combat = [c for c in data['cards'] if c.get('combatElo', 0) != 0]
print(f'Cards with combat elo: {len(cards_with_combat)}')
if cards_with_combat:
    top = sorted(cards_with_combat, key=lambda c: -c['combatElo'])[:5]
    for c in top:
        print(f'  {c[\"cardId\"]:35s} combatElo={c[\"combatElo\"]:.0f} combatRd={c[\"combatRd\"]:.0f}')
"
```

Expected: Pool ratings for each act/pool context, cards with non-zero combat Elo

- [ ] **Step 7: Run all tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: All PASS

- [ ] **Step 8: Commit**

```bash
git add src/Sts2Analytics.Core/Models/AnalyticsResults.cs src/Sts2Analytics.Cli/Commands/ExportCommand.cs
git commit -m "feat: export combat Elo ratings and encounter pool data to overlay JSON"
```

---

### Task 6: Integration Test with Real Data

**Files:**
- No new files — validation against the real database

- [ ] **Step 1: Run full pipeline against real DB**

```bash
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output mods/SpireOracle/overlay_data.json
```

- [ ] **Step 2: Sanity check the combat ratings**

```bash
python3 -c "
import json
with open('mods/SpireOracle/overlay_data.json') as f:
    data = json.load(f)

# Check: cards with many combat observations should have lower RD
cards = [c for c in data['cards'] if c.get('combatElo', 0) != 0]
by_rd = sorted(cards, key=lambda c: c['combatRd'])
print('Most confident combat ratings (lowest RD):')
for c in by_rd[:10]:
    print(f'  {c[\"cardId\"]:35s} combatElo={c[\"combatElo\"]:.0f} combatRd={c[\"combatRd\"]:.0f} pickElo={c[\"elo\"]:.0f}')

print()
print('Biggest pick vs combat Elo divergence:')
divergent = sorted(cards, key=lambda c: abs(c['combatElo'] - c['elo']) if c['combatRd'] < 200 else 0, reverse=True)
for c in divergent[:10]:
    if c['combatRd'] < 200:
        diff = c['combatElo'] - c['elo']
        label = 'combat > pick' if diff > 0 else 'pick > combat'
        print(f'  {c[\"cardId\"]:35s} pick={c[\"elo\"]:.0f} combat={c[\"combatElo\"]:.0f} diff={diff:+.0f} ({label})')
"
```

Expected: Reasonable ratings — common cards (strikes, defends) should have tight RD. Some divergence between pick and combat Elo.

- [ ] **Step 3: Verify encounter pool ratings make sense**

```bash
python3 -c "
import json
with open('mods/SpireOracle/overlay_data.json') as f:
    data = json.load(f)
pools = data.get('encounterPools', {})
for k in sorted(pools.keys()):
    v = pools[k]
    print(f'{k:20s} elo={v[\"elo\"]:.0f} rd={v[\"rd\"]:.0f}')
# Expect: elite/boss pools rated higher (harder) than weak/normal
"
```

Expected: Elite and boss pools should have higher Elo than weak/normal (they're harder opponents).

- [ ] **Step 4: Commit the updated overlay data**

```bash
git add mods/SpireOracle/overlay_data.json
git commit -m "data: update overlay data with combat Elo ratings"
```

# Blind Spot Analysis & Personal Rating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add personal Glicko-2 player rating (per character, win/loss vs ascension-scaled opponents), blind spot detection (dual-signal: pick-rate mismatch + outcome penalty), in-game blind spot badges, and dashboard tabs for both.

**Architecture:** Layered pipeline — card Glicko-2 (existing) → player Glicko-2 (new, independent) → blind spot analyzer (new, reads card ratings + pick/win data). Each layer has its own DB tables, engine class, and tests.

**Tech Stack:** C#/.NET 9.0, SQLite/Dapper, xUnit, Godot 4.5.1/Harmony 2.4.2, Blazor WASM

**Spec:** `docs/superpowers/specs/2026-03-21-blind-spot-analysis-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Core/Elo/PlayerRatingEngine.cs` | Process runs chronologically, compute player Glicko-2 per character |
| `src/Sts2Analytics.Core/Analytics/BlindSpotAnalyzer.cs` | Compute blind spot scores from card ratings + pick/win data |
| `src/Sts2Analytics.Core/Models/BlindSpotResult.cs` | Result records for blind spot data |
| `src/Sts2Analytics.Core/Models/PlayerRatingResult.cs` | Result records for player rating data |
| `src/Sts2Analytics.Core/Analytics/BlindSpotConstants.cs` | All tunable constants in one place |
| `tests/Sts2Analytics.Core.Tests/Elo/PlayerRatingEngineTests.cs` | Player rating engine tests |
| `tests/Sts2Analytics.Core.Tests/Analytics/BlindSpotAnalyzerTests.cs` | Blind spot analyzer tests |
| `src/Sts2Analytics.Web/Pages/PlayerRating.razor` | Dashboard: My Rating tab |
| `src/Sts2Analytics.Web/Pages/BlindSpots.razor` | Dashboard: Blind Spots tab |

### Modified Files
| File | Changes |
|------|---------|
| `src/Sts2Analytics.Core/Database/Schema.cs` | Add PlayerRatings, PlayerRatingHistory, BlindSpots tables |
| `src/Sts2Analytics.Core/Models/AnalyticsResults.cs` | Add blind spot fields to `ModCardStats` and `ModOverlayData` v3 |
| `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | Include blind spot data in mod export, bump to v3 |
| `src/Sts2Analytics.Cli/Commands/RatingCommand.cs` | Add `--player` flag |
| `src/Sts2Analytics.Mod/Data/OverlayData.cs` | Add blind spot fields to `CardStats` record |
| `src/Sts2Analytics.Mod/UI/OverlayFactory.cs` | Render blind spot badges |
| `src/Sts2Analytics.Web/Layout/MainLayout.razor` | Add nav links for My Rating and Blind Spots |
| `src/Sts2Analytics.Web/Services/DataService.cs` | Add player rating + blind spot data to `ExportData` |

---

### Task 1: DB Schema — PlayerRatings and PlayerRatingHistory Tables

**Files:**
- Modify: `src/Sts2Analytics.Core/Database/Schema.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs`

- [ ] **Step 1: Write the failing test**

In `SchemaTests.cs`, add:

```csharp
[Fact]
public void Initialize_CreatesPlayerRatingsTables()
{
    using var conn = new SqliteConnection("Data Source=:memory:");
    conn.Open();
    Schema.Initialize(conn);

    var playerRatings = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='PlayerRatings'").ToList();
    Assert.Single(playerRatings);

    var playerRatingHistory = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='PlayerRatingHistory'").ToList();
    Assert.Single(playerRatingHistory);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesPlayerRatingsTables" -v n`
Expected: FAIL — tables don't exist yet

- [ ] **Step 3: Add tables to Schema.cs**

In `Schema.cs`, add to the SQL string after the Glicko2History table:

```sql
CREATE TABLE IF NOT EXISTS PlayerRatings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Context TEXT NOT NULL UNIQUE,
    Rating REAL NOT NULL DEFAULT 1500.0,
    RatingDeviation REAL NOT NULL DEFAULT 350.0,
    Volatility REAL NOT NULL DEFAULT 0.06,
    GamesPlayed INTEGER NOT NULL DEFAULT 0,
    LastUpdatedRunId INTEGER
);

CREATE TABLE IF NOT EXISTS PlayerRatingHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerRatingId INTEGER NOT NULL REFERENCES PlayerRatings(Id),
    RunId INTEGER NOT NULL REFERENCES Runs(Id),
    RatingBefore REAL NOT NULL DEFAULT 0,
    RatingAfter REAL NOT NULL DEFAULT 0,
    RdBefore REAL NOT NULL DEFAULT 0,
    RdAfter REAL NOT NULL DEFAULT 0,
    VolatilityBefore REAL NOT NULL DEFAULT 0,
    VolatilityAfter REAL NOT NULL DEFAULT 0,
    Opponent TEXT NOT NULL DEFAULT '',
    OpponentRating REAL NOT NULL DEFAULT 0,
    Outcome REAL NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS IX_PlayerRatingHistory_PlayerRatingId ON PlayerRatingHistory(PlayerRatingId);
CREATE INDEX IF NOT EXISTS IX_PlayerRatingHistory_RunId ON PlayerRatingHistory(RunId);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesPlayerRatingsTables" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Database/Schema.cs tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs
git commit -m "feat: add PlayerRatings and PlayerRatingHistory tables to schema"
```

---

### Task 2: DB Schema — BlindSpots Table

**Files:**
- Modify: `src/Sts2Analytics.Core/Database/Schema.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Initialize_CreatesBlindSpotsTable()
{
    using var conn = new SqliteConnection("Data Source=:memory:");
    conn.Open();
    Schema.Initialize(conn);

    var blindSpots = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='BlindSpots'").ToList();
    Assert.Single(blindSpots);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesBlindSpotsTable" -v n`
Expected: FAIL

- [ ] **Step 3: Add BlindSpots table to Schema.cs**

```sql
CREATE TABLE IF NOT EXISTS BlindSpots (
    CardId TEXT NOT NULL,
    Context TEXT NOT NULL,
    BlindSpotType TEXT NOT NULL,
    Score REAL NOT NULL DEFAULT 0,
    PickRate REAL NOT NULL DEFAULT 0,
    ExpectedPickRate REAL NOT NULL DEFAULT 0,
    WinRateDelta REAL NOT NULL DEFAULT 0,
    GamesAnalyzed INTEGER NOT NULL DEFAULT 0,
    LastUpdated TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (CardId, Context)
);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesBlindSpotsTable" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Database/Schema.cs tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs
git commit -m "feat: add BlindSpots table to schema"
```

---

### Task 3: Player Rating Result Models

**Files:**
- Create: `src/Sts2Analytics.Core/Models/PlayerRatingResult.cs`

- [ ] **Step 1: Create the result records**

```csharp
namespace Sts2Analytics.Core.Models;

public record PlayerRatingResult(
    string Context, double Rating, double RatingDeviation,
    double Volatility, int GamesPlayed);

public record PlayerRatingHistoryResult(
    string Context, long RunId,
    double RatingBefore, double RatingAfter,
    double RdBefore, double RdAfter,
    double VolatilityBefore, double VolatilityAfter,
    string Opponent, double OpponentRating, double Outcome);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Core`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Core/Models/PlayerRatingResult.cs
git commit -m "feat: add PlayerRatingResult and PlayerRatingHistoryResult records"
```

---

### Task 4: PlayerRatingEngine — Core Processing

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/PlayerRatingEngine.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Elo/PlayerRatingEngineTests.cs`

- [ ] **Step 1: Write the failing test — processes a win and creates rating**

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class PlayerRatingEngineTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportSampleRun(SqliteConnection conn, string fixture = "sample_win.run")
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, fixture);
        return repo.ImportRun(run, floors, floorData, runFile.Players[0]);
    }

    [Fact]
    public void ProcessAllRuns_CreatesPlayerRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);

        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();

        var ratings = conn.Query("SELECT * FROM PlayerRatings").ToList();
        Assert.True(ratings.Count >= 2); // at least overall + character
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesPlayerRatings" -v n`
Expected: FAIL — PlayerRatingEngine doesn't exist

- [ ] **Step 3: Implement PlayerRatingEngine**

```csharp
using System.Data;
using Dapper;
using static Sts2Analytics.Core.Elo.Glicko2Calculator;

namespace Sts2Analytics.Core.Elo;

public class PlayerRatingEngine
{
    private readonly IDbConnection _connection;

    // Ascension opponent rating: A0=1200, A20=2400
    private const double BaseOpponentRating = 1200.0;
    private const double OpponentRatingPerAscension = 60.0;
    private const double OpponentRd = 50.0;

    public PlayerRatingEngine(IDbConnection connection) => _connection = connection;

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM PlayerRatingHistory ph
                JOIN PlayerRatings pr ON ph.PlayerRatingId = pr.Id
                WHERE ph.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        foreach (var runId in unprocessedRunIds)
            ProcessRun(runId);
    }

    private void ProcessRun(long runId)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character, Ascension, Win FROM Runs WHERE Id = @RunId",
            new { RunId = runId });
        if (run is null) return;

        var opponentRating = BaseOpponentRating + run.Ascension * OpponentRatingPerAscension;
        var opponent = new Glicko2Rating(opponentRating, OpponentRd, 0.06);
        double score = run.Win == 1 ? 1.0 : 0.0;
        var opponentLabel = $"A{run.Ascension}";

        using var transaction = _connection.BeginTransaction();
        try
        {
            // Update both character-specific and overall contexts
            var contexts = new[] { run.Character, "overall" };
            foreach (var context in contexts)
            {
                var rating = GetOrCreateRating(context, transaction);

                // Apply inactivity decay if not the first run
                var currentRating = new Glicko2Rating(rating.Rating, rating.RatingDeviation, rating.Volatility);
                if (rating.LastUpdatedRunId is not null)
                {
                    var missedRuns = _connection.QueryFirstOrDefault<int>("""
                        SELECT COUNT(*) FROM Runs
                        WHERE Id > @LastRunId AND Id < @CurrentRunId
                        AND Character = @Character
                        """, new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId,
                            Character = context == "overall" ? "%" : run.Character },
                        transaction);
                    for (int i = 0; i < missedRuns; i++)
                        currentRating = ApplyInactivityDecay(currentRating);
                }

                var results = new[] { (Rating: opponent, Score: score) };
                var newRating = UpdateRating(currentRating, results);

                // Record history
                _connection.Execute("""
                    INSERT INTO PlayerRatingHistory
                        (PlayerRatingId, RunId, RatingBefore, RatingAfter,
                         RdBefore, RdAfter, VolatilityBefore, VolatilityAfter,
                         Opponent, OpponentRating, Outcome)
                    VALUES (@PlayerRatingId, @RunId, @RatingBefore, @RatingAfter,
                            @RdBefore, @RdAfter, @VolatilityBefore, @VolatilityAfter,
                            @Opponent, @OpponentRating, @Outcome)
                    """, new {
                        PlayerRatingId = rating.Id,
                        RunId = runId,
                        RatingBefore = currentRating.Rating,
                        RatingAfter = newRating.Rating,
                        RdBefore = currentRating.RatingDeviation,
                        RdAfter = newRating.RatingDeviation,
                        VolatilityBefore = currentRating.Volatility,
                        VolatilityAfter = newRating.Volatility,
                        Opponent = opponentLabel,
                        OpponentRating = opponentRating,
                        Outcome = score
                    }, transaction);

                // Update current rating
                _connection.Execute("""
                    UPDATE PlayerRatings
                    SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                        GamesPlayed = GamesPlayed + 1, LastUpdatedRunId = @RunId
                    WHERE Id = @Id
                    """, new {
                        Rating = newRating.Rating, Rd = newRating.RatingDeviation,
                        Vol = newRating.Volatility, RunId = runId, Id = rating.Id
                    }, transaction);
            }

            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private RatingInfo GetOrCreateRating(string context, IDbTransaction transaction)
    {
        var existing = _connection.QueryFirstOrDefault<RatingInfo>(
            "SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId FROM PlayerRatings WHERE Context = @Context",
            new { Context = context }, transaction);

        if (existing is not null) return existing;

        _connection.Execute(
            "INSERT INTO PlayerRatings (Context) VALUES (@Context)",
            new { Context = context }, transaction);

        var id = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
        return new RatingInfo { Id = id, Rating = 1500.0, RatingDeviation = 350.0, Volatility = 0.06, GamesPlayed = 0 };
    }

    private record RunInfo(long Id, string Character, long Ascension, long Win);
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

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesPlayerRatings" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/PlayerRatingEngine.cs tests/Sts2Analytics.Core.Tests/Elo/PlayerRatingEngineTests.cs
git commit -m "feat: add PlayerRatingEngine with core processing"
```

---

### Task 5: PlayerRatingEngine — Additional Tests

**Files:**
- Modify: `tests/Sts2Analytics.Core.Tests/Elo/PlayerRatingEngineTests.cs`

- [ ] **Step 1: Add test — creates history records**

```csharp
[Fact]
public void ProcessAllRuns_RecordsHistory()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);

    var engine = new PlayerRatingEngine(conn);
    engine.ProcessAllRuns();

    var history = conn.Query("SELECT * FROM PlayerRatingHistory").ToList();
    Assert.True(history.Count >= 2); // overall + character
}
```

- [ ] **Step 2: Add test — win increases rating**

```csharp
[Fact]
public void ProcessAllRuns_WinIncreasesRating()
{
    using var conn = CreateDb();
    ImportSampleRun(conn, "sample_win.run");

    var engine = new PlayerRatingEngine(conn);
    engine.ProcessAllRuns();

    var history = conn.Query("SELECT RatingBefore, RatingAfter FROM PlayerRatingHistory WHERE Outcome = 1.0").First();
    Assert.True((double)history.RatingAfter > (double)history.RatingBefore);
}
```

- [ ] **Step 3: Add test — loss decreases rating**

```csharp
[Fact]
public void ProcessAllRuns_LossDecreasesRating()
{
    using var conn = CreateDb();
    ImportSampleRun(conn, "sample_loss.run");

    var engine = new PlayerRatingEngine(conn);
    engine.ProcessAllRuns();

    var history = conn.Query("SELECT RatingBefore, RatingAfter FROM PlayerRatingHistory WHERE Outcome = 0.0").First();
    Assert.True((double)history.RatingAfter < (double)history.RatingBefore);
}
```

- [ ] **Step 4: Add test — idempotent reprocessing**

```csharp
[Fact]
public void ProcessAllRuns_Idempotent()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);

    var engine = new PlayerRatingEngine(conn);
    engine.ProcessAllRuns();
    var countAfterFirst = conn.QueryFirst<long>("SELECT COUNT(*) FROM PlayerRatingHistory");

    engine.ProcessAllRuns();
    var countAfterSecond = conn.QueryFirst<long>("SELECT COUNT(*) FROM PlayerRatingHistory");

    Assert.Equal(countAfterFirst, countAfterSecond);
}
```

- [ ] **Step 5: Add test — opponent rating scales with ascension**

```csharp
[Fact]
public void ProcessAllRuns_OpponentRatingScalesWithAscension()
{
    using var conn = CreateDb();
    ImportSampleRun(conn);

    var engine = new PlayerRatingEngine(conn);
    engine.ProcessAllRuns();

    var run = conn.QueryFirst("SELECT Ascension FROM Runs LIMIT 1");
    var history = conn.QueryFirst("SELECT OpponentRating FROM PlayerRatingHistory LIMIT 1");

    double expectedOpponent = 1200.0 + (long)run.Ascension * 60.0;
    Assert.Equal(expectedOpponent, (double)history.OpponentRating, 1);
}
```

- [ ] **Step 6: Run all player rating tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "PlayerRatingEngineTests" -v n`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add tests/Sts2Analytics.Core.Tests/Elo/PlayerRatingEngineTests.cs
git commit -m "test: add comprehensive PlayerRatingEngine tests"
```

---

### Task 6: BlindSpot Constants and Result Models

**Files:**
- Create: `src/Sts2Analytics.Core/Analytics/BlindSpotConstants.cs`
- Create: `src/Sts2Analytics.Core/Models/BlindSpotResult.cs`

- [ ] **Step 1: Create BlindSpotConstants**

```csharp
namespace Sts2Analytics.Core.Analytics;

public static class BlindSpotConstants
{
    /// <summary>Divisor in logistic function controlling expected pick rate curve steepness.</summary>
    public const double LogisticDivisor = 200.0;

    /// <summary>Minimum blind spot score to flag a card.</summary>
    public const double ScoreThreshold = 0.02;

    /// <summary>Minimum times offered before a card can be flagged.</summary>
    public const int MinSampleSize = 5;

    /// <summary>Minimum confidence weight (1 - RD/350) to flag a card.</summary>
    public const double MinConfidenceWeight = 0.3;
}
```

- [ ] **Step 2: Create BlindSpotResult**

```csharp
namespace Sts2Analytics.Core.Models;

public record BlindSpotResult(
    string CardId, string Context, string BlindSpotType,
    double Score, double PickRate, double ExpectedPickRate,
    double WinRateDelta, int GamesAnalyzed);
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Core`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Core/Analytics/BlindSpotConstants.cs src/Sts2Analytics.Core/Models/BlindSpotResult.cs
git commit -m "feat: add BlindSpotConstants and BlindSpotResult record"
```

---

### Task 7: BlindSpotAnalyzer — Core Detection

**Files:**
- Create: `src/Sts2Analytics.Core/Analytics/BlindSpotAnalyzer.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Analytics/BlindSpotAnalyzerTests.cs`

- [ ] **Step 1: Write the failing test — detects over-pick**

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class BlindSpotAnalyzerTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static void ImportSampleRuns(SqliteConnection conn)
    {
        var repo = new RunRepository(conn);
        foreach (var fixture in new[] { "sample_win.run", "sample_loss.run" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
            var runFile = RunFileParser.Parse(path);
            var (run, floors, floorData) = RunFileMapper.Map(runFile, fixture);
            repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        }
    }

    [Fact]
    public void Analyze_ReturnsResults()
    {
        using var conn = CreateDb();
        ImportSampleRuns(conn);

        // Need card ratings first
        var g2Engine = new Glicko2Engine(conn);
        g2Engine.ProcessAllRuns();

        var analyzer = new BlindSpotAnalyzer(conn);
        var results = analyzer.Analyze();

        // Should return a list (may be empty with limited sample data, but shouldn't throw)
        Assert.NotNull(results);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Analyze_ReturnsResults" -v n`
Expected: FAIL — BlindSpotAnalyzer doesn't exist

- [ ] **Step 3: Implement BlindSpotAnalyzer**

```csharp
using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;
using static Sts2Analytics.Core.Analytics.BlindSpotConstants;

namespace Sts2Analytics.Core.Analytics;

public class BlindSpotAnalyzer
{
    private readonly IDbConnection _connection;

    public BlindSpotAnalyzer(IDbConnection connection) => _connection = connection;

    public List<BlindSpotResult> Analyze(string? character = null, int? actIndex = null)
    {
        var cardAnalytics = new CardAnalytics(_connection);
        var filter = new AnalyticsFilter(Character: character, ActIndex: actIndex);

        var pickRates = cardAnalytics.GetCardPickRates(filter).ToDictionary(c => c.CardId);
        var winRates = cardAnalytics.GetCardWinRates(filter).ToDictionary(c => c.CardId);

        // Build context string matching Glicko2Ratings convention
        string context;
        if (character != null && actIndex != null)
            context = $"{character}_ACT{actIndex + 1}";
        else if (character != null)
            context = character;
        else
            context = "overall";

        var ratings = _connection.Query<RatingRow>(
            "SELECT CardId, Rating, RatingDeviation FROM Glicko2Ratings WHERE Context = @Context",
            new { Context = context }).ToDictionary(r => r.CardId);

        var skipRating = ratings.TryGetValue("SKIP", out var skip) ? skip.Rating : 1500.0;

        var results = new List<BlindSpotResult>();

        foreach (var (cardId, pick) in pickRates)
        {
            if (cardId == "SKIP") continue;
            if (pick.TimesOffered < MinSampleSize) continue;
            if (!ratings.TryGetValue(cardId, out var rating)) continue;
            if (!winRates.TryGetValue(cardId, out var win)) continue;

            var confidenceWeight = Math.Max(0, 1.0 - rating.RatingDeviation / 350.0);
            if (confidenceWeight < MinConfidenceWeight) continue;

            var expectedPickRate = 1.0 / (1.0 + Math.Exp(-(rating.Rating - skipRating) / LogisticDivisor));
            var pickRateDeviation = pick.PickRate - expectedPickRate;
            var winRateDelta = win.WinRateDelta;

            var score = Math.Abs(pickRateDeviation) * Math.Abs(winRateDelta) * confidenceWeight;
            if (score < ScoreThreshold) continue;

            string? blindSpotType = null;
            if (pickRateDeviation > 0 && winRateDelta < 0)
                blindSpotType = "over_pick";
            else if (pickRateDeviation < 0 && winRateDelta > 0)
                blindSpotType = "under_pick";

            if (blindSpotType is null) continue;

            results.Add(new BlindSpotResult(
                cardId, context, blindSpotType, score,
                pick.PickRate, expectedPickRate, winRateDelta, pick.TimesOffered));
        }

        // Persist to DB
        PersistResults(results, context);

        return results.OrderByDescending(r => r.Score).ToList();
    }

    public List<BlindSpotResult> AnalyzeAllContexts()
    {
        var results = new List<BlindSpotResult>();
        results.AddRange(Analyze()); // overall

        var characters = _connection.Query<string>(
            "SELECT DISTINCT Character FROM Runs").ToList();

        foreach (var character in characters)
        {
            results.AddRange(Analyze(character)); // per-character
            for (int act = 0; act < 3; act++)
                results.AddRange(Analyze(character, act)); // per-character-per-act
        }

        return results;
    }

    private void PersistResults(List<BlindSpotResult> results, string context)
    {
        // Clear existing for this context
        _connection.Execute(
            "DELETE FROM BlindSpots WHERE Context = @Context",
            new { Context = context });

        foreach (var r in results)
        {
            _connection.Execute("""
                INSERT INTO BlindSpots (CardId, Context, BlindSpotType, Score,
                    PickRate, ExpectedPickRate, WinRateDelta, GamesAnalyzed, LastUpdated)
                VALUES (@CardId, @Context, @BlindSpotType, @Score,
                    @PickRate, @ExpectedPickRate, @WinRateDelta, @GamesAnalyzed, @LastUpdated)
                """, new {
                    r.CardId, r.Context, r.BlindSpotType, r.Score,
                    r.PickRate, r.ExpectedPickRate, r.WinRateDelta, r.GamesAnalyzed,
                    LastUpdated = DateTime.UtcNow.ToString("o")
                });
        }
    }

    private record RatingRow(string CardId, double Rating, double RatingDeviation);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Analyze_ReturnsResults" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Analytics/BlindSpotAnalyzer.cs tests/Sts2Analytics.Core.Tests/Analytics/BlindSpotAnalyzerTests.cs
git commit -m "feat: add BlindSpotAnalyzer with dual-signal detection"
```

---

### Task 8: BlindSpotAnalyzer — Additional Tests

**Files:**
- Modify: `tests/Sts2Analytics.Core.Tests/Analytics/BlindSpotAnalyzerTests.cs`

- [ ] **Step 1: Add test — persists results to DB**

```csharp
[Fact]
public void Analyze_PersistsToBlindSpotsTable()
{
    using var conn = CreateDb();
    ImportSampleRuns(conn);

    var g2Engine = new Glicko2Engine(conn);
    g2Engine.ProcessAllRuns();

    var analyzer = new BlindSpotAnalyzer(conn);
    analyzer.Analyze();

    // Even if no blind spots found, the table should have been cleared for this context
    var count = conn.QueryFirst<long>("SELECT COUNT(*) FROM BlindSpots");
    // Count matches the returned results
    var results = analyzer.Analyze();
    var dbCount = conn.QueryFirst<long>("SELECT COUNT(*) FROM BlindSpots WHERE Context = 'overall'");
    Assert.Equal(results.Count, (int)dbCount);
}
```

- [ ] **Step 2: Add test — expected pick rate logistic function**

```csharp
[Fact]
public void ExpectedPickRate_EqualToSkip_Returns50Percent()
{
    // Verify the logistic function by checking the math directly
    double skipRating = 1500.0;
    double cardRating = 1500.0; // equal to skip
    double expected = 1.0 / (1.0 + Math.Exp(-(cardRating - skipRating) / 200.0));
    Assert.Equal(0.5, expected, 3);
}

[Fact]
public void ExpectedPickRate_WellAboveSkip_ReturnsHigh()
{
    double skipRating = 1500.0;
    double cardRating = 1700.0; // 200 above
    double expected = 1.0 / (1.0 + Math.Exp(-(cardRating - skipRating) / 200.0));
    Assert.InRange(expected, 0.70, 0.76);
}
```

- [ ] **Step 3: Add test — AnalyzeAllContexts runs without error**

```csharp
[Fact]
public void AnalyzeAllContexts_ReturnsResults()
{
    using var conn = CreateDb();
    ImportSampleRuns(conn);

    var g2Engine = new Glicko2Engine(conn);
    g2Engine.ProcessAllRuns();

    var analyzer = new BlindSpotAnalyzer(conn);
    var results = analyzer.AnalyzeAllContexts();

    Assert.NotNull(results);
}
```

- [ ] **Step 4: Run all blind spot tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "BlindSpotAnalyzerTests" -v n`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add tests/Sts2Analytics.Core.Tests/Analytics/BlindSpotAnalyzerTests.cs
git commit -m "test: add comprehensive BlindSpotAnalyzer tests"
```

---

### Task 9: Update ModCardStats and ModOverlayData for v3

**Files:**
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`

- [ ] **Step 1: Add blind spot fields to ModCardStats**

Update the `ModCardStats` record to add blind spot fields:

```csharp
public record ModCardStats(
    string CardId, double Elo, double Rd, double PickRate,
    double WinRatePicked, double WinRateSkipped, double Delta,
    double EloAct1, double RdAct1, double EloAct2, double RdAct2, double EloAct3, double RdAct3,
    string? BlindSpot = null, double BlindSpotScore = 0,
    double BlindSpotPickRate = 0, double BlindSpotWinRateDelta = 0);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Core`
Expected: Build succeeded. Existing callers use positional args so the new optional params won't break them.

- [ ] **Step 3: Run all existing tests to check nothing breaks**

Run: `dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: All PASS

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Core/Models/AnalyticsResults.cs
git commit -m "feat: add blind spot fields to ModCardStats for v3 export"
```

---

### Task 10: Update ExportCommand for Blind Spot Data

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`

- [ ] **Step 1: Read the current ExportCommand**

Read `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` fully to understand the current mod export flow.

- [ ] **Step 2: Add blind spot processing to mod export**

After the existing Glicko-2 processing block (where `g2Count == 0` is checked), add:

```csharp
// Process player ratings
var playerEngine = new PlayerRatingEngine(conn);
playerEngine.ProcessAllRuns();

// Compute blind spots
var blindSpotAnalyzer = new BlindSpotAnalyzer(conn);
blindSpotAnalyzer.AnalyzeAllContexts();
```

Then, in the card building loop, look up blind spot data for each card:

```csharp
var blindSpots = conn.Query(
    "SELECT CardId, BlindSpotType, Score, PickRate, WinRateDelta FROM BlindSpots WHERE Context = 'overall'")
    .ToDictionary(b => (string)b.CardId);
```

And when constructing each `ModCardStats`, add the blind spot fields:

```csharp
string? blindSpotType = null;
double bsScore = 0, bsPickRate = 0, bsWinDelta = 0;
if (blindSpots.TryGetValue(id, out var bs))
{
    blindSpotType = (string)bs.BlindSpotType;
    bsScore = (double)bs.Score;
    bsPickRate = (double)bs.PickRate;
    bsWinDelta = (double)bs.WinRateDelta;
}

return new ModCardStats(id, elo, rd, pickRate, winPicked, winSkipped, delta,
    act1, rdAct1, act2, rdAct2, act3, rdAct3,
    blindSpotType, bsScore, bsPickRate, bsWinDelta);
```

Update the version number from 2 to 3 in the `ModOverlayData` constructor.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Cli`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/ExportCommand.cs
git commit -m "feat: include blind spot data in mod export, bump to v3"
```

---

### Task 11: Update RatingCommand with --player Flag

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/RatingCommand.cs`

- [ ] **Step 1: Read the current RatingCommand**

Read `src/Sts2Analytics.Cli/Commands/RatingCommand.cs` fully.

- [ ] **Step 2: Add --player option**

Add a `--player` boolean option to the command:

```csharp
var playerOption = new Option<bool>("--player", "Show personal player ratings instead of card ratings");
command.AddOption(playerOption);
```

In the handler, when `--player` is true, query `PlayerRatings` and display:

```csharp
if (player)
{
    var playerEngine = new PlayerRatingEngine(conn);
    playerEngine.ProcessAllRuns();

    var playerRatings = conn.Query(
        "SELECT Context, Rating, RatingDeviation, Volatility, GamesPlayed FROM PlayerRatings ORDER BY Rating DESC")
        .ToList();

    Console.WriteLine();
    Console.WriteLine("  Player Ratings");
    Console.WriteLine("  ─────────────────────────────────────────────");
    Console.WriteLine($"  {"Context",-15} {"Rating",8} {"±RD",6} {"Games",7}");
    Console.WriteLine("  ─────────────────────────────────────────────");

    foreach (var r in playerRatings)
    {
        Console.WriteLine($"  {(string)r.Context,-15} {(double)r.Rating,8:F0} {(double)r.RatingDeviation,6:F0} {(long)r.GamesPlayed,7}");
    }

    Console.WriteLine();
    return;
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Cli`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/RatingCommand.cs
git commit -m "feat: add --player flag to rating command"
```

---

### Task 12: Update Mod OverlayData for Blind Spot Fields

**Files:**
- Modify: `src/Sts2Analytics.Mod/Data/OverlayData.cs`

- [ ] **Step 1: Add blind spot fields to CardStats record**

```csharp
public record CardStats(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("elo")] double Elo,
    [property: JsonPropertyName("rd")] double Rd = 350,
    [property: JsonPropertyName("pickRate")] double PickRate = 0,
    [property: JsonPropertyName("winRatePicked")] double WinRatePicked = 0,
    [property: JsonPropertyName("winRateSkipped")] double WinRateSkipped = 0,
    [property: JsonPropertyName("delta")] double Delta = 0,
    [property: JsonPropertyName("eloAct1")] double EloAct1 = 0,
    [property: JsonPropertyName("rdAct1")] double RdAct1 = 350,
    [property: JsonPropertyName("eloAct2")] double EloAct2 = 0,
    [property: JsonPropertyName("rdAct2")] double RdAct2 = 350,
    [property: JsonPropertyName("eloAct3")] double EloAct3 = 0,
    [property: JsonPropertyName("rdAct3")] double RdAct3 = 350,
    [property: JsonPropertyName("blindSpot")] string? BlindSpot = null,
    [property: JsonPropertyName("blindSpotScore")] double BlindSpotScore = 0,
    [property: JsonPropertyName("blindSpotPickRate")] double BlindSpotPickRate = 0,
    [property: JsonPropertyName("blindSpotWinRateDelta")] double BlindSpotWinRateDelta = 0);
```

- [ ] **Step 2: Verify the mod project compiles**

Run: `dotnet build src/Sts2Analytics.Mod` (or verify manually if Godot build is needed)
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/Data/OverlayData.cs
git commit -m "feat: add blind spot fields to mod CardStats record"
```

---

### Task 13: Render Blind Spot Badges in Overlay

**Files:**
- Modify: `src/Sts2Analytics.Mod/UI/OverlayFactory.cs`

- [ ] **Step 1: Read the current OverlayFactory**

Read `src/Sts2Analytics.Mod/UI/OverlayFactory.cs` fully to understand the existing badge rendering.

- [ ] **Step 2: Add blind spot badge rendering**

In the `AddOverlay` method, after the existing Elo badge creation and before adding the strip to the card holder, add a blind spot badge:

```csharp
// --- Blind Spot Badge (top-right corner) ---
if (!string.IsNullOrEmpty(stats.BlindSpot))
{
    var bsBadge = new PanelContainer();
    bsBadge.Name = "SpireOracleBlindSpot";
    bsBadge.AddToGroup(OverlayGroup);

    var bsStyle = new StyleBoxFlat();
    bsStyle.CornerRadiusBottomLeft = 4;
    bsStyle.CornerRadiusBottomRight = 4;
    bsStyle.CornerRadiusTopLeft = 4;
    bsStyle.CornerRadiusTopRight = 4;
    bsStyle.ContentMarginLeft = 6;
    bsStyle.ContentMarginRight = 6;
    bsStyle.ContentMarginTop = 2;
    bsStyle.ContentMarginBottom = 2;

    var isOverPick = stats.BlindSpot == "over_pick";
    bsStyle.BgColor = isOverPick
        ? new Color(0.94f, 0.27f, 0.27f) // red
        : new Color(0.96f, 0.62f, 0.04f); // amber

    bsBadge.AddThemeStyleboxOverride("panel", bsStyle);

    var bsLabel = new Label();
    bsLabel.Text = isOverPick ? "⚠ OVER-PICK" : "⚠ UNDER-PICK";
    bsLabel.AddThemeFontSizeOverride("font_size", 18);
    bsLabel.AddThemeColorOverride("font_color", isOverPick
        ? Colors.White
        : new Color(0.1f, 0.1f, 0.1f));
    bsBadge.AddChild(bsLabel);

    // Position top-right
    bsBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
    bsBadge.Position = new Vector2(-10, -10);
    cardHolder.AddChild(bsBadge);
}
```

- [ ] **Step 3: Add blind spot info to hover detail panel**

In the detail panel section of `AddOverlay`, after the existing stat rows, add:

```csharp
if (!string.IsNullOrEmpty(stats.BlindSpot))
{
    var bsType = stats.BlindSpot == "over_pick" ? "Over-pick" : "Under-pick";
    AddStatRow(vbox, "Blind Spot", $"{bsType} (score: {stats.BlindSpotScore:F2})");
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/UI/OverlayFactory.cs
git commit -m "feat: render blind spot badges on card reward screen"
```

---

### Task 14: Dashboard — Player Rating Page

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/PlayerRating.razor`
- Modify: `src/Sts2Analytics.Web/Layout/MainLayout.razor`
- Modify: `src/Sts2Analytics.Web/Services/DataService.cs`

- [ ] **Step 1: Add player rating data to ExportData**

In `DataService.cs`, add to the `ExportData` class:

```csharp
public List<PlayerRatingExport> PlayerRatings { get; set; } = [];
public List<PlayerRatingHistoryExport> PlayerRatingHistory { get; set; } = [];
```

And add the export records:

```csharp
public class PlayerRatingExport
{
    public string Context { get; set; } = "";
    public double Rating { get; set; }
    public double RatingDeviation { get; set; }
    public int GamesPlayed { get; set; }
}

public class PlayerRatingHistoryExport
{
    public string Context { get; set; } = "";
    public long RunId { get; set; }
    public double RatingBefore { get; set; }
    public double RatingAfter { get; set; }
    public string Opponent { get; set; } = "";
    public double Outcome { get; set; }
}
```

- [ ] **Step 2: Create PlayerRating.razor**

Create the page with per-character rating cards and a recent runs history table. Follow the same patterns as `RatingLeaderboard.razor` for data loading and filtering:

```razor
@page "/player-rating"
@inject DataService Data
@inject FilterState Filter

<div class="oracle-page">
    <h2 class="page-title">My Rating</h2>
    <p class="page-subtitle">Your Glicko-2 player rating per character</p>

    @if (_loading)
    {
        <p class="loading">Loading...</p>
    }
    else
    {
        <div class="rating-cards">
            @foreach (var r in _ratings)
            {
                <div class="player-rating-card @(r.Context == _selectedContext ? "selected" : "")"
                     @onclick="() => SelectContext(r.Context)">
                    <div class="rating-context">@FormatContext(r.Context)</div>
                    <div class="rating-value">@r.Rating.ToString("F0")</div>
                    <div class="rating-rd">±@r.RatingDeviation.ToString("F0") RD</div>
                    <div class="rating-games">@r.GamesPlayed games</div>
                </div>
            }
        </div>

        <h3>Recent Runs</h3>
        <table class="oracle-table">
            <thead>
                <tr>
                    <th>Character</th>
                    <th>Opponent</th>
                    <th>Result</th>
                    <th>Rating Change</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var h in _history.Take(20))
                {
                    var change = h.RatingAfter - h.RatingBefore;
                    <tr>
                        <td>@FormatContext(h.Context)</td>
                        <td>@h.Opponent</td>
                        <td class="@(h.Outcome > 0 ? "win" : "loss")">@(h.Outcome > 0 ? "WIN" : "LOSS")</td>
                        <td class="@(change > 0 ? "positive" : "negative")">
                            @(change > 0 ? "+" : "")@change.ToString("F0")
                            (@h.RatingBefore.ToString("F0") → @h.RatingAfter.ToString("F0"))
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    private bool _loading = true;
    private List<PlayerRatingExport> _ratings = [];
    private List<PlayerRatingHistoryExport> _history = [];
    private string _selectedContext = "overall";

    protected override async Task OnInitializedAsync()
    {
        var data = await Data.GetDataAsync();
        _ratings = data.PlayerRatings.OrderByDescending(r => r.Rating).ToList();
        _history = data.PlayerRatingHistory
            .Where(h => h.Context == _selectedContext)
            .OrderByDescending(h => h.RunId)
            .ToList();
        _loading = false;
    }

    private async Task SelectContext(string context)
    {
        _selectedContext = context;
        var data = await Data.GetDataAsync();
        _history = data.PlayerRatingHistory
            .Where(h => h.Context == context)
            .OrderByDescending(h => h.RunId)
            .ToList();
    }

    private string FormatContext(string context) =>
        context == "overall" ? "Overall" : context.Replace("_", " ");
}
```

- [ ] **Step 3: Add nav link in MainLayout.razor**

After the "Economy" nav link, add a new section:

```razor
<div class="nav-section-label">Player</div>
<NavLink class="nav-link" href="player-rating">
    <span class="nav-icon">&#x2605;</span> My Rating
</NavLink>
<NavLink class="nav-link" href="blind-spots">
    <span class="nav-icon">&#x25CE;</span> Blind Spots
</NavLink>
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Web`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Web/Pages/PlayerRating.razor src/Sts2Analytics.Web/Layout/MainLayout.razor src/Sts2Analytics.Web/Services/DataService.cs
git commit -m "feat: add My Rating dashboard page"
```

---

### Task 15: Dashboard — Blind Spots Page

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/BlindSpots.razor`
- Modify: `src/Sts2Analytics.Web/Services/DataService.cs`

- [ ] **Step 1: Add blind spot data to ExportData**

In `DataService.cs`, add to `ExportData`:

```csharp
public List<BlindSpotExport> BlindSpots { get; set; } = [];
```

And the export record:

```csharp
public class BlindSpotExport
{
    public string CardId { get; set; } = "";
    public string Context { get; set; } = "";
    public string BlindSpotType { get; set; } = "";
    public double Score { get; set; }
    public double PickRate { get; set; }
    public double ExpectedPickRate { get; set; }
    public double WinRateDelta { get; set; }
    public int GamesAnalyzed { get; set; }
}
```

- [ ] **Step 2: Create BlindSpots.razor**

```razor
@page "/blind-spots"
@inject DataService Data
@inject FilterState Filter

<div class="oracle-page">
    <h2 class="page-title">Blind Spots</h2>
    <p class="page-subtitle">Cards where your pick behavior and outcomes disagree</p>

    @if (_loading)
    {
        <p class="loading">Loading...</p>
    }
    else
    {
        <div class="filter-bar">
            <button class="filter-btn @(_contextFilter == null ? "active" : "")"
                    @onclick="() => SetContext(null)">All</button>
            @foreach (var ctx in _contexts)
            {
                <button class="filter-btn @(_contextFilter == ctx ? "active" : "")"
                        @onclick="() => SetContext(ctx)">@FormatContext(ctx)</button>
            }
        </div>

        @if (_overPicks.Any())
        {
            <h3 class="blind-spot-header over-pick">Over-Picks — Cards Hurting You</h3>
            <table class="oracle-table">
                <thead>
                    <tr><th>Card</th><th>Rating</th><th>Pick Rate</th><th>Win Δ</th><th>Score</th></tr>
                </thead>
                <tbody>
                    @foreach (var bs in _overPicks)
                    {
                        <tr>
                            <td>@FormatName(bs.CardId)</td>
                            <td>@GetRating(bs.CardId).ToString("F0")</td>
                            <td>@bs.PickRate.ToString("P0")</td>
                            <td class="negative">@bs.WinRateDelta.ToString("+0.0%;-0.0%")</td>
                            <td>@bs.Score.ToString("F2")</td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        @if (_underPicks.Any())
        {
            <h3 class="blind-spot-header under-pick">Under-Picks — Cards You're Sleeping On</h3>
            <table class="oracle-table">
                <thead>
                    <tr><th>Card</th><th>Rating</th><th>Pick Rate</th><th>Win Δ</th><th>Score</th></tr>
                </thead>
                <tbody>
                    @foreach (var bs in _underPicks)
                    {
                        <tr>
                            <td>@FormatName(bs.CardId)</td>
                            <td>@GetRating(bs.CardId).ToString("F0")</td>
                            <td>@bs.PickRate.ToString("P0")</td>
                            <td class="positive">@bs.WinRateDelta.ToString("+0.0%;-0.0%")</td>
                            <td>@bs.Score.ToString("F2")</td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        @if (!_overPicks.Any() && !_underPicks.Any())
        {
            <p class="empty-state">No blind spots detected. Play more runs or adjust filters.</p>
        }
    }
</div>

@code {
    private bool _loading = true;
    private string? _contextFilter;
    private List<string> _contexts = [];
    private List<BlindSpotExport> _overPicks = [];
    private List<BlindSpotExport> _underPicks = [];
    private ExportData? _data;

    protected override async Task OnInitializedAsync()
    {
        _data = await Data.GetDataAsync();
        _contexts = _data.BlindSpots
            .Select(b => b.Context)
            .Where(c => c != "overall")
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        ApplyFilter();
        _loading = false;
    }

    private void SetContext(string? context)
    {
        _contextFilter = context;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = _data!.BlindSpots.AsEnumerable();
        if (_contextFilter != null)
            filtered = filtered.Where(b => b.Context == _contextFilter);
        else
            filtered = filtered.Where(b => b.Context == "overall");

        _overPicks = filtered.Where(b => b.BlindSpotType == "over_pick")
            .OrderByDescending(b => b.Score).ToList();
        _underPicks = filtered.Where(b => b.BlindSpotType == "under_pick")
            .OrderByDescending(b => b.Score).ToList();
    }

    private double GetRating(string cardId)
    {
        var rating = _data?.Glicko2Ratings.FirstOrDefault(r => r.CardId == cardId && r.Context == "overall");
        return rating?.Rating ?? 0;
    }

    private string FormatName(string cardId) =>
        cardId.Replace("CARD.", "").Replace("_", " ").Replace("+", " +");

    private string FormatContext(string context) =>
        context == "overall" ? "Overall" : context.Replace("_", " ");
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Web`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Web/Pages/BlindSpots.razor src/Sts2Analytics.Web/Services/DataService.cs
git commit -m "feat: add Blind Spots dashboard page"
```

---

### Task 16: Update Dashboard Export to Include New Data

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`

- [ ] **Step 1: Read the current dashboard export logic**

Read the full `ExportCommand.cs` to find the dashboard export section (separate from the mod export).

- [ ] **Step 2: Add player ratings and blind spots to dashboard export**

In the dashboard export section, after the existing data queries, add:

```csharp
// Player ratings
var playerRatings = conn.Query(
    "SELECT Context, Rating, RatingDeviation, Volatility, GamesPlayed FROM PlayerRatings")
    .Select(r => new { Context = (string)r.Context, Rating = (double)r.Rating,
        RatingDeviation = (double)r.RatingDeviation, GamesPlayed = (int)(long)r.GamesPlayed })
    .ToList();

// Player rating history
var playerHistory = conn.Query("""
    SELECT pr.Context, ph.RunId, ph.RatingBefore, ph.RatingAfter,
           ph.Opponent, ph.Outcome
    FROM PlayerRatingHistory ph
    JOIN PlayerRatings pr ON ph.PlayerRatingId = pr.Id
    ORDER BY ph.RunId DESC
    """).Select(h => new { Context = (string)h.Context, RunId = (long)h.RunId,
        RatingBefore = (double)h.RatingBefore, RatingAfter = (double)h.RatingAfter,
        Opponent = (string)h.Opponent, Outcome = (double)h.Outcome })
    .ToList();

// Blind spots
var blindSpotData = conn.Query(
    "SELECT CardId, Context, BlindSpotType, Score, PickRate, ExpectedPickRate, WinRateDelta, GamesAnalyzed FROM BlindSpots")
    .Select(b => new { CardId = (string)b.CardId, Context = (string)b.Context,
        BlindSpotType = (string)b.BlindSpotType, Score = (double)b.Score,
        PickRate = (double)b.PickRate, ExpectedPickRate = (double)b.ExpectedPickRate,
        WinRateDelta = (double)b.WinRateDelta, GamesAnalyzed = (int)(long)b.GamesAnalyzed })
    .ToList();
```

Add these collections to the exported JSON object.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Cli`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/ExportCommand.cs
git commit -m "feat: include player ratings and blind spots in dashboard export"
```

---

### Task 17: Run Full Test Suite and Verify

**Files:** None (verification only)

- [ ] **Step 1: Run all tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: All tests PASS, no regressions

- [ ] **Step 2: Build all projects**

Run: `dotnet build Sts2Analytics.sln`
Expected: Build succeeded for all projects

- [ ] **Step 3: Verify export generates valid v3 JSON**

If sample data is available, run: `dotnet run --project src/Sts2Analytics.Cli -- export --mod --output /tmp/test_overlay.json --db <test-db-path>`
Verify the JSON contains `"version": 3` and `blindSpot` fields on cards.

- [ ] **Step 4: Commit any fixes if needed**

Only if issues were found in steps 1-3.

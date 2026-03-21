# Ancient Choice Glicko-2 Ratings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Glicko-2 ratings for ancient choices (Neow rewards + post-act ancients), with separate DB tables, CLI support, mod overlay badges, and a dashboard tab.

**Architecture:** New `AncientRatingEngine` following the same pattern as `Glicko2Engine`. Separate `AncientGlicko2Ratings` and `AncientGlicko2History` tables. Each ancient floor's chosen option beats unchosen options. Ratings computed in 3 contexts: (ALL, overall), (ALL, timing), (character, timing).

**Tech Stack:** C#/.NET 9.0, SQLite/Dapper, xUnit, Godot 4.5.1/Harmony 2.4.2, Blazor WASM

**Spec:** `docs/superpowers/specs/2026-03-21-ancient-ratings-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Core/Elo/AncientRatingEngine.cs` | Process ancient choices per run, compute Glicko-2 ratings |
| `tests/Sts2Analytics.Core.Tests/Elo/AncientRatingEngineTests.cs` | Engine tests |
| `src/Sts2Analytics.Web/Pages/AncientRatings.razor` | Dashboard: Ancient Ratings tab |

### Modified Files
| File | Changes |
|------|---------|
| `src/Sts2Analytics.Core/Database/Schema.cs` | Add AncientGlicko2Ratings + AncientGlicko2History tables |
| `src/Sts2Analytics.Core/Models/AnalyticsResults.cs` | Add `ModAncientStats` record, update `ModOverlayData` |
| `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | Include ancient ratings in mod + dashboard export |
| `src/Sts2Analytics.Cli/Commands/RatingCommand.cs` | Add `--ancient` flag |
| `src/Sts2Analytics.Mod/Data/OverlayData.cs` | Add `AncientStats` record, update `OverlayData` |
| `src/Sts2Analytics.Mod/Data/DataLoader.cs` | Load ancient choice data |
| `src/Sts2Analytics.Mod/UI/OverlayFactory.cs` | Add `AddAncientOverlay` method |
| `src/Sts2Analytics.Web/Layout/MainLayout.razor` | Add nav link |
| `src/Sts2Analytics.Web/Services/DataService.cs` | Add ancient rating export classes |

---

### Task 1: DB Schema — AncientGlicko2Ratings and AncientGlicko2History Tables

**Files:**
- Modify: `src/Sts2Analytics.Core/Database/Schema.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Initialize_CreatesAncientGlicko2Tables()
{
    using var conn = new SqliteConnection("Data Source=:memory:");
    conn.Open();
    Schema.Initialize(conn);

    var ratings = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='AncientGlicko2Ratings'").ToList();
    Assert.Single(ratings);

    var history = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='AncientGlicko2History'").ToList();
    Assert.Single(history);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesAncientGlicko2Tables" -v n`
Expected: FAIL

- [ ] **Step 3: Add tables to Schema.cs**

Add after the existing `BlindSpots` table:

```sql
CREATE TABLE IF NOT EXISTS AncientGlicko2Ratings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ChoiceKey TEXT NOT NULL,
    Character TEXT NOT NULL,
    Context TEXT NOT NULL DEFAULT 'overall',
    Rating REAL NOT NULL DEFAULT 1500.0,
    RatingDeviation REAL NOT NULL DEFAULT 350.0,
    Volatility REAL NOT NULL DEFAULT 0.06,
    GamesPlayed INTEGER NOT NULL DEFAULT 0,
    LastUpdatedRunId INTEGER,
    UNIQUE(ChoiceKey, Character, Context)
);

CREATE TABLE IF NOT EXISTS AncientGlicko2History (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AncientGlicko2RatingId INTEGER NOT NULL REFERENCES AncientGlicko2Ratings(Id),
    RunId INTEGER NOT NULL REFERENCES Runs(Id),
    RatingBefore REAL NOT NULL DEFAULT 0,
    RatingAfter REAL NOT NULL DEFAULT 0,
    RdBefore REAL NOT NULL DEFAULT 0,
    RdAfter REAL NOT NULL DEFAULT 0,
    VolatilityBefore REAL NOT NULL DEFAULT 0,
    VolatilityAfter REAL NOT NULL DEFAULT 0,
    Timestamp TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS IX_AncientGlicko2Ratings_ChoiceKey ON AncientGlicko2Ratings(ChoiceKey);
CREATE INDEX IF NOT EXISTS IX_AncientGlicko2History_RatingId ON AncientGlicko2History(AncientGlicko2RatingId);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "Initialize_CreatesAncientGlicko2Tables" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Database/Schema.cs tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs
git commit -m "feat: add AncientGlicko2Ratings and AncientGlicko2History tables"
```

---

### Task 2: AncientRatingEngine — Core Processing

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/AncientRatingEngine.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Elo/AncientRatingEngineTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class AncientRatingEngineTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportSampleRun(SqliteConnection conn, string fixture = "sample_loss.run")
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, fixture);
        return repo.ImportRun(run, floors, floorData, runFile.Players[0]);
    }

    [Fact]
    public void ProcessAllRuns_CreatesAncientRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_loss.run"); // sample_loss.run has ancient choices

        var engine = new AncientRatingEngine(conn);
        engine.ProcessAllRuns();

        var ratings = conn.Query("SELECT * FROM AncientGlicko2Ratings").ToList();
        Assert.True(ratings.Count >= 1);
    }
}
```

Note: `sample_loss.run` is used because it contains the `EVENT.NEOW` ancient choice data. `sample_win.run` has post-act ancient data.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesAncientRatings" -v n`
Expected: FAIL — AncientRatingEngine doesn't exist

- [ ] **Step 3: Implement AncientRatingEngine**

```csharp
using System.Data;
using Dapper;
using static Sts2Analytics.Core.Elo.Glicko2Calculator;

namespace Sts2Analytics.Core.Elo;

public class AncientRatingEngine
{
    private readonly IDbConnection _connection;

    public AncientRatingEngine(IDbConnection connection) => _connection = connection;

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM AncientGlicko2History ah
                JOIN AncientGlicko2Ratings ar ON ah.AncientGlicko2RatingId = ar.Id
                WHERE ah.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        foreach (var runId in unprocessedRunIds)
            ProcessRun(runId);
    }

    private void ProcessRun(long runId)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character FROM Runs WHERE Id = @RunId",
            new { RunId = runId });
        if (run is null) return;

        // Get all ancient floors for this run with their choices
        var choices = _connection.Query<AncientChoiceRow>("""
            SELECT ac.TextKey, ac.WasChosen, f.ActIndex, f.Id as FloorId
            FROM AncientChoices ac
            JOIN Floors f ON ac.FloorId = f.Id
            WHERE f.RunId = @RunId
            ORDER BY f.Id
            """, new { RunId = runId }).ToList();

        if (choices.Count == 0) return;

        // Group by floor (each floor is one ancient choice screen)
        var choicesByFloor = choices.GroupBy(c => c.FloorId).ToList();

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var floorGroup in choicesByFloor)
            {
                var floorChoices = floorGroup.ToList();
                var picked = floorChoices.Where(c => c.WasChosen).ToList();
                var skipped = floorChoices.Where(c => !c.WasChosen).ToList();

                if (picked.Count == 0 || skipped.Count == 0) continue;

                var actIndex = floorChoices[0].ActIndex;
                var timingContext = actIndex switch
                {
                    0 => "neow",
                    1 => "post_act1",
                    2 => "post_act2",
                    _ => $"post_act{actIndex}"
                };

                // Build matchups: each picked beats each skipped
                var matchups = new Dictionary<string, List<(string Opponent, double Score)>>();

                foreach (var p in picked)
                {
                    if (!matchups.ContainsKey(p.TextKey))
                        matchups[p.TextKey] = new();
                    foreach (var s in skipped)
                        matchups[p.TextKey].Add((s.TextKey, 1.0));
                }

                foreach (var s in skipped)
                {
                    if (!matchups.ContainsKey(s.TextKey))
                        matchups[s.TextKey] = new();
                    foreach (var p in picked)
                        matchups[s.TextKey].Add((p.TextKey, 0.0));
                }

                // 3 contexts: (ALL, overall), (ALL, timing), (character, timing)
                var contexts = new[]
                {
                    ("ALL", "overall"),
                    ("ALL", timingContext),
                    (run.Character, timingContext)
                };

                foreach (var (character, context) in contexts)
                {
                    foreach (var (choiceKey, opponents) in matchups)
                    {
                        var rating = GetOrCreateRating(choiceKey, character, context, transaction);
                        var currentRating = new Glicko2Rating(rating.Rating, rating.RatingDeviation, rating.Volatility);

                        // Apply inactivity decay for runs where this choice was absent
                        if (rating.LastUpdatedRunId is not null)
                        {
                            var runsWithChoice = _connection.QueryFirstOrDefault<int>("""
                                SELECT COUNT(DISTINCT f.RunId) FROM AncientChoices ac
                                JOIN Floors f ON ac.FloorId = f.Id
                                WHERE f.RunId > @LastRunId AND f.RunId < @CurrentRunId
                                AND ac.TextKey = @ChoiceKey
                                """, new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId,
                                    ChoiceKey = choiceKey }, transaction);
                            var totalRunsBetween = _connection.QueryFirstOrDefault<int>(
                                "SELECT COUNT(*) FROM Runs WHERE Id > @LastRunId AND Id < @CurrentRunId",
                                new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId }, transaction);
                            var missedRuns = totalRunsBetween - runsWithChoice;
                            for (int i = 0; i < missedRuns; i++)
                                currentRating = ApplyInactivityDecay(currentRating);
                        }

                        var opponentRatings = opponents.Select(o =>
                        {
                            var oppRating = GetOrCreateRating(o.Opponent, character, context, transaction);
                            return (Rating: new Glicko2Rating(oppRating.Rating, oppRating.RatingDeviation, oppRating.Volatility),
                                    Score: o.Score);
                        }).ToArray();

                        var newRating = UpdateRating(currentRating, opponentRatings);

                        _connection.Execute("""
                            INSERT INTO AncientGlicko2History
                                (AncientGlicko2RatingId, RunId, RatingBefore, RatingAfter,
                                 RdBefore, RdAfter, VolatilityBefore, VolatilityAfter, Timestamp)
                            VALUES (@RatingId, @RunId, @RatingBefore, @RatingAfter,
                                    @RdBefore, @RdAfter, @VolBefore, @VolAfter, @Timestamp)
                            """, new {
                                RatingId = rating.Id, RunId = runId,
                                RatingBefore = currentRating.Rating, RatingAfter = newRating.Rating,
                                RdBefore = currentRating.RatingDeviation, RdAfter = newRating.RatingDeviation,
                                VolBefore = currentRating.Volatility, VolAfter = newRating.Volatility,
                                Timestamp = DateTime.UtcNow.ToString("o")
                            }, transaction);

                        _connection.Execute("""
                            UPDATE AncientGlicko2Ratings
                            SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                                GamesPlayed = GamesPlayed + 1, LastUpdatedRunId = @RunId
                            WHERE Id = @Id
                            """, new {
                                Rating = newRating.Rating, Rd = newRating.RatingDeviation,
                                Vol = newRating.Volatility, RunId = runId, Id = rating.Id
                            }, transaction);
                    }
                }
            }

            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private RatingInfo GetOrCreateRating(string choiceKey, string character, string context, IDbTransaction transaction)
    {
        var existing = _connection.QueryFirstOrDefault<RatingInfo>(
            "SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId FROM AncientGlicko2Ratings WHERE ChoiceKey = @ChoiceKey AND Character = @Character AND Context = @Context",
            new { ChoiceKey = choiceKey, Character = character, Context = context }, transaction);

        if (existing is not null) return existing;

        _connection.Execute(
            "INSERT INTO AncientGlicko2Ratings (ChoiceKey, Character, Context) VALUES (@ChoiceKey, @Character, @Context)",
            new { ChoiceKey = choiceKey, Character = character, Context = context }, transaction);

        var id = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
        return new RatingInfo { Id = id, Rating = 1500.0, RatingDeviation = 350.0, Volatility = 0.06, GamesPlayed = 0 };
    }

    private record RunInfo(long Id, string Character);
    private record AncientChoiceRow(string TextKey, bool WasChosen, long ActIndex, long FloorId);
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

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "ProcessAllRuns_CreatesAncientRatings" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/AncientRatingEngine.cs tests/Sts2Analytics.Core.Tests/Elo/AncientRatingEngineTests.cs
git commit -m "feat: add AncientRatingEngine with core processing"
```

---

### Task 3: AncientRatingEngine — Additional Tests

**Files:**
- Modify: `tests/Sts2Analytics.Core.Tests/Elo/AncientRatingEngineTests.cs`

- [ ] **Step 1: Add test — records history**

```csharp
[Fact]
public void ProcessAllRuns_RecordsHistory()
{
    using var conn = CreateDb();
    ImportSampleRun(conn, "sample_loss.run");
    var engine = new AncientRatingEngine(conn);
    engine.ProcessAllRuns();
    var history = conn.Query("SELECT * FROM AncientGlicko2History").ToList();
    Assert.True(history.Count >= 1);
}
```

- [ ] **Step 2: Add test — creates 3 context rows per choice**

```csharp
[Fact]
public void ProcessAllRuns_Creates3ContextsPerChoice()
{
    using var conn = CreateDb();
    ImportSampleRun(conn, "sample_loss.run");
    var engine = new AncientRatingEngine(conn);
    engine.ProcessAllRuns();

    // Pick any choice that was in the ancient floor
    var firstChoice = conn.QueryFirst<string>("SELECT DISTINCT ChoiceKey FROM AncientGlicko2Ratings LIMIT 1");
    var contexts = conn.Query("SELECT Character, Context FROM AncientGlicko2Ratings WHERE ChoiceKey = @Key",
        new { Key = firstChoice }).ToList();

    // Should have 3 rows: (ALL, overall), (ALL, timing), (character, timing)
    Assert.Equal(3, contexts.Count);
    Assert.Contains(contexts, c => (string)c.Character == "ALL" && (string)c.Context == "overall");
}
```

- [ ] **Step 3: Add test — idempotent**

```csharp
[Fact]
public void ProcessAllRuns_Idempotent()
{
    using var conn = CreateDb();
    ImportSampleRun(conn, "sample_loss.run");
    var engine = new AncientRatingEngine(conn);
    engine.ProcessAllRuns();
    var countFirst = conn.QueryFirst<long>("SELECT COUNT(*) FROM AncientGlicko2History");
    engine.ProcessAllRuns();
    var countSecond = conn.QueryFirst<long>("SELECT COUNT(*) FROM AncientGlicko2History");
    Assert.Equal(countFirst, countSecond);
}
```

- [ ] **Step 4: Add test — processes post-act ancients from win run**

```csharp
[Fact]
public void ProcessAllRuns_HandlesPostActAncients()
{
    using var conn = CreateDb();
    ImportSampleRun(conn, "sample_win.run"); // has post-act ancient floors
    var engine = new AncientRatingEngine(conn);
    engine.ProcessAllRuns();

    var contexts = conn.Query<string>("SELECT DISTINCT Context FROM AncientGlicko2Ratings").ToList();
    // Should have overall + at least one timing context
    Assert.Contains("overall", contexts);
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "AncientRatingEngineTests" -v n`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add tests/Sts2Analytics.Core.Tests/Elo/AncientRatingEngineTests.cs
git commit -m "test: add comprehensive AncientRatingEngine tests"
```

---

### Task 4: Update ModOverlayData and Add ModAncientStats

**Files:**
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`

- [ ] **Step 1: Add ModAncientStats record**

```csharp
public record ModAncientStats(
    string ChoiceKey, double Rating, double Rd,
    double RatingNeow, double RdNeow,
    double RatingPostAct1, double RdPostAct1,
    double RatingPostAct2, double RdPostAct2);
```

- [ ] **Step 2: Update ModOverlayData to include ancient choices**

```csharp
public record ModOverlayData(
    int Version, string ExportedAt, double SkipElo,
    Dictionary<string, double> SkipEloByAct,
    List<ModCardStats> Cards,
    List<ModAncientStats>? AncientChoices = null);
```

- [ ] **Step 3: Verify it compiles and tests pass**

Run: `dotnet build src/Sts2Analytics.Core && dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: Build succeeded, all tests PASS

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Core/Models/AnalyticsResults.cs
git commit -m "feat: add ModAncientStats record and update ModOverlayData"
```

---

### Task 5: Update ExportCommand for Ancient Ratings

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`

- [ ] **Step 1: Read the current ExportCommand**

Read `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` fully.

- [ ] **Step 2: Add ancient rating processing to mod export**

After the existing blind spot processing, add:

```csharp
// Process ancient ratings
var ancientEngine = new AncientRatingEngine(conn);
ancientEngine.ProcessAllRuns();
```

Then build the ancient stats list for the mod export:

```csharp
var ancientRatings = conn.Query("""
    SELECT ChoiceKey, Character, Context, Rating, RatingDeviation
    FROM AncientGlicko2Ratings
    """).ToList();

var ancientByKey = ancientRatings.GroupBy(r => (string)r.ChoiceKey).ToList();

var ancientStats = ancientByKey.Select(g =>
{
    var key = g.Key;
    double rating = 0, rd = 350;
    double rNeow = 0, rdNeow = 350;
    double rPost1 = 0, rdPost1 = 350;
    double rPost2 = 0, rdPost2 = 350;

    foreach (var r in g)
    {
        var ctx = (string)r.Context;
        var chr = (string)r.Character;
        if (chr == "ALL" && ctx == "overall") { rating = (double)r.Rating; rd = (double)r.RatingDeviation; }
        else if (chr == "ALL" && ctx == "neow") { rNeow = (double)r.Rating; rdNeow = (double)r.RatingDeviation; }
        else if (chr == "ALL" && ctx == "post_act1") { rPost1 = (double)r.Rating; rdPost1 = (double)r.RatingDeviation; }
        else if (chr == "ALL" && ctx == "post_act2") { rPost2 = (double)r.Rating; rdPost2 = (double)r.RatingDeviation; }
    }

    return new ModAncientStats(key, rating, rd, rNeow, rdNeow, rPost1, rdPost1, rPost2, rdPost2);
}).ToList();
```

Pass `ancientStats` as the `AncientChoices` parameter when constructing `ModOverlayData`.

- [ ] **Step 3: Add ancient ratings to dashboard export**

In the dashboard export section, add:

```csharp
var ancientRatingExport = conn.Query(
    "SELECT ChoiceKey, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed FROM AncientGlicko2Ratings")
    .ToList();
```

Add as `ancientRatings` to the dashboard JSON export object.

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Cli`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/ExportCommand.cs
git commit -m "feat: include ancient ratings in mod and dashboard export"
```

---

### Task 6: Add --ancient Flag to RatingCommand

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/RatingCommand.cs`

- [ ] **Step 1: Read the current RatingCommand**

Read `src/Sts2Analytics.Cli/Commands/RatingCommand.cs` fully.

- [ ] **Step 2: Add --ancient option**

```csharp
var ancientOption = new Option<bool>("--ancient") { Description = "Show ancient choice ratings" };
cmd.AddOption(ancientOption);
```

Add `ancient` to the handler parameter list. At the start of the handler (after the `--player` early return), add:

```csharp
if (ancient)
{
    var ancientEngine = new AncientRatingEngine(conn);
    ancientEngine.ProcessAllRuns();

    string context = "overall";
    if (act is not null)
    {
        context = act switch { 0 => "neow", 1 => "post_act1", 2 => "post_act2", _ => "overall" };
    }

    var sql = character != null
        ? "SELECT ChoiceKey, Rating, RatingDeviation, GamesPlayed FROM AncientGlicko2Ratings WHERE Character = @Character AND Context = @Context ORDER BY Rating DESC"
        : "SELECT ChoiceKey, Rating, RatingDeviation, GamesPlayed FROM AncientGlicko2Ratings WHERE Character = 'ALL' AND Context = @Context ORDER BY Rating DESC";

    var ancientRatings = conn.Query(sql, new { Character = character, Context = context })
        .Where(r => (long)r.GamesPlayed >= minGames)
        .Take(top).ToList();

    Console.WriteLine();
    Console.WriteLine("  Ancient Choice Ratings");
    Console.WriteLine("  ─────────────────────────────────────────────");
    Console.WriteLine($"  {"Choice",-25} {"Rating",8} {"±RD",6} {"Games",7}");
    Console.WriteLine("  ─────────────────────────────────────────────");

    foreach (var r in ancientRatings)
    {
        Console.WriteLine($"  {(string)r.ChoiceKey,-25} {(double)r.Rating,8:F0} {(double)r.RatingDeviation,6:F0} {(long)r.GamesPlayed,7}");
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
git commit -m "feat: add --ancient flag to rating command"
```

---

### Task 7: Update Mod OverlayData and DataLoader

**Files:**
- Modify: `src/Sts2Analytics.Mod/Data/OverlayData.cs`
- Modify: `src/Sts2Analytics.Mod/Data/DataLoader.cs`

- [ ] **Step 1: Add AncientStats record to OverlayData.cs**

```csharp
public record AncientStats(
    [property: JsonPropertyName("choiceKey")] string ChoiceKey,
    [property: JsonPropertyName("rating")] double Rating,
    [property: JsonPropertyName("rd")] double Rd = 350,
    [property: JsonPropertyName("ratingNeow")] double RatingNeow = 0,
    [property: JsonPropertyName("rdNeow")] double RdNeow = 350,
    [property: JsonPropertyName("ratingPostAct1")] double RatingPostAct1 = 0,
    [property: JsonPropertyName("rdPostAct1")] double RdPostAct1 = 350,
    [property: JsonPropertyName("ratingPostAct2")] double RatingPostAct2 = 0,
    [property: JsonPropertyName("rdPostAct2")] double RdPostAct2 = 350);
```

- [ ] **Step 2: Update OverlayData record**

```csharp
public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("skipEloByAct")] Dictionary<string, double>? SkipEloByAct,
    [property: JsonPropertyName("cards")] List<CardStats> Cards,
    [property: JsonPropertyName("ancientChoices")] List<AncientStats>? AncientChoices = null);
```

- [ ] **Step 3: Update DataLoader to load ancient choices**

Read `DataLoader.cs` first. Add a `_ancientChoices` dictionary and populate it in `Load()`:

```csharp
private static Dictionary<string, AncientStats>? _ancientChoices;

// In Load(), after loading cards:
_ancientChoices = new Dictionary<string, AncientStats>(StringComparer.OrdinalIgnoreCase);
if (data.AncientChoices != null)
{
    foreach (var ac in data.AncientChoices)
    {
        if (!string.IsNullOrEmpty(ac.ChoiceKey))
            _ancientChoices[ac.ChoiceKey] = ac;
    }
}
GD.Print($"[SpireOracle] Loaded {_ancientChoices.Count} ancient choices");
```

Add a public accessor:
```csharp
public static AncientStats? GetAncientChoice(string choiceKey) =>
    _ancientChoices?.TryGetValue(choiceKey, out var stats) == true ? stats : null;
```

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Data/OverlayData.cs src/Sts2Analytics.Mod/Data/DataLoader.cs
git commit -m "feat: add ancient choice data to mod overlay and DataLoader"
```

---

### Task 8: Render Ancient Choice Badges in Overlay

**Files:**
- Modify: `src/Sts2Analytics.Mod/UI/OverlayFactory.cs`
- A new Harmony patch may be needed for the ancient choice screen

- [ ] **Step 1: Read existing OverlayFactory.cs and CardRewardPatch.cs**

Read both files to understand the pattern. The ancient choice screen will need its own Harmony patch targeting the ancient/Neow UI screen.

- [ ] **Step 2: Add AddAncientOverlay method to OverlayFactory**

```csharp
public static void AddAncientOverlay(Control choiceHolder, AncientStats stats)
{
    RemoveOverlay(choiceHolder);

    var badge = new PanelContainer();
    badge.Name = "SpireOracleAncientBadge";
    badge.AddToGroup(OverlayGroup);

    var badgeStyle = new StyleBoxFlat();
    badgeStyle.BgColor = stats.Rating >= 1600
        ? new Color(0.83f, 0.33f, 0.16f) // ember - strong
        : stats.Rating >= 1450
            ? new Color(0.14f, 0.19f, 0.27f) // grey - average
            : new Color(0.16f, 0.10f, 0.10f); // dark red - weak
    badgeStyle.CornerRadiusBottomLeft = 6;
    badgeStyle.CornerRadiusBottomRight = 6;
    badgeStyle.CornerRadiusTopLeft = 6;
    badgeStyle.CornerRadiusTopRight = 6;
    badgeStyle.ContentMarginLeft = 8;
    badgeStyle.ContentMarginRight = 8;
    badgeStyle.ContentMarginTop = 4;
    badgeStyle.ContentMarginBottom = 4;
    badge.AddThemeStyleboxOverride("panel", badgeStyle);

    var label = new Label();
    label.Text = $"{stats.Rating:F0}";
    label.AddThemeFontSizeOverride("font_size", 28);
    label.AddThemeColorOverride("font_color", Colors.White);
    badge.AddChild(label);

    var strip = new HBoxContainer();
    strip.Name = "SpireOracleStrip";
    strip.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
    strip.AnchorTop = 1f;
    strip.Position = new Vector2(-30, 240);
    strip.AddChild(badge);
    choiceHolder.AddChild(strip);
}
```

- [ ] **Step 3: Create AncientChoicePatch.cs**

Create `src/Sts2Analytics.Mod/Patches/AncientChoicePatch.cs`. This needs to target the ancient choice UI screen class. The exact class name needs to be discovered from the STS2 game assemblies — look for the ancient/Neow reward selection screen. Follow the same Harmony postfix pattern as `CardRewardPatch`:

```csharp
using HarmonyLib;
using Godot;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

// NOTE: The target class name needs to be confirmed from STS2 game code.
// Look for the ancient choice/Neow selection screen class.
// Pattern follows CardRewardPatch exactly.
[HarmonyPatch] // Target TBD — implementer should discover the correct class
public static class AncientChoicePatch
{
    [HarmonyPostfix]
    public static void Postfix(/* params depend on target method */)
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        // Iterate over ancient choice UI elements
        // For each choice, look up the TextKey and call:
        // var stats = DataLoader.GetAncientChoice(textKey);
        // if (stats != null) OverlayFactory.AddAncientOverlay(holder, stats);
    }
}
```

Note to implementer: The exact Harmony patch target depends on the STS2 game's ancient choice screen class, which needs to be discovered. The pattern is identical to `CardRewardPatch` — patch the refresh/display method, iterate over UI children, look up data, and call `AddAncientOverlay`.

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/UI/OverlayFactory.cs src/Sts2Analytics.Mod/Patches/AncientChoicePatch.cs
git commit -m "feat: add ancient choice overlay badges and Harmony patch"
```

---

### Task 9: Dashboard — Ancient Ratings Page

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/AncientRatings.razor`
- Modify: `src/Sts2Analytics.Web/Layout/MainLayout.razor`
- Modify: `src/Sts2Analytics.Web/Services/DataService.cs`

- [ ] **Step 1: Add ancient rating data to DataService**

In `DataService.cs`, add to `ExportData`:

```csharp
public List<AncientRatingExport> AncientRatings { get; set; } = [];
```

Add the export class:

```csharp
public class AncientRatingExport
{
    public string ChoiceKey { get; set; } = "";
    public string Character { get; set; } = "";
    public string Context { get; set; } = "";
    public double Rating { get; set; }
    public double RatingDeviation { get; set; }
    public int GamesPlayed { get; set; }
}
```

- [ ] **Step 2: Create AncientRatings.razor**

```razor
@page "/ancient-ratings"
@inject DataService Data
@inject FilterState Filter

<div class="oracle-page">
    <h2 class="page-title">Ancient Ratings</h2>
    <p class="page-subtitle">Glicko-2 ratings for Neow rewards and post-act ancient choices</p>

    @if (_loading)
    {
        <p class="loading">Loading...</p>
    }
    else
    {
        <div class="filter-bar" style="display:flex;gap:0.5rem;margin-bottom:1.5rem;flex-wrap:wrap;">
            <button class="filter-btn @(_timingFilter == null ? "active" : "")"
                    @onclick="() => SetTiming(null)">All</button>
            <button class="filter-btn @(_timingFilter == "neow" ? "active" : "")"
                    @onclick='() => SetTiming("neow")'>Neow</button>
            <button class="filter-btn @(_timingFilter == "post_act1" ? "active" : "")"
                    @onclick='() => SetTiming("post_act1")'>Post-Act 1</button>
            <button class="filter-btn @(_timingFilter == "post_act2" ? "active" : "")"
                    @onclick='() => SetTiming("post_act2")'>Post-Act 2</button>
        </div>

        <table class="oracle-table">
            <thead>
                <tr>
                    <th>#</th>
                    <th>Choice</th>
                    <th>Rating</th>
                    <th>±RD</th>
                    @if (_timingFilter == null)
                    {
                        <th>Neow</th>
                        <th>Post-Act1</th>
                        <th>Post-Act2</th>
                    }
                    <th>Games</th>
                </tr>
            </thead>
            <tbody>
                @{ int rank = 1; }
                @foreach (var r in _filteredRatings)
                {
                    <tr>
                        <td>@(rank++)</td>
                        <td>@FormatName(r.ChoiceKey)</td>
                        <td>@r.Rating.ToString("F0")</td>
                        <td>@r.RatingDeviation.ToString("F0")</td>
                        @if (_timingFilter == null)
                        {
                            <td>@GetTimingRating(r.ChoiceKey, "neow")</td>
                            <td>@GetTimingRating(r.ChoiceKey, "post_act1")</td>
                            <td>@GetTimingRating(r.ChoiceKey, "post_act2")</td>
                        }
                        <td>@r.GamesPlayed</td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    private bool _loading = true;
    private string? _timingFilter;
    private List<AncientRatingExport> _allRatings = [];
    private List<AncientRatingExport> _filteredRatings = [];
    private ExportData? _data;

    protected override async Task OnInitializedAsync()
    {
        _data = await Data.GetDataAsync();
        _allRatings = _data.AncientRatings;
        ApplyFilter();
        _loading = false;
    }

    private void SetTiming(string? timing)
    {
        _timingFilter = timing;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var charFilter = Filter.Character;
        var character = charFilter ?? "ALL";
        var context = _timingFilter ?? "overall";

        _filteredRatings = _allRatings
            .Where(r => r.Character == character && r.Context == context)
            .OrderByDescending(r => r.Rating)
            .ToList();
    }

    private string GetTimingRating(string choiceKey, string timing)
    {
        var r = _allRatings.FirstOrDefault(r => r.ChoiceKey == choiceKey && r.Character == "ALL" && r.Context == timing);
        return r != null ? r.Rating.ToString("F0") : "—";
    }

    private string FormatName(string key) => key.Replace("_", " ");
}
```

- [ ] **Step 3: Add nav link in MainLayout.razor**

In the "Collection" section, after the "Matchups" link, add:

```razor
<NavLink class="nav-link" href="ancient-ratings">
    <span class="nav-icon">&#x2726;</span> Ancient
</NavLink>
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build src/Sts2Analytics.Web`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Web/Pages/AncientRatings.razor src/Sts2Analytics.Web/Layout/MainLayout.razor src/Sts2Analytics.Web/Services/DataService.cs
git commit -m "feat: add Ancient Ratings dashboard page"
```

---

### Task 10: Run Full Test Suite and Verify

**Files:** None (verification only)

- [ ] **Step 1: Run all tests**

Run: `dotnet test tests/Sts2Analytics.Core.Tests -v n`
Expected: All tests PASS

- [ ] **Step 2: Build all projects**

Run: `dotnet build Sts2Analytics.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit any fixes if needed**

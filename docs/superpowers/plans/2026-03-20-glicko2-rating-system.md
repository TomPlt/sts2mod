# Glicko-2 Rating System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Elo rating system with Glicko-2 to provide built-in confidence intervals and temporal decay.

**Architecture:** Pure Glicko-2 math in a static calculator class, engine class that batches matchups per rating period (run) and routes to 21 contexts (overall + per-character + per-character-per-act), analytics query class for reading ratings. SQLite tables replaced, CLI and dashboard updated.

**Tech Stack:** C#/.NET 9.0, SQLite, Dapper, System.CommandLine, Blazor WASM

**Spec:** `docs/superpowers/specs/2026-03-20-glicko2-rating-system-design.md`

**Run tests with:** `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/`

**IMPORTANT:** This must be implemented in a git worktree since the user is working in parallel on another issue.

---

## File Map

### New files
| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Core/Elo/Glicko2Calculator.cs` | Pure Glicko-2 math: scale conversion, expected score, volatility iteration, rating update |
| `src/Sts2Analytics.Core/Elo/Glicko2Engine.cs` | Process runs into batched matchups, route to contexts, apply temporal decay |
| `src/Sts2Analytics.Core/Elo/Glicko2Analytics.cs` | Query layer for ratings, history, trends, matchups |
| `src/Sts2Analytics.Cli/Commands/RatingCommand.cs` | CLI command (replaces EloCommand) |
| `src/Sts2Analytics.Web/Pages/RatingLeaderboard.razor` | Dashboard page (replaces EloLeaderboard) |
| `tests/Sts2Analytics.Core.Tests/Elo/Glicko2CalculatorTests.cs` | Calculator math tests |
| `tests/Sts2Analytics.Core.Tests/Elo/Glicko2EngineTests.cs` | Engine integration tests |

### Modified files
| File | Changes |
|------|---------|
| `src/Sts2Analytics.Core/Database/Schema.cs` | Add Glicko2Ratings + Glicko2History tables, drop EloRatings + EloHistory |
| `src/Sts2Analytics.Core/Models/Entities.cs` | Add Glicko2RatingEntity + Glicko2HistoryEntity, remove Elo entities |
| `src/Sts2Analytics.Core/Models/AnalyticsResults.cs` | Add Glicko2RatingResult + Glicko2HistoryResult, remove Elo result types |
| `src/Sts2Analytics.Cli/Program.cs` | Replace `EloCommand.Create()` with `RatingCommand.Create()` |
| `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | Use Glicko2Analytics + Glicko2Engine instead of Elo equivalents |
| `src/Sts2Analytics.Web/Services/DataService.cs` | Replace EloRatingResult with Glicko2RatingResult in ExportData |
| `src/Sts2Analytics.Web/Pages/Home.razor` | Replace `EloRatingResult` / `_data.EloRatings` with Glicko-2 equivalents |
| `src/Sts2Analytics.Web/Pages/CardExplorer.razor` | Replace `_data.EloRatings` with `_data.Glicko2Ratings` |
| `src/Sts2Analytics.Web/Pages/CardMatchups.razor` | Replace `EloRatingResult` / `_data.EloRatings` with Glicko-2 equivalents |
| `src/Sts2Analytics.Web/Layout/MainLayout.razor` | Update nav text from "Elo Rankings" to "Card Ratings" |

### Removed files
| File | Replaced by |
|------|------------|
| `src/Sts2Analytics.Core/Elo/EloCalculator.cs` | Glicko2Calculator.cs |
| `src/Sts2Analytics.Core/Elo/EloEngine.cs` | Glicko2Engine.cs |
| `src/Sts2Analytics.Core/Elo/EloAnalytics.cs` | Glicko2Analytics.cs |
| `src/Sts2Analytics.Cli/Commands/EloCommand.cs` | RatingCommand.cs |
| `src/Sts2Analytics.Web/Pages/EloLeaderboard.razor` | RatingLeaderboard.razor |
| `tests/Sts2Analytics.Core.Tests/Elo/EloCalculatorTests.cs` | Glicko2CalculatorTests.cs |
| `tests/Sts2Analytics.Core.Tests/Elo/EloEngineTests.cs` | Glicko2EngineTests.cs |

---

## Task 1: Glicko2Calculator — Pure Math

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/Glicko2Calculator.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Elo/Glicko2CalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Sts2Analytics.Core.Tests/Elo/Glicko2CalculatorTests.cs`:

```csharp
using Sts2Analytics.Core.Elo;

namespace Sts2Analytics.Core.Tests.Elo;

public class Glicko2CalculatorTests
{
    // Reference values from Glickman's paper (Example in Section 3)
    // Player: rating 1500, RD 200, vol 0.06
    // Opponents: (1400, 30, win), (1550, 100, loss), (1700, 300, loss)
    // Expected result: rating ~1464.06, RD ~151.52

    [Fact]
    public void UpdateRating_GlickmanExample_MatchesExpected()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06);
        var opponents = new[]
        {
            (Rating: new Glicko2Calculator.Glicko2Rating(1400, 30, 0.06), Score: 1.0),
            (Rating: new Glicko2Calculator.Glicko2Rating(1550, 100, 0.06), Score: 0.0),
            (Rating: new Glicko2Calculator.Glicko2Rating(1700, 300, 0.06), Score: 0.0),
        };

        var result = Glicko2Calculator.UpdateRating(rating, opponents);

        Assert.InRange(result.Rating, 1460, 1468);
        Assert.InRange(result.RatingDeviation, 148, 155);
        Assert.True(result.Volatility > 0);
    }

    [Fact]
    public void UpdateRating_NoGames_OnlyRdIncreases()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 100, 0.06);
        var result = Glicko2Calculator.ApplyInactivityDecay(rating);

        Assert.Equal(1500, result.Rating);
        Assert.True(result.RatingDeviation > 100);
        Assert.Equal(0.06, result.Volatility);
    }

    [Fact]
    public void UpdateRating_EqualOpponents_Win_RatingIncreases()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06);
        var opponents = new[]
        {
            (Rating: new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06), Score: 1.0),
        };

        var result = Glicko2Calculator.UpdateRating(rating, opponents);
        Assert.True(result.Rating > 1500);
    }

    [Fact]
    public void UpdateRating_EqualOpponents_Loss_RatingDecreases()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06);
        var opponents = new[]
        {
            (Rating: new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06), Score: 0.0),
        };

        var result = Glicko2Calculator.UpdateRating(rating, opponents);
        Assert.True(result.Rating < 1500);
    }

    [Fact]
    public void ApplyInactivityDecay_MultiplePeriodsGrowsRd()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 100, 0.06);

        var decayed = rating;
        for (int i = 0; i < 5; i++)
            decayed = Glicko2Calculator.ApplyInactivityDecay(decayed);

        Assert.True(decayed.RatingDeviation > rating.RatingDeviation);
        Assert.True(decayed.RatingDeviation <= 350); // Capped at initial RD
        Assert.Equal(1500, decayed.Rating); // Rating unchanged
    }

    [Fact]
    public void UpdateRating_LowRd_SmallRatingChange()
    {
        var established = new Glicko2Calculator.Glicko2Rating(1500, 50, 0.06);
        var fresh = new Glicko2Calculator.Glicko2Rating(1500, 300, 0.06);
        var opponent = new[] { (Rating: new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06), Score: 1.0) };

        var resultEstablished = Glicko2Calculator.UpdateRating(established, opponent);
        var resultFresh = Glicko2Calculator.UpdateRating(fresh, opponent);

        // Fresh rating should move more than established
        Assert.True(Math.Abs(resultFresh.Rating - 1500) > Math.Abs(resultEstablished.Rating - 1500));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/ --filter "Glicko2CalculatorTests"`
Expected: Build error — `Glicko2Calculator` does not exist

- [ ] **Step 3: Implement Glicko2Calculator**

Create `src/Sts2Analytics.Core/Elo/Glicko2Calculator.cs`:

```csharp
namespace Sts2Analytics.Core.Elo;

public static class Glicko2Calculator
{
    // System constant — constrains volatility change per period.
    // Lower = more conservative. Range 0.3–1.2. 0.5 is reasonable default.
    private const double Tau = 0.5;
    private const double ConvergenceTolerance = 0.000001;
    private const double MaxRd = 350.0;

    // Glicko-2 scale factor: 173.7178 = 400 / ln(10)
    private const double ScaleFactor = 173.7178;

    public record Glicko2Rating(double Rating, double RatingDeviation, double Volatility);

    /// <summary>
    /// Update a player's rating after a rating period with one or more game results.
    /// </summary>
    public static Glicko2Rating UpdateRating(
        Glicko2Rating player,
        ReadOnlySpan<(Glicko2Rating Rating, double Score)> results)
    {
        if (results.Length == 0)
            return ApplyInactivityDecay(player);

        // Step 1: Convert to Glicko-2 scale
        double mu = (player.Rating - 1500) / ScaleFactor;
        double phi = player.RatingDeviation / ScaleFactor;
        double sigma = player.Volatility;

        // Convert opponents
        Span<double> muJ = stackalloc double[results.Length];
        Span<double> phiJ = stackalloc double[results.Length];
        Span<double> scores = stackalloc double[results.Length];
        for (int i = 0; i < results.Length; i++)
        {
            muJ[i] = (results[i].Rating.Rating - 1500) / ScaleFactor;
            phiJ[i] = results[i].Rating.RatingDeviation / ScaleFactor;
            scores[i] = results[i].Score;
        }

        // Step 2: Compute v (estimated variance)
        double v = 0;
        for (int i = 0; i < results.Length; i++)
        {
            double g = G(phiJ[i]);
            double e = E(mu, muJ[i], phiJ[i]);
            v += g * g * e * (1 - e);
        }
        v = 1.0 / v;

        // Step 3: Compute delta (estimated improvement)
        double delta = 0;
        for (int i = 0; i < results.Length; i++)
        {
            double g = G(phiJ[i]);
            double e = E(mu, muJ[i], phiJ[i]);
            delta += g * (scores[i] - e);
        }
        delta *= v;

        // Step 4: Determine new volatility via Illinois algorithm
        double sigmaNew = ComputeNewVolatility(sigma, phi, v, delta);

        // Step 5: Update phi using new sigma
        double phiStar = Math.Sqrt(phi * phi + sigmaNew * sigmaNew);

        // Step 6: Update phi and mu
        double phiNew = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
        double muNew = mu + phiNew * phiNew * delta / v;

        // Step 7: Convert back to original scale
        double ratingNew = muNew * ScaleFactor + 1500;
        double rdNew = Math.Min(phiNew * ScaleFactor, MaxRd);

        return new Glicko2Rating(ratingNew, rdNew, sigmaNew);
    }

    /// <summary>
    /// Apply inactivity decay — RD grows when card is not seen.
    /// phi' = sqrt(phi^2 + sigma^2), capped at MaxRd.
    /// </summary>
    public static Glicko2Rating ApplyInactivityDecay(Glicko2Rating rating)
    {
        double phi = rating.RatingDeviation / ScaleFactor;
        double phiNew = Math.Sqrt(phi * phi + rating.Volatility * rating.Volatility);
        double rdNew = Math.Min(phiNew * ScaleFactor, MaxRd);
        return rating with { RatingDeviation = rdNew };
    }

    // g(phi) = 1 / sqrt(1 + 3*phi^2 / pi^2)
    private static double G(double phi)
        => 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));

    // E(mu, mu_j, phi_j) = 1 / (1 + exp(-g(phi_j) * (mu - mu_j)))
    private static double E(double mu, double muJ, double phiJ)
        => 1.0 / (1.0 + Math.Exp(-G(phiJ) * (mu - muJ)));

    // Illinois algorithm to find new volatility
    private static double ComputeNewVolatility(double sigma, double phi, double v, double delta)
    {
        double a = Math.Log(sigma * sigma);
        double phiSq = phi * phi;
        double deltaSq = delta * delta;

        double F(double x)
        {
            double ex = Math.Exp(x);
            double d = phiSq + v + ex;
            double part1 = ex * (deltaSq - phiSq - v - ex) / (2.0 * d * d);
            double part2 = (x - a) / (Tau * Tau);
            return part1 - part2;
        }

        // Initial bounds
        double A = a;
        double B;

        if (deltaSq > phiSq + v)
        {
            B = Math.Log(deltaSq - phiSq - v);
        }
        else
        {
            int k = 1;
            while (F(a - k * Tau) < 0)
                k++;
            B = a - k * Tau;
        }

        double fA = F(A);
        double fB = F(B);

        while (Math.Abs(B - A) > ConvergenceTolerance)
        {
            double C = A + (A - B) * fA / (fB - fA);
            double fC = F(C);

            if (fC * fB <= 0)
            {
                A = B;
                fA = fB;
            }
            else
            {
                fA /= 2.0;
            }

            B = C;
            fB = fC;
        }

        return Math.Exp(A / 2.0);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/ --filter "Glicko2CalculatorTests"`
Expected: All 6 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/Glicko2Calculator.cs tests/Sts2Analytics.Core.Tests/Elo/Glicko2CalculatorTests.cs
git commit -m "feat: add Glicko-2 calculator with pure math implementation"
```

---

## Task 2: Schema & Model Changes

**Files:**
- Modify: `src/Sts2Analytics.Core/Database/Schema.cs:182-208`
- Modify: `src/Sts2Analytics.Core/Models/Entities.cs:189-207`
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs:21-25`

- [ ] **Step 1: Update Schema.cs — replace Elo tables with Glicko-2 tables**

In `src/Sts2Analytics.Core/Database/Schema.cs`, replace the EloRatings and EloHistory CREATE TABLE statements (lines 182-199) and their indexes (lines 206-207) with:

```sql
CREATE TABLE IF NOT EXISTS Glicko2Ratings (
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

CREATE TABLE IF NOT EXISTS Glicko2History (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Glicko2RatingId INTEGER NOT NULL REFERENCES Glicko2Ratings(Id),
    RunId INTEGER NOT NULL REFERENCES Runs(Id),
    RatingBefore REAL NOT NULL DEFAULT 0,
    RatingAfter REAL NOT NULL DEFAULT 0,
    RdBefore REAL NOT NULL DEFAULT 0,
    RdAfter REAL NOT NULL DEFAULT 0,
    VolatilityBefore REAL NOT NULL DEFAULT 0,
    VolatilityAfter REAL NOT NULL DEFAULT 0,
    Timestamp TEXT NOT NULL DEFAULT ''
);
```

And replace the EloRatings/EloHistory indexes with:

```sql
CREATE INDEX IF NOT EXISTS IX_Glicko2Ratings_CardId ON Glicko2Ratings(CardId);
CREATE INDEX IF NOT EXISTS IX_Glicko2History_Glicko2RatingId ON Glicko2History(Glicko2RatingId);
```

- [ ] **Step 2: Update Entities.cs — replace Elo entities**

In `src/Sts2Analytics.Core/Models/Entities.cs`, replace `EloRatingEntity` and `EloHistoryEntity` (lines 189-207) with:

```csharp
public record Glicko2RatingEntity
{
    public long Id { get; init; }
    public string CardId { get; init; } = "";
    public string Character { get; init; } = "";
    public string Context { get; init; } = "overall";
    public double Rating { get; init; } = 1500.0;
    public double RatingDeviation { get; init; } = 350.0;
    public double Volatility { get; init; } = 0.06;
    public int GamesPlayed { get; init; }
    public long? LastUpdatedRunId { get; init; }
}

public record Glicko2HistoryEntity
{
    public long Id { get; init; }
    public long Glicko2RatingId { get; init; }
    public long RunId { get; init; }
    public double RatingBefore { get; init; }
    public double RatingAfter { get; init; }
    public double RdBefore { get; init; }
    public double RdAfter { get; init; }
    public double VolatilityBefore { get; init; }
    public double VolatilityAfter { get; init; }
    public string Timestamp { get; init; } = "";
}
```

- [ ] **Step 3: Update AnalyticsResults.cs — replace Elo result types**

In `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`, replace lines 21-25 with:

```csharp
public record Glicko2RatingResult(
    string CardId, string Character, string Context,
    double Rating, double RatingDeviation, double Volatility, int GamesPlayed);

public record Glicko2HistoryResult(
    double RatingBefore, double RatingAfter,
    double RdBefore, double RdAfter,
    string Timestamp);

public record CardMatchupResult(string CardA, string CardB, int AWinsOverB, int BWinsOverA);
```

- [ ] **Step 4: Do NOT commit yet**

The solution will not build until old Elo code is removed (Task 5) and consumers are updated. These changes will be committed together with Task 5 to maintain a buildable state at each commit.

---

## Task 3: Glicko2Engine — Run Processing

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/Glicko2Engine.cs`
- Test: `tests/Sts2Analytics.Core.Tests/Elo/Glicko2EngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Sts2Analytics.Core.Tests/Elo/Glicko2EngineTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class Glicko2EngineTests
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
    public void ProcessRun_CreatesGlicko2Ratings()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2Ratings");
        Assert.True(count > 0);
    }

    [Fact]
    public void ProcessRun_SkipGetsRating()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var skipRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'SKIP' AND Context = 'overall'");
        Assert.NotNull(skipRating);
    }

    [Fact]
    public void ProcessRun_CreatesPerCharacterContext()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);
        var character = conn.QueryFirst<string>("SELECT Character FROM Runs WHERE Id = @Id", new { Id = runId });

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var charRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'SKIP' AND Context = @Character",
            new { Character = character });
        Assert.NotNull(charRating);
    }

    [Fact]
    public void ProcessRun_CreatesActSpecificContext()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);
        var character = conn.QueryFirst<string>("SELECT Character FROM Runs WHERE Id = @Id", new { Id = runId });

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        // Should have at least one act-specific context
        var actContextCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(DISTINCT Context) FROM Glicko2Ratings WHERE Context LIKE @Pattern",
            new { Pattern = $"{character}_ACT%" });
        Assert.True(actContextCount > 0);
    }

    [Fact]
    public void ProcessRun_RecordsHistory()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var historyCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2History");
        Assert.True(historyCount > 0);
    }

    [Fact]
    public void ProcessRun_HistoryIncludesRdAndVolatility()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var history = conn.QueryFirst(
            "SELECT RdBefore, RdAfter, VolatilityBefore, VolatilityAfter FROM Glicko2History LIMIT 1");
        Assert.True((double)history.RdBefore > 0);
        Assert.True((double)history.RdAfter > 0);
        Assert.True((double)history.VolatilityBefore > 0);
        Assert.True((double)history.VolatilityAfter > 0);
    }

    [Fact]
    public void ProcessRun_RatingDeviationShrinks()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        // Cards that participated should have RD < 350 (the default)
        var minRd = conn.ExecuteScalar<double>(
            "SELECT MIN(RatingDeviation) FROM Glicko2Ratings WHERE GamesPlayed > 0");
        Assert.True(minRd < 350.0);
    }

    [Fact]
    public void ProcessAllRuns_Idempotent()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var countBefore = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2History");

        engine.ProcessAllRuns(); // second call — should be no-op

        var countAfter = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2History");
        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public void ProcessRun_UpgradedCardsGetSeparateEntity()
    {
        using var conn = CreateDb();
        Schema.Initialize(conn);

        // Insert a synthetic run with an upgraded card choice
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('test', 'seed', 'IRONCLAD', 1, '2026-01-01')");
        var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')",
            new { RunId = runId });
        var floorId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");

        // One upgraded card picked, one base card skipped
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.INFLAME', 1, 1)",
            new { FloorId = floorId });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.BASH', 0, 0)",
            new { FloorId = floorId });

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        // The upgraded card should be stored as "CARD.INFLAME+1"
        var upgradeRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'CARD.INFLAME+1' AND Context = 'overall'");
        Assert.NotNull(upgradeRating);
    }

    [Fact]
    public void ProcessRun_TemporalDecay_RdGrowsBetweenRuns()
    {
        using var conn = CreateDb();

        // Run 1: card A picked over card B
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('run1', 's1', 'IRONCLAD', 1, '2026-01-01')");
        var run1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = run1Id });
        var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.AAA', 1, 0)", new { FloorId = floor1Id });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.BBB', 0, 0)", new { FloorId = floor1Id });

        // Run 2: different cards (AAA is absent)
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('run2', 's2', 'IRONCLAD', 1, '2026-01-02')");
        var run2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = run2Id });
        var floor2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.CCC', 1, 0)", new { FloorId = floor2Id });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.DDD', 0, 0)", new { FloorId = floor2Id });

        // Run 3: AAA appears again
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('run3', 's3', 'IRONCLAD', 1, '2026-01-03')");
        var run3Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = run3Id });
        var floor3Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.AAA', 1, 0)", new { FloorId = floor3Id });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.EEE', 0, 0)", new { FloorId = floor3Id });

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        // AAA's RD after run 3 should reflect decay from being absent in run 2
        // Check history: the RdBefore for run 3 should be higher than RdAfter from run 1
        var history = conn.Query<(double RdBefore, double RdAfter, long RunId)>("""
            SELECT gh.RdBefore, gh.RdAfter, gh.RunId FROM Glicko2History gh
            JOIN Glicko2Ratings gr ON gh.Glicko2RatingId = gr.Id
            WHERE gr.CardId = 'CARD.AAA' AND gr.Context = 'overall'
            ORDER BY gh.RunId
            """).ToList();

        Assert.Equal(2, history.Count); // appeared in run 1 and run 3
        var rdAfterRun1 = history[0].RdAfter;
        var rdBeforeRun3 = history[1].RdBefore;
        Assert.True(rdBeforeRun3 > rdAfterRun1, "RD should grow due to inactivity decay");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/ --filter "Glicko2EngineTests"`
Expected: Build error — `Glicko2Engine` does not exist

- [ ] **Step 3: Implement Glicko2Engine**

Create `src/Sts2Analytics.Core/Elo/Glicko2Engine.cs`:

```csharp
using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class Glicko2Engine
{
    private readonly IDbConnection _connection;

    public Glicko2Engine(IDbConnection connection)
    {
        _connection = connection;
    }

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM Glicko2History gh
                JOIN Glicko2Ratings gr ON gh.Glicko2RatingId = gr.Id
                WHERE gh.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        foreach (var runId in unprocessedRunIds)
        {
            ProcessRun(runId);
        }
    }

    public void ProcessRun(long runId)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character, Win, StartTime FROM Runs WHERE Id = @RunId",
            new { RunId = runId });

        if (run is null) return;

        var choices = _connection.Query<ChoiceRow>("""
            SELECT cc.FloorId, cc.CardId, cc.WasPicked, cc.UpgradeLevel, f.ActIndex
            FROM CardChoices cc
            JOIN Floors f ON cc.FloorId = f.Id
            WHERE f.RunId = @RunId
            ORDER BY cc.FloorId
            """, new { RunId = runId }).ToList();

        if (choices.Count == 0) return;

        // Group by FloorId — each group is one card reward screen
        var groups = choices.GroupBy(c => c.FloorId).ToList();

        // Collect all matchups per card per context across the entire run (one rating period)
        // Key: (cardId, context), Value: list of (opponentCardId, score)
        var matchupsByCardContext = new Dictionary<(string CardId, string Character, string Context), List<(string OpponentId, double Score)>>();

        foreach (var group in groups)
        {
            var actIndex = group.First().ActIndex;
            var picked = group.Where(c => c.WasPicked != 0).Select(c => MakeEntityId(c.CardId, c.UpgradeLevel)).ToList();
            var skipped = group.Where(c => c.WasPicked == 0).Select(c => MakeEntityId(c.CardId, c.UpgradeLevel)).ToList();

            var matchups = new List<(string winner, string loser)>();

            if (picked.Count == 0)
            {
                foreach (var cardId in skipped)
                    matchups.Add(("SKIP", cardId));
            }
            else
            {
                foreach (var pickedCard in picked)
                {
                    foreach (var skippedCard in skipped)
                        matchups.Add((pickedCard, skippedCard));
                    matchups.Add((pickedCard, "SKIP"));
                }
            }

            // Determine contexts for this floor
            var contexts = GetContexts(run.Character, actIndex);

            foreach (var (winner, loser) in matchups)
            {
                foreach (var (character, context) in contexts)
                {
                    var winnerKey = (winner, character, context);
                    var loserKey = (loser, character, context);

                    if (!matchupsByCardContext.ContainsKey(winnerKey))
                        matchupsByCardContext[winnerKey] = [];
                    if (!matchupsByCardContext.ContainsKey(loserKey))
                        matchupsByCardContext[loserKey] = [];

                    matchupsByCardContext[winnerKey].Add((loser, 1.0));
                    matchupsByCardContext[loserKey].Add((winner, 0.0));
                }
            }
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            // For each card that appeared in this run, apply Glicko-2 update
            foreach (var ((cardId, character, context), results) in matchupsByCardContext)
            {
                var rating = GetOrCreateRating(cardId, character, context, transaction);

                // Apply temporal decay for missed runs
                var currentRating = new Glicko2Calculator.Glicko2Rating(
                    rating.Rating, rating.RatingDeviation, rating.Volatility);

                if (rating.LastUpdatedRunId is not null)
                {
                    var missedRuns = CountRunsBetween(rating.LastUpdatedRunId.Value, run.Id, transaction);
                    for (int i = 0; i < missedRuns; i++)
                        currentRating = Glicko2Calculator.ApplyInactivityDecay(currentRating);
                }

                // Build opponent ratings
                var opponents = new (Glicko2Calculator.Glicko2Rating Rating, double Score)[results.Count];
                for (int i = 0; i < results.Count; i++)
                {
                    var oppRating = GetOrCreateRating(results[i].OpponentId, character, context, transaction);
                    opponents[i] = (
                        new Glicko2Calculator.Glicko2Rating(oppRating.Rating, oppRating.RatingDeviation, oppRating.Volatility),
                        results[i].Score
                    );
                }

                var newRating = Glicko2Calculator.UpdateRating(currentRating, opponents);

                // Update rating
                _connection.Execute("""
                    UPDATE Glicko2Ratings
                    SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                        GamesPlayed = @Games, LastUpdatedRunId = @RunId
                    WHERE Id = @Id
                    """,
                    new
                    {
                        Rating = newRating.Rating,
                        Rd = newRating.RatingDeviation,
                        Vol = newRating.Volatility,
                        Games = rating.GamesPlayed + 1,
                        RunId = run.Id,
                        rating.Id
                    },
                    transaction);

                // Record history
                _connection.Execute("""
                    INSERT INTO Glicko2History
                        (Glicko2RatingId, RunId, RatingBefore, RatingAfter, RdBefore, RdAfter,
                         VolatilityBefore, VolatilityAfter, Timestamp)
                    VALUES (@RatingId, @RunId, @RatingBefore, @RatingAfter, @RdBefore, @RdAfter,
                            @VolBefore, @VolAfter, @Timestamp)
                    """,
                    new
                    {
                        RatingId = rating.Id,
                        RunId = run.Id,
                        RatingBefore = currentRating.Rating,
                        RatingAfter = newRating.Rating,
                        RdBefore = currentRating.RatingDeviation,
                        RdAfter = newRating.RatingDeviation,
                        VolBefore = currentRating.Volatility,
                        VolAfter = newRating.Volatility,
                        Timestamp = run.StartTime
                    },
                    transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string MakeEntityId(string cardId, long upgradeLevel)
        => upgradeLevel > 0 ? $"{cardId}+{upgradeLevel}" : cardId;

    private static List<(string Character, string Context)> GetContexts(string character, long actIndex)
    {
        var actContext = $"{character}_ACT{actIndex + 1}";
        return
        [
            ("ALL", "overall"),
            (character, character),
            (character, actContext),
        ];
    }

    private int CountRunsBetween(long fromRunId, long toRunId, IDbTransaction transaction)
    {
        return _connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Runs WHERE Id > @From AND Id < @To",
            new { From = fromRunId, To = toRunId },
            transaction);
    }

    private RatingInfo GetOrCreateRating(string cardId, string character, string context, IDbTransaction transaction)
    {
        _connection.Execute(
            "INSERT OR IGNORE INTO Glicko2Ratings (CardId, Character, Context) VALUES (@CardId, @Character, @Context)",
            new { CardId = cardId, Character = character, Context = context },
            transaction);

        return _connection.QueryFirst<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM Glicko2Ratings
            WHERE CardId = @CardId AND Character = @Character AND Context = @Context
            """,
            new { CardId = cardId, Character = character, Context = context },
            transaction);
    }

    private record RunInfo(long Id, string Character, long Win, string StartTime);
    private record ChoiceRow(long FloorId, string CardId, long WasPicked, long UpgradeLevel, long ActIndex);
    private record RatingInfo(long Id, double Rating, double RatingDeviation, double Volatility, int GamesPlayed, long? LastUpdatedRunId);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/ --filter "Glicko2EngineTests"`
Expected: All 9 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/Glicko2Engine.cs tests/Sts2Analytics.Core.Tests/Elo/Glicko2EngineTests.cs
git commit -m "feat: add Glicko-2 engine with batched matchups, act contexts, and temporal decay"
```

---

## Task 4: Glicko2Analytics — Query Layer

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/Glicko2Analytics.cs`

- [ ] **Step 1: Create Glicko2Analytics**

Create `src/Sts2Analytics.Core/Elo/Glicko2Analytics.cs`:

```csharp
using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Elo;

public class Glicko2Analytics
{
    private readonly IDbConnection _connection;

    public Glicko2Analytics(IDbConnection connection)
    {
        _connection = connection;
    }

    public List<Glicko2RatingResult> GetRatings(AnalyticsFilter? filter = null)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (filter?.Character is not null)
        {
            conditions.Add("Character = @Character");
            parameters.Add("Character", filter.Character);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var sql = $"""
            SELECT CardId, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed
            FROM Glicko2Ratings
            {where}
            ORDER BY Rating DESC
            """;

        return _connection.Query<Glicko2RatingResult>(sql, parameters).ToList();
    }

    public List<Glicko2HistoryResult> GetHistory(string cardId, string context = "overall")
    {
        return _connection.Query<Glicko2HistoryResult>("""
            SELECT gh.RatingBefore, gh.RatingAfter, gh.RdBefore, gh.RdAfter, gh.Timestamp
            FROM Glicko2History gh
            JOIN Glicko2Ratings gr ON gh.Glicko2RatingId = gr.Id
            WHERE gr.CardId = @CardId AND gr.Context = @Context
            ORDER BY gh.Id ASC
            """, new { CardId = cardId, Context = context }).ToList();
    }

    /// <summary>
    /// Get the trend direction based on last N rating changes.
    /// Returns: 1 = trending up, -1 = trending down, 0 = stable
    /// </summary>
    public int GetTrend(long ratingId, int lookback = 3)
    {
        var recentHistory = _connection.Query<(double RatingBefore, double RatingAfter)>("""
            SELECT RatingBefore, RatingAfter FROM Glicko2History
            WHERE Glicko2RatingId = @RatingId
            ORDER BY RunId DESC
            LIMIT @Lookback
            """, new { RatingId = ratingId, Lookback = lookback }).ToList();

        if (recentHistory.Count == 0) return 0;

        var netChange = recentHistory.Sum(h => h.RatingAfter - h.RatingBefore);
        return netChange switch
        {
            > 1.0 => 1,
            < -1.0 => -1,
            _ => 0
        };
    }

    public CardMatchupResult GetCardMatchups(string cardA, string cardB)
    {
        var aOverB = _connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM CardChoices a
            JOIN CardChoices b ON a.FloorId = b.FloorId
            WHERE a.CardId = @CardA AND a.WasPicked = 1
              AND b.CardId = @CardB AND b.WasPicked = 0
            """, new { CardA = cardA, CardB = cardB });

        var bOverA = _connection.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM CardChoices a
            JOIN CardChoices b ON a.FloorId = b.FloorId
            WHERE a.CardId = @CardB AND a.WasPicked = 1
              AND b.CardId = @CardA AND b.WasPicked = 0
            """, new { CardA = cardA, CardB = cardB });

        return new CardMatchupResult(cardA, cardB, aOverB, bOverA);
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `/home/tom/.dotnet/dotnet build src/Sts2Analytics.Core/`
Expected: Build succeeds (may still have errors in other projects referencing old Elo types, which is fine)

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Core/Elo/Glicko2Analytics.cs
git commit -m "feat: add Glicko-2 analytics query layer"
```

---

## Task 5: Remove Old Elo Code

**Files:**
- Delete: `src/Sts2Analytics.Core/Elo/EloCalculator.cs`
- Delete: `src/Sts2Analytics.Core/Elo/EloEngine.cs`
- Delete: `src/Sts2Analytics.Core/Elo/EloAnalytics.cs`
- Delete: `tests/Sts2Analytics.Core.Tests/Elo/EloCalculatorTests.cs`
- Delete: `tests/Sts2Analytics.Core.Tests/Elo/EloEngineTests.cs`

- [ ] **Step 1: Delete old Elo files**

```bash
git rm src/Sts2Analytics.Core/Elo/EloCalculator.cs
git rm src/Sts2Analytics.Core/Elo/EloEngine.cs
git rm src/Sts2Analytics.Core/Elo/EloAnalytics.cs
git rm tests/Sts2Analytics.Core.Tests/Elo/EloCalculatorTests.cs
git rm tests/Sts2Analytics.Core.Tests/Elo/EloEngineTests.cs
```

- [ ] **Step 2: Verify core project builds**

Run: `/home/tom/.dotnet/dotnet build src/Sts2Analytics.Core/`
Expected: Build succeeds

- [ ] **Step 3: Run remaining tests**

Run: `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/`
Expected: All Glicko-2 tests pass, old Elo tests are gone

- [ ] **Step 4: Commit schema changes + Elo removal together**

```bash
git add src/Sts2Analytics.Core/Database/Schema.cs src/Sts2Analytics.Core/Models/Entities.cs src/Sts2Analytics.Core/Models/AnalyticsResults.cs
git add -A
git commit -m "refactor: replace Elo schema/models with Glicko-2, remove old Elo code"
```

---

## Task 6: CLI — RatingCommand

**Files:**
- Create: `src/Sts2Analytics.Cli/Commands/RatingCommand.cs`
- Delete: `src/Sts2Analytics.Cli/Commands/EloCommand.cs`
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`

- [ ] **Step 1: Create RatingCommand**

Create `src/Sts2Analytics.Cli/Commands/RatingCommand.cs`:

```csharp
using System.CommandLine;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class RatingCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var topOption = new Option<int>("--top") { Description = "Number of results", DefaultValueFactory = _ => 20 };
        var characterOption = new Option<string?>("--character") { Description = "Filter by character" };
        var actOption = new Option<int?>("--act") { Description = "Filter by act (1, 2, or 3)" };
        var minGamesOption = new Option<int>("--min-games") { Description = "Minimum games played", DefaultValueFactory = _ => 0 };
        var matchupOption = new Option<string[]?>("--matchup") { Description = "Head-to-head: --matchup CARD_A CARD_B", Arity = new ArgumentArity(2, 2) };

        var cmd = new Command("elo", "Show Glicko-2 rating leaderboard or card matchups")
        {
            dbOption, topOption, characterOption, actOption, minGamesOption, matchupOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var top = parseResult.GetValue(topOption);
            var character = parseResult.GetValue(characterOption);
            var act = parseResult.GetValue(actOption);
            var minGames = parseResult.GetValue(minGamesOption);
            var matchup = parseResult.GetValue(matchupOption);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Process ratings if needed
            var ratingCount = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
            if (ratingCount == 0)
            {
                Console.WriteLine("No rating data found. Processing all runs...");
                var engine = new Glicko2Engine(conn);
                engine.ProcessAllRuns();
                Console.WriteLine("Rating processing complete.");
            }

            var analytics = new Glicko2Analytics(conn);

            if (matchup is { Length: 2 })
            {
                var result = analytics.GetCardMatchups(matchup[0], matchup[1]);
                Console.WriteLine();
                Console.WriteLine("=== Card Matchup ===");
                Console.WriteLine($"{result.CardA} vs {result.CardB}");
                Console.WriteLine($"  {result.CardA} picked over {result.CardB}: {result.AWinsOverB} times");
                Console.WriteLine($"  {result.CardB} picked over {result.CardA}: {result.BWinsOverA} times");
                var total = result.AWinsOverB + result.BWinsOverA;
                if (total > 0)
                    Console.WriteLine($"  {result.CardA} pick rate in matchup: {(double)result.AWinsOverB / total:P1}");
                Console.WriteLine();
                return;
            }

            var filter = character != null ? new AnalyticsFilter(Character: character) : null;
            var ratings = analytics.GetRatings(filter);

            // Determine context
            string context;
            if (act is not null && character is not null)
                context = $"{character}_ACT{act}";
            else if (character is not null)
                context = character;
            else
                context = "overall";

            var filtered = ratings
                .Where(r => r.Context == context)
                .Where(r => r.GamesPlayed >= minGames)
                .OrderByDescending(r => r.Rating)
                .ToList();

            Console.WriteLine();
            Console.WriteLine($"=== Rating Leaderboard ({context}) ===");
            Console.WriteLine($"{"#",-5} {"Card",-35} {"Rating",7} {"±",5} {"Games",6} {"Trend",5}");

            // Get rating IDs for trend lookup
            var ratingIds = conn.Query<(long Id, string CardId, string Context)>(
                "SELECT Id, CardId, Context FROM Glicko2Ratings WHERE Context = @Context",
                new { Context = context }).ToDictionary(r => r.CardId, r => r.Id);

            var rank = 1;
            foreach (var rating in filtered.Take(top))
            {
                var trend = ratingIds.TryGetValue(rating.CardId, out var ratingId)
                    ? analytics.GetTrend(ratingId)
                    : 0;
                var trendChar = trend switch { 1 => "▲", -1 => "▼", _ => "─" };
                var rd = rating.RatingDeviation;

                Console.WriteLine($"{rank,-5} {rating.CardId,-35} {rating.Rating,7:F0} {rd,5:F0} {rating.GamesPlayed,6} {trendChar,5}");
                rank++;
            }
            Console.WriteLine();
        });

        return cmd;
    }
}
```

- [ ] **Step 2: Delete old EloCommand**

```bash
git rm src/Sts2Analytics.Cli/Commands/EloCommand.cs
```

- [ ] **Step 3: Update any reference to EloCommand.Create() in the CLI program**

Search for `EloCommand.Create()` in the CLI project and replace with `RatingCommand.Create()`. The file is likely `src/Sts2Analytics.Cli/Program.cs` or wherever commands are registered.

- [ ] **Step 4: Update ExportCommand**

In `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`, replace all Elo references:

- Change `using Sts2Analytics.Core.Elo;` — keep this (namespace unchanged, new classes in same namespace)
- Replace the Elo processing block (lines 48-56) with:

```csharp
// Glicko-2 ratings
var g2Count = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
if (g2Count == 0)
{
    Console.WriteLine("Processing Glicko-2 ratings...");
    var engine = new Glicko2Engine(conn);
    engine.ProcessAllRuns();
}
var g2Analytics = new Glicko2Analytics(conn);
var glicko2Ratings = g2Analytics.GetRatings();
```

- In the anonymous export object, replace `eloRatings` with `glicko2Ratings`
- Update the summary line: replace `{eloRatings.Count} Elo ratings` with `{glicko2Ratings.Count} ratings`

- [ ] **Step 5: Verify CLI project builds**

Run: `/home/tom/.dotnet/dotnet build src/Sts2Analytics.Cli/`
Expected: Build succeeds

- [ ] **Step 6: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/RatingCommand.cs src/Sts2Analytics.Cli/Commands/ExportCommand.cs
git add -A  # picks up deleted EloCommand.cs
git commit -m "feat: add rating CLI command with confidence and trend display"
```

---

## Task 7: Dashboard — RatingLeaderboard

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/RatingLeaderboard.razor`
- Delete: `src/Sts2Analytics.Web/Pages/EloLeaderboard.razor`
- Modify: `src/Sts2Analytics.Web/Services/DataService.cs`

- [ ] **Step 1: Update DataService.cs**

In `src/Sts2Analytics.Web/Services/DataService.cs`, replace:

```csharp
public List<EloRatingResult> EloRatings { get; set; } = [];
```

with:

```csharp
public List<Glicko2RatingResult> Glicko2Ratings { get; set; } = [];
```

The JSON field name in the export will be `glicko2Ratings` (from Task 6 ExportCommand changes), and `PropertyNameCaseInsensitive = true` handles the casing.

- [ ] **Step 2: Create RatingLeaderboard.razor**

Create `src/Sts2Analytics.Web/Pages/RatingLeaderboard.razor`:

```razor
@page "/elo"
@inject DataService Data
@inject FilterState Filter
@implements IDisposable

<PageTitle>Spire Oracle — Card Ratings</PageTitle>

@if (_loading)
{
    <p class="dim-text">Consulting the oracle...</p>
}
else
{
    <h2>Card Ratings</h2>

    <!-- Skip reference card -->
    @if (_skipRating != null)
    {
        <div class="skip-reference">
            <div class="skip-label">Skip Baseline</div>
            <div class="skip-elo mono">@_skipRating.Rating.ToString("F0") <span class="skip-rd">±@_skipRating.Rd.ToString("F0")</span></div>
            <div class="skip-note">Cards above this line are worth picking. Cards below — skip.</div>
        </div>
    }

    <!-- Act filter -->
    <div class="act-filter">
        <button class="@(_actFilter == null ? "active" : "")" @onclick="() => SetActFilter(null)">All Acts</button>
        <button class="@(_actFilter == 1 ? "active" : "")" @onclick="() => SetActFilter(1)">Act 1</button>
        <button class="@(_actFilter == 2 ? "active" : "")" @onclick="() => SetActFilter(2)">Act 2</button>
        <button class="@(_actFilter == 3 ? "active" : "")" @onclick="() => SetActFilter(3)">Act 3</button>
    </div>

    @if (Filter.HasAscensionFilter)
    {
        <div class="filter-note">
            <span>Note: Ratings are aggregated across all ascension levels.</span>
        </div>
    }

    <div class="panel" style="padding: 0; overflow: hidden;">
        <table class="oracle-table elo-table">
            <thead>
                <tr>
                    <th style="width: 3rem; text-align: center;">#</th>
                    <th>Card</th>
                    <th style="width: 7rem; text-align: right;">Rating</th>
                    <th style="width: 4rem; text-align: right;">±</th>
                    <th style="width: 5rem; text-align: right;">Games</th>
                    <th style="width: 12rem;">Confidence</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var row in _filteredRatings.Take(_showCount))
                {
                    <tr class="@(row.CardId == "SKIP" ? "skip-highlight" : "") @(row.LowConfidence ? "low-confidence" : "")">
                        <td style="text-align: center;" class="dim-text mono">@row.Rank</td>
                        <td style="font-weight: 500;">@row.Name</td>
                        <td style="text-align: right;">
                            <span class="elo-badge @GetRatingBadgeClass(row.Rating)">@row.Rating.ToString("F0")</span>
                        </td>
                        <td style="text-align: right;" class="dim-text mono">@row.Rd.ToString("F0")</td>
                        <td style="text-align: right;" class="dim-text mono">@row.Games</td>
                        <td>
                            <div class="elo-bar-track">
                                <div class="elo-bar-fill @(row.CardId == "SKIP" ? "skip" : "") @(row.LowConfidence ? "low-conf" : "")"
                                     style="width: @(Math.Clamp((row.Rating - _minRating) / (_maxRating - _minRating) * 100, 2, 100))%"></div>
                            </div>
                        </td>
                    </tr>
                }
            </tbody>
        </table>

        @if (_filteredRatings.Count > _showCount)
        {
            <div class="show-more" @onclick="ShowMore">
                Show more (@(_filteredRatings.Count - _showCount) remaining)
            </div>
        }
    </div>
}

@code {
    private bool _loading = true;
    private ExportData? _data;
    private RatingRow? _skipRating;
    private List<RatingRow> _allRatings = [];
    private List<RatingRow> _filteredRatings = [];
    private double _minRating = 1300;
    private double _maxRating = 1800;
    private int _showCount = 40;
    private int? _actFilter;

    record RatingRow(int Rank, string CardId, string Name, double Rating, double Rd, long Games, bool LowConfidence);

    protected override async Task OnInitializedAsync()
    {
        _data = await Data.GetDataAsync();
        Filter.OnChange += OnFilterChanged;
        BuildRatings();
        _loading = false;
    }

    private void OnFilterChanged()
    {
        ApplyFilter();
        InvokeAsync(StateHasChanged);
    }

    private void SetActFilter(int? act)
    {
        _actFilter = act;
        ApplyFilter();
    }

    private void BuildRatings()
    {
        if (_data is null) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_data is null) return;

        string context;
        if (_actFilter is not null && Filter.Character is not null)
            context = $"{Filter.Character}_ACT{_actFilter}";
        else if (Filter.Character is not null)
            context = Filter.Character;
        else if (_actFilter is not null)
            context = "overall"; // Can't filter by act without character
        else
            context = "overall";

        var ratings = _data.Glicko2Ratings
            .Where(e => e.Context == context)
            .OrderByDescending(e => e.Rating)
            .ToList();

        _skipRating = ratings
            .Where(e => e.CardId == "SKIP")
            .Select(e => new RatingRow(0, e.CardId, "SKIP", e.Rating, e.RatingDeviation, e.GamesPlayed, e.RatingDeviation > 200))
            .FirstOrDefault();

        _allRatings = ratings.Select((e, i) => new RatingRow(
            i + 1, e.CardId, FormatName(e.CardId), e.Rating, e.RatingDeviation, e.GamesPlayed, e.RatingDeviation > 200
        )).ToList();

        if (_allRatings.Count > 0)
        {
            _maxRating = _allRatings.Max(r => r.Rating);
            _minRating = _allRatings.Min(r => r.Rating);
        }

        _filteredRatings = _allRatings;
        _showCount = 40;
    }

    private void ShowMore() => _showCount += 40;

    public void Dispose()
    {
        Filter.OnChange -= OnFilterChanged;
    }

    static string FormatName(string id)
    {
        // Handle upgraded cards: "CARD.INFLAME+1" -> "Inflame +1"
        var plusIdx = id.IndexOf('+');
        var suffix = "";
        var baseId = id;
        if (plusIdx >= 0)
        {
            suffix = " " + id[plusIdx..];
            baseId = id[..plusIdx];
        }

        var dot = baseId.IndexOf('.');
        var name = dot >= 0 ? baseId[(dot + 1)..] : baseId;
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w)) + suffix;
    }

    static string GetRatingBadgeClass(double rating) => rating switch
    {
        >= 1650 => "high",
        >= 1500 => "mid",
        _ => "low"
    };
}

<style>
    .skip-reference {
        background: rgba(212, 85, 42, 0.06);
        border: 1px solid rgba(212, 85, 42, 0.15);
        border-radius: 6px;
        padding: 0.75rem 1rem;
        margin-bottom: var(--gap-lg);
        display: flex;
        align-items: center;
        gap: 1rem;
    }

    .skip-label {
        font-family: 'Cinzel', serif;
        font-size: 0.8rem;
        color: var(--ember-bright);
        white-space: nowrap;
    }

    .skip-elo {
        font-size: 1.4rem;
        font-weight: 500;
        color: var(--ember-glow);
    }

    .skip-rd {
        font-size: 0.85rem;
        color: var(--text-dim);
    }

    .skip-note {
        font-size: 0.75rem;
        color: var(--text-dim);
        flex: 1;
    }

    .act-filter {
        display: flex;
        gap: 0.5rem;
        margin-bottom: var(--gap-md);
    }

    .act-filter button {
        background: var(--void-2);
        border: 1px solid var(--surface-border);
        border-radius: 4px;
        padding: 0.3rem 0.75rem;
        font-size: 0.75rem;
        color: var(--text-dim);
        cursor: pointer;
        transition: all 0.15s;
    }

    .act-filter button.active {
        background: rgba(212, 85, 42, 0.15);
        border-color: var(--ember);
        color: var(--ember-bright);
    }

    .act-filter button:hover:not(.active) {
        border-color: var(--text-dim);
        color: var(--text);
    }

    .elo-table tbody td { font-size: 0.85rem; }

    .low-confidence td { opacity: 0.5; }

    .elo-bar-track {
        height: 4px;
        background: var(--void-3);
        border-radius: 2px;
        overflow: hidden;
    }

    .elo-bar-fill {
        height: 100%;
        border-radius: 2px;
        background: linear-gradient(90deg, var(--frost-dim), var(--frost));
        transition: width 0.4s ease;
    }

    .elo-bar-fill.skip {
        background: linear-gradient(90deg, var(--ember-dim), var(--ember));
    }

    .elo-bar-fill.low-conf {
        opacity: 0.4;
    }

    .show-more {
        text-align: center;
        padding: 0.6rem;
        font-size: 0.75rem;
        color: var(--text-dim);
        cursor: pointer;
        border-top: 1px solid var(--surface-border);
        transition: color 0.15s;
    }

    .show-more:hover {
        color: var(--ember-bright);
    }

    .filter-note {
        font-size: 0.75rem;
        color: var(--text-dim);
        background: rgba(91, 184, 212, 0.06);
        border: 1px solid rgba(91, 184, 212, 0.12);
        border-radius: 4px;
        padding: 0.4rem 0.75rem;
        margin-bottom: var(--gap-md);
    }
</style>
```

- [ ] **Step 3: Delete old EloLeaderboard.razor**

```bash
git rm src/Sts2Analytics.Web/Pages/EloLeaderboard.razor
```

- [ ] **Step 4: Update all other Blazor pages referencing Elo types**

These pages reference `_data.EloRatings` and `EloRatingResult` which no longer exist:

**`src/Sts2Analytics.Web/Pages/Home.razor`:**
- Replace `List<EloRatingResult>` with `List<Glicko2RatingResult>` (line 173)
- Replace `_data.EloRatings` with `_data.Glicko2Ratings` (line 233)
- Note: `EloRatingResult` used `.Rating` and `.GamesPlayed` — `Glicko2RatingResult` has the same fields, so other usage is compatible

**`src/Sts2Analytics.Web/Pages/CardExplorer.razor`:**
- Replace `_data.EloRatings` with `_data.Glicko2Ratings` (line 57)
- The lookup uses `.Rating` which still exists on the new type

**`src/Sts2Analytics.Web/Pages/CardMatchups.razor`:**
- Replace `_data.EloRatings` with `_data.Glicko2Ratings` (lines 99, 108-109)
- Replace `EloRatingResult` type references with `Glicko2RatingResult`

**`src/Sts2Analytics.Web/Layout/MainLayout.razor`:**
- Replace `Elo Rankings` nav text with `Card Ratings` (line 27)

- [ ] **Step 5: Verify web project builds**

Run: `/home/tom/.dotnet/dotnet build src/Sts2Analytics.Web/`
Expected: Build succeeds

- [ ] **Step 6: Commit**

```bash
git add src/Sts2Analytics.Web/Pages/RatingLeaderboard.razor src/Sts2Analytics.Web/Services/DataService.cs
git add -A
git commit -m "feat: add rating dashboard with confidence intervals and act filter"
```

---

## Task 8: Full Integration Verification

**Files:** None (verification only)

- [ ] **Step 1: Run all tests**

Run: `/home/tom/.dotnet/dotnet test tests/Sts2Analytics.Core.Tests/`
Expected: All tests pass, no old Elo tests remain

- [ ] **Step 2: Build entire solution**

Run: `/home/tom/.dotnet/dotnet build`
Expected: Clean build with no errors or warnings about missing Elo types

- [ ] **Step 3: Verify no stale Elo references remain**

Search for any remaining references to `EloCalculator`, `EloEngine`, `EloAnalytics`, `EloRatingResult`, `EloHistoryResult`, `EloRatingEntity`, `EloHistoryEntity` across the codebase. The only acceptable references are in git history and the spec/plan docs.

- [ ] **Step 4: Commit any final cleanup if needed**

```bash
git add -A
git commit -m "chore: final cleanup of Elo to Glicko-2 migration"
```

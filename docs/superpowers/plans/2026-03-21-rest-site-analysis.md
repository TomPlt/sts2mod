# Rest Site Decision Analysis — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface rest site decision patterns — when to smith vs heal, which cards to upgrade, and how HP threshold affects optimal choice — so the player can make better rest site decisions.

**Architecture:** New `RestSiteAnalytics` class in Core following existing analytics pattern (Dapper queries, `AnalyticsFilter`, typed results). Export to `data.json`. New Blazor dashboard page. Three analysis dimensions: choice win rates by HP bucket, card upgrade impact, and per-act decision patterns.

**Tech Stack:** C#/.NET 9.0, Dapper, SQLite, Blazor WASM, Radzen charts

---

## File Structure

| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Core/Analytics/RestSiteAnalytics.cs` | **Create.** Queries RestSiteChoices/Floors/Runs for decision win rates, HP-bucketed analysis, card upgrade impact |
| `src/Sts2Analytics.Core/Models/AnalyticsResults.cs` | **Modify.** Add result records for rest site analytics |
| `tests/Sts2Analytics.Core.Tests/Analytics/RestSiteAnalyticsTests.cs` | **Create.** Integration tests against test DB |
| `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | **Modify.** Add rest site data to JSON export |
| `src/Sts2Analytics.Web/Services/DataService.cs` | **Modify.** Add rest site export types to `ExportData` |
| `src/Sts2Analytics.Web/Pages/RestSites.razor` | **Create.** Dashboard page with charts and tables |
| `src/Sts2Analytics.Web/Layout/MainLayout.razor` | **Modify.** Add nav link |

---

### Task 1: Result Records

**Files:**
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`

- [ ] **Step 1: Add rest site result records**

```csharp
// After the EliteCorrelationByAct record

public record RestSiteDecisionRate(string Choice, int Count, int Wins, double WinRate);

public record RestSiteHpBucket(string Choice, int HpBucketMin, int HpBucketMax, int Count, int Wins, double WinRate);

public record RestSiteUpgradeImpact(string CardId, int TimesUpgraded, int Wins, double WinRate);

public record RestSiteActBreakdown(int Act, string Choice, int Count, int Wins, double WinRate, double AvgHpPercent);
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Sts2Analytics.Core`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Core/Models/AnalyticsResults.cs
git commit -m "feat: add rest site analytics result records"
```

---

### Task 2: RestSiteAnalytics Class

**Files:**
- Create: `src/Sts2Analytics.Core/Analytics/RestSiteAnalytics.cs`

- [ ] **Step 1: Write failing test for GetDecisionWinRates**

Create `tests/Sts2Analytics.Core.Tests/Analytics/RestSiteAnalyticsTests.cs`:

```csharp
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;

namespace Sts2Analytics.Core.Tests.Analytics;

public class RestSiteAnalyticsTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public RestSiteAnalyticsTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Schema.Initialize(_conn);
        SeedData();
    }

    private void SeedData()
    {
        // Run 1: Win, Ironclad, A5
        _conn.Execute("INSERT INTO Runs (Id, FileName, Character, Ascension, Win, Seed, GameMode, StartTime) VALUES (1, 'run1.run', 'CHARACTER.IRONCLAD', 5, 1, 'seed1', 'STANDARD', '2026-01-01')");
        // Floor 5: rest site, 40/80 HP (50%) — chose SMITH
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (10, 1, 5, 0, 'rest_site', 40, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (1, 10, 'SMITH')");
        _conn.Execute("INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId) VALUES (1, 'CARD.INFLAME')");
        // Floor 12: rest site, 20/80 HP (25%) — chose HEAL
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (11, 1, 12, 1, 'rest_site', 20, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (2, 11, 'HEAL')");

        // Run 2: Loss, Ironclad, A5
        _conn.Execute("INSERT INTO Runs (Id, FileName, Character, Ascension, Win, Seed, GameMode, StartTime) VALUES (2, 'run2.run', 'CHARACTER.IRONCLAD', 5, 0, 'seed2', 'STANDARD', '2026-01-02')");
        // Floor 5: rest site, 60/80 HP (75%) — chose HEAL
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (20, 2, 5, 0, 'rest_site', 60, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (3, 20, 'HEAL')");
        // Floor 10: rest site, 30/80 HP (37.5%) — chose SMITH
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (21, 2, 10, 0, 'rest_site', 30, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (4, 21, 'SMITH')");
        _conn.Execute("INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId) VALUES (4, 'CARD.BASH')");
    }

    [Fact]
    public void GetDecisionWinRates_ReturnsCorrectRates()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetDecisionWinRates();

        var smith = results.First(r => r.Choice == "SMITH");
        Assert.Equal(2, smith.Count);
        Assert.Equal(1, smith.Wins);
        Assert.Equal(0.5, smith.WinRate);

        var heal = results.First(r => r.Choice == "HEAL");
        Assert.Equal(2, heal.Count);
        Assert.Equal(1, heal.Wins);
    }

    [Fact]
    public void GetDecisionsByHpBucket_BucketsCorrectly()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetDecisionsByHpBucket();

        // 50% HP SMITH (run 1 win) -> 50-75 bucket; 37.5% HP SMITH (run 2 loss) -> 25-50 bucket
        var smithMid = results.Where(r => r.Choice == "SMITH" && r.HpBucketMin == 50).ToList();
        Assert.Single(smithMid); // 50% HP falls in 50-75 bucket
        Assert.Equal(1, smithMid[0].Wins);

        var smithLow = results.Where(r => r.Choice == "SMITH" && r.HpBucketMin == 25).ToList();
        Assert.Single(smithLow); // 37.5% HP falls in 25-50 bucket
        Assert.Equal(0, smithLow[0].Wins);
    }

    [Fact]
    public void GetUpgradeImpact_ReturnsCardWinRates()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetUpgradeImpact();

        var inflame = results.First(r => r.CardId == "CARD.INFLAME");
        Assert.Equal(1, inflame.TimesUpgraded);
        Assert.Equal(1, inflame.Wins);
        Assert.Equal(1.0, inflame.WinRate);

        var bash = results.First(r => r.CardId == "CARD.BASH");
        Assert.Equal(1, bash.TimesUpgraded);
        Assert.Equal(0, bash.Wins);
    }

    [Fact]
    public void GetActBreakdown_SplitsByAct()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetActBreakdown();

        var act0 = results.Where(r => r.Act == 0).ToList();
        Assert.True(act0.Count >= 2); // SMITH and HEAL both appear in act 0
    }

    public void Dispose() => _conn.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "RestSiteAnalytics" -v n`
Expected: FAIL — `RestSiteAnalytics` class not found

- [ ] **Step 3: Implement RestSiteAnalytics**

Create `src/Sts2Analytics.Core/Analytics/RestSiteAnalytics.cs`:

```csharp
using System.Data;
using Dapper;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Analytics;

public class RestSiteAnalytics
{
    private readonly IDbConnection _connection;

    public RestSiteAnalytics(IDbConnection connection) => _connection = connection;

    public List<RestSiteDecisionRate> GetDecisionWinRates(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                rsc.Choice,
                COUNT(*) AS Count,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM RestSiteChoices rsc
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rsc.Choice
            ORDER BY COUNT(*) DESC
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int count = (int)(long)row.Count;
            int wins = (int)(long)row.Wins;
            return new RestSiteDecisionRate((string)row.Choice, count, wins,
                count > 0 ? (double)wins / count : 0);
        }).ToList();
    }

    public List<RestSiteHpBucket> GetDecisionsByHpBucket(AnalyticsFilter? filter = null)
    {
        var (conditions, parameters) = BuildConditions(filter);
        conditions.Add("f.MaxHp > 0");
        var where = "WHERE " + string.Join(" AND ", conditions);

        var sql = $"""
            SELECT
                rsc.Choice,
                CASE
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.25 THEN 0
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.50 THEN 25
                    WHEN CAST(f.CurrentHp AS REAL) / f.MaxHp < 0.75 THEN 50
                    ELSE 75
                END AS HpBucket,
                COUNT(*) AS Count,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM RestSiteChoices rsc
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rsc.Choice, HpBucket
            ORDER BY HpBucket, rsc.Choice
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int bucket = (int)(long)row.HpBucket;
            int count = (int)(long)row.Count;
            int wins = (int)(long)row.Wins;
            return new RestSiteHpBucket((string)row.Choice, bucket, bucket + 25, count, wins,
                count > 0 ? (double)wins / count : 0);
        }).ToList();
    }

    public List<RestSiteUpgradeImpact> GetUpgradeImpact(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                rsu.CardId,
                COUNT(*) AS TimesUpgraded,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins
            FROM RestSiteUpgrades rsu
            JOIN RestSiteChoices rsc ON rsu.RestSiteChoiceId = rsc.Id
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY rsu.CardId
            ORDER BY COUNT(*) DESC
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int count = (int)(long)row.TimesUpgraded;
            int wins = (int)(long)row.Wins;
            return new RestSiteUpgradeImpact((string)row.CardId, count, wins,
                count > 0 ? (double)wins / count : 0);
        }).ToList();
    }

    public List<RestSiteActBreakdown> GetActBreakdown(AnalyticsFilter? filter = null)
    {
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $"""
            SELECT
                f.ActIndex AS Act,
                rsc.Choice,
                COUNT(*) AS Count,
                SUM(CASE WHEN r.Win = 1 THEN 1 ELSE 0 END) AS Wins,
                AVG(CAST(f.CurrentHp AS REAL) / NULLIF(f.MaxHp, 0)) AS AvgHpPercent
            FROM RestSiteChoices rsc
            JOIN Floors f ON rsc.FloorId = f.Id
            JOIN Runs r ON f.RunId = r.Id
            {where}
            GROUP BY f.ActIndex, rsc.Choice
            ORDER BY f.ActIndex, COUNT(*) DESC
            """;

        return _connection.Query(sql, parameters).Select(row =>
        {
            int count = (int)(long)row.Count;
            int wins = (int)(long)row.Wins;
            return new RestSiteActBreakdown(
                (int)(long)row.Act, (string)row.Choice, count, wins,
                count > 0 ? (double)wins / count : 0,
                (double)(row.AvgHpPercent ?? 0));
        }).ToList();
    }

    private static (List<string> Conditions, DynamicParameters Parameters) BuildConditions(AnalyticsFilter? filter)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        if (filter is null) return (conditions, parameters);

        if (filter.Character is not null)
        {
            conditions.Add("r.Character = @Character");
            parameters.Add("Character", filter.Character);
        }
        if (filter.AscensionMin is not null)
        {
            conditions.Add("r.Ascension >= @AscensionMin");
            parameters.Add("AscensionMin", filter.AscensionMin);
        }
        if (filter.AscensionMax is not null)
        {
            conditions.Add("r.Ascension <= @AscensionMax");
            parameters.Add("AscensionMax", filter.AscensionMax);
        }
        if (filter.DateFrom is not null)
        {
            conditions.Add("r.StartTime >= @DateFrom");
            parameters.Add("DateFrom", filter.DateFrom.Value.ToString("o"));
        }
        if (filter.DateTo is not null)
        {
            conditions.Add("r.StartTime <= @DateTo");
            parameters.Add("DateTo", filter.DateTo.Value.ToString("o"));
        }
        if (filter.GameMode is not null)
        {
            conditions.Add("r.GameMode = @GameMode");
            parameters.Add("GameMode", filter.GameMode);
        }

        return (conditions, parameters);
    }

    private static (string Where, DynamicParameters Parameters) BuildWhereClause(AnalyticsFilter? filter)
    {
        var (conditions, parameters) = BuildConditions(filter);
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        return (where, parameters);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sts2Analytics.Core.Tests --filter "RestSiteAnalytics" -v n`
Expected: All 4 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Core/Analytics/RestSiteAnalytics.cs tests/Sts2Analytics.Core.Tests/Analytics/RestSiteAnalyticsTests.cs
git commit -m "feat: add RestSiteAnalytics with decision rates, HP buckets, upgrade impact, act breakdown"
```

---

### Task 3: Export Rest Site Data

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`
- Modify: `src/Sts2Analytics.Web/Services/DataService.cs`

- [ ] **Step 1: Add export types to DataService.cs**

Add after the `BlindSpotExport` class:

```csharp
public class RestSiteDecisionExport
{
    public string Choice { get; set; } = "";
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class RestSiteHpBucketExport
{
    public string Choice { get; set; } = "";
    public int HpBucketMin { get; set; }
    public int HpBucketMax { get; set; }
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class RestSiteUpgradeExport
{
    public string CardId { get; set; } = "";
    public int TimesUpgraded { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class RestSiteActExport
{
    public int Act { get; set; }
    public string Choice { get; set; } = "";
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double AvgHpPercent { get; set; }
}
```

Add to `ExportData` class:

```csharp
public List<RestSiteDecisionExport> RestSiteDecisions { get; set; } = [];
public List<RestSiteHpBucketExport> RestSiteHpBuckets { get; set; } = [];
public List<RestSiteUpgradeExport> RestSiteUpgrades { get; set; } = [];
public List<RestSiteActExport> RestSiteActBreakdown { get; set; } = [];
```

- [ ] **Step 2: Add rest site analytics to ExportCommand**

In the main export method (not ExportMod), after the path analytics section, add:

```csharp
// Rest site analytics
var restSiteAnalytics = new RestSiteAnalytics(conn);
var restSiteDecisions = restSiteAnalytics.GetDecisionWinRates();
var restSiteHpBuckets = restSiteAnalytics.GetDecisionsByHpBucket();
var restSiteUpgrades = restSiteAnalytics.GetUpgradeImpact();
var restSiteActBreakdown = restSiteAnalytics.GetActBreakdown();
```

Add to the export object (after `blindSpots`):

```csharp
restSiteDecisions,
restSiteHpBuckets,
restSiteUpgrades,
restSiteActBreakdown,
```

Add the `using` at the top if not present:

```csharp
using Sts2Analytics.Core.Analytics;
```

- [ ] **Step 3: Build and verify export**

Run:
```bash
dotnet build
dotnet run --project src/Sts2Analytics.Cli -- export --output src/Sts2Analytics.Web/wwwroot/data.json
```

Verify:
```bash
python3 -c "import json; d=json.load(open('src/Sts2Analytics.Web/wwwroot/data.json')); print('restSiteDecisions:', len(d.get('restSiteDecisions', []))); print('restSiteHpBuckets:', len(d.get('restSiteHpBuckets', [])))"
```

Expected: Non-zero counts for all four rest site arrays.

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/ExportCommand.cs src/Sts2Analytics.Web/Services/DataService.cs
git commit -m "feat: export rest site analytics to dashboard JSON"
```

---

### Task 4: Dashboard Page

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/RestSites.razor`
- Modify: `src/Sts2Analytics.Web/Layout/MainLayout.razor`

- [ ] **Step 1: Add nav link**

In `MainLayout.razor`, add after the existing nav links (before the closing `</nav>` or after the last `NavLink`):

```html
<NavLink class="nav-link" href="rest-sites">
    <span class="nav-icon">&#x2668;</span> Rest Sites
</NavLink>
```

- [ ] **Step 2: Create the Rest Sites page**

Create `src/Sts2Analytics.Web/Pages/RestSites.razor`:

```razor
@page "/rest-sites"
@inject DataService Data
@inject FilterState Filter
@implements IDisposable

<PageTitle>Rest Site Analysis — STS2 Analytics</PageTitle>

<h2>Rest Site Analysis</h2>

@if (_loading)
{
    <p class="dim-text">Loading...</p>
}
else
{
    <div class="rest-section">
        <h3>Decision Win Rates</h3>
        <p class="section-note">Win rate of runs where you made each choice at a rest site.</p>
        <div class="panel" style="padding: 0; overflow: hidden;">
            <table class="oracle-table">
                <thead>
                    <tr>
                        <th>Choice</th>
                        <th style="text-align: right;">Count</th>
                        <th style="text-align: right;">Wins</th>
                        <th style="text-align: right;">Win Rate</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var d in _decisions)
                    {
                        <tr>
                            <td style="font-weight: 500;">@d.Choice</td>
                            <td style="text-align: right;" class="mono">@d.Count</td>
                            <td style="text-align: right;" class="mono">@d.Wins</td>
                            <td style="text-align: right;">
                                <span class="elo-badge @WinRateClass(d.WinRate)">@d.WinRate.ToString("P0")</span>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>

    <div class="rest-section">
        <h3>Smith vs Heal by HP%</h3>
        <p class="section-note">Should you upgrade or rest? Win rate by HP threshold when making the decision.</p>
        <div class="hp-grid">
            @foreach (var bucket in _hpBuckets.GroupBy(b => new { b.HpBucketMin, b.HpBucketMax }).OrderBy(g => g.Key.HpBucketMin))
            {
                <div class="hp-bucket-card">
                    <div class="hp-bucket-label">@bucket.Key.HpBucketMin–@bucket.Key.HpBucketMax% HP</div>
                    @foreach (var row in bucket.OrderByDescending(r => r.Count))
                    {
                        <div class="hp-bucket-row">
                            <span class="choice-name">@row.Choice</span>
                            <span class="choice-count">@row.Count×</span>
                            <span class="elo-badge @WinRateClass(row.WinRate)">@row.WinRate.ToString("P0")</span>
                        </div>
                    }
                </div>
            }
        </div>
    </div>

    <div class="rest-section">
        <h3>Card Upgrade Impact</h3>
        <p class="section-note">Which cards you upgrade at rest sites, and the run win rate when you do.</p>
        <div class="panel" style="padding: 0; overflow: hidden;">
            <table class="oracle-table">
                <thead>
                    <tr>
                        <th>Card</th>
                        <th style="text-align: right;">Times Upgraded</th>
                        <th style="text-align: right;">Win Rate</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var u in _upgrades.Where(u => u.TimesUpgraded >= 2).OrderByDescending(u => u.WinRate))
                    {
                        <tr>
                            <td style="font-weight: 500;">@FormatName(u.CardId)</td>
                            <td style="text-align: right;" class="mono">@u.TimesUpgraded</td>
                            <td style="text-align: right;">
                                <span class="elo-badge @WinRateClass(u.WinRate)">@u.WinRate.ToString("P0")</span>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>

    <div class="rest-section">
        <h3>Decisions by Act</h3>
        <p class="section-note">How your rest site choices change through the run.</p>
        @foreach (var actGroup in _actBreakdown.GroupBy(a => a.Act).OrderBy(g => g.Key))
        {
            <h4 style="color: var(--frost, #4fc3f7); margin-top: 1rem;">Act @(actGroup.Key + 1)</h4>
            <div class="panel" style="padding: 0; overflow: hidden; margin-bottom: 1rem;">
                <table class="oracle-table">
                    <thead>
                        <tr>
                            <th>Choice</th>
                            <th style="text-align: right;">Count</th>
                            <th style="text-align: right;">Win Rate</th>
                            <th style="text-align: right;">Avg HP%</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in actGroup.OrderByDescending(r => r.Count))
                        {
                            <tr>
                                <td style="font-weight: 500;">@row.Choice</td>
                                <td style="text-align: right;" class="mono">@row.Count</td>
                                <td style="text-align: right;">
                                    <span class="elo-badge @WinRateClass(row.WinRate)">@row.WinRate.ToString("P0")</span>
                                </td>
                                <td style="text-align: right;" class="mono">@row.AvgHpPercent.ToString("P0")</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    </div>
}

@code {
    private bool _loading = true;
    private ExportData? _data;
    private List<RestSiteDecisionExport> _decisions = [];
    private List<RestSiteHpBucketExport> _hpBuckets = [];
    private List<RestSiteUpgradeExport> _upgrades = [];
    private List<RestSiteActExport> _actBreakdown = [];

    protected override async Task OnInitializedAsync()
    {
        _data = await Data.GetDataAsync();
        Filter.OnChange += OnFilterChanged;
        LoadData();
        _loading = false;
    }

    private void LoadData()
    {
        if (_data is null) return;
        _decisions = _data.RestSiteDecisions;
        _hpBuckets = _data.RestSiteHpBuckets;
        _upgrades = _data.RestSiteUpgrades;
        _actBreakdown = _data.RestSiteActBreakdown;
    }

    private void OnFilterChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose() => Filter.OnChange -= OnFilterChanged;

    static string WinRateClass(double wr) => wr switch
    {
        >= 0.6 => "high",
        >= 0.4 => "mid",
        _ => "low"
    };

    static string FormatName(string id)
    {
        var plusIdx = id.IndexOf('+');
        var suffix = "";
        var baseId = id;
        if (plusIdx >= 0) { suffix = " " + id[plusIdx..]; baseId = id[..plusIdx]; }
        var dot = baseId.IndexOf('.');
        var name = dot >= 0 ? baseId[(dot + 1)..] : baseId;
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w)) + suffix;
    }
}

<style>
    .rest-section {
        margin-bottom: 2rem;
    }

    .rest-section h3 {
        color: var(--text-secondary, #b0b0c8);
        margin-bottom: 0.25rem;
    }

    .section-note {
        font-size: 0.8rem;
        color: var(--text-dim, #666);
        margin-bottom: 0.75rem;
    }

    .hp-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
        gap: 1rem;
    }

    .hp-bucket-card {
        background: var(--void-1, #1a1a2e);
        border: 1px solid var(--surface-border, #2a2a4a);
        border-radius: 8px;
        padding: 1rem;
    }

    .hp-bucket-label {
        font-size: 0.85rem;
        font-weight: 600;
        color: var(--frost, #4fc3f7);
        margin-bottom: 0.5rem;
    }

    .hp-bucket-row {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        padding: 0.2rem 0;
    }

    .choice-name {
        flex: 1;
        font-size: 0.85rem;
    }

    .choice-count {
        font-size: 0.75rem;
        color: var(--text-dim, #666);
        font-family: 'JetBrains Mono', monospace;
    }
</style>
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/Sts2Analytics.Web`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Web/Pages/RestSites.razor src/Sts2Analytics.Web/Layout/MainLayout.razor
git commit -m "feat: add rest site analysis dashboard page"
```

---

### Task 5: End-to-End Verification

- [ ] **Step 1: Re-export data and start dashboard**

```bash
dotnet run --project src/Sts2Analytics.Cli -- export --output src/Sts2Analytics.Web/wwwroot/data.json
cd src/Sts2Analytics.Web && dotnet run --launch-profile http
```

- [ ] **Step 2: Verify rest site page loads at http://localhost:5202/rest-sites**

Check:
- Decision win rates table shows SMITH/HEAL/etc with counts
- HP bucket grid shows 4 buckets with per-choice win rates
- Card upgrade impact table shows cards upgraded 2+ times
- Act breakdown shows per-act decision patterns

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git commit -m "fix: rest site page adjustments"
```

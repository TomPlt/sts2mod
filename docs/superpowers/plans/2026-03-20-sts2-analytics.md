# STS2 Analytics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a C# analytics platform that parses STS2 run history files into SQLite and surfaces card/relic/path analytics with an Elo rating system, via CLI and Blazor WASM dashboard.

**Architecture:** Three-project .NET 9.0 solution — Core library (models, parsing, DB, analytics, Elo), CLI console app, Blazor WASM dashboard. Core is the shared brain; CLI and Web are thin consumers.

**Tech Stack:** .NET 9.0, System.Text.Json, Microsoft.Data.Sqlite, Dapper, System.CommandLine, Blazor WASM, Radzen.Blazor

**Spec:** `docs/superpowers/specs/2026-03-20-sts2-analytics-design.md`

**Sample data:** Run files at `/mnt/c/Users/Tom/AppData/Roaming/SlayTheSpire2/steam/76561198332920058/profile1/saves/history/` (JSON schema v8)

**Deferred to later:** `--watch` mode for import, `GetCardSynergies`, `GetArchetypeDetection`, `GetWinStreaks` (need more data/design work)

**Note:** SavePathDetector checks both native Windows `%AppData%` and WSL `/mnt/c/Users/*/AppData` paths.

---

## File Map

### Sts2Analytics.Core

| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Core/Models/RunFile.cs` | JSON deserialization models matching .run file schema |
| `src/Sts2Analytics.Core/Models/Entities.cs` | Database entity records (Run, Floor, CardChoice, etc.) |
| `src/Sts2Analytics.Core/Models/AnalyticsFilter.cs` | Shared filter record used by all analytics services |
| `src/Sts2Analytics.Core/Models/AnalyticsResults.cs` | Result DTOs for analytics queries |
| `src/Sts2Analytics.Core/Parsing/RunFileParser.cs` | Deserializes .run JSON into RunFile models |
| `src/Sts2Analytics.Core/Parsing/RunFileMapper.cs` | Maps RunFile → database entities |
| `src/Sts2Analytics.Core/Parsing/SavePathDetector.cs` | Auto-detects STS2 save paths on Windows/Mac/Linux |
| `src/Sts2Analytics.Core/Database/Schema.cs` | SQLite CREATE TABLE statements, migration logic |
| `src/Sts2Analytics.Core/Database/RunRepository.cs` | Insert/query runs and related entities |
| `src/Sts2Analytics.Core/Database/AnalyticsRepository.cs` | Raw SQL queries for analytics aggregations |
| `src/Sts2Analytics.Core/Analytics/CardAnalytics.cs` | Card win rates, pick rates, synergies, impact |
| `src/Sts2Analytics.Core/Analytics/RelicAnalytics.cs` | Relic win rates, pick rates, timing |
| `src/Sts2Analytics.Core/Analytics/PotionAnalytics.cs` | Potion pick rates, usage timing, waste |
| `src/Sts2Analytics.Core/Analytics/PathAnalytics.cs` | Path patterns, elite correlation, shop timing |
| `src/Sts2Analytics.Core/Analytics/EconomyAnalytics.cs` | Gold efficiency, shop patterns, removal impact |
| `src/Sts2Analytics.Core/Analytics/CombatAnalytics.cs` | Damage by encounter, death floors, HP thresholds |
| `src/Sts2Analytics.Core/Analytics/RunAnalytics.cs` | Overall win rate, run length, archetypes, streaks |
| `src/Sts2Analytics.Core/Elo/EloCalculator.cs` | Pure Elo math: expected score, rating update |
| `src/Sts2Analytics.Core/Elo/EloEngine.cs` | Processes runs through Elo system, manages ratings |
| `src/Sts2Analytics.Core/Elo/EloAnalytics.cs` | Queries: rankings, history, matchups, skip Elo |

### Sts2Analytics.Cli

| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Cli/Program.cs` | Root command setup, DI |
| `src/Sts2Analytics.Cli/Commands/ImportCommand.cs` | Parse .run files into DB |
| `src/Sts2Analytics.Cli/Commands/StatsCommand.cs` | Overall stats summary |
| `src/Sts2Analytics.Cli/Commands/CardsCommand.cs` | Card pick/win rate table |
| `src/Sts2Analytics.Cli/Commands/RelicsCommand.cs` | Relic pick/win rate table |
| `src/Sts2Analytics.Cli/Commands/EloCommand.cs` | Elo leaderboard, matchups |
| `src/Sts2Analytics.Cli/Commands/RunCommand.cs` | Single run breakdown |
| `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | Export DB to JSON |

### Sts2Analytics.Web

| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Web/Program.cs` | Blazor WASM entry point |
| `src/Sts2Analytics.Web/Services/DataService.cs` | Loads exported JSON, bridges to analytics |
| `src/Sts2Analytics.Web/Pages/Overview.razor` | Dashboard home |
| `src/Sts2Analytics.Web/Pages/CardExplorer.razor` | Card table + detail |
| `src/Sts2Analytics.Web/Pages/CardMatchups.razor` | Head-to-head grid |
| `src/Sts2Analytics.Web/Pages/RelicExplorer.razor` | Relic table + detail |
| `src/Sts2Analytics.Web/Pages/RunHistory.razor` | Run list + floor timeline |
| `src/Sts2Analytics.Web/Pages/PathAnalysis.razor` | Path heatmap |
| `src/Sts2Analytics.Web/Pages/Economy.razor` | Gold charts |
| `src/Sts2Analytics.Web/Pages/Combat.razor` | Combat stats |
| `src/Sts2Analytics.Web/Pages/EloLeaderboard.razor` | Elo rankings |
| `src/Sts2Analytics.Web/Components/FilterBar.razor` | Shared filter component |
| `src/Sts2Analytics.Web/Components/RunTimeline.razor` | Floor-by-floor visualization |

### Tests

| File | Responsibility |
|------|---------------|
| `tests/Sts2Analytics.Core.Tests/Parsing/RunFileParserTests.cs` | JSON parsing correctness |
| `tests/Sts2Analytics.Core.Tests/Parsing/RunFileMapperTests.cs` | Entity mapping correctness |
| `tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs` | Table creation, migration |
| `tests/Sts2Analytics.Core.Tests/Database/RunRepositoryTests.cs` | Insert/query round-trip |
| `tests/Sts2Analytics.Core.Tests/Analytics/CardAnalyticsTests.cs` | Card analytics queries |
| `tests/Sts2Analytics.Core.Tests/Analytics/RunAnalyticsTests.cs` | Run analytics queries |
| `tests/Sts2Analytics.Core.Tests/Elo/EloCalculatorTests.cs` | Elo math |
| `tests/Sts2Analytics.Core.Tests/Elo/EloEngineTests.cs` | Full Elo processing |
| `tests/Sts2Analytics.Core.Tests/Fixtures/` | Copy of sample .run files for tests |

---

## Task 1: Solution Scaffolding

**Files:**
- Create: `Sts2Analytics.sln`
- Create: `src/Sts2Analytics.Core/Sts2Analytics.Core.csproj`
- Create: `src/Sts2Analytics.Cli/Sts2Analytics.Cli.csproj`
- Create: `src/Sts2Analytics.Web/Sts2Analytics.Web.csproj`
- Create: `tests/Sts2Analytics.Core.Tests/Sts2Analytics.Core.Tests.csproj`
- Create: `.gitignore`

- [ ] **Step 1: Create solution and Core project**

```bash
cd /home/tom/projects/sts2mod
dotnet new sln -n Sts2Analytics
mkdir -p src/Sts2Analytics.Core
dotnet new classlib -n Sts2Analytics.Core -o src/Sts2Analytics.Core -f net9.0
dotnet sln add src/Sts2Analytics.Core/Sts2Analytics.Core.csproj
```

- [ ] **Step 2: Create CLI project**

```bash
mkdir -p src/Sts2Analytics.Cli
dotnet new console -n Sts2Analytics.Cli -o src/Sts2Analytics.Cli -f net9.0
dotnet sln add src/Sts2Analytics.Cli/Sts2Analytics.Cli.csproj
dotnet add src/Sts2Analytics.Cli/Sts2Analytics.Cli.csproj reference src/Sts2Analytics.Core/Sts2Analytics.Core.csproj
```

- [ ] **Step 3: Create Web project**

```bash
mkdir -p src/Sts2Analytics.Web
# .NET 9 uses 'blazor' template with render mode flag (blazorwasm is deprecated)
dotnet new blazor -n Sts2Analytics.Web -o src/Sts2Analytics.Web -f net9.0 --interactivity WebAssembly --empty
dotnet sln add src/Sts2Analytics.Web/Sts2Analytics.Web.csproj
dotnet add src/Sts2Analytics.Web/Sts2Analytics.Web.csproj reference src/Sts2Analytics.Core/Sts2Analytics.Core.csproj
```

If `--interactivity WebAssembly` is not supported, try `dotnet new blazorwasm` as fallback. Verify the template exists with `dotnet new list blazor`.

- [ ] **Step 4: Create test project**

```bash
mkdir -p tests/Sts2Analytics.Core.Tests
dotnet new xunit -n Sts2Analytics.Core.Tests -o tests/Sts2Analytics.Core.Tests -f net9.0
dotnet sln add tests/Sts2Analytics.Core.Tests/Sts2Analytics.Core.Tests.csproj
dotnet add tests/Sts2Analytics.Core.Tests/Sts2Analytics.Core.Tests.csproj reference src/Sts2Analytics.Core/Sts2Analytics.Core.csproj
dotnet add tests/Sts2Analytics.Core.Tests/Sts2Analytics.Core.Tests.csproj package Microsoft.Data.Sqlite
dotnet add tests/Sts2Analytics.Core.Tests/Sts2Analytics.Core.Tests.csproj package Dapper
```

- [ ] **Step 5: Add NuGet dependencies to Core**

```bash
dotnet add src/Sts2Analytics.Core/Sts2Analytics.Core.csproj package Microsoft.Data.Sqlite
dotnet add src/Sts2Analytics.Core/Sts2Analytics.Core.csproj package Dapper
dotnet add src/Sts2Analytics.Cli/Sts2Analytics.Cli.csproj package System.CommandLine
```

- [ ] **Step 6: Add .gitignore, copy test fixture, build**

Create `.gitignore` for .NET (bin/, obj/, *.user, etc.).

Copy one sample .run file to `tests/Sts2Analytics.Core.Tests/Fixtures/sample_win.run`.
Copy one losing run to `tests/Sts2Analytics.Core.Tests/Fixtures/sample_loss.run`.

```bash
dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 7: Init git repo and commit**

```bash
git init
git add -A
git commit -m "chore: scaffold solution with Core, CLI, Web, and test projects"
```

---

## Task 2: JSON Deserialization Models

**Files:**
- Create: `src/Sts2Analytics.Core/Models/RunFile.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Parsing/RunFileParserTests.cs`
- Create: `src/Sts2Analytics.Core/Parsing/RunFileParser.cs`

- [ ] **Step 1: Write failing test — parse sample run file**

```csharp
// tests/Sts2Analytics.Core.Tests/Parsing/RunFileParserTests.cs
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Parsing;

public class RunFileParserTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample_win.run");

    [Fact]
    public void Parse_SampleWinRun_ReturnsRunFileWithMetadata()
    {
        var result = RunFileParser.Parse(_fixturePath);

        Assert.Equal("J2R2Z14RCT", result.Seed);
        Assert.Equal(0, result.Ascension);
        Assert.Equal("standard", result.GameMode);
        Assert.True(result.Win);
        Assert.False(result.WasAbandoned);
        Assert.Equal("v0.98.0", result.BuildId);
        Assert.Equal(8, result.SchemaVersion);
        Assert.Equal("steam", result.PlatformType);
        Assert.Equal(3, result.Acts.Count);
    }

    [Fact]
    public void Parse_SampleWinRun_ReturnsPlayerData()
    {
        var result = RunFileParser.Parse(_fixturePath);

        Assert.Single(result.Players);
        Assert.Equal("CHARACTER.IRONCLAD", result.Players[0].Character);
        Assert.Equal(3, result.Players[0].MaxPotionSlotCount);
        Assert.True(result.Players[0].Deck.Count > 10);
        Assert.True(result.Players[0].Relics.Count > 5);
    }

    [Fact]
    public void Parse_SampleWinRun_ReturnsMapPointHistory()
    {
        var result = RunFileParser.Parse(_fixturePath);

        Assert.Equal(3, result.MapPointHistory.Count); // 3 acts
        Assert.True(result.MapPointHistory[0].Count > 5); // act 1 floors
        Assert.Equal("monster", result.MapPointHistory[0][0].MapPointType);
    }

    [Fact]
    public void Parse_SampleWinRun_ParsesCardChoices()
    {
        var result = RunFileParser.Parse(_fixturePath);
        var floor1 = result.MapPointHistory[0][0];
        var stats = floor1.PlayerStats[0];

        Assert.Equal(3, stats.CardChoices.Count);
        Assert.Equal("CARD.SETUP_STRIKE", stats.CardChoices[0].Card.Id);
        Assert.True(stats.CardChoices[0].WasPicked);
        Assert.Equal("CARD.TREMBLE", stats.CardChoices[1].Card.Id);
        Assert.False(stats.CardChoices[1].WasPicked);
    }

    [Fact]
    public void Parse_SampleWinRun_ParsesEnchantments()
    {
        var result = RunFileParser.Parse(_fixturePath);
        // Act 3 floor 1 (index 0) has enchanted card choices with ENCHANTMENT.GLAM
        var act3 = result.MapPointHistory[2];
        var monsterFloor = act3[1]; // second floor in act 3
        var stats = monsterFloor.PlayerStats[0];
        var pickedCard = stats.CardChoices.First(c => c.WasPicked);

        Assert.NotNull(pickedCard.Card.Enchantment);
        Assert.Equal("ENCHANTMENT.GLAM", pickedCard.Card.Enchantment.Id);
    }
}
```

- [ ] **Step 2: Ensure test fixtures are copied to output**

Add to `tests/Sts2Analytics.Core.Tests/Sts2Analytics.Core.Tests.csproj`:
```xml
<ItemGroup>
  <None Update="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "RunFileParserTests" -v n
```
Expected: FAIL — RunFileParser class doesn't exist.

- [ ] **Step 4: Create RunFile models**

```csharp
// src/Sts2Analytics.Core/Models/RunFile.cs
using System.Text.Json.Serialization;

namespace Sts2Analytics.Core.Models;

public record RunFile
{
    [JsonPropertyName("acts")] public List<string> Acts { get; init; } = [];
    [JsonPropertyName("ascension")] public int Ascension { get; init; }
    [JsonPropertyName("build_id")] public string BuildId { get; init; } = "";
    [JsonPropertyName("game_mode")] public string GameMode { get; init; } = "";
    [JsonPropertyName("killed_by_encounter")] public string KilledByEncounter { get; init; } = "NONE.NONE";
    [JsonPropertyName("killed_by_event")] public string KilledByEvent { get; init; } = "NONE.NONE";
    [JsonPropertyName("map_point_history")] public List<List<MapPoint>> MapPointHistory { get; init; } = [];
    [JsonPropertyName("modifiers")] public List<string> Modifiers { get; init; } = [];
    [JsonPropertyName("platform_type")] public string PlatformType { get; init; } = "";
    [JsonPropertyName("players")] public List<PlayerData> Players { get; init; } = [];
    [JsonPropertyName("run_time")] public int RunTime { get; init; }
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
    [JsonPropertyName("seed")] public string Seed { get; init; } = "";
    [JsonPropertyName("start_time")] public long StartTime { get; init; }
    [JsonPropertyName("was_abandoned")] public bool WasAbandoned { get; init; }
    [JsonPropertyName("win")] public bool Win { get; init; }
}

public record MapPoint
{
    [JsonPropertyName("map_point_type")] public string MapPointType { get; init; } = "";
    [JsonPropertyName("player_stats")] public List<PlayerFloorStats> PlayerStats { get; init; } = [];
    [JsonPropertyName("rooms")] public List<Room> Rooms { get; init; } = [];
}

public record Room
{
    [JsonPropertyName("model_id")] public string? ModelId { get; init; }
    [JsonPropertyName("monster_ids")] public List<string>? MonsterIds { get; init; }
    [JsonPropertyName("room_type")] public string RoomType { get; init; } = "";
    [JsonPropertyName("turns_taken")] public int TurnsTaken { get; init; }
}

public record PlayerFloorStats
{
    [JsonPropertyName("player_id")] public int PlayerId { get; init; }
    [JsonPropertyName("current_hp")] public int CurrentHp { get; init; }
    [JsonPropertyName("max_hp")] public int MaxHp { get; init; }
    [JsonPropertyName("damage_taken")] public int DamageTaken { get; init; }
    [JsonPropertyName("hp_healed")] public int HpHealed { get; init; }
    [JsonPropertyName("max_hp_gained")] public int MaxHpGained { get; init; }
    [JsonPropertyName("max_hp_lost")] public int MaxHpLost { get; init; }
    [JsonPropertyName("current_gold")] public int CurrentGold { get; init; }
    [JsonPropertyName("gold_gained")] public int GoldGained { get; init; }
    [JsonPropertyName("gold_spent")] public int GoldSpent { get; init; }
    [JsonPropertyName("gold_lost")] public int GoldLost { get; init; }
    [JsonPropertyName("gold_stolen")] public int GoldStolen { get; init; }

    [JsonPropertyName("card_choices")] public List<CardChoiceEntry>? CardChoices { get; init; }
    [JsonPropertyName("cards_gained")] public List<CardEntry>? CardsGained { get; init; }
    [JsonPropertyName("cards_removed")] public List<CardEntry>? CardsRemoved { get; init; }
    [JsonPropertyName("cards_transformed")] public List<CardTransformEntry>? CardsTransformed { get; init; }
    [JsonPropertyName("cards_enchanted")] public List<CardEnchantedEntry>? CardsEnchanted { get; init; }
    [JsonPropertyName("relic_choices")] public List<RelicChoiceEntry>? RelicChoices { get; init; }
    [JsonPropertyName("bought_relics")] public List<string>? BoughtRelics { get; init; }
    [JsonPropertyName("bought_colorless")] public List<string>? BoughtColorless { get; init; }
    [JsonPropertyName("potion_choices")] public List<PotionChoiceEntry>? PotionChoices { get; init; }
    [JsonPropertyName("potion_used")] public List<string>? PotionUsed { get; init; }
    [JsonPropertyName("potion_discarded")] public List<string>? PotionDiscarded { get; init; }
    [JsonPropertyName("event_choices")] public List<EventChoiceEntry>? EventChoices { get; init; }
    [JsonPropertyName("ancient_choice")] public List<AncientChoiceEntry>? AncientChoice { get; init; }
    [JsonPropertyName("rest_site_choices")] public List<string>? RestSiteChoices { get; init; }
    [JsonPropertyName("upgraded_cards")] public List<string>? UpgradedCards { get; init; }
}

public record CardChoiceEntry
{
    [JsonPropertyName("card")] public CardEntry Card { get; init; } = new();
    [JsonPropertyName("was_picked")] public bool WasPicked { get; init; }
}

public record CardEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("current_upgrade_level")] public int? CurrentUpgradeLevel { get; init; }
    [JsonPropertyName("floor_added_to_deck")] public int? FloorAddedToDeck { get; init; }
    [JsonPropertyName("enchantment")] public EnchantmentEntry? Enchantment { get; init; }
}

public record EnchantmentEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("amount")] public int Amount { get; init; }
}

public record CardTransformEntry
{
    [JsonPropertyName("original_card")] public CardEntry OriginalCard { get; init; } = new();
    [JsonPropertyName("final_card")] public CardEntry FinalCard { get; init; } = new();
}

public record CardEnchantedEntry
{
    [JsonPropertyName("card")] public CardEntry Card { get; init; } = new();
    [JsonPropertyName("enchantment")] public string Enchantment { get; init; } = "";
}

public record RelicChoiceEntry
{
    [JsonPropertyName("choice")] public string Choice { get; init; } = "";
    [JsonPropertyName("was_picked")] public bool WasPicked { get; init; }
}

public record PotionChoiceEntry
{
    [JsonPropertyName("choice")] public string Choice { get; init; } = "";
    [JsonPropertyName("was_picked")] public bool WasPicked { get; init; }
}

public record EventChoiceEntry
{
    [JsonPropertyName("title")] public LocalizedText? Title { get; init; }
    [JsonPropertyName("variables")] public Dictionary<string, object>? Variables { get; init; }
}

public record LocalizedText
{
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("table")] public string Table { get; init; } = "";
}

public record AncientChoiceEntry
{
    [JsonPropertyName("TextKey")] public string TextKey { get; init; } = "";
    [JsonPropertyName("title")] public LocalizedText? Title { get; init; }
    [JsonPropertyName("was_chosen")] public bool WasChosen { get; init; }
}

public record PlayerData
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("character")] public string Character { get; init; } = "";
    [JsonPropertyName("max_potion_slot_count")] public int MaxPotionSlotCount { get; init; }
    [JsonPropertyName("deck")] public List<CardEntry> Deck { get; init; } = [];
    [JsonPropertyName("relics")] public List<RelicEntry> Relics { get; init; } = [];
    [JsonPropertyName("potions")] public List<PotionEntry> Potions { get; init; } = [];
}

public record RelicEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("floor_added_to_deck")] public int FloorAddedToDeck { get; init; }
    [JsonPropertyName("props")] public object? Props { get; init; }
}

public record PotionEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("slot_index")] public int SlotIndex { get; init; }
}
```

- [ ] **Step 5: Create RunFileParser**

```csharp
// src/Sts2Analytics.Core/Parsing/RunFileParser.cs
using System.Text.Json;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Parsing;

public static class RunFileParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static RunFile Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<RunFile>(json, Options)
            ?? throw new InvalidOperationException($"Failed to parse run file: {filePath}");
    }

    public static RunFile ParseJson(string json)
    {
        return JsonSerializer.Deserialize<RunFile>(json, Options)
            ?? throw new InvalidOperationException("Failed to parse run JSON");
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "RunFileParserTests" -v n
```
Expected: All 5 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add JSON deserialization models and RunFileParser"
```

---

## Task 3: Database Schema and Entity Models

**Files:**
- Create: `src/Sts2Analytics.Core/Models/Entities.cs`
- Create: `src/Sts2Analytics.Core/Database/Schema.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs`

- [ ] **Step 1: Write failing test — schema creates all tables**

```csharp
// tests/Sts2Analytics.Core.Tests/Database/SchemaTests.cs
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;

namespace Sts2Analytics.Core.Tests.Database;

public class SchemaTests
{
    [Fact]
    public void Initialize_CreatesAllTables()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        Schema.Initialize(conn);

        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
            .ToList();

        Assert.Contains("Runs", tables);
        Assert.Contains("Floors", tables);
        Assert.Contains("CardChoices", tables);
        Assert.Contains("RelicChoices", tables);
        Assert.Contains("PotionChoices", tables);
        Assert.Contains("PotionEvents", tables);
        Assert.Contains("EventChoices", tables);
        Assert.Contains("RestSiteChoices", tables);
        Assert.Contains("RestSiteUpgrades", tables);
        Assert.Contains("CardTransforms", tables);
        Assert.Contains("Monsters", tables);
        Assert.Contains("FinalDecks", tables);
        Assert.Contains("FinalRelics", tables);
        Assert.Contains("FinalPotions", tables);
        Assert.Contains("CardsGained", tables);
        Assert.Contains("CardRemovals", tables);
        Assert.Contains("CardEnchantments", tables);
        Assert.Contains("AncientChoices", tables);
        Assert.Contains("EloRatings", tables);
        Assert.Contains("EloHistory", tables);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        Schema.Initialize(conn);
        Schema.Initialize(conn); // should not throw

        var count = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table'");
        Assert.True(count >= 20);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "SchemaTests" -v n
```
Expected: FAIL — Schema class doesn't exist.

- [ ] **Step 3: Create Entities.cs**

```csharp
// src/Sts2Analytics.Core/Models/Entities.cs
namespace Sts2Analytics.Core.Models;

public record RunEntity
{
    public long Id { get; init; }
    public string FileName { get; init; } = "";
    public string Seed { get; init; } = "";
    public string Character { get; init; } = "";
    public int Ascension { get; init; }
    public string GameMode { get; init; } = "";
    public string BuildVersion { get; init; } = "";
    public bool Win { get; init; }
    public bool WasAbandoned { get; init; }
    public string KilledByEncounter { get; init; } = "NONE.NONE";
    public string KilledByEvent { get; init; } = "NONE.NONE";
    public long StartTime { get; init; }
    public int RunTime { get; init; }
    public string Acts { get; init; } = "";
    public int SchemaVersion { get; init; }
    public string PlatformType { get; init; } = "";
    public string? Modifiers { get; init; }
    public int MaxPotionSlots { get; init; }
}

public record FloorEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public int ActIndex { get; init; }
    public int FloorIndex { get; init; }
    public string MapPointType { get; init; } = "";
    public string? EncounterId { get; init; }
    public string? RoomType { get; init; }
    public int TurnsTaken { get; init; }
    public int PlayerId { get; init; }
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
    public int DamageTaken { get; init; }
    public int HpHealed { get; init; }
    public int MaxHpGained { get; init; }
    public int MaxHpLost { get; init; }
    public int CurrentGold { get; init; }
    public int GoldGained { get; init; }
    public int GoldSpent { get; init; }
    public int GoldLost { get; init; }
    public int GoldStolen { get; init; }
}

public record CardChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public bool WasPicked { get; init; }
    public bool WasBought { get; init; }
    public int UpgradeLevel { get; init; }
    public string? EnchantmentId { get; init; }
    public int EnchantmentAmount { get; init; }
    public string Source { get; init; } = "";
}

public record RelicChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string RelicId { get; init; } = "";
    public bool WasPicked { get; init; }
    public bool WasBought { get; init; }
    public string Source { get; init; } = "";
}

public record PotionChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string PotionId { get; init; } = "";
    public bool WasPicked { get; init; }
}

public record PotionEventEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string PotionId { get; init; } = "";
    public string Action { get; init; } = "";
}

public record EventChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string? EventId { get; init; }
    public string? ChoiceKey { get; init; }
    public string? ChoiceTable { get; init; }
    public string? Variables { get; init; }
}

public record RestSiteChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string Choice { get; init; } = "";
}

public record RestSiteUpgradeEntity
{
    public long Id { get; init; }
    public long RestSiteChoiceId { get; init; }
    public string CardId { get; init; } = "";
}

public record CardTransformEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string OriginalCardId { get; init; } = "";
    public string FinalCardId { get; init; } = "";
}

public record MonsterEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string MonsterId { get; init; } = "";
}

public record FinalDeckEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public string CardId { get; init; } = "";
    public int UpgradeLevel { get; init; }
    public int FloorAdded { get; init; }
    public string? EnchantmentId { get; init; }
}

public record FinalRelicEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public string RelicId { get; init; } = "";
    public int FloorAdded { get; init; }
    public string? Props { get; init; }
}

public record FinalPotionEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public string PotionId { get; init; } = "";
    public int SlotIndex { get; init; }
}

public record CardsGainedEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public int UpgradeLevel { get; init; }
    public string? EnchantmentId { get; init; }
    public string Source { get; init; } = "";
}

public record CardRemovalEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public int? FloorAddedToDeck { get; init; }
}

public record CardEnchantmentEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public string EnchantmentId { get; init; } = "";
    public int EnchantmentAmount { get; init; }
}

public record AncientChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string TextKey { get; init; } = "";
    public bool WasChosen { get; init; }
}

public record EloRatingEntity
{
    public long Id { get; init; }
    public string CardId { get; init; } = "";
    public string Character { get; init; } = "";
    public string Context { get; init; } = "overall";
    public double Rating { get; init; } = 1500.0;
    public int GamesPlayed { get; init; }
}

public record EloHistoryEntity
{
    public long Id { get; init; }
    public long EloRatingId { get; init; }
    public long RunId { get; init; }
    public double RatingBefore { get; init; }
    public double RatingAfter { get; init; }
    public long Timestamp { get; init; }
}
```

- [ ] **Step 4: Create Schema.cs**

```csharp
// src/Sts2Analytics.Core/Database/Schema.cs
using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Database;

public static class Schema
{
    public static void Initialize(IDbConnection connection)
    {
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Runs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName TEXT UNIQUE NOT NULL,
                Seed TEXT NOT NULL,
                Character TEXT NOT NULL,
                Ascension INTEGER NOT NULL,
                GameMode TEXT NOT NULL,
                BuildVersion TEXT NOT NULL,
                Win BOOLEAN NOT NULL,
                WasAbandoned BOOLEAN NOT NULL,
                KilledByEncounter TEXT NOT NULL DEFAULT 'NONE.NONE',
                KilledByEvent TEXT NOT NULL DEFAULT 'NONE.NONE',
                StartTime INTEGER NOT NULL,
                RunTime INTEGER NOT NULL,
                Acts TEXT NOT NULL,
                SchemaVersion INTEGER NOT NULL,
                PlatformType TEXT NOT NULL,
                Modifiers TEXT,
                MaxPotionSlots INTEGER NOT NULL DEFAULT 3
            );

            CREATE TABLE IF NOT EXISTS Floors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL REFERENCES Runs(Id),
                ActIndex INTEGER NOT NULL,
                FloorIndex INTEGER NOT NULL,
                MapPointType TEXT NOT NULL,
                EncounterId TEXT,
                RoomType TEXT,
                TurnsTaken INTEGER NOT NULL DEFAULT 0,
                PlayerId INTEGER NOT NULL DEFAULT 1,
                CurrentHp INTEGER NOT NULL DEFAULT 0,
                MaxHp INTEGER NOT NULL DEFAULT 0,
                DamageTaken INTEGER NOT NULL DEFAULT 0,
                HpHealed INTEGER NOT NULL DEFAULT 0,
                MaxHpGained INTEGER NOT NULL DEFAULT 0,
                MaxHpLost INTEGER NOT NULL DEFAULT 0,
                CurrentGold INTEGER NOT NULL DEFAULT 0,
                GoldGained INTEGER NOT NULL DEFAULT 0,
                GoldSpent INTEGER NOT NULL DEFAULT 0,
                GoldLost INTEGER NOT NULL DEFAULT 0,
                GoldStolen INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS CardChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                CardId TEXT NOT NULL,
                WasPicked BOOLEAN NOT NULL,
                WasBought BOOLEAN NOT NULL DEFAULT 0,
                UpgradeLevel INTEGER NOT NULL DEFAULT 0,
                EnchantmentId TEXT,
                EnchantmentAmount INTEGER NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT 'reward'
            );

            CREATE TABLE IF NOT EXISTS RelicChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                RelicId TEXT NOT NULL,
                WasPicked BOOLEAN NOT NULL,
                WasBought BOOLEAN NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT 'reward'
            );

            CREATE TABLE IF NOT EXISTS PotionChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                PotionId TEXT NOT NULL,
                WasPicked BOOLEAN NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PotionEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                PotionId TEXT NOT NULL,
                Action TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS EventChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                EventId TEXT,
                ChoiceKey TEXT,
                ChoiceTable TEXT,
                Variables TEXT
            );

            CREATE TABLE IF NOT EXISTS RestSiteChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                Choice TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RestSiteUpgrades (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RestSiteChoiceId INTEGER NOT NULL REFERENCES RestSiteChoices(Id),
                CardId TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CardTransforms (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                OriginalCardId TEXT NOT NULL,
                FinalCardId TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Monsters (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                MonsterId TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS FinalDecks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL REFERENCES Runs(Id),
                CardId TEXT NOT NULL,
                UpgradeLevel INTEGER NOT NULL DEFAULT 0,
                FloorAdded INTEGER NOT NULL DEFAULT 0,
                EnchantmentId TEXT
            );

            CREATE TABLE IF NOT EXISTS FinalRelics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL REFERENCES Runs(Id),
                RelicId TEXT NOT NULL,
                FloorAdded INTEGER NOT NULL DEFAULT 0,
                Props TEXT
            );

            CREATE TABLE IF NOT EXISTS FinalPotions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL REFERENCES Runs(Id),
                PotionId TEXT NOT NULL,
                SlotIndex INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CardsGained (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                CardId TEXT NOT NULL,
                UpgradeLevel INTEGER NOT NULL DEFAULT 0,
                EnchantmentId TEXT,
                Source TEXT NOT NULL DEFAULT 'reward'
            );

            CREATE TABLE IF NOT EXISTS CardRemovals (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                CardId TEXT NOT NULL,
                FloorAddedToDeck INTEGER
            );

            CREATE TABLE IF NOT EXISTS CardEnchantments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                CardId TEXT NOT NULL,
                EnchantmentId TEXT NOT NULL,
                EnchantmentAmount INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AncientChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorId INTEGER NOT NULL REFERENCES Floors(Id),
                TextKey TEXT NOT NULL,
                WasChosen BOOLEAN NOT NULL
            );

            CREATE TABLE IF NOT EXISTS EloRatings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CardId TEXT NOT NULL,
                Character TEXT NOT NULL,
                Context TEXT NOT NULL DEFAULT 'overall',
                Rating REAL NOT NULL DEFAULT 1500.0,
                GamesPlayed INTEGER NOT NULL DEFAULT 0,
                UNIQUE(CardId, Character, Context)
            );

            CREATE TABLE IF NOT EXISTS EloHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EloRatingId INTEGER NOT NULL REFERENCES EloRatings(Id),
                RunId INTEGER NOT NULL REFERENCES Runs(Id),
                RatingBefore REAL NOT NULL,
                RatingAfter REAL NOT NULL,
                Timestamp INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Floors_RunId ON Floors(RunId);
            CREATE INDEX IF NOT EXISTS IX_CardChoices_FloorId ON CardChoices(FloorId);
            CREATE INDEX IF NOT EXISTS IX_CardChoices_CardId ON CardChoices(CardId);
            CREATE INDEX IF NOT EXISTS IX_RelicChoices_FloorId ON RelicChoices(FloorId);
            CREATE INDEX IF NOT EXISTS IX_FinalDecks_RunId ON FinalDecks(RunId);
            CREATE INDEX IF NOT EXISTS IX_EloRatings_CardId ON EloRatings(CardId);
            CREATE INDEX IF NOT EXISTS IX_EloHistory_EloRatingId ON EloHistory(EloRatingId);
            """);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "SchemaTests" -v n
```
Expected: Both tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add database schema and entity models"
```

---

## Task 4: RunFile → Entity Mapper + Repository

**Files:**
- Create: `src/Sts2Analytics.Core/Parsing/RunFileMapper.cs`
- Create: `src/Sts2Analytics.Core/Database/RunRepository.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Parsing/RunFileMapperTests.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Database/RunRepositoryTests.cs`

- [ ] **Step 1: Write failing test — mapper converts RunFile to entities**

```csharp
// tests/Sts2Analytics.Core.Tests/Parsing/RunFileMapperTests.cs
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Parsing;

public class RunFileMapperTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample_win.run");

    [Fact]
    public void MapRun_ExtractsRunMetadata()
    {
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, _) = RunFileMapper.Map(runFile, "sample_win.run");

        Assert.Equal("sample_win.run", run.FileName);
        Assert.Equal("CHARACTER.IRONCLAD", run.Character);
        Assert.True(run.Win);
        Assert.Equal("J2R2Z14RCT", run.Seed);
    }

    [Fact]
    public void MapRun_ExtractsAllFloors()
    {
        var runFile = RunFileParser.Parse(_fixturePath);
        var (_, floors, _) = RunFileMapper.Map(runFile, "sample_win.run");

        Assert.True(floors.Count > 30); // 3 acts worth of floors
        Assert.Equal("monster", floors[0].MapPointType);
        Assert.Equal(0, floors[0].ActIndex);
    }

    [Fact]
    public void MapRun_ExtractsCardChoicesWithSkips()
    {
        var runFile = RunFileParser.Parse(_fixturePath);
        var (_, _, floorData) = RunFileMapper.Map(runFile, "sample_win.run");

        // Floor 12 (act 1, index 11) is where all 3 cards were skipped
        var skippedFloor = floorData.First(f =>
            f.CardChoices.Count > 0 && f.CardChoices.All(c => !c.WasPicked));
        Assert.Equal(3, skippedFloor.CardChoices.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "RunFileMapperTests" -v n
```
Expected: FAIL.

- [ ] **Step 3: Create FloorData record and implement RunFileMapper — run metadata + floor extraction**

Create `FloorData` record to hold all child entities for a floor:
```csharp
public record FloorData(
    List<CardChoiceEntity> CardChoices,
    List<RelicChoiceEntity> RelicChoices,
    List<PotionChoiceEntity> PotionChoices,
    List<PotionEventEntity> PotionEvents,
    List<EventChoiceEntity> EventChoices,
    List<RestSiteChoiceEntity> RestSiteChoices,
    List<string> RestSiteUpgradedCards,
    List<CardTransformEntity> CardTransforms,
    List<MonsterEntity> Monsters,
    List<CardsGainedEntity> CardsGained,
    List<CardRemovalEntity> CardRemovals,
    List<CardEnchantmentEntity> CardEnchantments,
    List<AncientChoiceEntity> AncientChoices
);
```

Implement `RunFileMapper.Map(RunFile, fileName)` returning `(RunEntity, List<FloorEntity>, List<FloorData>)`. Start with:
- Map `RunEntity` from top-level fields + `Players[0].Character`
- Iterate `MapPointHistory` (acts × floors), create `FloorEntity` from `PlayerStats[0]` + `Rooms[0]`
- Create empty `FloorData` for each floor (populated in next step)

- [ ] **Step 3b: Implement FloorData population — card/relic/potion/event/rest site extraction**

For each map point, populate `FloorData`:
- `CardChoices`: from `PlayerStats[0].CardChoices`, set Source from MapPointType (shop→"shop", else→"reward"), set `WasBought` by checking `BoughtColorless`
- `RelicChoices`: from `PlayerStats[0].RelicChoices`, set `WasBought` by checking `BoughtRelics`
- `PotionChoices`: from `PlayerStats[0].PotionChoices`
- `PotionEvents`: from `PotionUsed` (action="used") + `PotionDiscarded` (action="discarded")
- `EventChoices`: `EventId` from `Rooms[0].ModelId`, key/table from title
- `RestSiteChoices` + `RestSiteUpgradedCards` from `RestSiteChoices`/`UpgradedCards`
- `Monsters` from `Rooms[0].MonsterIds`
- `CardsGained`, `CardRemovals`, `CardEnchantments`, `CardTransforms`, `AncientChoices`

Also map final deck/relics/potions from `Players[0]`.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "RunFileMapperTests" -v n
```
Expected: All 3 tests PASS.

- [ ] **Step 5: Write failing test — repository round-trip**

```csharp
// tests/Sts2Analytics.Core.Tests/Database/RunRepositoryTests.cs
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Database;

public class RunRepositoryTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample_win.run");

    private SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    [Fact]
    public void ImportRun_InsertsAndReturnsId()
    {
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");

        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        Assert.True(runId > 0);
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Runs");
        Assert.Equal(1, count);
    }

    [Fact]
    public void ImportRun_SkipsDuplicateFileName()
    {
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");

        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        var secondId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        Assert.Equal(-1, secondId); // indicates skip
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Runs");
        Assert.Equal(1, count);
    }

    [Fact]
    public void ImportRun_InsertsFloorAndChildData()
    {
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");

        repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var floorCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Floors");
        var cardChoiceCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CardChoices");
        var finalDeckCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM FinalDecks");

        Assert.True(floorCount > 30);
        Assert.True(cardChoiceCount > 50);
        Assert.True(finalDeckCount > 20);
    }
}
```

- [ ] **Step 6: Implement RunRepository**

`RunRepository` wraps all inserts in a transaction. `ImportRun` checks for duplicate FileName first, then inserts Run → Floors → child entities → final deck/relics/potions. Uses Dapper `Execute` with parameterized SQL. Returns the new run ID or -1 if duplicate.

- [ ] **Step 7: Run all tests**

```bash
dotnet test tests/Sts2Analytics.Core.Tests -v n
```
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add RunFileMapper and RunRepository for import pipeline"
```

---

## Task 5: CLI Import Command

**Files:**
- Create: `src/Sts2Analytics.Core/Parsing/SavePathDetector.cs`
- Create: `src/Sts2Analytics.Cli/Commands/ImportCommand.cs`
- Modify: `src/Sts2Analytics.Cli/Program.cs`

- [ ] **Step 1: Implement SavePathDetector**

```csharp
// src/Sts2Analytics.Core/Parsing/SavePathDetector.cs
namespace Sts2Analytics.Core.Parsing;

public static class SavePathDetector
{
    public static List<string> FindHistoryDirectories()
    {
        var results = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var sts2Dir = Path.Combine(appData, "SlayTheSpire2", "steam");

        if (!Directory.Exists(sts2Dir)) return results;

        foreach (var steamIdDir in Directory.GetDirectories(sts2Dir))
        {
            foreach (var profileDir in Directory.GetDirectories(steamIdDir, "profile*"))
            {
                var historyDir = Path.Combine(profileDir, "saves", "history");
                if (Directory.Exists(historyDir))
                    results.Add(historyDir);
            }
        }

        return results;
    }

    public static string GetDefaultDbPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".sts2analytics");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "data.db");
    }
}
```

- [ ] **Step 2: Implement ImportCommand**

```csharp
// src/Sts2Analytics.Cli/Commands/ImportCommand.cs
using System.CommandLine;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class ImportCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string?>("path", () => null,
            "Path to .run files directory (auto-detects if omitted)");
        var dbOption = new Option<string?>("--db", "Database path");
        var cmd = new Command("import", "Import .run files into database")
        {
            pathArg, dbOption
        };

        cmd.SetHandler(async (path, dbPath) =>
        {
            dbPath ??= SavePathDetector.GetDefaultDbPath();
            var dirs = path != null
                ? [path]
                : SavePathDetector.FindHistoryDirectories();

            if (dirs.Count == 0)
            {
                Console.WriteLine("No STS2 save directories found. Specify a path.");
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            Schema.Initialize(conn);
            var repo = new RunRepository(conn);

            var imported = 0;
            var skipped = 0;
            foreach (var dir in dirs)
            {
                Console.WriteLine($"Scanning: {dir}");
                foreach (var file in Directory.GetFiles(dir, "*.run"))
                {
                    var fileName = Path.GetFileName(file);
                    try
                    {
                        var runFile = RunFileParser.Parse(file);
                        var (run, floors, floorData) = RunFileMapper.Map(runFile, fileName);
                        var id = repo.ImportRun(run, floors, floorData, runFile.Players[0]);
                        if (id > 0) imported++; else skipped++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error parsing {fileName}: {ex.Message}");
                    }
                }
            }

            // Process Elo for all newly imported runs
            if (imported > 0)
            {
                Console.WriteLine("Computing Elo ratings...");
                var engine = new Sts2Analytics.Core.Elo.EloEngine(conn);
                engine.ProcessAllRuns(); // processes unprocessed runs in chronological order
                Console.WriteLine("Elo ratings updated.");
            }

            Console.WriteLine($"Done: {imported} imported, {skipped} already in database.");
        }, pathArg, dbOption);

        return cmd;
    }
}
```

- [ ] **Step 3: Wire up Program.cs**

```csharp
// src/Sts2Analytics.Cli/Program.cs
using System.CommandLine;
using Sts2Analytics.Cli.Commands;

var root = new RootCommand("STS2 Analytics — run data analysis tool");
root.AddCommand(ImportCommand.Create());
return await root.InvokeAsync(args);
```

- [ ] **Step 4: Test manually against real data**

```bash
dotnet run --project src/Sts2Analytics.Cli -- import "/mnt/c/Users/Tom/AppData/Roaming/SlayTheSpire2/steam/76561198332920058/profile1/saves/history"
```
Expected: "Done: N imported, 0 already in database." (N = number of .run files found)

Run again — expected: "Done: 0 imported, N already in database."

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add CLI import command with auto-detection"
```

---

## Task 6: Core Analytics — Cards, Runs, Relics

**Files:**
- Create: `src/Sts2Analytics.Core/Models/AnalyticsFilter.cs`
- Create: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`
- Create: `src/Sts2Analytics.Core/Database/AnalyticsRepository.cs`
- Create: `src/Sts2Analytics.Core/Analytics/CardAnalytics.cs`
- Create: `src/Sts2Analytics.Core/Analytics/RunAnalytics.cs`
- Create: `src/Sts2Analytics.Core/Analytics/RelicAnalytics.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Analytics/CardAnalyticsTests.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Analytics/RunAnalyticsTests.cs`

- [ ] **Step 1: Create AnalyticsFilter and AnalyticsResults**

```csharp
// src/Sts2Analytics.Core/Models/AnalyticsFilter.cs
namespace Sts2Analytics.Core.Models;

public record AnalyticsFilter(
    string? Character = null,
    int? AscensionMin = null,
    int? AscensionMax = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? GameMode = null,
    int? ActIndex = null
);
```

```csharp
// src/Sts2Analytics.Core/Models/AnalyticsResults.cs
namespace Sts2Analytics.Core.Models;

public record CardWinRate(
    string CardId, int TimesPicked, int TimesSkipped,
    int WinsWhenPicked, int WinsWhenSkipped,
    double WinRateWhenPicked, double WinRateWhenSkipped, double WinRateDelta);

public record CardPickRate(string CardId, int TimesOffered, int TimesPicked, double PickRate);

public record RelicWinRate(
    string RelicId, int TimesPicked, int TimesSkipped,
    double WinRateWhenPicked, double WinRateWhenSkipped);

public record RelicPickRate(string RelicId, int TimesOffered, int TimesPicked, double PickRate);

public record RunSummary(int TotalRuns, int Wins, int Losses, double WinRate,
    Dictionary<string, int> RunsByCharacter);

public record CardImpactScore(string CardId, double PickRate, double WinRateDelta, double ImpactScore);
```

- [ ] **Step 2: Write failing test — card win rates**

```csharp
// tests/Sts2Analytics.Core.Tests/Analytics/CardAnalyticsTests.cs
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class CardAnalyticsTests
{
    private SqliteConnection SetupDbWithRun(string fixtureName)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, fixtureName);
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        return conn;
    }

    [Fact]
    public void GetCardWinRates_ReturnsRatesForPickedCards()
    {
        using var conn = SetupDbWithRun("sample_win.run");
        var analytics = new CardAnalytics(conn);

        var rates = analytics.GetCardWinRates();

        Assert.True(rates.Count > 0);
        // CARD.SETUP_STRIKE was picked in floor 1 of a winning run
        var setupStrike = rates.FirstOrDefault(r => r.CardId == "CARD.SETUP_STRIKE");
        Assert.NotNull(setupStrike);
        Assert.True(setupStrike.TimesPicked >= 1);
    }

    [Fact]
    public void GetCardPickRates_ReturnsRatesForOfferedCards()
    {
        using var conn = SetupDbWithRun("sample_win.run");
        var analytics = new CardAnalytics(conn);

        var rates = analytics.GetCardPickRates();

        Assert.True(rates.Count > 0);
        var anyCard = rates.First();
        Assert.True(anyCard.TimesOffered > 0);
        Assert.True(anyCard.PickRate >= 0.0 && anyCard.PickRate <= 1.0);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "CardAnalyticsTests" -v n
```
Expected: FAIL.

- [ ] **Step 4: Implement AnalyticsRepository**

SQL queries that join Runs ↔ Floors ↔ CardChoices etc., applying `AnalyticsFilter` as WHERE clauses. Methods:
- `QueryCardWinRates(filter?)` — GROUP BY CardId, count wins/total for picked vs skipped
- `QueryCardPickRates(filter?)` — GROUP BY CardId, count picked/total offered
- `QueryRelicWinRates(filter?)` — same pattern for relics
- `QueryRelicPickRates(filter?)`
- `QueryRunSummary(filter?)` — total runs, wins, by character

Each method builds SQL dynamically based on filter fields present.

- [ ] **Step 5: Implement CardAnalytics, RunAnalytics, RelicAnalytics**

Thin wrappers over `AnalyticsRepository` that return the result DTOs.

- [ ] **Step 6: Write RunAnalytics test**

```csharp
// tests/Sts2Analytics.Core.Tests/Analytics/RunAnalyticsTests.cs
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class RunAnalyticsTests
{
    [Fact]
    public void GetOverallWinRate_WithOneWinningRun_Returns100Percent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var analytics = new RunAnalytics(conn);
        var summary = analytics.GetOverallWinRate();

        Assert.Equal(1, summary.TotalRuns);
        Assert.Equal(1, summary.Wins);
        Assert.Equal(1.0, summary.WinRate);
    }
}
```

- [ ] **Step 7: Run all tests**

```bash
dotnet test tests/Sts2Analytics.Core.Tests -v n
```
Expected: All PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add card, relic, and run analytics with filtering"
```

---

## Task 7: Elo Rating System

**Files:**
- Create: `src/Sts2Analytics.Core/Elo/EloCalculator.cs`
- Create: `src/Sts2Analytics.Core/Elo/EloEngine.cs`
- Create: `src/Sts2Analytics.Core/Elo/EloAnalytics.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Elo/EloCalculatorTests.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Elo/EloEngineTests.cs`

- [ ] **Step 1: Write failing test — Elo math**

```csharp
// tests/Sts2Analytics.Core.Tests/Elo/EloCalculatorTests.cs
using Sts2Analytics.Core.Elo;

namespace Sts2Analytics.Core.Tests.Elo;

public class EloCalculatorTests
{
    [Fact]
    public void ExpectedScore_EqualRatings_Returns0_5()
    {
        var expected = EloCalculator.ExpectedScore(1500, 1500);
        Assert.Equal(0.5, expected, precision: 3);
    }

    [Fact]
    public void ExpectedScore_HigherRating_ReturnsAbove0_5()
    {
        var expected = EloCalculator.ExpectedScore(1600, 1400);
        Assert.True(expected > 0.5);
        Assert.True(expected < 1.0);
    }

    [Fact]
    public void UpdateRating_WinnerGains_LoserLoses()
    {
        var (newA, newB) = EloCalculator.UpdateRatings(1500, 1500, true, kFactor: 20);
        Assert.True(newA > 1500);
        Assert.True(newB < 1500);
        Assert.Equal(3000.0, newA + newB, precision: 1); // zero-sum
    }

    [Fact]
    public void GetKFactor_ScalesWithGamesPlayed()
    {
        Assert.Equal(40, EloCalculator.GetKFactor(5));
        Assert.Equal(20, EloCalculator.GetKFactor(20));
        Assert.Equal(10, EloCalculator.GetKFactor(50));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "EloCalculatorTests" -v n
```
Expected: FAIL.

- [ ] **Step 3: Implement EloCalculator**

```csharp
// src/Sts2Analytics.Core/Elo/EloCalculator.cs
namespace Sts2Analytics.Core.Elo;

public static class EloCalculator
{
    public static double ExpectedScore(double ratingA, double ratingB)
        => 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));

    public static (double newA, double newB) UpdateRatings(
        double ratingA, double ratingB, bool aWins, int kFactor = 20)
    {
        var expectedA = ExpectedScore(ratingA, ratingB);
        var scoreA = aWins ? 1.0 : 0.0;
        var newA = ratingA + kFactor * (scoreA - expectedA);
        var newB = ratingB + kFactor * ((1.0 - scoreA) - (1.0 - expectedA));
        return (newA, newB);
    }

    public static int GetKFactor(int gamesPlayed) => gamesPlayed switch
    {
        < 10 => 40,
        < 30 => 20,
        _ => 10
    };
}
```

- [ ] **Step 4: Run Elo calculator tests**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "EloCalculatorTests" -v n
```
Expected: All 4 PASS.

- [ ] **Step 5: Write failing test — EloEngine processes a run**

```csharp
// tests/Sts2Analytics.Core.Tests/Elo/EloEngineTests.cs
using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class EloEngineTests
{
    [Fact]
    public void ProcessRun_WinningRun_PickedCardsGainElo()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var engine = new EloEngine(conn);
        engine.ProcessRun(runId);

        // CARD.SETUP_STRIKE was picked in a winning run — should be above 1500
        var rating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM EloRatings WHERE CardId = 'CARD.SETUP_STRIKE' AND Context = 'overall'");
        Assert.NotNull(rating);
        Assert.True(rating > 1500);
    }

    [Fact]
    public void ProcessRun_SkipGetsEloEntry()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var engine = new EloEngine(conn);
        engine.ProcessRun(runId);

        var skipRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM EloRatings WHERE CardId = 'SKIP' AND Context = 'overall'");
        Assert.NotNull(skipRating);
    }
}
```

- [ ] **Step 6: Implement EloEngine**

`EloEngine.ProcessAllRuns()`: queries all runs not yet processed (LEFT JOIN EloHistory to find unprocessed), processes them in chronological order (ORDER BY StartTime). Calls `ProcessRun` for each.

`EloEngine.ProcessRun(runId)`:
1. Query the run's character and win status
2. Query all card choice floors for this run (JOIN Floors + CardChoices)
3. Group choices by FloorId — each group is a "matchup"
4. For each matchup:
   - Identify picked cards, skipped cards, and whether all were skipped (Skip wins)
   - For each winner/loser pair, get or create EloRating entries (contexts: "overall" + per-character)
   - Update ratings using `EloCalculator`, respecting K-factor
   - Insert `EloHistory` records
5. All within a transaction

- [ ] **Step 7: Implement EloAnalytics**

```csharp
// src/Sts2Analytics.Core/Elo/EloAnalytics.cs
```

Methods querying EloRatings/EloHistory tables:
- `GetCardEloRatings(filter?)` — ORDER BY Rating DESC
- `GetCardEloHistory(cardId)` — JOIN EloHistory + Runs for timeline
- `GetCardMatchups(cardA, cardB)` — count wins when A picked over B vs B picked over A
- `GetSkipEloByContext()` — all SKIP entries

- [ ] **Step 8: Run all tests**

```bash
dotnet test tests/Sts2Analytics.Core.Tests -v n
```
Expected: All PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add Elo rating system with skip tracking"
```

---

## Task 8: Remaining Analytics Services

**Files:**
- Create: `src/Sts2Analytics.Core/Analytics/PotionAnalytics.cs`
- Create: `src/Sts2Analytics.Core/Analytics/PathAnalytics.cs`
- Create: `src/Sts2Analytics.Core/Analytics/EconomyAnalytics.cs`
- Create: `src/Sts2Analytics.Core/Analytics/CombatAnalytics.cs`
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Analytics/CombatAnalyticsTests.cs`
- Create: `tests/Sts2Analytics.Core.Tests/Analytics/PathAnalyticsTests.cs`

- [ ] **Step 1: Add result DTOs for remaining analytics**

Add to `AnalyticsResults.cs`:
```csharp
public record PotionPickRate(string PotionId, int TimesOffered, int TimesPicked, double PickRate);
public record PotionUsageTiming(string PotionId, string RoomType, int TimesUsed);
public record PathPatternWinRate(string PathSignature, int TotalRuns, int Wins, double WinRate);
public record EliteCorrelation(int EliteCount, int TotalRuns, int Wins, double WinRate);
public record GoldEfficiency(string Category, int TotalSpent, int RunCount, double WinRate);
public record DamageByEncounter(string EncounterId, double AvgDamage, int SampleSize);
public record DeathFloor(int ActIndex, int FloorIndex, string? EncounterId, int DeathCount);
public record HpThreshold(int FloorIndex, int HpBucket, int TotalRuns, int Wins, double WinRate);
```

- [ ] **Step 2: Write failing test — CombatAnalytics**

```csharp
// tests/Sts2Analytics.Core.Tests/Analytics/CombatAnalyticsTests.cs
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class CombatAnalyticsTests
{
    private SqliteConnection SetupDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        return conn;
    }

    [Fact]
    public void GetDamageTakenByEncounter_ReturnsEncounters()
    {
        using var conn = SetupDb();
        var analytics = new CombatAnalytics(conn);
        var results = analytics.GetDamageTakenByEncounter();
        Assert.True(results.Count > 0);
        Assert.All(results, r => Assert.True(r.SampleSize > 0));
    }

    [Fact]
    public void GetDeathFloorDistribution_WinningRunReturnsEmpty()
    {
        using var conn = SetupDb();
        var analytics = new CombatAnalytics(conn);
        var results = analytics.GetDeathFloorDistribution();
        Assert.Empty(results); // winning run has no death floor
    }
}
```

- [ ] **Step 3: Write failing test — PathAnalytics**

```csharp
// tests/Sts2Analytics.Core.Tests/Analytics/PathAnalyticsTests.cs
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class PathAnalyticsTests
{
    private SqliteConnection SetupDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        return conn;
    }

    [Fact]
    public void GetEliteCountCorrelation_ReturnsData()
    {
        using var conn = SetupDb();
        var analytics = new PathAnalytics(conn);
        var results = analytics.GetEliteCountCorrelation();
        Assert.True(results.Count > 0);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test tests/Sts2Analytics.Core.Tests --filter "CombatAnalyticsTests|PathAnalyticsTests" -v n
```
Expected: FAIL.

- [ ] **Step 5: Implement PotionAnalytics**

`GetPotionPickRates`: `SELECT PotionId, COUNT(*) as Offered, SUM(CASE WHEN WasPicked THEN 1 ELSE 0 END) as Picked FROM PotionChoices JOIN Floors ON ... JOIN Runs ON ... GROUP BY PotionId`
`GetPotionUsageTiming`: `SELECT PotionId, f.RoomType, COUNT(*) FROM PotionEvents JOIN Floors f ON ... WHERE Action='used' GROUP BY PotionId, f.RoomType`
`GetPotionWasteRate`: count discarded / (used + discarded) per potion

- [ ] **Step 6: Implement PathAnalytics**

`GetPathPatternWinRates`: `SELECT GROUP_CONCAT(MapPointType, '-') as PathSig, r.Win FROM Floors JOIN Runs r ON ... GROUP BY RunId ORDER BY FloorIndex` — then group by PathSig to compute win rates.
`GetEliteCountCorrelation`: `SELECT COUNT(*) as EliteCount, r.Win FROM Floors JOIN Runs r ON ... WHERE MapPointType='elite' GROUP BY RunId` — then aggregate by EliteCount.
`GetShopTimingImpact`: compare first-shop FloorIndex vs win rate.

- [ ] **Step 7: Implement EconomyAnalytics**

`GetGoldEfficiency`: `SELECT SUM(GoldSpent), r.Win FROM Floors JOIN Runs r ON ... GROUP BY RunId` — bucket by gold ranges.
`GetShopPurchasePatterns`: count CardChoices with WasBought, RelicChoices with WasBought, CardRemovals at shop floors.
`GetCardRemovalImpact`: join CardRemovals with Runs.Win, group by CardId.

- [ ] **Step 8: Implement CombatAnalytics**

`GetDamageTakenByEncounter`: `SELECT EncounterId, AVG(DamageTaken), COUNT(*) FROM Floors WHERE EncounterId IS NOT NULL GROUP BY EncounterId`
`GetTurnsByEncounter`: same pattern with TurnsTaken.
`GetDeathFloorDistribution`: for losing runs, find the floor with max FloorIndex per run.
`GetHpThresholdAnalysis`: bucket HP at each floor into ranges (0-20, 20-40, etc.), compute win rate per bucket per floor.

- [ ] **Step 9: Run all tests**

```bash
dotnet test tests/Sts2Analytics.Core.Tests -v n
```
Expected: All PASS.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add potion, path, economy, and combat analytics"
```

---

## Task 9: CLI Stats, Cards, Relics, Elo Commands

**Files:**
- Create: `src/Sts2Analytics.Cli/Commands/StatsCommand.cs`
- Create: `src/Sts2Analytics.Cli/Commands/CardsCommand.cs`
- Create: `src/Sts2Analytics.Cli/Commands/RelicsCommand.cs`
- Create: `src/Sts2Analytics.Cli/Commands/EloCommand.cs`
- Create: `src/Sts2Analytics.Cli/Commands/RunCommand.cs`
- Create: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`
- Modify: `src/Sts2Analytics.Cli/Program.cs`

- [ ] **Step 1: Implement StatsCommand**

Opens DB, calls `RunAnalytics.GetOverallWinRate()`, prints summary table:
```
Total Runs: 93  |  Wins: 42  |  Losses: 51  |  Win Rate: 45.2%

By Character:
  IRONCLAD    38 runs  47.4% win rate
  SILENT      22 runs  40.9% win rate
  ...
```

- [ ] **Step 2: Implement CardsCommand**

`--sort winrate|pickrate|impact`, `--top N`, `--character`. Prints table:
```
Card                    Pick%   Win%(picked)  Win%(skipped)  Delta    Impact
CARD.OFFERING           89.5%   68.2%         31.4%          +36.8%   32.9
CARD.BATTLE_TRANCE      76.3%   61.5%         38.1%          +23.4%   17.9
...
```

- [ ] **Step 2b: Implement RelicsCommand**

Same pattern as CardsCommand. `--sort winrate|pickrate`. Prints:
```
Relic                   Pick%   Win%(picked)  Win%(skipped)
RELIC.VAJRA             92.3%   55.0%         38.2%
...
```

- [ ] **Step 3: Implement EloCommand**

Default: leaderboard. `--matchup A B`: head-to-head. `--history`: not implemented in CLI (web only). Prints:
```
Elo Leaderboard (overall):
#   Card                    Elo    Games   Pick%
1   CARD.OFFERING           1642   34      89.5%
2   CARD.BATTLE_TRANCE      1598   47      76.3%
...
42  SKIP                    1487   312     —
```

- [ ] **Step 4: Implement RunCommand, ExportCommand**

`RunCommand`: query single run by ID or seed, print floor-by-floor summary.
`ExportCommand`: serialize full DB content to JSON file for web dashboard.

- [ ] **Step 5: Wire all commands into Program.cs**

```csharp
root.AddCommand(ImportCommand.Create());
root.AddCommand(StatsCommand.Create());
root.AddCommand(CardsCommand.Create());
root.AddCommand(RelicsCommand.Create());
root.AddCommand(EloCommand.Create());
root.AddCommand(RunCommand.Create());
root.AddCommand(ExportCommand.Create());
```

- [ ] **Step 6: Test manually**

```bash
dotnet run --project src/Sts2Analytics.Cli -- stats
dotnet run --project src/Sts2Analytics.Cli -- cards --top 10
dotnet run --project src/Sts2Analytics.Cli -- elo --top 10
dotnet run --project src/Sts2Analytics.Cli -- export --output data.json
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add CLI stats, cards, relics, elo, run, and export commands"
```

---

## Task 10: Blazor WASM Dashboard — Setup + Overview Page

**Files:**
- Modify: `src/Sts2Analytics.Web/Program.cs`
- Create: `src/Sts2Analytics.Web/Services/DataService.cs`
- Create: `src/Sts2Analytics.Web/Pages/Overview.razor`
- Create: `src/Sts2Analytics.Web/Components/FilterBar.razor`

- [ ] **Step 1: Add Radzen.Blazor dependency**

```bash
dotnet add src/Sts2Analytics.Web/Sts2Analytics.Web.csproj package Radzen.Blazor
```

- [ ] **Step 1b: Configure Radzen.Blazor**

Add to `_Imports.razor`:
```razor
@using Radzen
@using Radzen.Blazor
```

Add to `wwwroot/index.html` (in `<head>`):
```html
<link rel="stylesheet" href="_content/Radzen.Blazor/css/material-base.css">
```

Add to `wwwroot/index.html` (before `</body>`):
```html
<script src="_content/Radzen.Blazor/Radzen.Blazor.js"></script>
```

Add `builder.Services.AddRadzenComponents();` in `Program.cs`.

- [ ] **Step 2: Create DataService**

Loads the exported JSON file (from `ExportCommand`). Provides access to all analytics via in-memory Core library calls. Acts as the bridge between the exported data and the analytics engine.

Alternative for WASM: use `sql.js` or pre-compute all analytics at export time and serve as JSON. **Recommendation:** Pre-compute at export time — the CLI `export` command generates a JSON file with all analytics results pre-calculated. The Web dashboard just renders this data. This avoids SQLite-in-WASM complexity.

The export JSON structure:
```json
{
  "summary": { ... },
  "cardWinRates": [ ... ],
  "cardPickRates": [ ... ],
  "relicWinRates": [ ... ],
  "eloRatings": [ ... ],
  "runs": [ ... ],
  "pathPatterns": [ ... ],
  ...
}
```

- [ ] **Step 3: Create Overview.razor**

Wire up Radzen chart components:
- Win rate over time line chart (from runs sorted by StartTime)
- Runs by character pie chart
- Recent runs table (last 20)
- Win/loss streak counter

- [ ] **Step 4: Create FilterBar.razor**

Shared component with dropdowns for character, ascension range. Emits `EventCallback<AnalyticsFilter>` on change.

- [ ] **Step 5: Run and verify**

```bash
dotnet run --project src/Sts2Analytics.Web
```
Open browser, verify overview page renders with real data.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add Blazor WASM dashboard with overview page"
```

---

## Task 11: Dashboard — Card Explorer + Elo Leaderboard

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/CardExplorer.razor`
- Create: `src/Sts2Analytics.Web/Pages/CardMatchups.razor`
- Create: `src/Sts2Analytics.Web/Pages/EloLeaderboard.razor`

- [ ] **Step 1: Implement CardExplorer.razor**

Radzen DataGrid with sortable columns: Name, Elo, Pick%, Win%(picked), Win%(skipped), Delta, Impact. FilterBar at top. Click row → navigate to card detail (inline expand or separate route) showing Elo history chart and matchup breakdown.

- [ ] **Step 2: Implement CardMatchups.razor**

Two card selectors. Shows head-to-head record when both offered. Skip Elo baseline displayed as a reference.

- [ ] **Step 3: Implement EloLeaderboard.razor**

Radzen DataGrid ranked by Elo. Sparkline column for Elo history (mini line chart). Tabs for overall / per-character. SKIP entry highlighted with a distinct row style.

- [ ] **Step 4: Run and verify**

```bash
dotnet run --project src/Sts2Analytics.Web
```
Navigate to /cards, /cards/matchups, /elo — verify data renders.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add card explorer, matchups, and Elo leaderboard pages"
```

---

## Task 12: Dashboard — Relic Explorer + Run History

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/RelicExplorer.razor`
- Create: `src/Sts2Analytics.Web/Pages/RunHistory.razor`
- Create: `src/Sts2Analytics.Web/Components/RunTimeline.razor`

- [ ] **Step 1: Implement RelicExplorer.razor**

Same pattern as CardExplorer — Radzen DataGrid with relic stats (Relic, Pick%, Win%(picked), Win%(skipped)). Click for detail view.

- [ ] **Step 2: Implement RunHistory.razor**

Filterable Radzen DataGrid of runs: Character, Ascension, Win/Loss, Seed, Date, Floors Reached. FilterBar at top.

- [ ] **Step 3: Implement RunTimeline.razor**

Click run in RunHistory → expand `RunTimeline` component showing floor-by-floor vertical timeline with:
- Node type indicator (monster/shop/campfire/boss/elite/event/treasure)
- Card picked / skipped at each stop
- HP bar + gold counter
- Encounter name + turns taken

- [ ] **Step 4: Run and verify**

```bash
dotnet run --project src/Sts2Analytics.Web
```
Navigate to /relics, /runs — verify data renders.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add relic explorer and run history pages with timeline"
```

---

## Task 13: Dashboard — Path, Economy, and Combat Pages

**Files:**
- Create: `src/Sts2Analytics.Web/Pages/PathAnalysis.razor`
- Create: `src/Sts2Analytics.Web/Pages/Economy.razor`
- Create: `src/Sts2Analytics.Web/Pages/Combat.razor`

- [ ] **Step 1: Implement PathAnalysis.razor**

Radzen DataGrid showing path signatures with win rate, colored by performance. Radzen Chart scatter plot for elite count vs win rate.

- [ ] **Step 2: Implement Economy.razor**

Radzen line chart: gold over time (overlay multiple runs, x-axis = floor, y-axis = gold). Radzen pie chart: shop spending by category (cards/relics/removals).

- [ ] **Step 3: Implement Combat.razor**

Radzen bar chart: damage by encounter type. Radzen bar chart: death floor histogram. Radzen line chart: HP threshold analysis (win probability by HP range at key floors).

- [ ] **Step 4: Run and verify all pages**

```bash
dotnet run --project src/Sts2Analytics.Web
```
Click through all pages — verify data renders with no errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add path analysis, economy, and combat dashboard pages"
```

---

## Task 14: End-to-End Test + Polish

**Files:**
- Various: fix any issues discovered during integration

- [ ] **Step 1: Full pipeline test**

```bash
# Fresh DB
rm -f ~/.sts2analytics/data.db

# Import
dotnet run --project src/Sts2Analytics.Cli -- import

# Compute Elo (import should trigger this, verify)
dotnet run --project src/Sts2Analytics.Cli -- elo --top 20

# Stats
dotnet run --project src/Sts2Analytics.Cli -- stats

# Export for web
dotnet run --project src/Sts2Analytics.Cli -- export --output src/Sts2Analytics.Web/wwwroot/data.json

# Run web
dotnet run --project src/Sts2Analytics.Web
```

- [ ] **Step 2: Verify all CLI commands produce reasonable output**

Check: card names resolve, Elo ratings are distributed around 1500, win rates match expectations.

- [ ] **Step 3: Verify all dashboard pages render with real data**

Check: charts have data points, tables are populated, filters work, no console errors.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test -v n
```
Expected: All tests PASS.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "chore: end-to-end integration fixes and polish"
```

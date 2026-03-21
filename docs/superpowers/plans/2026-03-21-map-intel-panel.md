# Map Intel Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a legend panel to the map screen showing per-character average damage for each fight pool (weak/normal/elite/boss) and listing possible encounters per pool for the current act.

**Architecture:** Pre-compute per-character per-act damage stats and encounter lists in the CLI export, bake into `overlay_data.json` (version bump to 4). The mod reads this data and renders a static panel on the map screen via a new Harmony patch on the map screen class. Since we haven't discovered the map screen class yet, we'll use a `_Process` polling approach on the existing toggle node to detect when the map is visible.

**Tech Stack:** C#/.NET 9.0, Godot 4 (via MegaCrit STS2 modding API), Harmony, Dapper/SQLite

---

### Task 1: Add MapIntel data model to Core

**Files:**
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`

- [ ] **Step 1: Add the MapIntel records**

```csharp
public record MapIntelPool(string Pool, double AvgDamage, int SampleSize, List<string> Encounters);

public record MapIntelAct(int ActIndex, List<MapIntelPool> Pools);

public record MapIntelCharacter(string Character, List<MapIntelAct> Acts);
```

Append these after the existing `ModOverlayData` record at the bottom of the file.

- [ ] **Step 2: Add MapIntel field to ModOverlayData**

Update the `ModOverlayData` record to include the new field:

```csharp
public record ModOverlayData(
    int Version, string ExportedAt, double SkipElo,
    Dictionary<string, double> SkipEloByAct,
    List<ModCardStats> Cards,
    List<ModAncientStats>? AncientChoices = null,
    List<MapIntelCharacter>? MapIntel = null);
```

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Core/Models/AnalyticsResults.cs
git commit -m "feat: add MapIntel data models for map panel overlay"
```

---

### Task 2: Query and export MapIntel data in CLI

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`

- [ ] **Step 1: Add MapIntel query method to ExportMod**

After the ancient stats export block (around line 311), add the MapIntel query and construction. Insert this before the `var overlayData = new ModOverlayData(...)` line:

```csharp
// Build map intel data per character per act
var mapIntelSql = """
    SELECT
        r.Character,
        f.ActIndex,
        CASE
            WHEN f.EncounterId LIKE '%\_WEAK' ESCAPE '\' THEN 'weak'
            WHEN f.EncounterId LIKE '%\_NORMAL' ESCAPE '\' THEN 'normal'
            WHEN f.EncounterId LIKE '%\_ELITE' ESCAPE '\' THEN 'elite'
            WHEN f.EncounterId LIKE '%\_BOSS' ESCAPE '\' THEN 'boss'
            ELSE NULL
        END AS Pool,
        f.EncounterId,
        f.DamageTaken
    FROM Floors f
    JOIN Runs r ON f.RunId = r.Id
    WHERE f.EncounterId IS NOT NULL
      AND f.MapPointType IN ('monster', 'elite', 'boss')
    """;

var mapIntelRows = conn.Query(mapIntelSql).ToList();

var mapIntel = mapIntelRows
    .Where(r => r.Pool != null)
    .GroupBy(r => (string)r.Character)
    .Select(charGroup => new MapIntelCharacter(
        charGroup.Key,
        charGroup
            .GroupBy(r => (int)(long)r.ActIndex)
            .OrderBy(g => g.Key)
            .Select(actGroup => new MapIntelAct(
                actGroup.Key,
                actGroup
                    .GroupBy(r => (string)r.Pool)
                    .Select(poolGroup => new MapIntelPool(
                        poolGroup.Key,
                        poolGroup.Average(r => (double)(long)r.DamageTaken),
                        poolGroup.Count(),
                        poolGroup.Select(r => (string)r.EncounterId).Distinct().OrderBy(e => e).ToList()))
                    .OrderBy(p => p.Pool switch { "weak" => 0, "normal" => 1, "elite" => 2, "boss" => 3, _ => 4 })
                    .ToList()))
            .ToList()))
    .ToList();
```

- [ ] **Step 2: Pass mapIntel to ModOverlayData constructor**

Update the `var overlayData = new ModOverlayData(...)` call to include the new field:

```csharp
var overlayData = new ModOverlayData(
    Version: 4,
    ExportedAt: DateTime.UtcNow.ToString("o"),
    SkipElo: skipElo,
    SkipEloByAct: skipEloByAct,
    Cards: cards,
    AncientChoices: ancientStats,
    MapIntel: mapIntel);
```

- [ ] **Step 3: Build and test export**

```bash
dotnet build src/Sts2Analytics.Cli -c Release
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output /tmp/test_overlay.json
```

Verify the output contains `mapIntel` array with per-character per-act data:
```bash
cat /tmp/test_overlay.json | python3 -m json.tool | grep -A 5 '"mapIntel"'
```

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Cli/Commands/ExportCommand.cs
git commit -m "feat: export per-character map intel data to overlay JSON"
```

---

### Task 3: Add MapIntel records and loader to Mod

**Files:**
- Modify: `src/Sts2Analytics.Mod/Data/OverlayData.cs`
- Modify: `src/Sts2Analytics.Mod/Data/DataLoader.cs`

- [ ] **Step 1: Add MapIntel records to OverlayData.cs**

Append after the existing `AncientStats` record:

```csharp
public record MapIntelPool(
    [property: JsonPropertyName("pool")] string Pool,
    [property: JsonPropertyName("avgDamage")] double AvgDamage,
    [property: JsonPropertyName("sampleSize")] int SampleSize,
    [property: JsonPropertyName("encounters")] List<string> Encounters);

public record MapIntelAct(
    [property: JsonPropertyName("actIndex")] int ActIndex,
    [property: JsonPropertyName("pools")] List<MapIntelPool> Pools);

public record MapIntelCharacter(
    [property: JsonPropertyName("character")] string Character,
    [property: JsonPropertyName("acts")] List<MapIntelAct> Acts);
```

- [ ] **Step 2: Add mapIntel field to OverlayData record**

```csharp
public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("skipEloByAct")] Dictionary<string, double>? SkipEloByAct,
    [property: JsonPropertyName("cards")] List<CardStats> Cards,
    [property: JsonPropertyName("ancientChoices")] List<AncientStats>? AncientChoices = null,
    [property: JsonPropertyName("mapIntel")] List<MapIntelCharacter>? MapIntel = null);
```

- [ ] **Step 3: Add MapIntel lookup to DataLoader.cs**

Add a new field and accessor. After the `_ancientChoices` field (line 14):

```csharp
private static Dictionary<string, MapIntelCharacter>? _mapIntel;
```

In the `Load` method, after the ancient choices loading block (after line 90):

```csharp
_mapIntel = new Dictionary<string, MapIntelCharacter>(StringComparer.OrdinalIgnoreCase);
if (data.MapIntel != null)
{
    foreach (var mic in data.MapIntel)
    {
        if (!string.IsNullOrEmpty(mic.Character))
            _mapIntel[mic.Character] = mic;
    }
}
GD.Print($"[SpireOracle] Loaded map intel for {_mapIntel.Count} characters");
```

Add a public accessor after `GetAncientChoice`:

```csharp
public static MapIntelCharacter? GetMapIntel(string character) =>
    _mapIntel?.TryGetValue(character, out var intel) == true ? intel : null;

public static List<string> GetMapIntelCharacters() =>
    _mapIntel?.Keys.ToList() ?? new List<string>();
```

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/Data/OverlayData.cs src/Sts2Analytics.Mod/Data/DataLoader.cs
git commit -m "feat: load map intel data in mod DataLoader"
```

---

### Task 4: Create MapIntelPanel UI component

**Files:**
- Create: `src/Sts2Analytics.Mod/UI/MapIntelPanel.cs`

- [ ] **Step 1: Create the panel UI class**

This is a self-contained Godot `PanelContainer` that renders the map intel legend. It exposes `UpdateForContext(character, actIndex)` to refresh its contents.

```csharp
using System.Collections.Generic;
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public partial class MapIntelPanel : PanelContainer
{
    private VBoxContainer _content = null!;
    private string? _currentCharacter;
    private int _currentAct = -1;

    public override void _Ready()
    {
        Name = "SpireOracleMapIntel";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // Dark semi-transparent background with ember border
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        style.BorderColor = new Color(0.83f, 0.33f, 0.16f, 0.6f);
        style.BorderWidthBottom = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 12;
        style.ContentMarginBottom = 12;
        AddThemeStyleboxOverride("panel", style);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        AddChild(_content);

        // Position: top-right of screen
        SetAnchorsPreset(LayoutPreset.TopRight);
        Position = new Vector2(-320, 80);
        CustomMinimumSize = new Vector2(300, 0);
    }

    public void UpdateForContext(string character, int actIndex)
    {
        if (character == _currentCharacter && actIndex == _currentAct)
            return; // already showing this

        _currentCharacter = character;
        _currentAct = actIndex;

        // Clear existing content
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var intel = DataLoader.GetMapIntel(character);
        if (intel == null)
        {
            AddNoDataLabel();
            return;
        }

        // Find the act data
        MapIntelAct? actData = null;
        foreach (var act in intel.Acts)
        {
            if (act.ActIndex == actIndex)
            {
                actData = act;
                break;
            }
        }

        if (actData == null)
        {
            AddNoDataLabel();
            return;
        }

        // Header
        var charName = character.Replace("CHARACTER.", "");
        var header = new Label();
        header.Text = $"Map Intel — {charName}, Act {actIndex + 1}";
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        _content.AddChild(header);

        // Separator
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        _content.AddChild(sep);

        // Pool rows
        foreach (var pool in actData.Pools)
        {
            AddPoolRow(pool);
        }

        Visible = ModEntry.OverlayEnabled;
    }

    private void AddPoolRow(MapIntelPool pool)
    {
        // Pool header with damage
        var row = new HBoxContainer();

        var dot = new Label();
        dot.Text = pool.Pool switch
        {
            "weak" => "\u25cf", // filled circle
            "normal" => "\u25cf",
            "elite" => "\u25cf",
            "boss" => "\u25cf",
            _ => "\u25cb"
        };
        dot.AddThemeFontSizeOverride("font_size", 20);
        dot.AddThemeColorOverride("font_color", GetPoolColor(pool.Pool));
        row.AddChild(dot);

        var spacer = new Label();
        spacer.Text = " ";
        spacer.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(spacer);

        var poolLabel = new Label();
        poolLabel.Text = GetPoolDisplayName(pool.Pool);
        poolLabel.AddThemeFontSizeOverride("font_size", 20);
        poolLabel.AddThemeColorOverride("font_color", Colors.White);
        poolLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(poolLabel);

        var dmgLabel = new Label();
        dmgLabel.Text = $"~{pool.AvgDamage:F0} dmg";
        dmgLabel.AddThemeFontSizeOverride("font_size", 20);
        dmgLabel.AddThemeColorOverride("font_color", GetDamageColor(pool.AvgDamage));
        row.AddChild(dmgLabel);

        var sizeLabel = new Label();
        sizeLabel.Text = $"  (n={pool.SampleSize})";
        sizeLabel.AddThemeFontSizeOverride("font_size", 16);
        sizeLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.5f));
        row.AddChild(sizeLabel);

        _content.AddChild(row);

        // Encounter list (smaller text, indented)
        var encounters = new Label();
        var names = new List<string>();
        foreach (var enc in pool.Encounters)
        {
            // ENCOUNTER.NIBBITS_WEAK -> Nibbits
            var name = enc.Replace("ENCOUNTER.", "");
            var suffixIdx = name.LastIndexOf('_');
            if (suffixIdx > 0)
            {
                var suffix = name.Substring(suffixIdx + 1);
                if (suffix is "WEAK" or "NORMAL" or "ELITE" or "BOSS")
                    name = name.Substring(0, suffixIdx);
            }
            name = name.Replace("_", " ");
            // Title case
            name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLower());
            names.Add(name);
        }
        encounters.Text = "  " + string.Join(", ", names);
        encounters.AddThemeFontSizeOverride("font_size", 16);
        encounters.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        encounters.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(encounters);
    }

    private void AddNoDataLabel()
    {
        var label = new Label();
        label.Text = "No map intel data available";
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
        _content.AddChild(label);
        Visible = ModEntry.OverlayEnabled;
    }

    private static string GetPoolDisplayName(string pool) => pool switch
    {
        "weak" => "Easy Hallway",
        "normal" => "Hard Hallway",
        "elite" => "Elite",
        "boss" => "Boss",
        _ => pool
    };

    private static Color GetPoolColor(string pool) => pool switch
    {
        "weak" => new Color(0.3f, 0.85f, 0.3f),   // green
        "normal" => new Color(0.95f, 0.85f, 0.2f), // yellow
        "elite" => new Color(0.95f, 0.3f, 0.3f),   // red
        "boss" => new Color(0.7f, 0.3f, 0.9f),     // purple
        _ => Colors.White
    };

    private static Color GetDamageColor(double avg) => avg switch
    {
        < 5 => new Color(0.3f, 0.85f, 0.3f),   // green — low
        < 10 => new Color(0.95f, 0.85f, 0.2f),  // yellow — medium
        < 18 => new Color(0.95f, 0.5f, 0.2f),   // orange — high
        _ => new Color(0.95f, 0.3f, 0.3f)        // red — very high
    };
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Sts2Analytics.Mod/UI/MapIntelPanel.cs
git commit -m "feat: add MapIntelPanel UI component for map screen"
```

---

### Task 5: Integrate panel into game via scene tree polling

**Files:**
- Modify: `src/Sts2Analytics.Mod/UI/OverlayToggle.cs`

Since we haven't discovered the exact map screen Godot node class to patch with Harmony, we'll detect the map screen by polling the scene tree in the existing `OverlayToggle._Process` loop. This is the same node that already handles F2 toggling.

- [ ] **Step 1: Read current OverlayToggle.cs**

Read the file to see its current structure.

- [ ] **Step 2: Add map screen detection and panel management**

Add a `MapIntelPanel` field and update `_Process` to detect when a map-like screen is active. The detection strategy: look for a node whose class name or name contains "Map" in the scene tree. When found, show the panel and update context from the current run's character/act.

Add fields:

```csharp
private MapIntelPanel? _mapPanel;
private bool _mapVisible;
```

In `_Ready` (or `_EnterTree`), create the panel:

```csharp
_mapPanel = new MapIntelPanel();
GetTree().Root.CallDeferred("add_child", _mapPanel);
```

In `_Process`, add map detection logic:

```csharp
// Detect map screen visibility
var mapScreen = FindMapScreen();
if (mapScreen != null && mapScreen.Visible)
{
    if (!_mapVisible)
    {
        _mapVisible = true;
        var (character, actIndex) = DetectCurrentContext();
        _mapPanel?.UpdateForContext(character, actIndex);
    }
}
else
{
    if (_mapVisible)
    {
        _mapVisible = false;
        if (_mapPanel != null) _mapPanel.Visible = false;
    }
}
```

Add helper methods:

```csharp
private Control? FindMapScreen()
{
    // Search for the map screen node in the scene tree
    // STS2 likely uses a node with "Map" in its name/type
    var root = GetTree().Root;
    return FindNodeByPattern(root, "Map");
}

private static Control? FindNodeByPattern(Node node, string pattern)
{
    // BFS to find a visible Control with "Map" in name, limiting depth
    var queue = new System.Collections.Generic.Queue<(Node node, int depth)>();
    queue.Enqueue((node, 0));
    while (queue.Count > 0)
    {
        var (current, depth) = queue.Dequeue();
        if (depth > 6) continue; // don't go too deep
        if (current is Control ctrl && current.Name.ToString().Contains(pattern, System.StringComparison.OrdinalIgnoreCase))
            return ctrl;
        foreach (var child in current.GetChildren())
            queue.Enqueue((child, depth + 1));
    }
    return null;
}

private static (string character, int actIndex) DetectCurrentContext()
{
    // Try to read the current run's character and act from the game state
    // Fallback: use the first character with data
    try
    {
        var gameState = Engine.GetMainLoop() as SceneTree;
        // TODO: read actual character/act from game state once we discover the API
        // For now, search for nodes that might contain run info
    }
    catch { }

    // Fallback: return first character with data, act 0
    var characters = DataLoader.GetMapIntelCharacters();
    return (characters.Count > 0 ? characters[0] : "CHARACTER.IRONCLAD", 0);
}
```

**Note:** The `DetectCurrentContext` and `FindMapScreen` methods are best-effort. Once we decompile and discover the actual STS2 map screen class, we should replace the BFS search with a direct Harmony patch (like `CardRewardPatch`). For now, this polling approach works without knowing the exact class names.

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build src/Sts2Analytics.Mod -c Release
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Sts2Analytics.Mod/UI/OverlayToggle.cs
git commit -m "feat: integrate map intel panel via scene tree polling"
```

---

### Task 6: Export updated overlay data and deploy

**Files:**
- Modify: `mods/SpireOracle/overlay_data.json` (via CLI export)

- [ ] **Step 1: Run full export**

```bash
dotnet run --project src/Sts2Analytics.Cli -- import
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output mods/SpireOracle/overlay_data.json
```

- [ ] **Step 2: Verify mapIntel in output**

```bash
python3 -c "
import json
with open('mods/SpireOracle/overlay_data.json') as f:
    data = json.load(f)
print(f'Version: {data[\"version\"]}')
for char in data.get('mapIntel', []):
    print(f'{char[\"character\"]}:')
    for act in char['acts']:
        pools = ', '.join(f'{p[\"pool\"]}={p[\"avgDamage\"]:.1f}' for p in act['pools'])
        print(f'  Act {act[\"actIndex\"]+1}: {pools}')
"
```

- [ ] **Step 3: Deploy using /deploy skill**

Close STS2, then:
```bash
cp src/Sts2Analytics.Mod/bin/Release/net9.0/SpireOracle.dll mods/SpireOracle/
cp mods/SpireOracle/SpireOracle.dll mods/SpireOracle/overlay_data.json mods/SpireOracle/mod_manifest.json "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/SpireOracle/"
```

- [ ] **Step 4: Commit**

```bash
git add mods/SpireOracle/overlay_data.json
git commit -m "feat: export map intel data to overlay JSON"
```

---

### Task 7: In-game testing and map screen discovery

- [ ] **Step 1: Launch STS2 and navigate to map screen**

Open the game, start a run, and check the Godot debug output for `[SpireOracle]` log messages. Look for whether the map screen was detected.

- [ ] **Step 2: If map not detected, use Godot scene tree inspector**

Press F2 to toggle overlay. Check console output. If the map panel doesn't appear, we need to discover the map screen node name. Add temporary debug logging in `FindMapScreen` to print all top-level node names when on the map.

- [ ] **Step 3: Once map screen class is discovered, replace polling with Harmony patch**

Create `src/Sts2Analytics.Mod/Patches/MapScreenPatch.cs` with a proper `[HarmonyPatch]` targeting the discovered class, similar to `CardRewardPatch`. Remove the polling logic from `OverlayToggle`.

This is an iterative discovery step — the exact patch target depends on what we find in the game.

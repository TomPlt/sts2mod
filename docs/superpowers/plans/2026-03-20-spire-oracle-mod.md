# SpireOracle Mod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an STS2 mod that overlays Elo ratings, pick rates, win rates, and pick/skip recommendations on the card reward screen.

**Architecture:** Standalone mod DLL using Harmony to patch `NCardRewardSelectionScreen.RefreshOptions`. Reads pre-computed analytics from a JSON file. UI created as Godot Control nodes injected into the card reward hierarchy.

**Tech Stack:** C#/.NET 9.0, Godot.NET.Sdk, Lib.Harmony 2.4.2, System.Text.Json

**Spec:** `docs/superpowers/specs/2026-03-20-spire-oracle-mod-design.md`

**Game DLL:** `/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll`

**Decompiled references:** `decompiled_NCardRewardSelectionScreen.txt`, `decompiled_CardHolders.txt`, `decompiled_Modding.txt`

---

## Key Game Internals

**Card reward screen:** `MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen`
- `RefreshOptions(IReadOnlyList<CardCreationResult>, IReadOnlyList<CardRewardAlternative>)` — creates `NGridCardHolder` per card, adds to `_cardRow`
- `_cardRow` is `Control` at node path `"UI/CardRow"`

**Card hierarchy:** `NGridCardHolder : NCardHolder : Control` → contains `NCard : Control`
- Card ID: `holder.CardModel.Id.ToString()` → `"CARD.OFFERING"`
- Hover: `FocusEntered`/`FocusExited` signals on the holder

**Mod system:**
- Manifest `"id"` field = DLL filename (without .dll)
- `[ModInitializer("MethodName")]` on a class, calls the static method
- Mod path: `ModManager.LoadedMods` → `Mod.path`

---

## File Map

### Mod Project

| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj` | Project file: Godot.NET.Sdk, Harmony, assembly name SpireOracle |
| `src/Sts2Analytics.Mod/ModEntry.cs` | `[ModInitializer]` entry point, loads data, applies patches |
| `src/Sts2Analytics.Mod/Data/OverlayData.cs` | Data models: CardStats, OverlayData |
| `src/Sts2Analytics.Mod/Data/DataLoader.cs` | Reads and deserializes data.json, builds lookup dictionary |
| `src/Sts2Analytics.Mod/Patches/CardRewardPatch.cs` | Harmony postfix on RefreshOptions to inject overlay nodes |
| `src/Sts2Analytics.Mod/UI/OverlayFactory.cs` | Creates Elo badge, recommendation pill, detail panel as Godot nodes |
| `src/Sts2Analytics.Mod/UI/OverlayToggle.cs` | Node added to scene tree for F2 hotkey |

### CLI Changes

| File | Responsibility |
|------|---------------|
| `src/Sts2Analytics.Cli/Commands/ExportCommand.cs` | Add `--mod` flag for slim overlay data export |

### Deployment

| File | Responsibility |
|------|---------------|
| `mods/SpireOracle/mod_manifest.json` | Mod metadata for game loader |

---

## Task 1: CLI `--mod` Export

**Files:**
- Modify: `src/Sts2Analytics.Cli/Commands/ExportCommand.cs`
- Modify: `src/Sts2Analytics.Core/Models/AnalyticsResults.cs`

- [ ] **Step 1: Add mod overlay data models to AnalyticsResults.cs**

```csharp
// Add to src/Sts2Analytics.Core/Models/AnalyticsResults.cs
public record ModCardStats(
    string CardId, double Elo, double PickRate,
    double WinRatePicked, double WinRateSkipped, double Delta);

public record ModOverlayData(
    int Version, string ExportedAt, double SkipElo,
    List<ModCardStats> Cards);
```

- [ ] **Step 2: Add --mod flag to ExportCommand**

Read the existing ExportCommand to understand the current API pattern, then add a `--mod` option. When `--mod` is specified:

1. Query `EloRatings WHERE CardId = 'SKIP' AND Character = 'ALL' AND Context = 'overall'` → skipElo
2. Query `CardAnalytics.GetCardWinRates()` → win rates by card
3. Query `CardAnalytics.GetCardPickRates()` → pick rates by card
4. Query `EloAnalytics.GetCardEloRatings()` → filter Context = "overall"
5. Join all by CardId into `ModCardStats` records
6. Serialize `ModOverlayData` with version=1 and timestamp

```csharp
// Inside the --mod handler:
var eloAnalytics = new EloAnalytics(conn);
var cardAnalytics = new CardAnalytics(conn);

var skipElo = conn.QueryFirstOrDefault<double?>(
    "SELECT Rating FROM EloRatings WHERE CardId = 'SKIP' AND Character = 'ALL' AND Context = 'overall'") ?? 1500.0;

var winRates = cardAnalytics.GetCardWinRates().ToDictionary(c => c.CardId);
var pickRates = cardAnalytics.GetCardPickRates().ToDictionary(c => c.CardId);
var eloRatings = eloAnalytics.GetCardEloRatings()
    .Where(e => e.Context == "overall")
    .ToDictionary(e => e.CardId);

var cards = eloRatings.Keys.Union(winRates.Keys).Select(cardId =>
{
    var elo = eloRatings.TryGetValue(cardId, out var e) ? e.Rating : 1500.0;
    var pr = pickRates.TryGetValue(cardId, out var p) ? p.PickRate : 0;
    var wr = winRates.TryGetValue(cardId, out var w) ? w : null;
    return new ModCardStats(
        cardId, elo, pr,
        wr?.WinRateWhenPicked ?? 0, wr?.WinRateWhenSkipped ?? 0, wr?.WinRateDelta ?? 0);
}).ToList();

var overlay = new ModOverlayData(1, DateTime.UtcNow.ToString("o"), skipElo, cards);
var json = JsonSerializer.Serialize(overlay, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
File.WriteAllText(outputPath, json);
```

- [ ] **Step 3: Test the --mod export**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output /tmp/spireoracle-data.json
cat /tmp/spireoracle-data.json | head -20
```

Expected: JSON with `version`, `exportedAt`, `skipElo`, and `cards` array.

- [ ] **Step 4: Verify existing tests pass**

```bash
dotnet test tests/Sts2Analytics.Core.Tests -v n
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add --mod export flag for SpireOracle overlay data"
```

---

## Task 2: Mod Project Scaffold

**Files:**
- Create: `src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj`
- Create: `mods/SpireOracle/mod_manifest.json`

- [ ] **Step 1: Create the mod project directory**

```bash
mkdir -p src/Sts2Analytics.Mod
```

- [ ] **Step 2: Create the csproj**

```xml
<!-- src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj -->
<Project Sdk="Godot.NET.Sdk/4.4.0">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>SpireOracle</AssemblyName>
    <RootNamespace>SpireOracle</RootNamespace>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Lib.Harmony" Version="2.4.2" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>..\..\lib\sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

Note: We reference sts2.dll from a local `lib/` directory. Copy it there:

```bash
mkdir -p lib
cp "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll" lib/
```

Also copy the Harmony DLL that ships with the game (so we don't duplicate it):

```bash
cp "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/0Harmony.dll" lib/
```

Update the csproj to reference Harmony from lib too instead of NuGet (the game already loads it):

```xml
<ItemGroup>
  <Reference Include="0Harmony">
    <HintPath>..\..\lib\0Harmony.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

Remove the PackageReference for Lib.Harmony.

- [ ] **Step 3: Add to solution**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet sln add src/Sts2Analytics.Mod/Sts2Analytics.Mod.csproj
```

Note: The Godot.NET.Sdk may require the Godot editor installed, or specific workloads. If the build fails, try adding `<GodotProjectDir>.</GodotProjectDir>` to suppress Godot editor checks, or create a minimal `project.godot` in the mod directory.

- [ ] **Step 4: Create mod_manifest.json**

```json
{
  "id": "SpireOracle",
  "name": "Spire Oracle",
  "author": "sts2mod",
  "description": "Card analytics overlay — Elo ratings, pick rates, and recommendations on reward screens",
  "version": "0.1.0",
  "has_dll": true,
  "has_pck": false,
  "affects_gameplay": false
}
```

Save to `mods/SpireOracle/mod_manifest.json`.

- [ ] **Step 5: Try to build**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Sts2Analytics.Mod
```

This may fail due to Godot SDK requirements. If so, troubleshoot:
- The Godot.NET.Sdk version must match the game's Godot version (4.5.1). Try `Godot.NET.Sdk/4.4.0` first, then 4.3.0 if needed.
- May need to add `<GodotSharpVersion>` property.
- Check what SDK version the game's own `sts2.deps.json` references.

The goal is to get the project to compile. Even if there are warnings, 0 errors is the target.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold SpireOracle mod project with manifest"
```

---

## Task 3: Data Loading

**Files:**
- Create: `src/Sts2Analytics.Mod/Data/OverlayData.cs`
- Create: `src/Sts2Analytics.Mod/Data/DataLoader.cs`
- Create: `src/Sts2Analytics.Mod/ModEntry.cs`

- [ ] **Step 1: Create OverlayData models**

```csharp
// src/Sts2Analytics.Mod/Data/OverlayData.cs
using System.Text.Json.Serialization;

namespace SpireOracle.Data;

public record CardStats(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("elo")] double Elo,
    [property: JsonPropertyName("pickRate")] double PickRate,
    [property: JsonPropertyName("winRatePicked")] double WinRatePicked,
    [property: JsonPropertyName("winRateSkipped")] double WinRateSkipped,
    [property: JsonPropertyName("delta")] double Delta);

public record OverlayData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedAt")] string ExportedAt,
    [property: JsonPropertyName("skipElo")] double SkipElo,
    [property: JsonPropertyName("cards")] List<CardStats> Cards);
```

- [ ] **Step 2: Create DataLoader**

```csharp
// src/Sts2Analytics.Mod/Data/DataLoader.cs
using System.Text.Json;

namespace SpireOracle.Data;

public static class DataLoader
{
    private static Dictionary<string, CardStats>? _cardLookup;
    private static OverlayData? _data;

    public static OverlayData? Data => _data;
    public static double SkipElo => _data?.SkipElo ?? 1500.0;
    public static bool IsLoaded => _data != null;

    public static bool Load(string modPath)
    {
        var jsonPath = Path.Combine(modPath, "data.json");
        if (!File.Exists(jsonPath))
        {
            GD.PrintErr($"[SpireOracle] data.json not found at: {jsonPath}");
            return false;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            _data = JsonSerializer.Deserialize<OverlayData>(json);
            if (_data == null)
            {
                GD.PrintErr("[SpireOracle] Failed to deserialize data.json");
                return false;
            }

            _cardLookup = _data.Cards.ToDictionary(c => c.CardId, c => c);
            GD.Print($"[SpireOracle] Loaded {_cardLookup.Count} cards, Skip Elo: {_data.SkipElo:F0}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Error loading data.json: {ex.Message}");
            return false;
        }
    }

    public static CardStats? GetCard(string cardId)
    {
        if (_cardLookup == null) return null;
        return _cardLookup.TryGetValue(cardId, out var stats) ? stats : null;
    }
}
```

Note: `GD.Print` and `GD.PrintErr` are Godot's logging functions, available via the Godot SDK.

- [ ] **Step 3: Create ModEntry**

```csharp
// src/Sts2Analytics.Mod/ModEntry.cs
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace SpireOracle;

[ModInitializer("Initialize")]
public class ModEntry
{
    private static Harmony? _harmony;
    public static bool OverlayEnabled { get; set; } = true;

    public static void Initialize()
    {
        GD.Print("[SpireOracle] Initializing...");

        // Find our mod path
        string? modPath = null;
        foreach (var mod in ModManager.LoadedMods)
        {
            if (mod.manifest?.id == "SpireOracle")
            {
                modPath = mod.path;
                break;
            }
        }

        if (modPath == null)
        {
            GD.PrintErr("[SpireOracle] Could not find mod path!");
            return;
        }

        // Load data
        if (!DataLoader.Load(modPath))
        {
            GD.PrintErr("[SpireOracle] Data loading failed, overlay disabled.");
            return;
        }

        // Apply Harmony patches
        _harmony = new Harmony("com.sts2mod.spireoracle");
        _harmony.PatchAll(typeof(ModEntry).Assembly);
        GD.Print("[SpireOracle] Harmony patches applied.");

        // Add F2 toggle node to scene tree
        var toggle = new UI.OverlayToggle();
        toggle.Name = "SpireOracleToggle";
        // Add to root viewport's tree so it persists
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred("add_child", toggle);

        GD.Print("[SpireOracle] Ready!");
    }
}
```

- [ ] **Step 4: Build**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Sts2Analytics.Mod
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add mod entry point and data loading"
```

---

## Task 4: Overlay UI Factory

**Files:**
- Create: `src/Sts2Analytics.Mod/UI/OverlayFactory.cs`
- Create: `src/Sts2Analytics.Mod/UI/OverlayToggle.cs`

- [ ] **Step 1: Create OverlayFactory**

This creates Godot UI nodes programmatically for the overlay elements.

```csharp
// src/Sts2Analytics.Mod/UI/OverlayFactory.cs
using Godot;
using SpireOracle.Data;

namespace SpireOracle.UI;

public static class OverlayFactory
{
    private const string GroupName = "spire_oracle_overlay";

    // Elo thresholds
    private const double HighElo = 1650;
    private const double LowElo = 1500;
    private const double PickThreshold = 50;

    public static void AddOverlay(Control cardHolder, CardStats stats, double skipElo)
    {
        // Remove any existing overlay on this holder
        RemoveOverlay(cardHolder);

        // Create Elo badge (top-right)
        var badge = CreateEloBadge(stats.Elo);
        badge.AddToGroup(GroupName);
        cardHolder.AddChild(badge);

        // Create recommendation pill (bottom-center)
        var pill = CreateRecommendationPill(stats.Elo, skipElo);
        pill.AddToGroup(GroupName);
        cardHolder.AddChild(pill);

        // Create detail panel (below card, hidden by default)
        var detail = CreateDetailPanel(stats);
        detail.AddToGroup(GroupName);
        detail.Visible = false;
        detail.Name = "SpireOracleDetail";
        cardHolder.AddChild(detail);
    }

    public static void RemoveOverlay(Control cardHolder)
    {
        foreach (var child in cardHolder.GetChildren())
        {
            if (child is Node node && node.IsInGroup(GroupName))
            {
                node.QueueFree();
            }
        }
    }

    public static void ShowDetail(Control cardHolder)
    {
        var detail = cardHolder.GetNodeOrNull<Control>("SpireOracleDetail");
        if (detail != null) detail.Visible = true;
    }

    public static void HideDetail(Control cardHolder)
    {
        var detail = cardHolder.GetNodeOrNull<Control>("SpireOracleDetail");
        if (detail != null) detail.Visible = false;
    }

    private static PanelContainer CreateEloBadge(double elo)
    {
        var panel = new PanelContainer();
        panel.Name = "SpireOracleEloBadge";

        // Position top-right
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.Position = new Vector2(-10, -10);
        panel.AnchorLeft = 1f;
        panel.AnchorTop = 0f;

        // Style
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = elo >= HighElo
            ? new Color(0.83f, 0.33f, 0.16f, 0.9f)   // ember
            : elo >= LowElo
                ? new Color(0.14f, 0.19f, 0.27f, 0.9f) // grey
                : new Color(0.17f, 0.1f, 0.1f, 0.9f);  // dark red
        stylebox.CornerRadiusBottomLeft = 3;
        stylebox.CornerRadiusBottomRight = 3;
        stylebox.CornerRadiusTopLeft = 3;
        stylebox.CornerRadiusTopRight = 3;
        stylebox.ContentMarginLeft = 6;
        stylebox.ContentMarginRight = 6;
        stylebox.ContentMarginTop = 2;
        stylebox.ContentMarginBottom = 2;
        panel.AddThemeStyleboxOverride("panel", stylebox);

        var label = new Label();
        label.Text = $"{elo:F0}";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color",
            elo >= HighElo ? new Color(1f, 0.61f, 0.42f) // ember glow
            : elo >= LowElo ? new Color(0.6f, 0.57f, 0.51f) // grey
            : new Color(0.83f, 0.25f, 0.25f)); // red
        panel.AddChild(label);

        return panel;
    }

    private static PanelContainer CreateRecommendationPill(double elo, double skipElo)
    {
        var panel = new PanelContainer();
        panel.Name = "SpireOraclePill";

        // Position bottom-center
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        panel.AnchorTop = 1f;
        panel.Position = new Vector2(0, 10);

        string text;
        Color textColor;
        Color bgColor;

        if (elo > skipElo + PickThreshold)
        {
            text = "\u25b2 PICK"; // ▲ PICK
            textColor = new Color(0.31f, 0.68f, 0.42f); // verdant
            bgColor = new Color(0.1f, 0.23f, 0.17f, 0.9f);
        }
        else if (elo < skipElo - PickThreshold)
        {
            text = "\u25bc SKIP"; // ▼ SKIP
            textColor = new Color(0.83f, 0.25f, 0.25f); // crimson
            bgColor = new Color(0.23f, 0.1f, 0.1f, 0.9f);
        }
        else
        {
            text = "\u2014 OK"; // — OK
            textColor = new Color(0.83f, 0.66f, 0.42f); // gold
            bgColor = new Color(0.23f, 0.17f, 0.1f, 0.9f);
        }

        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = bgColor;
        stylebox.CornerRadiusBottomLeft = 2;
        stylebox.CornerRadiusBottomRight = 2;
        stylebox.CornerRadiusTopLeft = 2;
        stylebox.CornerRadiusTopRight = 2;
        stylebox.ContentMarginLeft = 8;
        stylebox.ContentMarginRight = 8;
        stylebox.ContentMarginTop = 2;
        stylebox.ContentMarginBottom = 2;
        panel.AddThemeStyleboxOverride("panel", stylebox);

        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", textColor);
        panel.AddChild(label);

        return panel;
    }

    private static PanelContainer CreateDetailPanel(CardStats stats)
    {
        var panel = new PanelContainer();
        panel.Name = "SpireOracleDetail";

        // Position below card
        panel.Position = new Vector2(-20, 230); // below the card
        panel.CustomMinimumSize = new Vector2(170, 0);

        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.02f, 0.03f, 0.05f, 0.95f);
        stylebox.BorderColor = new Color(0.83f, 0.33f, 0.16f, 0.25f);
        stylebox.BorderWidthBottom = 1;
        stylebox.BorderWidthTop = 1;
        stylebox.BorderWidthLeft = 1;
        stylebox.BorderWidthRight = 1;
        stylebox.CornerRadiusBottomLeft = 4;
        stylebox.CornerRadiusBottomRight = 4;
        stylebox.CornerRadiusTopLeft = 4;
        stylebox.CornerRadiusTopRight = 4;
        stylebox.ContentMarginLeft = 10;
        stylebox.ContentMarginRight = 10;
        stylebox.ContentMarginTop = 8;
        stylebox.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", stylebox);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);

        var dimColor = new Color(0.38f, 0.35f, 0.32f);
        var brightColor = new Color(0.91f, 0.88f, 0.84f);

        AddStatRow(vbox, "Elo", $"{stats.Elo:F0}", new Color(1f, 0.61f, 0.42f), dimColor);
        AddStatRow(vbox, "Pick Rate", $"{stats.PickRate:P1}", brightColor, dimColor);
        AddStatRow(vbox, "Win (Pick)", $"{stats.WinRatePicked:P1}", new Color(0.31f, 0.68f, 0.42f), dimColor);
        AddStatRow(vbox, "Win (Skip)", $"{stats.WinRateSkipped:P1}", new Color(0.83f, 0.25f, 0.25f), dimColor);
        AddStatRow(vbox, "Delta", $"{(stats.Delta >= 0 ? "+" : "")}{stats.Delta:P1}",
            stats.Delta >= 0 ? new Color(0.31f, 0.68f, 0.42f) : new Color(0.83f, 0.25f, 0.25f), dimColor);

        panel.AddChild(vbox);
        return panel;
    }

    private static void AddStatRow(VBoxContainer parent, string labelText, string valueText, Color valueColor, Color labelColor)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);

        var lbl = new Label();
        lbl.Text = labelText;
        lbl.AddThemeFontSizeOverride("font_size", 11);
        lbl.AddThemeColorOverride("font_color", labelColor);
        lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(lbl);

        var val = new Label();
        val.Text = valueText;
        val.AddThemeFontSizeOverride("font_size", 11);
        val.AddThemeColorOverride("font_color", valueColor);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        hbox.AddChild(val);

        parent.AddChild(hbox);
    }

    public static void SetAllOverlaysVisible(bool visible)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        foreach (var node in tree.GetNodesInGroup(GroupName))
        {
            if (node is Control control)
                control.Visible = visible;
        }
    }
}
```

- [ ] **Step 2: Create OverlayToggle**

```csharp
// src/Sts2Analytics.Mod/UI/OverlayToggle.cs
using Godot;

namespace SpireOracle.UI;

public partial class OverlayToggle : Node
{
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.F2)
        {
            ModEntry.OverlayEnabled = !ModEntry.OverlayEnabled;
            OverlayFactory.SetAllOverlaysVisible(ModEntry.OverlayEnabled);
            GD.Print($"[SpireOracle] Overlay {(ModEntry.OverlayEnabled ? "enabled" : "disabled")}");
            GetViewport().SetInputAsHandled();
        }
    }
}
```

- [ ] **Step 3: Build**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Sts2Analytics.Mod
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add overlay UI factory and F2 toggle"
```

---

## Task 5: Harmony Patch

**Files:**
- Create: `src/Sts2Analytics.Mod/Patches/CardRewardPatch.cs`

- [ ] **Step 1: Create the Harmony patch**

```csharp
// src/Sts2Analytics.Mod/Patches/CardRewardPatch.cs
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NCardRewardSelectionScreen), "RefreshOptions")]
public static class CardRewardPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        try
        {
            // _cardRow is private, access via node path
            var cardRow = __instance.GetNodeOrNull<Control>("UI/CardRow");
            if (cardRow == null) return;

            foreach (var child in cardRow.GetChildren())
            {
                if (child is NGridCardHolder holder)
                {
                    var cardId = holder.CardModel?.Id.ToString();
                    if (cardId == null) continue;

                    var stats = DataLoader.GetCard(cardId);
                    if (stats == null) continue;

                    OverlayFactory.AddOverlay(holder, stats, DataLoader.SkipElo);

                    // Connect hover signals for detail panel
                    holder.Connect(Control.SignalName.FocusEntered,
                        Callable.From(() => OverlayFactory.ShowDetail(holder)));
                    holder.Connect(Control.SignalName.FocusExited,
                        Callable.From(() => OverlayFactory.HideDetail(holder)));
                }
            }

            // Add skip line reference at bottom
            AddSkipLineReference(__instance);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Error in CardRewardPatch: {ex.Message}");
        }
    }

    private static void AddSkipLineReference(NCardRewardSelectionScreen screen)
    {
        var ui = screen.GetNodeOrNull<Control>("UI");
        if (ui == null) return;

        // Remove existing skip line if any
        var existing = ui.GetNodeOrNull("SpireOracleSkipLine");
        existing?.QueueFree();

        var container = new HBoxContainer();
        container.Name = "SpireOracleSkipLine";
        container.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        container.AnchorTop = 1f;
        container.Position = new Vector2(0, -40);
        container.Alignment = BoxContainer.AlignmentMode.Center;

        var label = new Label();
        label.Text = $"SKIP LINE: {DataLoader.SkipElo:F0}";
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f, 0.6f));
        container.AddChild(label);

        ui.AddChild(container);
    }
}
```

- [ ] **Step 2: Build**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Sts2Analytics.Mod
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add Harmony patch for card reward overlay injection"
```

---

## Task 6: Deploy and Test

**Files:**
- Various deployment steps

- [ ] **Step 1: Export mod data**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project src/Sts2Analytics.Cli -- export --mod --output mods/SpireOracle/data.json
```

- [ ] **Step 2: Build mod DLL**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Sts2Analytics.Mod -c Release
```

- [ ] **Step 3: Copy DLL to mods folder**

```bash
cp src/Sts2Analytics.Mod/bin/Release/net9.0/SpireOracle.dll mods/SpireOracle/
```

- [ ] **Step 4: Deploy to game**

```bash
# Copy entire mod folder to game's mods directory
cp -r mods/SpireOracle "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods/"
```

- [ ] **Step 5: Verify mod loads**

Launch STS2. Check the mod settings to see if SpireOracle appears. Check the game's log output for `[SpireOracle] Ready!`.

Start a run, get to a card reward. Verify:
- Elo badges appear on each card
- Recommendation pills show (PICK/OK/SKIP)
- Hovering a card shows the detail panel
- Skip line reference at bottom of screen
- F2 toggles the overlay on/off

- [ ] **Step 6: Fix any positioning issues**

The overlay positions (badge top-right, pill bottom-center, detail panel below) may need adjustment based on the actual card dimensions in-game. Update constants in `OverlayFactory.cs` as needed.

- [ ] **Step 7: Final commit**

```bash
git add -A
git commit -m "feat: SpireOracle mod v0.1.0 — card analytics overlay"
```

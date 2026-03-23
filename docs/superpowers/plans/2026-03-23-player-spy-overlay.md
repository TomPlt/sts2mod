# Player Spy Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an F5/F6 overlay that shows other players' hand, draw pile, and discard pile during multiplayer combat.

**Architecture:** New `PlayerSpyPanel` static class (matching `CombatOverlay` pattern) renders a top-right panel. `InputPatch` handles F5 toggle and F6 cycle. A static `_inCombat` flag gates the keybinds. A 0.5s Godot `Timer` node drives refresh. Card zones are read from other players via reflection on the combat state.

**Tech Stack:** C# / .NET 9.0, Godot 4, Harmony 2, Traverse (reflection)

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/Sts2Analytics.Mod/UI/PlayerSpyPanel.cs` | Panel UI: Show/Hide/Refresh, timer, card grouping |
| Modify | `src/Sts2Analytics.Mod/Patches/InputPatch.cs` | F5/F6 keybinds, `_inCombat` flag, spy index tracking |
| Modify | `src/Sts2Analytics.Mod/Patches/CombatPatch.cs` | Set `_inCombat = true`, hide spy on reset/exit |

---

### Task 1: Data Access Spike — Discover Card Zone API

Before writing any UI, we must confirm that hand/draw/discard are accessible for non-local players during combat.

**Files:**
- Read: `src/Sts2Analytics.Mod/Patches/CombatPatch.cs`
- Temporary logging only (no permanent changes)

- [ ] **Step 1: Add diagnostic logging in CombatPatch.Postfix**

After the existing `CombatOverlay.Show()` call, add temporary code to dump the combat state's type hierarchy and player card zones:

```csharp
// --- TEMP SPIKE: discover card zone API ---
try
{
    var cm = CombatManager.Instance;
    var cState = cm?.DebugOnlyGetState();
    if (cState != null)
    {
        // Log combat state type and all properties
        var cType = cState.GetType();
        GD.Print($"[SpireOracle][SPIKE] CombatState type: {cType.FullName}");
        foreach (var prop in cType.GetProperties())
            GD.Print($"[SpireOracle][SPIKE]   prop: {prop.Name} ({prop.PropertyType.Name})");
        foreach (var field in cType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            GD.Print($"[SpireOracle][SPIKE]   field: {field.Name} ({field.FieldType.Name})");

        // Check each player for card zone properties
        var rs = cState.RunState as MegaCrit.Sts2.Core.Runs.RunState;
        if (rs?.Players != null)
        {
            foreach (var p in rs.Players)
            {
                var pType = p.GetType();
                GD.Print($"[SpireOracle][SPIKE] Player type: {pType.FullName}, NetId={p.NetId}");
                foreach (var prop in pType.GetProperties())
                    GD.Print($"[SpireOracle][SPIKE]   prop: {prop.Name} ({prop.PropertyType.Name})");

                // Check Deck sub-properties
                if (p.Deck != null)
                {
                    var dType = p.Deck.GetType();
                    GD.Print($"[SpireOracle][SPIKE] Deck type: {dType.FullName}");
                    foreach (var prop in dType.GetProperties())
                        GD.Print($"[SpireOracle][SPIKE]   deck prop: {prop.Name} ({prop.PropertyType.Name})");
                    foreach (var field in dType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                        GD.Print($"[SpireOracle][SPIKE]   deck field: {field.Name} ({field.FieldType.Name})");
                }
            }
        }
    }
}
catch (Exception ex) { GD.PrintErr($"[SpireOracle][SPIKE] {ex}"); }
// --- END SPIKE ---
```

- [ ] **Step 2: Build and run the mod, enter a multiplayer combat**

```bash
cd src/Sts2Analytics.Mod && dotnet build
```

Deploy the mod, start a multiplayer game, enter combat. Check the Godot console output for `[SpireOracle][SPIKE]` lines.

- [ ] **Step 3: Record findings**

Document which properties/fields expose hand, draw pile, and discard pile. Update this plan's Task 2 with the exact reflection paths. Possible outcomes:
- **Best case**: `Player.Hand`, `Player.DrawPile`, `Player.DiscardPile` (or similar) exist as `List<Card>` or `IReadOnlyList<Card>`
- **Likely case**: Card zones are on a per-player combat entity accessible via `CombatState` — need `Traverse` to reach them
- **Worst case**: Card zones only exist for the local player — feature not feasible as designed, escalate to user

- [ ] **Step 4: Remove spike logging, commit**

Remove all `[SPIKE]` code from `CombatPatch.cs`.

```bash
git add src/Sts2Analytics.Mod/Patches/CombatPatch.cs
git commit -m "chore: complete card zone API spike (findings recorded in plan)"
```

---

### Task 2: Add `_inCombat` Flag to CombatPatch

**Files:**
- Modify: `src/Sts2Analytics.Mod/Patches/CombatPatch.cs`

- [ ] **Step 1: Add static flag to CombatPatch**

At the top of the `CombatPatch` class, add:

```csharp
internal static bool InCombat { get; private set; }
```

- [ ] **Step 2: Set flag in CombatPatch.Postfix**

At the start of `CombatPatch.Postfix()`, before the existing code, add:

```csharp
InCombat = true;
```

- [ ] **Step 3: Clear flag in CombatResetPatch.Postfix**

In `CombatResetPatch.Postfix()`, add before `CombatOverlay.Hide()`:

```csharp
CombatPatch.InCombat = false;
```

- [ ] **Step 4: Clear flag in CombatRoomExitPatch.Postfix**

In `CombatRoomExitPatch.Postfix()`, add before `CombatOverlay.Hide()`:

```csharp
CombatPatch.InCombat = false;
```

- [ ] **Step 5: Build to verify**

```bash
cd src/Sts2Analytics.Mod && dotnet build
```

- [ ] **Step 6: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/CombatPatch.cs
git commit -m "feat: add InCombat flag to CombatPatch for combat-scoped features"
```

---

### Task 3: Create PlayerSpyPanel Static Class

**Files:**
- Create: `src/Sts2Analytics.Mod/UI/PlayerSpyPanel.cs`

The exact card zone access paths will be filled in after Task 1's spike. The `ReadCardZone` method below is a **template that MUST be rewritten** with actual reflection paths discovered in the spike. Do NOT commit Task 3 until Task 1 findings are incorporated.

- [ ] **Step 1: Create PlayerSpyPanel.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SpireOracle.UI;

public static class PlayerSpyPanel
{
    private const string PanelName = "SpireOraclePlayerSpy";
    private static PanelContainer? _panel;
    private static Player? _player;
    private static Label? _headerLabel;
    private static Label? _handLabel;
    private static Label? _drawLabel;
    private static Label? _discardLabel;
    private static Timer? _timer;
    private static int _playerIndex;       // index into non-local player list
    private static int _nonLocalCount;     // total non-local players

    public static bool IsVisible => _panel != null && GodotObject.IsInstanceValid(_panel);

    public static void Show(Player player, int playerIndex, int nonLocalCount)
    {
        Hide();

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;

        _player = player;
        _playerIndex = playerIndex;
        _nonLocalCount = nonLocalCount;

        _panel = new PanelContainer();
        _panel.Name = PanelName;

        // Style — dark background, blue-ish border
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.88f);
        style.BorderColor = new Color(0.3f, 0.5f, 0.9f, 0.8f);
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
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        _panel.AddThemeStyleboxOverride("panel", style);

        // Scroll container for overflow — cap height to prevent extending off-screen
        var viewport = tree.Root.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        var scroll = new ScrollContainer();
        scroll.CustomMinimumSize = new Vector2(280, 0);
        scroll.CustomMaximumSize = new Vector2(0, viewport.Y - 200);
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);

        // Header
        _headerLabel = CreateLabel("", 18, new Color(0.83f, 0.33f, 0.16f));
        vbox.AddChild(_headerLabel);
        vbox.AddChild(new HSeparator());

        // Hand section
        vbox.AddChild(CreateLabel("Hand", 14, new Color(0.6f, 0.6f, 0.7f)));
        _handLabel = CreateLabel("(empty)", 12, Colors.White);
        vbox.AddChild(_handLabel);
        vbox.AddChild(new HSeparator());

        // Draw Pile section
        vbox.AddChild(CreateLabel("Draw Pile", 14, new Color(0.6f, 0.6f, 0.7f)));
        _drawLabel = CreateLabel("(empty)", 12, Colors.White);
        vbox.AddChild(_drawLabel);
        vbox.AddChild(new HSeparator());

        // Discard Pile section
        vbox.AddChild(CreateLabel("Discard Pile", 14, new Color(0.6f, 0.6f, 0.7f)));
        _discardLabel = CreateLabel("(empty)", 12, Colors.White);
        vbox.AddChild(_discardLabel);

        scroll.AddChild(vbox);
        _panel.AddChild(scroll);

        // Position: top-right using anchor
        _panel.AnchorRight = 1.0f;
        _panel.AnchorLeft = 1.0f;
        _panel.OffsetLeft = -320;
        _panel.OffsetRight = -20;
        _panel.OffsetTop = 100;
        _panel.ZIndex = 100;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Timer as child of panel (auto-cleanup on QueueFree)
        _timer = new Timer();
        _timer.WaitTime = 0.5;
        _timer.Autostart = true;
        _timer.Timeout += Refresh;
        _panel.AddChild(_timer);

        tree.Root.AddChild(_panel);

        // Initial refresh
        Refresh();
    }

    public static void Hide()
    {
        if (_timer != null)
        {
            _timer.Timeout -= Refresh;
            _timer = null;
        }
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
        _player = null;
        _headerLabel = null;
        _handLabel = null;
        _drawLabel = null;
        _discardLabel = null;
    }

    private static void Refresh()
    {
        if (_player == null || _headerLabel == null) return;

        try
        {
            // Header: "Ironclad (1/2)"
            var charId = _player.Character?.ToString() ?? "Unknown";
            var spaceIdx = charId.IndexOf(' ');
            if (spaceIdx > 0) charId = charId.Substring(0, spaceIdx);
            var charName = FormatCharacterName(charId);
            _headerLabel.Text = $"{charName} ({_playerIndex + 1}/{_nonLocalCount})";

            // Read card zones via reflection
            // TODO: Replace these paths with actual findings from Task 1 spike
            var hand = ReadCardZone(_player, "Hand");
            var draw = ReadCardZone(_player, "DrawPile");
            var discard = ReadCardZone(_player, "DiscardPile");

            _handLabel!.Text = FormatCardList(hand);
            _drawLabel!.Text = FormatCardList(draw);
            _discardLabel!.Text = FormatCardList(discard);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] PlayerSpyPanel.Refresh error: {ex.Message}");
        }
    }

    /// <summary>
    /// Read a card zone from a player via reflection.
    /// The exact path depends on Task 1 spike findings.
    /// </summary>
    private static List<string> ReadCardZone(Player player, string zoneName)
    {
        // TODO: Update after spike. Placeholder uses Traverse on Deck.
        try
        {
            var cards = Traverse.Create(player.Deck).Property(zoneName).GetValue<System.Collections.IEnumerable>();
            if (cards == null) return new List<string>();

            var result = new List<string>();
            foreach (var card in cards)
            {
                var cardId = Traverse.Create(card).Property("Id").GetValue<object>()?.ToString() ?? "";
                var cidSpace = cardId.IndexOf(' ');
                if (cidSpace > 0) cardId = cardId.Substring(0, cidSpace);
                var upgrade = Traverse.Create(card).Property("CurrentUpgradeLevel").GetValue<int>();
                if (upgrade > 0) cardId = $"{cardId}+{upgrade}";
                result.Add(cardId);
            }
            return result;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string FormatCardList(List<string> cards)
    {
        if (cards.Count == 0) return "(empty)";

        return string.Join("\n", cards
            .GroupBy(c => c)
            .OrderBy(g => g.Key)
            .Select(g => g.Count() > 1 ? $"{FormatCardName(g.Key)} x{g.Count()}" : FormatCardName(g.Key)));
    }

    private static string FormatCardName(string cardId)
    {
        // "CARD.DEFEND+1" -> "Defend+1"
        var name = cardId;
        if (name.StartsWith("CARD.")) name = name.Substring(5);

        // Separate upgrade suffix
        var plusIdx = name.IndexOf('+');
        string suffix = "";
        if (plusIdx > 0)
        {
            suffix = name.Substring(plusIdx);
            name = name.Substring(0, plusIdx);
        }

        // Title case
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));

        return name + suffix;
    }

    private static string FormatCharacterName(string charId)
    {
        // "CHARACTER.IRONCLAD" -> "Ironclad"
        var name = charId;
        if (name.StartsWith("CHARACTER.")) name = name.Substring(10);
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
    }

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd src/Sts2Analytics.Mod && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/Sts2Analytics.Mod/UI/PlayerSpyPanel.cs
git commit -m "feat: add PlayerSpyPanel static class for multiplayer hand viewing"
```

---

### Task 4: Add F5/F6 Keybinds to InputPatch

**Files:**
- Modify: `src/Sts2Analytics.Mod/Patches/InputPatch.cs`

- [ ] **Step 1: Add spy state fields**

At the top of `InputPatch` class, add:

```csharp
private static int _spyIndex = 0; // index into non-local players

internal static void ResetSpyIndex() => _spyIndex = 0;
```

- [ ] **Step 2: Add F5 handler before the F3 check**

After the F4 block and before the `if (keyEvent.Keycode != Key.F3)` line, add:

```csharp
if (keyEvent.Keycode == Key.F5)
{
    if (!CombatPatch.InCombat) return;

    // Toggle spy panel
    if (PlayerSpyPanel.IsVisible)
    {
        PlayerSpyPanel.Hide();
        GD.Print("[SpireOracle] Player spy hidden");
    }
    else
    {
        var nonLocal = GetNonLocalPlayers();
        if (nonLocal.Count == 0) return; // singleplayer, no-op

        _spyIndex = Math.Clamp(_spyIndex, 0, nonLocal.Count - 1);
        PlayerSpyPanel.Show(nonLocal[_spyIndex], _spyIndex, nonLocal.Count);
        GD.Print($"[SpireOracle] Player spy: showing player {_spyIndex + 1}/{nonLocal.Count}");
    }
    return;
}

if (keyEvent.Keycode == Key.F6)
{
    if (!CombatPatch.InCombat) return;
    if (!PlayerSpyPanel.IsVisible) return; // no-op when hidden

    var nonLocal = GetNonLocalPlayers();
    if (nonLocal.Count == 0) return;

    _spyIndex = (_spyIndex + 1) % nonLocal.Count;
    PlayerSpyPanel.Show(nonLocal[_spyIndex], _spyIndex, nonLocal.Count);
    GD.Print($"[SpireOracle] Player spy: cycled to player {_spyIndex + 1}/{nonLocal.Count}");
    return;
}
```

- [ ] **Step 3: Add GetNonLocalPlayers helper**

Add to `InputPatch` class:

```csharp
private static List<MegaCrit.Sts2.Core.Entities.Players.Player> GetNonLocalPlayers()
{
    try
    {
        var runManager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
        var state = runManager != null
            ? Traverse.Create(runManager).Property("State").GetValue<MegaCrit.Sts2.Core.Runs.RunState>()
            : null;
        if (state?.Players == null || state.Players.Count <= 1)
            return new List<MegaCrit.Sts2.Core.Entities.Players.Player>();

        var local = GetLocalPlayer(runManager, state);
        return state.Players.Where(p => p != local).ToList();
    }
    catch
    {
        return new List<MegaCrit.Sts2.Core.Entities.Players.Player>();
    }
}
```

- [ ] **Step 4: Add missing using**

Add at the top of `InputPatch.cs`:

```csharp
using System.Linq;
```

- [ ] **Step 5: Build to verify**

```bash
cd src/Sts2Analytics.Mod && dotnet build
```

- [ ] **Step 6: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/InputPatch.cs
git commit -m "feat: add F5/F6 keybinds for player spy panel"
```

---

### Task 5: Wire Up Combat Exit — Hide Spy Panel

**Files:**
- Modify: `src/Sts2Analytics.Mod/Patches/CombatPatch.cs`

- [ ] **Step 1: Add PlayerSpyPanel.Hide() to CombatResetPatch**

In `CombatResetPatch.Postfix()`, after `CombatPatch.InCombat = false;` (added in Task 2):

```csharp
PlayerSpyPanel.Hide();
InputPatch.ResetSpyIndex();
```

- [ ] **Step 2: Add PlayerSpyPanel.Hide() to CombatRoomExitPatch**

In `CombatRoomExitPatch.Postfix()`, after `CombatPatch.InCombat = false;`:

```csharp
PlayerSpyPanel.Hide();
InputPatch.ResetSpyIndex();
```

- [ ] **Step 3: Add using for UI namespace if not present**

Ensure `CombatPatch.cs` has:

```csharp
using SpireOracle.UI;
```

(Already present — verify.)

- [ ] **Step 4: Build to verify**

```bash
cd src/Sts2Analytics.Mod && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add src/Sts2Analytics.Mod/Patches/CombatPatch.cs
git commit -m "feat: hide player spy panel on combat end"
```

---

### Task 6: Integration Test — Manual Multiplayer Verification

- [ ] **Step 1: Build and deploy mod**

```bash
cd src/Sts2Analytics.Mod && dotnet build
```

Then deploy using the deploy skill/script.

- [ ] **Step 2: Start a multiplayer game, enter combat**

- [ ] **Step 3: Press F5 — verify panel appears top-right showing other player's card zones**

- [ ] **Step 4: Verify card names are grouped and sorted with upgrades (e.g., "Defend+1 x3")**

- [ ] **Step 5: Wait 1-2 seconds — verify hand updates as other player draws/plays cards**

- [ ] **Step 6: Press F6 — verify it cycles to next non-local player (if 3+ players)**

- [ ] **Step 7: Press F5 again — verify panel hides**

- [ ] **Step 8: Let combat end — verify panel auto-hides**

- [ ] **Step 9: Press F5 outside combat — verify it's a no-op**

- [ ] **Step 10: Test in singleplayer — verify F5 is a no-op**

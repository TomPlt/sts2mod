using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._EnterTree))]
public static class DeckViewPatch
{
    private static bool _showCardElos;
    private static NDeckViewScreen? _currentScreen;

    public static bool ShowCardElos => _showCardElos;
    public static NDeckViewScreen? CurrentScreen => _currentScreen;

    [HarmonyPostfix]
    public static void Postfix(NDeckViewScreen __instance)
    {
        _currentScreen = __instance;
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        try
        {
            ShowDeckSummary();
            // Delay badge application — cards aren't populated yet in _EnterTree
            if (_showCardElos)
            {
                var screen = __instance;
                var tree = __instance.GetTree();
                if (tree != null)
                {
                    tree.CreateTimer(0.1).Timeout += () =>
                    {
                        if (GodotObject.IsInstanceValid(screen))
                            AddCardEloBadges(screen);
                    };
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] DeckViewPatch error: {ex.Message}");
        }
    }

    public static void ToggleCardElos()
    {
        _showCardElos = !_showCardElos;
        if (_currentScreen != null && GodotObject.IsInstanceValid(_currentScreen))
        {
            if (_showCardElos)
                AddCardEloBadges(_currentScreen);
            else
                RemoveCardEloBadges(_currentScreen);
        }
        GD.Print($"[SpireOracle] Deck card Elos: {(_showCardElos ? "ON" : "OFF")}");
    }

    internal static void ShowDeckSummary()
    {
        try
        {
            var runManager = RunManager.Instance;
            var state = runManager != null
                ? Traverse.Create(runManager).Property("State").GetValue<RunState>()
                : null;
            if (state == null) return;

            var player = InputPatch.GetLocalPlayer(runManager, state);
            if (player == null) return;
            var deck = player.Deck;
            if (deck == null) return;

            var cardIds = new List<string>();
            foreach (var card in deck.Cards)
            {
                var cardId = card.Id.ToString() ?? "";
                var sp = cardId.IndexOf(' ');
                if (sp > 0) cardId = cardId.Substring(0, sp);
                if (card.CurrentUpgradeLevel > 0)
                    cardId = $"{cardId}+{card.CurrentUpgradeLevel}";
                cardIds.Add(cardId);
            }

            var actIndex = state.CurrentActIndex;
            var actNum = actIndex + 1;

            // Compute deck Elo per pool type for current act
            var poolTypes = new[] { ("weak", "Easy Hallway"), ("normal", "Hard Hallway"), ("elite", "Elite"), ("boss", "Boss") };
            var lines = new List<string>();
            lines.Add($"Deck Strength \u2014 Act {actNum}  ({cardIds.Count} cards)");
            lines.Add("F4 to toggle card ratings");
            lines.Add("");

            foreach (var (poolKey, poolLabel) in poolTypes)
            {
                var poolCtx = $"act{actNum}_{poolKey}";
                var (poolDeckElo, poolDeckRd) = DeckEloHelper.Compute(cardIds, poolCtx);

                // Get encounter pool Elo
                var poolRating = DataLoader.GetPoolRating(poolCtx);
                var oppElo = poolRating?.Elo ?? 1500;
                var oppRd = poolRating?.Rd ?? 350;

                // Compute expected score and damage
                var score = DeckEloHelper.GlickoExpectedScore(poolDeckElo, oppElo, oppRd);
                var dmgResult = DataLoader.GetExpectedDamage(poolCtx, score);

                var dmgStr = dmgResult.HasValue
                    ? $"~{dmgResult.Value.Expected:F0} HP ({dmgResult.Value.Low:F0}-{dmgResult.Value.High:F0})"
                    : "?";

                lines.Add($"{poolLabel}: Elo {poolDeckElo:F0} vs {oppElo:F0}  {dmgStr}");
            }

            lines.Add("");

            // Best/worst cards for current act overall
            var overallCtx = $"act{actNum}_normal"; // use normal as representative
            var seen = new HashSet<string>();
            string? strongest = null, weakest = null;
            double maxElo = double.MinValue, minElo = double.MaxValue;
            foreach (var cid in cardIds)
            {
                if (!seen.Add(cid)) continue;
                var cs = DataLoader.GetCard(cid);
                if (cs == null || cs.CombatElo <= 0 || cs.CombatRd > 200) continue;
                if (cs.CombatElo > maxElo) { maxElo = cs.CombatElo; strongest = cid; }
                if (cs.CombatElo < minElo) { minElo = cs.CombatElo; weakest = cid; }
            }

            if (strongest != null)
                lines.Add($"Best: {FormatCardName(strongest)} [{maxElo:F0}]");
            if (weakest != null)
                lines.Add($"Worst: {FormatCardName(weakest)} [{minElo:F0}]");

            CombatOverlay.Show(lines, 1500, DeckEloHelper.Compute(cardIds).Elo);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] DeckView summary error: {ex.Message}");
        }
    }

    private static string? DetectCharacterKey()
    {
        try
        {
            var runManager = RunManager.Instance;
            var state = runManager != null
                ? Traverse.Create(runManager).Property("State").GetValue<RunState>()
                : null;
            if (state?.Players != null && state.Players.Count > 0)
            {
                var charId = state.Players[0].Character?.ToString() ?? "";
                var sp = charId.IndexOf(' ');
                if (sp > 0) charId = charId.Substring(0, sp);
                return charId.Replace("CHARACTER.", "").ToLower();
            }
        }
        catch { }
        return null;
    }

    private static void AddCardEloBadges(Node root)
    {
        var charKey = DetectCharacterKey();
        var holders = new List<NGridCardHolder>();
        FindCardHolders(root, holders);

        foreach (var holder in holders)
        {
            var cardModel = holder.CardModel;
            if (cardModel == null) continue;

            var cardId = cardModel.Id.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (cardModel.CurrentUpgradeLevel > 0)
                cardId = $"{cardId}+{cardModel.CurrentUpgradeLevel}";

            var stats = DataLoader.GetCard(cardId);
            if (stats == null) continue;

            AddCombatEloBadge(holder, stats, charKey);
        }
    }

    private static int DetectActIndex()
    {
        try
        {
            var runManager = RunManager.Instance;
            var state = runManager != null
                ? Traverse.Create(runManager).Property("State").GetValue<RunState>()
                : null;
            return state?.CurrentActIndex ?? 0;
        }
        catch { return 0; }
    }

    private static void AddCombatEloBadge(NGridCardHolder holder, CardStats stats, string? charKey = null)
    {
        // Remove existing badge if any
        var existing = holder.GetNodeOrNull("SpireOracleDeckBadge");
        if (existing != null) { holder.RemoveChild(existing); existing.QueueFree(); }

        if (stats.CombatElo <= 0) return;

        var badge = new PanelContainer();
        badge.Name = "SpireOracleDeckBadge";

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        style.BorderColor = new Color(0.4f, 0.4f, 0.5f, 0.5f);
        style.BorderWidthBottom = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.ContentMarginLeft = 4;
        style.ContentMarginRight = 4;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        badge.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 0);

        // Overall combat Elo with label
        var overallLabel = new Label();
        overallLabel.Text = $"CMB {stats.CombatElo:F0}";
        overallLabel.AddThemeFontSizeOverride("font_size", 16);
        overallLabel.AddThemeColorOverride("font_color", GetEloColor(stats.CombatElo));
        overallLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(overallLabel);

        // Pick Elo if available
        if (stats.Elo > 0)
        {
            var pickLabel = new Label();
            pickLabel.Text = $"PICK {stats.Elo:F0}";
            pickLabel.AddThemeFontSizeOverride("font_size", 13);
            pickLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
            pickLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(pickLabel);
        }

        // Per-pool breakdown — only show current act
        var bp = stats.CombatByPool;
        if (bp != null)
        {
            var actIdx = DetectActIndex();
            var act = actIdx + 1;
            var poolTypes = new[] { ("weak", "W"), ("normal", "N"), ("elite", "E"), ("boss", "B") };

            var line = "";
            var hasData = false;
            foreach (var (pool, abbr) in poolTypes)
            {
                var key = $"act{act}_{pool}";
                if (bp.TryGetValue(key, out var pr) && pr.Elo > 0 && pr.Rd < 300)
                {
                    line += $"{abbr}{pr.Elo:F0} ";
                    hasData = true;
                }
            }
            if (hasData)
            {
                var actLabel = new Label();
                actLabel.Text = line.TrimEnd();
                actLabel.AddThemeFontSizeOverride("font_size", 12);
                actLabel.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
                vbox.AddChild(actLabel);
            }
        }

        badge.AddChild(vbox);

        badge.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        badge.Position = new Vector2(5, -50);
        badge.ZIndex = 5;
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        holder.AddChild(badge);
    }

    private static void RemoveCardEloBadges(Node root)
    {
        var holders = new List<NGridCardHolder>();
        FindCardHolders(root, holders);
        foreach (var holder in holders)
        {
            var badge = holder.GetNodeOrNull("SpireOracleDeckBadge");
            if (badge != null) { holder.RemoveChild(badge); badge.QueueFree(); }
        }
    }

    private static void FindCardHolders(Node node, List<NGridCardHolder> result)
    {
        if (node is NGridCardHolder holder)
            result.Add(holder);
        foreach (var child in node.GetChildren())
            FindCardHolders(child, result);
    }

    private static string FormatCardName(string cardId)
    {
        var name = cardId;
        if (name.StartsWith("CARD.")) name = name.Substring(5);
        var plus = name.IndexOf('+');
        var suffix = "";
        if (plus > 0) { suffix = name.Substring(plus); name = name.Substring(0, plus); }
        var parts = name.Split('_');
        name = string.Join(" ", System.Linq.Enumerable.Select(parts,
            w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + suffix;
    }

    private static Color GetEloColor(double elo)
    {
        if (elo >= 1600) return new Color(0.3f, 0.85f, 0.3f);   // green — strong
        if (elo >= 1500) return new Color(0.6f, 0.8f, 0.3f);    // yellow-green
        if (elo >= 1400) return new Color(0.95f, 0.85f, 0.2f);  // yellow
        if (elo >= 1300) return new Color(0.95f, 0.5f, 0.2f);   // orange
        return new Color(0.95f, 0.3f, 0.3f);                     // red — weak
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._ExitTree))]
public static class DeckViewExitPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        CombatOverlay.Hide();
    }
}

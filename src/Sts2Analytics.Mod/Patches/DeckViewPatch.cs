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
            // Only show summary + badges if F4 toggled on
            if (_showCardElos)
            {
                ShowDeckSummary();
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
            DebugLogOverlay.LogErr($"[SpireOracle] DeckViewPatch error: {ex.Message}");
        }
    }

    public static void ToggleCardElos()
    {
        _showCardElos = !_showCardElos;
        if (_currentScreen != null && GodotObject.IsInstanceValid(_currentScreen))
        {
            if (_showCardElos)
            {
                ShowDeckSummary();
                AddCardEloBadges(_currentScreen);
            }
            else
            {
                CombatOverlay.Hide();
                RemoveCardEloBadges(_currentScreen);
            }
        }
        DebugLogOverlay.Log($"[SpireOracle] Deck analytics: {(_showCardElos ? "ON" : "OFF")}");
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

            var lines = new List<string>();
            lines.Add($"Deck Analytics \u2014 Act {actNum}  ({cardIds.Count} cards)");
            lines.Add("");

            // --- Rating explanations ---
            lines.Add("Power (PWR) \u2014 Glicko-2 where picks only count as wins if the run wins.");
            lines.Add("  Picked+Won \u2192 card beats skipped (1.0). Picked+Lost \u2192 skipped beats card (0.0).");
            lines.Add("  High Power = picking this card correlates with winning runs.");
            lines.Add("");
            lines.Add("Popularity (POP) \u2014 Glicko-2 based on pick/skip preference alone.");
            lines.Add("  Every pick = win vs skipped cards. Ignores run outcome.");
            lines.Add("  High Popularity + Low Power = trap card (popular but loses).");
            lines.Add("");
            lines.Add("Combat (CMB) \u2014 Glicko-2 based on damage taken in fights.");
            lines.Add("  Compares deck performance vs encounter pools by percentile.");
            lines.Add("  High Combat = decks with this card take less damage.");
            lines.Add("");
            lines.Add("All ratings: 1500 = average. \u00b1RD = confidence (lower = more certain).");
            lines.Add("");

            // --- Deck averages ---
            double sumPower = 0, sumPop = 0, sumCombat = 0;
            int cntPower = 0, cntPop = 0, cntCombat = 0;
            string? bestPower = null, worstPower = null;
            string? bestPop = null, worstPop = null;
            string? bestCombat = null, worstCombat = null;
            double maxPower = double.MinValue, minPower = double.MaxValue;
            double maxPop = double.MinValue, minPop = double.MaxValue;
            double maxCombat = double.MinValue, minCombat = double.MaxValue;
            var seen = new HashSet<string>();

            foreach (var cid in cardIds)
            {
                if (!seen.Add(cid)) continue;
                var cs = DataLoader.GetCard(cid);
                if (cs == null) continue;

                if (cs.OutcomeElo > 0 && cs.OutcomeRd < 250)
                {
                    sumPower += cs.OutcomeElo; cntPower++;
                    if (cs.OutcomeElo > maxPower) { maxPower = cs.OutcomeElo; bestPower = cid; }
                    if (cs.OutcomeElo < minPower) { minPower = cs.OutcomeElo; worstPower = cid; }
                }
                if (cs.Elo > 0 && cs.Rd < 250)
                {
                    sumPop += cs.Elo; cntPop++;
                    if (cs.Elo > maxPop) { maxPop = cs.Elo; bestPop = cid; }
                    if (cs.Elo < minPop) { minPop = cs.Elo; worstPop = cid; }
                }
                if (cs.CombatElo > 0 && cs.CombatRd < 250)
                {
                    sumCombat += cs.CombatElo; cntCombat++;
                    if (cs.CombatElo > maxCombat) { maxCombat = cs.CombatElo; bestCombat = cid; }
                    if (cs.CombatElo < minCombat) { minCombat = cs.CombatElo; worstCombat = cid; }
                }
            }

            var avgPower = cntPower > 0 ? sumPower / cntPower : 0;
            var avgPop = cntPop > 0 ? sumPop / cntPop : 0;
            var avgCombat = cntCombat > 0 ? sumCombat / cntCombat : 0;

            if (cntPower > 0) lines.Add($"Avg Power: {avgPower:F0}  |  Avg Popularity: {avgPop:F0}  |  Avg Combat: {avgCombat:F0}");
            else if (cntPop > 0) lines.Add($"Avg Popularity: {avgPop:F0}  |  Avg Combat: {avgCombat:F0}");
            lines.Add("");

            // --- Best/worst per category ---
            if (bestPower != null)
                lines.Add($"Best Power: {FormatCardName(bestPower)} [{maxPower:F0}]    Worst: {FormatCardName(worstPower!)} [{minPower:F0}]");
            if (bestPop != null)
                lines.Add($"Best Popularity: {FormatCardName(bestPop)} [{maxPop:F0}]    Worst: {FormatCardName(worstPop!)} [{minPop:F0}]");
            if (bestCombat != null)
                lines.Add($"Best Combat: {FormatCardName(bestCombat)} [{maxCombat:F0}]    Worst: {FormatCardName(worstCombat!)} [{minCombat:F0}]");
            lines.Add("");

            // --- Combat forecast per pool ---
            var poolTypes = new[] { ("weak", "Easy Hallway"), ("normal", "Hard Hallway"), ("elite", "Elite"), ("boss", "Boss") };

            foreach (var (poolKey, poolLabel) in poolTypes)
            {
                var poolCtx = $"act{actNum}_{poolKey}";
                var (poolDeckElo, poolDeckRd) = DeckEloHelper.Compute(cardIds, poolCtx);

                var poolRating = DataLoader.GetPoolRating(poolCtx);
                var oppElo = poolRating?.Elo ?? 1500;
                var oppRd = poolRating?.Rd ?? 350;

                var score = DeckEloHelper.GlickoExpectedScore(poolDeckElo, oppElo, oppRd);
                var dmgResult = DataLoader.GetExpectedDamage(poolCtx, score);

                var dmgStr = dmgResult.HasValue
                    ? $"~{dmgResult.Value.Expected:F0} HP ({dmgResult.Value.Low:F0}-{dmgResult.Value.High:F0})"
                    : "?";

                lines.Add($"{poolLabel}: {poolDeckElo:F0} vs {oppElo:F0}  {dmgStr}");
            }

            CombatOverlay.Show(lines, 1500, DeckEloHelper.Compute(cardIds).Elo);
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DeckView summary error: {ex.Message}");
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

        // Need at least one rating to show
        if (stats.OutcomeElo <= 0 && stats.Elo <= 0 && stats.CombatElo <= 0) return;

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

        // Power (outcome elo) — main rating
        if (stats.OutcomeElo > 0)
        {
            var powerLabel = new Label();
            powerLabel.Text = $"PWR {stats.OutcomeElo:F0}";
            powerLabel.AddThemeFontSizeOverride("font_size", 16);
            powerLabel.AddThemeColorOverride("font_color", GetEloColor(stats.OutcomeElo));
            powerLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(powerLabel);
        }

        // Popularity (pick elo)
        if (stats.Elo > 0)
        {
            var popLabel = new Label();
            popLabel.Text = $"POP {stats.Elo:F0}";
            popLabel.AddThemeFontSizeOverride("font_size", 13);
            popLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
            popLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(popLabel);
        }

        // Combat
        if (stats.CombatElo > 0)
        {
            var combatLabel = new Label();
            combatLabel.Text = $"CMB {stats.CombatElo:F0}";
            combatLabel.AddThemeFontSizeOverride("font_size", 13);
            combatLabel.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
            combatLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(combatLabel);
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

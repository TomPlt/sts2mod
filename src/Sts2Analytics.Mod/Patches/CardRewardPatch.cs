using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
public static class CardRewardPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance,
        IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> extraOptions)
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;
        SpireOracle.UI.CombatOverlay.Hide();

        try
        {
            var cardRow = __instance.GetNodeOrNull<Control>("UI/CardRow");
            if (cardRow == null) return;

            // Read live deck for combat forecast
            var (currentDeck, character, actIndex) = ReadLiveDeck();

            int overlaysAdded = 0;
            foreach (var child in cardRow.GetChildren())
            {
                if (child is NGridCardHolder holder)
                {
                    var cardId = holder.CardModel?.Id.ToString();
                    if (cardId == null) continue;

                    // Strip Godot object ID suffix
                    var spaceIdx = cardId.IndexOf(' ');
                    if (spaceIdx > 0) cardId = cardId.Substring(0, spaceIdx);

                    // Add upgrade level
                    var upgradeLevel = holder.CardModel?.CurrentUpgradeLevel ?? 0;
                    var fullCardId = upgradeLevel > 0 ? $"{cardId}+{upgradeLevel}" : cardId;

                    var stats = DataLoader.GetCard(fullCardId) ?? DataLoader.GetCard(cardId);
                    if (stats == null) continue;

                    DebugLogOverlay.Log($"[SpireOracle] Card: {fullCardId} -> elo={stats.Elo:F0} combatElo={stats.CombatElo:F0}");
                    OverlayFactory.AddOverlay(holder, stats, DataLoader.SkipElo);
                    overlaysAdded++;

                    // Live capture: card offered
                    if (LiveRunDb.IsInitialized)
                    {
                        LiveRunDb.Enqueue(new DbAction(
                            DbActionKind.RewardDecision,
                            Id1: fullCardId,
                            Id2: null,
                            Amount: 0,
                            ActIndex: actIndex,
                            FloorIndex: 0,
                            Detail: "CARD_OFFER"
                        ));
                    }

                    // Add combat forecast if we have deck data
                    if (currentDeck != null && character != null && actIndex >= 0)
                    {
                        try
                        {
                            var forecast = CombatSimulator.ForecastCardPick(
                                currentDeck, fullCardId, character, actIndex);
                            if (forecast != null)
                                OverlayFactory.AddForecast(holder, forecast);
                        }
                        catch (Exception fex)
                        {
                            DebugLogOverlay.LogErr($"[SpireOracle] Forecast error for {fullCardId}: {fex.Message}");
                        }
                    }

                    // Connect hover signals for detail panel
                    // The hitbox child handles mouse hover, not the holder itself
                    var hitbox = holder.Hitbox;
                    if (hitbox != null)
                    {
                        hitbox.Connect(Control.SignalName.MouseEntered,
                            Callable.From(() => OverlayFactory.ShowDetail(holder)));
                        hitbox.Connect(Control.SignalName.MouseExited,
                            Callable.From(() => OverlayFactory.HideDetail(holder)));
                    }
                    // Also connect focus signals for controller support
                    holder.Connect(Control.SignalName.FocusEntered,
                        Callable.From(() => OverlayFactory.ShowDetail(holder)));
                    holder.Connect(Control.SignalName.FocusExited,
                        Callable.From(() => OverlayFactory.HideDetail(holder)));
                }
            }

            // Don't show skip line for screens with no analytics (e.g. boss malus choices)
            if (overlaysAdded == 0) return;

            // Add skip line reference
            var ui = __instance.GetNodeOrNull<Control>("UI");
            if (ui != null)
            {
                var existing = ui.GetNodeOrNull("SpireOracleSkipLine");
                existing?.QueueFree();

                var container = new HBoxContainer();
                container.Name = "SpireOracleSkipLine";
                container.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
                container.AnchorTop = 1f;
                container.Position = new Vector2(0, -80);
                container.Alignment = BoxContainer.AlignmentMode.Center;

                // Skip overall rating (use character-specific if available)
                var charContext = character != null ? $"CHARACTER.{character}" : null;
                var skipElo = charContext != null
                    ? DataLoader.GetSkipElo(characterContext: charContext)
                    : DataLoader.SkipElo;
                // Skip power (outcome) baseline
                var skipOutcome = charContext != null
                    ? DataLoader.GetSkipOutcomeElo(characterContext: charContext)
                    : DataLoader.SkipOutcomeElo;
                var skipOverall = new Label();
                skipOverall.Text = $"Skip — Power: {skipOutcome:F0}  Pop: {skipElo:F0}";
                skipOverall.AddThemeFontSizeOverride("font_size", 28);
                skipOverall.AddThemeColorOverride("font_color", Colors.White);
                container.AddChild(skipOverall);

                // Per-act skip ratings (character-specific) — show outcome elo
                var act1Skip = DataLoader.GetSkipOutcomeElo(characterContext: charContext, actIndex: 0);
                var act2Skip = DataLoader.GetSkipOutcomeElo(characterContext: charContext, actIndex: 1);
                var act3Skip = DataLoader.GetSkipOutcomeElo(characterContext: charContext, actIndex: 2);

                var skipActs = new Label();
                skipActs.Text = $"  A1: {act1Skip:F0}  A2: {act2Skip:F0}  A3: {act3Skip:F0}";
                skipActs.AddThemeFontSizeOverride("font_size", 28);
                skipActs.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                container.AddChild(skipActs);

                // Separator
                var sep = new Label();
                sep.Text = "  ";
                sep.AddThemeFontSizeOverride("font_size", 28);
                container.AddChild(sep);

                // Dashboard button
                var dashBtn = new Button();
                dashBtn.Text = "Dashboard";
                dashBtn.AddThemeFontSizeOverride("font_size", 22);
                dashBtn.AddThemeColorOverride("font_color", new Color(0.36f, 0.72f, 0.83f));
                var btnStyle = new StyleBoxFlat();
                btnStyle.BgColor = new Color(0.1f, 0.15f, 0.2f, 0.8f);
                btnStyle.BorderColor = new Color(0.36f, 0.72f, 0.83f, 0.3f);
                btnStyle.BorderWidthBottom = 1;
                btnStyle.BorderWidthTop = 1;
                btnStyle.BorderWidthLeft = 1;
                btnStyle.BorderWidthRight = 1;
                btnStyle.CornerRadiusBottomLeft = 4;
                btnStyle.CornerRadiusBottomRight = 4;
                btnStyle.CornerRadiusTopLeft = 4;
                btnStyle.CornerRadiusTopRight = 4;
                btnStyle.ContentMarginLeft = 12;
                btnStyle.ContentMarginRight = 12;
                btnStyle.ContentMarginTop = 4;
                btnStyle.ContentMarginBottom = 4;
                dashBtn.AddThemeStyleboxOverride("normal", btnStyle);
                var btnHover = (StyleBoxFlat)btnStyle.Duplicate();
                btnHover.BgColor = new Color(0.15f, 0.22f, 0.3f, 0.9f);
                dashBtn.AddThemeStyleboxOverride("hover", btnHover);
                dashBtn.Pressed += () =>
                {
                    OS.ShellOpen("https://tomplt.github.io/sts2mod/");
                };
                container.AddChild(dashBtn);

                ui.AddChild(container);
            }

            // Run stats panel — top 3 cards played, damage, block
            AddRunStatsPanel(__instance);
        }
        catch (System.Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Error in CardRewardPatch: {ex.Message}");
        }
    }

    /// <summary>
    /// Read the current deck from RunManager, returns (cardIds, character, actIndex).
    /// </summary>
    private static (List<string>? deck, string? character, int actIndex) ReadLiveDeck()
    {
        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return (null, null, -1);

            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            if (state == null) return (null, null, -1);

            var actIndex = state.CurrentActIndex;

            var players = state.Players;
            if (players == null || players.Count == 0) return (null, null, actIndex);
            var player = players[0];

            var characterId = player.Character?.ToString() ?? "";
            var charSpace = characterId.IndexOf(' ');
            if (charSpace > 0) characterId = characterId.Substring(0, charSpace);

            var deck = player.Deck;
            if (deck == null) return (null, characterId, actIndex);

            var cardIds = new List<string>();
            foreach (var card in deck.Cards)
            {
                var cardId = card.Id.ToString() ?? "";
                var cidSpace = cardId.IndexOf(' ');
                if (cidSpace > 0) cardId = cardId.Substring(0, cidSpace);
                if (card.CurrentUpgradeLevel > 0)
                    cardId = $"{cardId}+{card.CurrentUpgradeLevel}";
                cardIds.Add(cardId);
            }

            DebugLogOverlay.Log($"[SpireOracle] Live deck: {cardIds.Count} cards, {characterId} Act {actIndex + 1}");
            return (cardIds, characterId, actIndex);
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] ReadLiveDeck error: {ex.Message}");
            return (null, null, -1);
        }
    }

    private static void AddRunStatsPanel(NCardRewardSelectionScreen screen)
    {
        if (!LiveRunDb.IsInitialized || LiveRunDb.CurrentRunId <= 0) return;

        var ui = screen.GetNodeOrNull<Control>("UI");
        if (ui == null) return;

        var existing = ui.GetNodeOrNull("SpireOracleRunStats");
        existing?.QueueFree();

        var runId = LiveRunDb.CurrentRunId;

        // Get top 8 most played cards
        var topPlayed = LiveRunDb.QueryTopStats(
            @"SELECT a.SourceId, COUNT(*) as cnt
              FROM CombatActions a JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
              GROUP BY a.SourceId ORDER BY cnt DESC LIMIT 8", runId);

        if (topPlayed.Count == 0) return;

        // Get damage per card (keyed by card ID)
        var dmgByCard = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, SUM(a2.Amount) as total
              FROM CombatActions a1
              JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='DAMAGE_DEALT'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.TargetId NOT LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId", runId)
            .ToDictionary(x => x.label, x => x.value);

        var blkByCard = LiveRunDb.QueryTopStats(
            @"SELECT a1.SourceId, SUM(a2.Amount) as total
              FROM CombatActions a1
              JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                AND a2.ActionType='BLOCK_GAINED'
                AND a2.SourceId LIKE 'CHARACTER.%'
                AND a2.Seq < COALESCE(
                  (SELECT MIN(a3.Seq) FROM CombatActions a3
                   WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
              JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
              WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
              GROUP BY a1.SourceId", runId)
            .ToDictionary(x => x.label, x => x.value);

        // Find max values for bar scaling
        var maxPlayed = topPlayed.Max(x => x.value);
        var maxDmg = dmgByCard.Count > 0 ? dmgByCard.Values.Max() : 1;
        var maxBlk = blkByCard.Count > 0 ? blkByCard.Values.Max() : 1;
        var maxStat = Math.Max(maxDmg, maxBlk);
        if (maxStat == 0) maxStat = 1;

        const float BAR_WIDTH = 180f;

        // Background panel
        var panel = new PanelContainer();
        panel.Name = "SpireOracleRunStats";
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        panel.Position = new Vector2(10, 0);
        panel.GrowVertical = Control.GrowDirection.Both;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.02f, 0.02f, 0.05f, 0.85f);
        bgStyle.BorderColor = new Color(0.3f, 0.3f, 0.4f, 0.4f);
        bgStyle.BorderWidthBottom = 1;
        bgStyle.BorderWidthTop = 1;
        bgStyle.BorderWidthLeft = 1;
        bgStyle.BorderWidthRight = 1;
        bgStyle.CornerRadiusBottomLeft = 6;
        bgStyle.CornerRadiusBottomRight = 6;
        bgStyle.CornerRadiusTopLeft = 6;
        bgStyle.CornerRadiusTopRight = 6;
        bgStyle.ContentMarginLeft = 10;
        bgStyle.ContentMarginRight = 10;
        bgStyle.ContentMarginTop = 8;
        bgStyle.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", bgStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);

        // Title
        var title = new Label();
        title.Text = "Run Stats";
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(0.83f, 0.33f, 0.16f));
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        // Legend
        var legend = new HBoxContainer();
        legend.AddThemeConstantOverride("separation", 8);
        AddLegendItem(legend, "Plays", new Color(0.5f, 0.5f, 0.6f));
        AddLegendItem(legend, "Dmg", new Color(0.85f, 0.25f, 0.2f));
        AddLegendItem(legend, "Blk", new Color(0.2f, 0.5f, 0.85f));
        vbox.AddChild(legend);

        // Bar chart rows
        foreach (var (cardId, playCount) in topPlayed)
        {
            var dmg = dmgByCard.GetValueOrDefault(cardId, 0);
            var blk = blkByCard.GetValueOrDefault(cardId, 0);

            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 1);

            // Card name + counts
            var nameLabel = new Label();
            nameLabel.Text = $"{FormatCardName(cardId)}  {playCount}x";
            if (dmg > 0) nameLabel.Text += $"  {dmg} dmg";
            if (blk > 0) nameLabel.Text += $"  {blk} blk";
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
            row.AddChild(nameLabel);

            // Bar
            var barRow = new HBoxContainer();
            barRow.AddThemeConstantOverride("separation", 1);
            barRow.CustomMinimumSize = new Vector2(BAR_WIDTH, 8);

            // Play count bar (gray)
            var playBar = new ColorRect();
            playBar.Color = new Color(0.5f, 0.5f, 0.6f, 0.7f);
            playBar.CustomMinimumSize = new Vector2(BAR_WIDTH * playCount / maxPlayed, 8);
            barRow.AddChild(playBar);
            row.AddChild(barRow);

            // Damage + Block bar
            if (dmg > 0 || blk > 0)
            {
                var statBar = new HBoxContainer();
                statBar.AddThemeConstantOverride("separation", 1);
                statBar.CustomMinimumSize = new Vector2(BAR_WIDTH, 6);

                if (dmg > 0)
                {
                    var dmgBar = new ColorRect();
                    dmgBar.Color = new Color(0.85f, 0.25f, 0.2f, 0.8f);
                    dmgBar.CustomMinimumSize = new Vector2(BAR_WIDTH * dmg / maxStat, 6);
                    statBar.AddChild(dmgBar);
                }
                if (blk > 0)
                {
                    var blkBar = new ColorRect();
                    blkBar.Color = new Color(0.2f, 0.5f, 0.85f, 0.8f);
                    blkBar.CustomMinimumSize = new Vector2(BAR_WIDTH * blk / maxStat, 6);
                    statBar.AddChild(blkBar);
                }
                row.AddChild(statBar);
            }

            vbox.AddChild(row);
        }

        panel.AddChild(vbox);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        ui.AddChild(panel);
    }

    private static void AddLegendItem(HBoxContainer parent, string text, Color color)
    {
        var swatch = new ColorRect();
        swatch.Color = color;
        swatch.CustomMinimumSize = new Vector2(10, 10);
        parent.AddChild(swatch);
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f));
        parent.AddChild(label);
    }

    private static string FormatCardName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var name = id;
        if (name.StartsWith("CARD.")) name = name.Substring(5);
        var upgrade = "";
        var plusIdx = name.IndexOf('+');
        if (plusIdx > 0) { upgrade = name.Substring(plusIdx); name = name.Substring(0, plusIdx); }
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + upgrade;
    }
}

using System;
using System.Collections.Generic;
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
                var skipOverall = new Label();
                skipOverall.Text = $"Skip: {skipElo:F0}";
                skipOverall.AddThemeFontSizeOverride("font_size", 28);
                skipOverall.AddThemeColorOverride("font_color", Colors.White);
                container.AddChild(skipOverall);

                // Per-act skip ratings (character-specific)
                var act1Skip = DataLoader.GetSkipElo(characterContext: charContext, actIndex: 0);
                var act2Skip = DataLoader.GetSkipElo(characterContext: charContext, actIndex: 1);
                var act3Skip = DataLoader.GetSkipElo(characterContext: charContext, actIndex: 2);

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
}

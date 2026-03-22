using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.AfterCombatRoomLoaded))]
public static class CombatPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;

            var state = cm.DebugOnlyGetState();
            if (state == null) return;

            // Get encounter ID
            var encounter = state.Encounter;
            if (encounter == null) return;
            var encounterId = encounter.ToString() ?? "";
            var spaceIdx = encounterId.IndexOf(' ');
            if (spaceIdx > 0) encounterId = encounterId.Substring(0, spaceIdx);

            // Get act index from run state
            var actIndex = state.RunState?.CurrentActIndex ?? 0;

            // Get deck cards from first player
            var players = state.Players;
            if (players == null || players.Count == 0) return;
            var player = players[0];
            var deck = player.Deck;
            if (deck == null) return;

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

            // Look up encounter rating
            var encRating = DataLoader.GetEncounterRating(encounterId);

            // Derive pool context and look up pool rating
            string? poolContext = null;
            if (encounterId.EndsWith("_WEAK")) poolContext = $"act{actIndex + 1}_weak";
            else if (encounterId.EndsWith("_NORMAL")) poolContext = $"act{actIndex + 1}_normal";
            else if (encounterId.EndsWith("_ELITE")) poolContext = $"act{actIndex + 1}_elite";
            else if (encounterId.EndsWith("_BOSS")) poolContext = $"act{actIndex + 1}_boss";
            var poolRating = poolContext != null ? DataLoader.GetPoolRating(poolContext) : null;

            // Compute deck Elo from card combat ratings
            var cardRatings = new List<(double Rating, double Rd)>();
            foreach (var cid in cardIds)
            {
                var cs = DataLoader.GetCard(cid);
                if (cs != null && cs.CombatElo > 0)
                    cardRatings.Add((cs.CombatElo, cs.CombatRd));
            }

            double deckElo = 1500, deckRd = 350;
            if (cardRatings.Count > 0)
            {
                double sumWM = 0, sumW = 0, sumP = 0;
                foreach (var (r, rd) in cardRatings)
                {
                    if (rd <= 0) continue;
                    var w = 1.0 / rd;
                    sumWM += r * w;
                    sumW += w;
                    sumP += 1.0 / (rd * rd);
                }
                if (sumW > 0)
                {
                    deckElo = sumWM / sumW;
                    deckRd = 1.0 / Math.Sqrt(sumP);
                }
            }

            // Look up average damage for this encounter/pool
            var encName = FormatName(encounterId);
            var poolName = poolContext?.Replace("act", "Act ").Replace("_", " ") ?? "Unknown";

            // Build display text
            var lines = new List<string>();
            lines.Add($"vs {encName}");
            if (encRating != null)
                lines.Add($"Encounter Elo: {encRating.Elo:F0} ±{encRating.Rd:F0}");
            if (poolRating != null)
                lines.Add($"Pool Elo: {poolRating.Elo:F0} ±{poolRating.Rd:F0}");
            lines.Add($"Deck Elo: {deckElo:F0} ±{deckRd:F0}");

            // Show the overlay
            CombatOverlay.Show(lines, encRating?.Elo ?? poolRating?.Elo ?? 1500, deckElo);

            GD.Print($"[SpireOracle] Combat: {encounterId} (enc={encRating?.Elo:F0}, pool={poolRating?.Elo:F0}, deck={deckElo:F0})");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] CombatPatch error: {ex.Message}");
        }
    }

    private static string FormatName(string id)
    {
        // "ENCOUNTER.KNIGHTS_ELITE" -> "Knights"
        var name = id;
        if (name.StartsWith("ENCOUNTER.")) name = name.Substring(10);
        // Remove pool suffix
        foreach (var suffix in new[] { "_WEAK", "_NORMAL", "_ELITE", "_BOSS" })
            if (name.EndsWith(suffix)) { name = name.Substring(0, name.Length - suffix.Length); break; }
        // Title case
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
    }
}

/// <summary>
/// Hides the combat overlay when combat ends.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public static class CombatEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        CombatOverlay.Hide();
    }
}

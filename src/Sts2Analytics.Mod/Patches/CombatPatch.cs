using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

            // Get deck cards from local player
            var runManager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
            var runState = state.RunState as MegaCrit.Sts2.Core.Runs.RunState;
            var player = InputPatch.GetLocalPlayer(runManager, runState);
            if (player == null) return;
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

            // Compute deck Elo using act-specific card combat ratings for this pool
            var (deckElo, deckRd) = DeckEloHelper.Compute(cardIds, poolContext);

            // Compute expected damage from Elo matchup + net damage distribution
            var encName = FormatName(encounterId);
            var oppElo = encRating?.Elo ?? poolRating?.Elo ?? 1500;
            var oppRd = encRating?.Rd ?? poolRating?.Rd ?? 350;
            var score = DeckEloHelper.GlickoExpectedScore(deckElo, oppElo, oppRd);

            // Look up expected net damage from distribution at predicted percentile
            // Try encounter-specific distribution first, fall back to pool
            var dmgResult = DataLoader.GetExpectedDamage(encounterId, score)
                         ?? DataLoader.GetExpectedDamage(poolContext ?? "", score);

            // Build display text
            var lines = new List<string>();
            lines.Add($"vs {encName}");
            if (dmgResult.HasValue)
            {
                var (exp, lo, hi) = dmgResult.Value;
                lines.Add($"Expected: ~{exp:F0} HP ({lo:F0}-{hi:F0})");
            }
            lines.Add($"Encounter: {oppElo:F0}  Deck: {deckElo:F0}");

            CombatOverlay.Show(lines, oppElo, deckElo);

            // --- TEMP SPIKE: discover card zone API ---
            try
            {
                var cState = cm.DebugOnlyGetState();
                if (cState != null)
                {
                    // Log combat state type and all properties
                    var cType = cState.GetType();
                    GD.Print($"[SpireOracle][SPIKE] CombatState type: {cType.FullName}");
                    foreach (var prop in cType.GetProperties())
                        GD.Print($"[SpireOracle][SPIKE]   prop: {prop.Name} ({prop.PropertyType.Name})");
                    foreach (var field in cType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
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
                                foreach (var field in dType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                    GD.Print($"[SpireOracle][SPIKE]   deck field: {field.Name} ({field.FieldType.Name})");
                            }
                        }
                    }
                }
            }
            catch (Exception spikeEx) { GD.PrintErr($"[SpireOracle][SPIKE] {spikeEx}"); }
            // --- END SPIKE ---

            var expLog = dmgResult?.Expected;
            GD.Print($"[SpireOracle] Combat: {encounterId} score={score:F2} exp={expLog:F0} (enc={encRating?.Elo:F0}, pool={poolRating?.Elo:F0}, deck={deckElo:F0})");
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
/// Hides the combat overlay when combat ends (multiple hooks for reliability).
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public static class CombatResetPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        CombatOverlay.Hide();
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._ExitTree))]
public static class CombatRoomExitPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        CombatOverlay.Hide();
    }
}

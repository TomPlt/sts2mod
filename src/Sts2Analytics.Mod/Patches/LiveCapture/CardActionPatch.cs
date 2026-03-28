using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures CARD_PLAYED via Hook.AfterCardPlayed.
/// CardPlay has Card and Target; extracts IDs and enqueues CombatAction.
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterCardPlayed")]
public static class CardPlayedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var card = cardPlay?.Card;
            if (card == null) return;

            var cardId = card.Id.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (card.CurrentUpgradeLevel > 0)
                cardId = $"{cardId}+{card.CurrentUpgradeLevel}";

            var target = cardPlay?.Target;
            string? targetId = null;
            if (target != null)
            {
                targetId = target.ToString() ?? "";
                var tsp = targetId.IndexOf(' ');
                if (tsp > 0) targetId = targetId.Substring(0, tsp);
                if (string.IsNullOrEmpty(targetId)) targetId = null;
            }

            var (actIndex, floorIndex) = GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: cardId,
                Id2: targetId,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "CARD_PLAYED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardPlayedCapturePatch error: {ex.Message}");
        }
    }

    internal static (int actIndex, int floorIndex) GetRunPosition()
    {
        try
        {
            var runManager = RunManager.Instance;
            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            var actIndex = state?.CurrentActIndex ?? 0;
            var floorIndex = 0;
            if (state != null)
                floorIndex = Traverse.Create(state).Property("CurrentFloorIndex").GetValue<int>();
            return (actIndex, floorIndex);
        }
        catch { return (0, 0); }
    }
}

/// <summary>
/// Captures CARD_DRAWN via Hook.AfterCardDrawn.
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterCardDrawn")]
public static class CardDrawnCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState combatState, PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (card == null) return;

            var cardId = card.Id.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (card.CurrentUpgradeLevel > 0)
                cardId = $"{cardId}+{card.CurrentUpgradeLevel}";

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: cardId,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "CARD_DRAWN"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardDrawnCapturePatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Captures CARD_DISCARDED via Hook.AfterCardDiscarded.
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterCardDiscarded")]
public static class CardDiscardedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState combatState, PlayerChoiceContext choiceContext, CardModel card)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (card == null) return;

            var cardId = card.Id.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (card.CurrentUpgradeLevel > 0)
                cardId = $"{cardId}+{card.CurrentUpgradeLevel}";

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: cardId,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "CARD_DISCARDED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardDiscardedCapturePatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Captures CARD_EXHAUSTED via Hook.AfterCardExhausted.
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterCardExhausted")]
public static class CardExhaustedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState combatState, PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (card == null) return;

            var cardId = card.Id.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (card.CurrentUpgradeLevel > 0)
                cardId = $"{cardId}+{card.CurrentUpgradeLevel}";

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: cardId,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "CARD_EXHAUSTED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardExhaustedCapturePatch error: {ex.Message}");
        }
    }
}

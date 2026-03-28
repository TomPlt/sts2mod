using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures DAMAGE_DEALT via Hook.AfterDamageGiven.
/// AfterDamageGiven(PlayerChoiceContext, CombatState, Creature dealer, DamageResult results, ValueProp, Creature target, CardModel)
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterDamageGiven")]
public static class DamageGivenCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerChoiceContext choiceContext, CombatState combatState,
        Creature dealer, DamageResult results, ValueProp props, Creature target, CardModel cardSource)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (results == null) return;
            var amount = results.TotalDamage;
            if (amount <= 0) return;

            var dealerId = ExtractId(dealer);
            var targetId = ExtractId(target);

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: dealerId,
                Id2: targetId,
                Amount: amount,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "DAMAGE_DEALT"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DamageGivenCapturePatch error: {ex.Message}");
        }
    }

    private static string? ExtractId(Creature? c)
    {
        if (c == null) return null;
        var id = c.ToString() ?? "";
        var sp = id.IndexOf(' ');
        if (sp > 0) id = id.Substring(0, sp);
        return string.IsNullOrEmpty(id) ? null : id;
    }
}

/// <summary>
/// Captures DAMAGE_TAKEN via Hook.AfterDamageReceived.
/// AfterDamageReceived(PlayerChoiceContext, IRunState, CombatState, Creature target, DamageResult result, ValueProp, Creature dealer, CardModel)
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterDamageReceived")]
public static class DamageReceivedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerChoiceContext choiceContext, IRunState runState, CombatState combatState,
        Creature target, DamageResult result, ValueProp props, Creature dealer, CardModel cardSource)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (result == null) return;
            var amount = result.TotalDamage;
            if (amount <= 0) return;

            var targetId = ExtractId(target);
            var dealerId = ExtractId(dealer);

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: targetId,
                Id2: dealerId,
                Amount: amount,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "DAMAGE_TAKEN"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DamageReceivedCapturePatch error: {ex.Message}");
        }
    }

    private static string? ExtractId(Creature? c)
    {
        if (c == null) return null;
        var id = c.ToString() ?? "";
        var sp = id.IndexOf(' ');
        if (sp > 0) id = id.Substring(0, sp);
        return string.IsNullOrEmpty(id) ? null : id;
    }
}

/// <summary>
/// Captures BLOCK_GAINED via Hook.AfterBlockGained.
/// AfterBlockGained(CombatState, Creature, decimal amount, ValueProp, CardModel)
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterBlockGained")]
public static class BlockGainedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState combatState, Creature creature, decimal amount, ValueProp props, CardModel cardSource)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (amount <= 0) return;

            var creatureId = creature?.ToString() ?? "";
            var sp = creatureId.IndexOf(' ');
            if (sp > 0) creatureId = creatureId.Substring(0, sp);

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: creatureId,
                Id2: null,
                Amount: (int)amount,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "BLOCK_GAINED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] BlockGainedCapturePatch error: {ex.Message}");
        }
    }
}

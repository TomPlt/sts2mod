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
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: ExtractCreatureId(dealer),
                Id2: ExtractCreatureId(target),
                Amount: amount,
                ActIndex: 0, FloorIndex: 0,
                Detail: "DAMAGE_DEALT"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DamageGivenCapturePatch error: {ex.Message}");
        }
    }

    internal static string? ExtractCreatureId(Creature? c)
    {
        if (c == null) return null;
        try
        {
            var id = Traverse.Create(c).Property("Id").GetValue<object>()?.ToString() ?? "";
            var sp = id.IndexOf(' ');
            if (sp > 0) id = id.Substring(0, sp);
            if (!string.IsNullOrEmpty(id)) return id;
        }
        catch { }
        foreach (var name in new[] { "ModelId", "Name" })
        {
            try
            {
                var id = Traverse.Create(c).Property(name).GetValue<object>()?.ToString() ?? "";
                var sp = id.IndexOf(' ');
                if (sp > 0) id = id.Substring(0, sp);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            catch { }
        }
        return c.GetType().Name;
    }
}

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
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: DamageGivenCapturePatch.ExtractCreatureId(target),
                Id2: DamageGivenCapturePatch.ExtractCreatureId(dealer),
                Amount: amount,
                ActIndex: 0, FloorIndex: 0,
                Detail: "DAMAGE_TAKEN"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DamageReceivedCapturePatch error: {ex.Message}");
        }
    }
}

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
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: DamageGivenCapturePatch.ExtractCreatureId(creature) ?? "unknown",
                Id2: null,
                Amount: (int)amount,
                ActIndex: 0, FloorIndex: 0,
                Detail: "BLOCK_GAINED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] BlockGainedCapturePatch error: {ex.Message}");
        }
    }
}

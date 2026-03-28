using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

[HarmonyPatch(typeof(Hook), "AfterPowerAmountChanged")]
public static class PowerAmountChangedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState combatState, PowerModel power, decimal amount, Creature applier, CardModel cardSource)
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            if (power == null) return;

            var powerId = power.Id.ToString() ?? "";
            var sp = powerId.IndexOf(' ');
            if (sp > 0) powerId = powerId.Substring(0, sp);

            // Owner = creature the power is on; Applier = creature that applied it
            var ownerId = DamageGivenCapturePatch.ExtractCreatureId(power.Owner as Creature)
                       ?? DamageGivenCapturePatch.ExtractCreatureId(applier);

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: powerId,
                Id2: ownerId,
                Amount: (int)amount,
                ActIndex: 0, FloorIndex: 0,
                Detail: "POWER_CHANGED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] PowerAmountChangedCapturePatch error: {ex.Message}");
        }
    }
}

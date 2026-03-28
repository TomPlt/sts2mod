using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures POWER_CHANGED via Hook.AfterPowerAmountChanged.
/// AfterPowerAmountChanged(CombatState, PowerModel power, decimal amount, Creature applier, CardModel cardSource)
/// Id1 = powerId, Id2 = owner creature ID, Amount = amount as int.
/// </summary>
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

            var owner = power.Owner;
            string? ownerId = null;
            if (owner != null)
            {
                try
                {
                    ownerId = Traverse.Create(owner).Property("Id").GetValue<object>()?.ToString() ?? "";
                    var osp = ownerId.IndexOf(' ');
                    if (osp > 0) ownerId = ownerId.Substring(0, osp);
                    if (string.IsNullOrEmpty(ownerId)) ownerId = null;
                }
                catch { ownerId = owner.GetType().Name; }
            }

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: powerId,
                Id2: ownerId,
                Amount: (int)amount,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "POWER_CHANGED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] PowerAmountChangedCapturePatch error: {ex.Message}");
        }
    }
}

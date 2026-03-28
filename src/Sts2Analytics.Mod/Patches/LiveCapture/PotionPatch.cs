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
/// Captures POTION_USED via Hook.AfterPotionUsed.
/// AfterPotionUsed(IRunState runState, CombatState combatState, PotionModel potion, Creature target)
/// Id1 = potionId, Id2 = targetId (if any).
/// </summary>
[HarmonyPatch(typeof(Hook), "AfterPotionUsed")]
public static class PotionUsedCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(IRunState runState, CombatState combatState, PotionModel potion, Creature target)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            if (potion == null) return;

            var potionId = potion.Id.ToString() ?? "";
            var sp = potionId.IndexOf(' ');
            if (sp > 0) potionId = potionId.Substring(0, sp);

            string? targetId = null;
            if (target != null)
            {
                targetId = target.ToString() ?? "";
                var tsp = targetId.IndexOf(' ');
                if (tsp > 0) targetId = targetId.Substring(0, tsp);
                if (string.IsNullOrEmpty(targetId)) targetId = null;
            }

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: potionId,
                Id2: targetId,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: "POTION_USED"
            ));
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] PotionUsedCapturePatch error: {ex.Message}");
        }
    }
}

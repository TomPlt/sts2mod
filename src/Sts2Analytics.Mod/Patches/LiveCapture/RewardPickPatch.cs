using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures relic picks via PickRelicAction.ExecuteAction.
/// Manually patched like PlayCardAction since it's async.
/// </summary>
public static class RewardPickPatch
{
    public static void Apply(Harmony harmony)
    {
        // Patch PickRelicAction.ExecuteAction
        try
        {
            var method = typeof(PickRelicAction).GetMethod("ExecuteAction",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method != null)
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(RewardPickPatch), nameof(RelicPickPrefix)));
                DebugLogOverlay.Log("[SpireOracle] Patched PickRelicAction.ExecuteAction");
            }
            else
                DebugLogOverlay.LogErr("[SpireOracle] PickRelicAction.ExecuteAction not found");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch PickRelicAction: {ex.Message}");
        }
    }

    public static void RelicPickPrefix(object __instance)
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            // Try to get relic ID from the action
            string relicId = "";
            foreach (var name in new[] { "RelicId", "Relic", "RelicModelId", "_relicId", "_relic" })
            {
                if (!string.IsNullOrEmpty(relicId)) break;
                try { relicId = Traverse.Create(__instance).Property(name).GetValue<object>()?.ToString() ?? ""; } catch { }
                if (string.IsNullOrEmpty(relicId))
                    try { relicId = Traverse.Create(__instance).Field(name).GetValue<object>()?.ToString() ?? ""; } catch { }
            }
            var sp = relicId.IndexOf(' ');
            if (sp > 0) relicId = relicId.Substring(0, sp);

            // Discovery: log all properties if relic not found
            if (string.IsNullOrEmpty(relicId))
            {
                var props = __instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    try
                    {
                        var val = p.GetValue(__instance);
                        if (val != null)
                            DebugLogOverlay.Log($"[SpireOracle] PickRelicAction.{p.Name} = {val}");
                    }
                    catch { }
                }
            }

            if (string.IsNullOrEmpty(relicId)) return;

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.RewardDecision,
                Id1: relicId, Id2: null,
                Amount: 1,
                ActIndex: actIndex, FloorIndex: floorIndex,
                Detail: "RELIC_PICK"
            ));
            DebugLogOverlay.Log($"[SpireOracle] RELIC_PICK: {relicId}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RelicPickPrefix error: {ex.Message}");
        }
    }
}

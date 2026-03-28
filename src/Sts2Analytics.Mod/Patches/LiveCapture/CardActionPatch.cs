using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures CARD_PLAYED by manually patching all PlayCardAction constructors.
/// Applied during Harmony.PatchAll since the attribute-based approach fails on async methods.
/// </summary>
public static class CardPlayedCapturePatch
{
    public static void Apply(Harmony harmony)
    {
        try
        {
            // Try to patch ExecuteAction on PlayCardAction via reflection
            var methods = typeof(PlayCardAction).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var prefix = new HarmonyMethod(typeof(CardPlayedCapturePatch), nameof(ExecutePrefix));
            var patched = false;
            foreach (var method in methods)
            {
                DebugLogOverlay.Log($"[SpireOracle] PlayCardAction method: {method.Name} ({method.GetParameters().Length} params)");
                if (method.Name == "ExecuteAction")
                {
                    try
                    {
                        harmony.Patch(method, prefix: prefix);
                        DebugLogOverlay.Log($"[SpireOracle] Patched PlayCardAction.ExecuteAction");
                        patched = true;
                    }
                    catch (Exception ex)
                    {
                        DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch ExecuteAction: {ex.Message}");
                    }
                }
            }
            if (!patched)
            {
                // Also try base class GameAction
                DebugLogOverlay.Log("[SpireOracle] ExecuteAction not found on PlayCardAction, trying base...");
                var baseMethods = typeof(PlayCardAction).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in baseMethods)
                {
                    if (method.Name == "ExecuteAction" && !patched)
                    {
                        try
                        {
                            harmony.Patch(method, prefix: prefix);
                            DebugLogOverlay.Log($"[SpireOracle] Patched {method.DeclaringType?.Name}.ExecuteAction");
                            patched = true;
                        }
                        catch (Exception ex)
                        {
                            DebugLogOverlay.LogErr($"[SpireOracle] Failed: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardPlayedCapturePatch.Apply error: {ex.Message}");
        }
    }

    public static void ExecutePrefix(object __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            // Log all property values for diagnostics
            var cmid = Traverse.Create(__instance).Property("CardModelId").GetValue<object>();
            var tid = Traverse.Create(__instance).Property("TargetId").GetValue<object>();
            var player = Traverse.Create(__instance).Property("Player").GetValue<object>();
            DebugLogOverlay.Log($"[SpireOracle] PlayCard: cmid={cmid} tid={tid} player={player}");

            var cardId = cmid?.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (string.IsNullOrEmpty(cardId)) return;

            // TargetId is the target creature
            string? targetId = null;
            try
            {
                var targetObj = Traverse.Create(__instance).Property("TargetId").GetValue<object>();
                if (targetObj != null)
                {
                    targetId = targetObj.ToString() ?? "";
                    var tsp = targetId.IndexOf(' ');
                    if (tsp > 0) targetId = targetId.Substring(0, tsp);
                    if (string.IsNullOrEmpty(targetId) || targetId == "0") targetId = null;
                }
            }
            catch { }

            DebugLogOverlay.Log($"[SpireOracle] CARD_PLAYED: {cardId} -> {targetId ?? "none"}");
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.CombatAction,
                Id1: cardId,
                Id2: targetId,
                Amount: 0,
                ActIndex: 0,
                FloorIndex: 0,
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

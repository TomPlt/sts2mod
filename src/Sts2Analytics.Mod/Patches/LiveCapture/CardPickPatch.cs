using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs.History;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures card pick/skip decisions by patching CardChoiceHistoryEntry constructor.
/// The game creates these entries when a card reward decision is recorded.
/// </summary>
public static class CardPickPatch
{
    public static void Apply(Harmony harmony)
    {
        try
        {
            var ctors = typeof(CardChoiceHistoryEntry).GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var ctor in ctors)
            {
                try
                {
                    harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(CardPickPatch), nameof(CtorPostfix)));
                    DebugLogOverlay.Log($"[SpireOracle] Patched CardChoiceHistoryEntry ctor ({ctor.GetParameters().Length} params)");
                }
                catch (Exception ex)
                {
                    DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch CardChoiceHistoryEntry ctor: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardPickPatch.Apply error: {ex.Message}");
        }
    }

    public static void CtorPostfix(object __instance)
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            // Extract card ID and wasPicked
            string cardId = "";
            int wasPicked = 0;

            // Dump all properties and fields for discovery
            var props = __instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var p in props)
            {
                try
                {
                    var val = p.GetValue(__instance);
                    if (val != null)
                    {
                        var s = val.ToString() ?? "";
                        DebugLogOverlay.Log($"[SpireOracle] CCH.{p.Name}({p.PropertyType.Name}) = {s}");
                        // If it's a card-like object, try to get its Id
                        if (s == "SerializableCard" || p.PropertyType.Name.Contains("Card"))
                        {
                            try
                            {
                                var innerIdObj = Traverse.Create(val).Property("Id").GetValue<object>()
                                              ?? Traverse.Create(val).Field("Id").GetValue<object>();
                                if (innerIdObj != null)
                                    DebugLogOverlay.Log($"[SpireOracle] CCH.{p.Name}.Id = {innerIdObj}");
                            }
                            catch { }
                            try
                            {
                                var innerIdObj = Traverse.Create(val).Property("id").GetValue<object>()
                                              ?? Traverse.Create(val).Field("id").GetValue<object>();
                                if (innerIdObj != null)
                                    DebugLogOverlay.Log($"[SpireOracle] CCH.{p.Name}.id = {innerIdObj}");
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            var fields = __instance.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var f in fields)
            {
                try
                {
                    var val = f.GetValue(__instance);
                    if (val != null)
                    {
                        DebugLogOverlay.Log($"[SpireOracle] CCH._{f.Name}({f.FieldType.Name}) = {val}");
                        if (f.FieldType.Name.Contains("Card") || val.ToString() == "SerializableCard")
                        {
                            try
                            {
                                var innerIdObj = Traverse.Create(val).Property("Id").GetValue<object>()
                                              ?? Traverse.Create(val).Field("Id").GetValue<object>();
                                if (innerIdObj != null)
                                    DebugLogOverlay.Log($"[SpireOracle] CCH._{f.Name}.Id = {innerIdObj}");
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return; // Skip enqueue for now — just discovery

            var decisionType = wasPicked == 1 ? "CARD_PICK" : "CARD_SKIP";
            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.RewardDecision,
                Id1: cardId, Id2: null,
                Amount: wasPicked,
                ActIndex: actIndex, FloorIndex: floorIndex,
                Detail: decisionType
            ));
            DebugLogOverlay.Log($"[SpireOracle] {decisionType}: {cardId}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CardPickPatch error: {ex.Message}");
        }
    }
}

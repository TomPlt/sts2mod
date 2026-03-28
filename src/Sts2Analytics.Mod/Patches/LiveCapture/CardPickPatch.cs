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

            // Try known property names
            foreach (var name in new[] { "CardId", "Card", "CardModelId", "Id" })
            {
                if (!string.IsNullOrEmpty(cardId)) break;
                try { cardId = Traverse.Create(__instance).Property(name).GetValue<object>()?.ToString() ?? ""; } catch { }
                if (string.IsNullOrEmpty(cardId))
                    try { cardId = Traverse.Create(__instance).Field(name).GetValue<object>()?.ToString() ?? ""; } catch { }
            }
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);

            // Try WasPicked
            foreach (var name in new[] { "WasPicked", "wasPicked", "Picked", "IsSelected", "Selected" })
            {
                try
                {
                    wasPicked = Traverse.Create(__instance).Property(name).GetValue<bool>() ? 1 : 0;
                    break;
                }
                catch { }
                try
                {
                    wasPicked = Traverse.Create(__instance).Field(name).GetValue<bool>() ? 1 : 0;
                    break;
                }
                catch { }
            }

            // Discovery: dump all properties if card not found
            if (string.IsNullOrEmpty(cardId))
            {
                var props = __instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    try
                    {
                        var val = p.GetValue(__instance);
                        if (val != null)
                            DebugLogOverlay.Log($"[SpireOracle] CardChoiceHist.{p.Name}({p.PropertyType.Name}) = {val}");
                    }
                    catch { }
                }
                return;
            }

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

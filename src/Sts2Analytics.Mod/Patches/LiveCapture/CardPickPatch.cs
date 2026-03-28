using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs.History;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures card pick/skip decisions by patching CardChoiceHistoryEntry constructor.
/// Properties: Card (SerializableCard with .Id), _wasPicked (field).
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
            // Get card ID: Card property -> SerializableCard -> Id property
            var cardObj = Traverse.Create(__instance).Property("Card").GetValue<object>();
            if (cardObj == null) return;

            var cardId = Traverse.Create(cardObj).Property("Id").GetValue<object>()?.ToString() ?? "";
            var sp = cardId.IndexOf(' ');
            if (sp > 0) cardId = cardId.Substring(0, sp);
            if (string.IsNullOrEmpty(cardId)) return;

            // Get upgrade level from SerializableCard
            var upgrade = 0;
            try { upgrade = Traverse.Create(cardObj).Property("Upgrades").GetValue<int>(); } catch { }
            if (upgrade == 0)
                try { upgrade = Traverse.Create(cardObj).Property("CurrentUpgradeLevel").GetValue<int>(); } catch { }
            if (upgrade > 0) cardId = $"{cardId}+{upgrade}";

            // Get wasPicked from field
            var wasPicked = false;
            try { wasPicked = Traverse.Create(__instance).Field("_wasPicked").GetValue<bool>(); } catch { }

            var decisionType = wasPicked ? "CARD_PICK" : "CARD_SKIP";
            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.RewardDecision,
                Id1: cardId, Id2: null,
                Amount: wasPicked ? 1 : 0,
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

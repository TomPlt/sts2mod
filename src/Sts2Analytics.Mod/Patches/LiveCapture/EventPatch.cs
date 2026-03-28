using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures EventDecision via EventOption.Chosen (postfix).
/// EventOption has a TextKey like "EVENT_ID.OPTION_SEGMENT".
/// Id1 = event part (everything before the last dot), Id2 = option segment (after last dot).
/// Kind = EventDecision.
/// </summary>
[HarmonyPatch(typeof(EventOption), nameof(EventOption.Chosen))]
public static class EventOptionChosenCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(EventOption __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var textKey = __instance?.TextKey ?? "";
            if (string.IsNullOrEmpty(textKey)) return;

            // Split TextKey into event ID and option segment
            // TextKey format: "NAMESPACE.EVENT_ID.OPTION" or just "EVENT_ID.OPTION"
            string eventId;
            string optionSegment;

            var lastDot = textKey.LastIndexOf('.');
            if (lastDot > 0)
            {
                eventId = textKey.Substring(0, lastDot);
                optionSegment = textKey.Substring(lastDot + 1);
            }
            else
            {
                eventId = textKey;
                optionSegment = textKey;
            }

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.EventDecision,
                Id1: eventId,
                Id2: optionSegment,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] EventDecision: event={eventId} option={optionSegment}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] EventOptionChosenCapturePatch error: {ex.Message}");
        }
    }
}

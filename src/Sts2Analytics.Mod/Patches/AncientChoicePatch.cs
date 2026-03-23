using System;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NEventOptionButton), nameof(NEventOptionButton._Ready))]
public static class AncientChoicePatch
{
    [HarmonyPostfix]
    public static void Postfix(NEventOptionButton __instance)
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        try
        {
            // Only apply to ancient events, not regular events
            var eventModel = __instance.Event;
            if (eventModel == null || eventModel is not AncientEventModel) return;

            var option = __instance.Option;
            if (option == null) return;

            var textKey = option.TextKey;
            if (string.IsNullOrEmpty(textKey)) return;

            // TextKey format: "ANCIENT.pages.INITIAL.options.CHOICE_KEY"
            // Extract the last segment as the choice key
            var choiceKey = textKey;
            if (choiceKey.Contains('.'))
                choiceKey = choiceKey.Substring(choiceKey.LastIndexOf('.') + 1);

            var stats = DataLoader.GetAncientChoice(choiceKey);
            if (stats == null)
                stats = DataLoader.GetAncientChoice(textKey);

            if (stats == null) return;

            // Detect current character for per-character ratings
            var character = DetectCharacter();

            GD.Print($"[SpireOracle] Ancient overlay: {choiceKey} = {stats.Rating:F0} (char={character ?? "none"})");
            OverlayFactory.AddAncientOverlay(__instance, stats, character);

            // Wire up hover to show/hide detail panel
            __instance.MouseEntered += () => OverlayFactory.ShowDetail(__instance);
            __instance.MouseExited += () => OverlayFactory.HideDetail(__instance);
            __instance.FocusEntered += () => OverlayFactory.ShowDetail(__instance);
            __instance.FocusExited += () => OverlayFactory.HideDetail(__instance);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Error in AncientChoicePatch: {ex.Message}");
        }
    }

    private static string? DetectCharacter()
    {
        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return null;

            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            if (state?.Players == null || state.Players.Count == 0) return null;

            var characterId = state.Players[0].Character?.ToString() ?? "";
            var spaceIdx = characterId.IndexOf(' ');
            if (spaceIdx > 0) characterId = characterId.Substring(0, spaceIdx);
            return characterId;
        }
        catch
        {
            return null;
        }
    }
}

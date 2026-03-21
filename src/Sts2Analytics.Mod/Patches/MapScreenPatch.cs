using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
public static class MapScreenOpenPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!ModEntry.OverlayEnabled || !DataLoader.IsLoaded) return;

        try
        {
            // Refresh context when map opens (act may have changed)
            var (character, actIndex) = InputPatch.DetectContext();
            GD.Print($"[SpireOracle] Map opened, refreshing intel: {character} Act {actIndex + 1}");
            MapIntelPanelManager.Show(character, actIndex);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] MapScreenOpenPatch error: {ex.Message}");
        }
    }
}

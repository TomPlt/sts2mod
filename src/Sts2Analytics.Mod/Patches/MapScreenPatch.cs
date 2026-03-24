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
        SpireOracle.UI.CombatOverlay.Hide();
        if (!MapIntelPanelManager.IsVisible) return;

        try
        {
            // Refresh context when map opens (act may have changed), only if panel already visible
            var (character, actIndex, actName) = InputPatch.DetectContext();
            DebugLogOverlay.Log($"[SpireOracle] Map opened, refreshing intel: {character} Act {actIndex + 1} ({actName})");
            MapIntelPanelManager.Show(character, actIndex, actName);
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] MapScreenOpenPatch error: {ex.Message}");
        }
    }
}

using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
public static class InputPatch
{
    [HarmonyPostfix]
    public static void Postfix(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent) return;
        if (!keyEvent.Pressed || keyEvent.Echo) return;

        // Debug log works even when data isn't loaded
        if (keyEvent.Keycode == Key.F5)
        {
            DebugLogOverlay.Toggle();
            return;
        }

        if (keyEvent.Keycode == Key.F6)
        {
            RunStatsOverlay.Toggle();
            return;
        }

        if (!DataLoader.IsLoaded) return;

        if (keyEvent.Keycode == Key.F4)
        {
            if (CombatOverlay.IsInCombat)
                CombatOverlay.Toggle();
            else
                DeckViewPatch.ToggleCardElos();
            return;
        }

        if (keyEvent.Keycode != Key.F3) return;

        // Toggle map intel panel independently
        var isVisible = MapIntelPanelManager.IsVisible;
        if (isVisible)
        {
            MapIntelPanelManager.Hide();
            DebugLogOverlay.Log("[SpireOracle] Map intel hidden");
        }
        else
        {
            var (character, actIndex, actName) = DetectContext();
            DebugLogOverlay.Log($"[SpireOracle] Showing map intel: {character} Act {actIndex + 1} ({actName})");
            MapIntelPanelManager.Show(character, actIndex, actName);
        }
    }

    internal static (string character, int actIndex, string actName) DetectContext()
    {
        try
        {
            var runManager = RunManager.Instance;
            var state = runManager != null
                ? Traverse.Create(runManager).Property("State").GetValue<RunState>()
                : null;
            if (state != null)
            {
                var actIndex = state.CurrentActIndex;
                var actName = "";
                try
                {
                    var acts = state.Acts;
                    if (acts != null && actIndex < acts.Count)
                    {
                        var actId = acts[actIndex]?.ToString() ?? "";
                        var spaceIdx = actId.IndexOf(' ');
                        if (spaceIdx > 0) actId = actId.Substring(0, spaceIdx);
                        actName = actId;
                    }
                }
                catch { }

                var player = GetLocalPlayer(runManager, state);
                if (player != null)
                {
                    var characterId = player.Character?.ToString() ?? "";
                    // ToString returns "CHARACTER.IRONCLAD (4907296)" — strip the ID suffix
                    var spaceIdx = characterId.IndexOf(' ');
                    if (spaceIdx > 0) characterId = characterId.Substring(0, spaceIdx);
                    if (!string.IsNullOrEmpty(characterId))
                        return (characterId, actIndex, actName);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DetectContext error: {ex.Message}");
        }

        var characters = DataLoader.GetMapIntelCharacters();
        return (characters.Count > 0 ? characters[0] : "CHARACTER.IRONCLAD", 0, "");
    }

    /// <summary>
    /// Find the local player in a multiplayer game. Falls back to Players[0] in singleplayer.
    /// Uses RunManager.NetService.NetId to match against Player.NetId.
    /// </summary>
    internal static MegaCrit.Sts2.Core.Entities.Players.Player? GetLocalPlayer(
        RunManager? runManager, RunState? state)
    {
        if (state?.Players == null || state.Players.Count == 0) return null;
        if (state.Players.Count == 1) return state.Players[0];

        // Multiplayer: match by NetId
        try
        {
            var netService = runManager != null
                ? Traverse.Create(runManager).Property("NetService").GetValue<object>()
                : null;
            if (netService != null)
            {
                var localNetId = Traverse.Create(netService).Property("NetId").GetValue<ulong>();
                if (localNetId != 0)
                {
                    foreach (var p in state.Players)
                    {
                        if (p.NetId == localNetId) return p;
                    }
                }
            }
        }
        catch { }

        // Fallback: first player
        return state.Players[0];
    }
}

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
        if (keyEvent.Keycode != Key.F3) return;
        if (!DataLoader.IsLoaded) return;

        // Toggle map intel panel independently
        var isVisible = MapIntelPanelManager.IsVisible;
        if (isVisible)
        {
            MapIntelPanelManager.Hide();
            GD.Print("[SpireOracle] Map intel hidden");
        }
        else
        {
            var (character, actIndex, actName) = DetectContext();
            GD.Print($"[SpireOracle] Showing map intel: {character} Act {actIndex + 1} ({actName})");
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

                var players = state.Players;
                if (players != null && players.Count > 0)
                {
                    var characterId = players[0].Character?.ToString() ?? "";
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
            GD.PrintErr($"[SpireOracle] DetectContext error: {ex.Message}");
        }

        var characters = DataLoader.GetMapIntelCharacters();
        return (characters.Count > 0 ? characters[0] : "CHARACTER.IRONCLAD", 0, "");
    }
}

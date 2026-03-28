using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Patches RunManager.GenerateMap to capture the start of a new run.
/// GenerateMap is called once at run start after character/deck selection.
/// </summary>
[HarmonyPatch(typeof(RunManager), "GenerateMap")]
public static class RunStartPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return;

            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            if (state == null) return;

            // Get seed
            var seed = "";
            try
            {
                seed = Traverse.Create(state).Property("Seed").GetValue<object>()?.ToString() ?? "";
            }
            catch { }
            if (string.IsNullOrEmpty(seed))
            {
                try { seed = Traverse.Create(state).Field("Seed").GetValue<object>()?.ToString() ?? ""; } catch { }
            }

            // Get ascension level — try multiple names
            var ascension = 0;
            foreach (var name in new[] { "Ascension", "AscensionLevel" })
            {
                if (ascension != 0) break;
                try { ascension = Traverse.Create(state).Property(name).GetValue<int>(); } catch { }
                if (ascension == 0)
                    try { ascension = Traverse.Create(state).Field(name).GetValue<int>(); } catch { }
            }

            // Get character
            var player = InputPatch.GetLocalPlayer(runManager, state);
            var character = "";
            if (player != null)
            {
                character = player.Character?.ToString() ?? "";
                var spaceIdx = character.IndexOf(' ');
                if (spaceIdx > 0) character = character.Substring(0, spaceIdx);
            }

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.StartRun,
                Id1: seed,
                Id2: character,
                Amount: ascension,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] StartRun: seed={seed} char={character} asc={ascension}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunStartPatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Patches RunManager.WinRun to capture a victory (Amount=1).
/// </summary>
// WinRun is async; patch by string name since nameof() won't compile
[HarmonyPatch(typeof(RunManager), "WinRun")]
public static class RunWinPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.EndRun,
                Id1: null,
                Id2: null,
                Amount: 1,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));

            DebugLogOverlay.Log("[SpireOracle] EndRun: Win");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunWinPatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Patches RunManager.AbandonInternal to capture abandon/death (Amount=0).
/// </summary>
// AbandonInternal is async; patch by string name since nameof() won't compile
[HarmonyPatch(typeof(RunManager), "AbandonInternal")]
public static class RunAbandonPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.EndRun,
                Id1: null,
                Id2: null,
                Amount: 0,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));

            DebugLogOverlay.Log("[SpireOracle] EndRun: Abandon/Death");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunAbandonPatch error: {ex.Message}");
        }
    }
}

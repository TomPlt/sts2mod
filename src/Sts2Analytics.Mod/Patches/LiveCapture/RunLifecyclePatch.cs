using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Patches RunManager.GenerateMap to capture the start of a new run.
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

            // Discover seed — dump properties to find the right one
            var seed = "";
            foreach (var name in new[] { "Seed", "SeedString", "RunSeed", "SeedValue" })
            {
                if (!string.IsNullOrEmpty(seed)) break;
                try { seed = Traverse.Create(state).Property(name).GetValue<object>()?.ToString() ?? ""; } catch { }
                if (string.IsNullOrEmpty(seed))
                    try { seed = Traverse.Create(state).Field(name).GetValue<object>()?.ToString() ?? ""; } catch { }
            }

            // Try SeedHelper static properties/fields
            if (string.IsNullOrEmpty(seed))
            {
                try
                {
                    var seedHelperType = state.GetType().Assembly.GetType("MegaCrit.Sts2.Core.Helpers.SeedHelper");
                    if (seedHelperType != null)
                    {
                        var sprops = seedHelperType.GetProperties(BindingFlags.Public | BindingFlags.Static);
                        foreach (var p in sprops)
                        {
                            try
                            {
                                var val = p.GetValue(null);
                                if (val != null)
                                    DebugLogOverlay.Log($"[SpireOracle] SeedHelper.{p.Name}({p.PropertyType.Name}) = {val}");
                            }
                            catch { }
                        }
                        var sfields = seedHelperType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        foreach (var f in sfields)
                        {
                            try
                            {
                                var val = f.GetValue(null);
                                if (val != null)
                                    DebugLogOverlay.Log($"[SpireOracle] SeedHelper._{f.Name}({f.FieldType.Name}) = {val}");
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // Ascension
            var ascension = 0;
            foreach (var name in new[] { "Ascension", "AscensionLevel", "CurrentAscension" })
            {
                if (ascension != 0) break;
                try { ascension = Traverse.Create(state).Property(name).GetValue<int>(); } catch { }
                if (ascension == 0)
                    try { ascension = Traverse.Create(state).Field(name).GetValue<int>(); } catch { }
            }

            // Character
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
/// Manually patches RunManager.WinRun and AbandonInternal via reflection.
/// String-based [HarmonyPatch] doesn't work for these async methods.
/// </summary>
public static class RunEndPatch
{
    public static void Apply(Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(RunEndPatch), nameof(WinPrefix));
        var abandonPrefix = new HarmonyMethod(typeof(RunEndPatch), nameof(AbandonPrefix));

        // Patch WinRun
        try
        {
            var winMethod = typeof(RunManager).GetMethod("WinRun",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (winMethod != null)
            {
                harmony.Patch(winMethod, prefix: prefix);
                DebugLogOverlay.Log("[SpireOracle] Patched RunManager.WinRun");
            }
            else
                DebugLogOverlay.LogErr("[SpireOracle] RunManager.WinRun not found");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch WinRun: {ex.Message}");
        }

        // Patch AbandonInternal
        try
        {
            var abandonMethod = typeof(RunManager).GetMethod("AbandonInternal",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (abandonMethod != null)
            {
                harmony.Patch(abandonMethod, prefix: abandonPrefix);
                DebugLogOverlay.Log("[SpireOracle] Patched RunManager.AbandonInternal");
            }
            else
                DebugLogOverlay.LogErr("[SpireOracle] RunManager.AbandonInternal not found");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch AbandonInternal: {ex.Message}");
        }
    }

    public static void WinPrefix()
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.EndRun,
                Id1: null, Id2: null,
                Amount: 1, ActIndex: 0, FloorIndex: 0,
                Detail: null
            ));
            DebugLogOverlay.Log("[SpireOracle] EndRun: Win");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunWinPatch error: {ex.Message}");
        }
    }

    public static void AbandonPrefix()
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.EndRun,
                Id1: null, Id2: null,
                Amount: 0, ActIndex: 0, FloorIndex: 0,
                Detail: null
            ));
            DebugLogOverlay.Log("[SpireOracle] EndRun: Abandon/Death");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunAbandonPatch error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called from ModEntry's run file watcher when a .run file appears.
    /// Parses the .run file to extract win/seed, links it to the current LiveRun.
    /// </summary>
    public static void LinkRunFile(string fileName, string filePath)
    {
        if (!LiveRunDb.IsInitialized) return;

        // Parse .run file to get win status and seed
        var win = 0;
        var seed = "";
        try
        {
            var json = System.IO.File.ReadAllText(filePath);
            // Quick parse — look for "win":true/false and "seed":"..."
            if (json.Contains("\"win\":true") || json.Contains("\"win\": true"))
                win = 1;
            // Extract seed
            var seedIdx = json.IndexOf("\"seed\"");
            if (seedIdx >= 0)
            {
                var colonIdx = json.IndexOf(':', seedIdx);
                var quoteStart = json.IndexOf('"', colonIdx + 1);
                var quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteStart >= 0 && quoteEnd > quoteStart)
                    seed = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Failed to parse .run file: {ex.Message}");
        }

        // End the run with win status, seed, and filename
        LiveRunDb.Enqueue(new DbAction(
            Kind: DbActionKind.EndRun,
            Id1: fileName, Id2: seed,
            Amount: win, ActIndex: 0, FloorIndex: 0,
            Detail: null
        ));
        DebugLogOverlay.Log($"[SpireOracle] Run ended: {fileName} win={win} seed={seed}");
    }
}

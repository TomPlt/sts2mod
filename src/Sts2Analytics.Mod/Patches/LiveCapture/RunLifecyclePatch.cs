using System;
using System.Linq;
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

            // Detect profile from RunState's save path or the game's profile-scoped data path
            var profile = "";
            try
            {
                // The game stores profile info on the RunState or via file paths
                // Try to find it from the RunState's save directory
                foreach (var name in new[] { "ProfileId", "Profile", "ProfileIndex", "SaveProfile" })
                {
                    if (!string.IsNullOrEmpty(profile)) break;
                    try { profile = Traverse.Create(state).Property(name).GetValue<object>()?.ToString() ?? ""; } catch { }
                    if (string.IsNullOrEmpty(profile))
                        try { profile = Traverse.Create(state).Field(name).GetValue<object>()?.ToString() ?? ""; } catch { }
                }
                // Fallback: scan AppData for current_run.save to find which profile is active
                if (string.IsNullOrEmpty(profile))
                {
                    var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                    var sts2Dir = System.IO.Path.Combine(appData, "SlayTheSpire2", "steam");
                    if (System.IO.Directory.Exists(sts2Dir))
                    {
                        // Find the most recently modified current_run.save
                        System.IO.FileInfo? newest = null;
                        foreach (var file in new System.IO.DirectoryInfo(sts2Dir).GetFiles("current_run.save", System.IO.SearchOption.AllDirectories))
                        {
                            if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                                newest = file;
                        }
                        if (newest != null)
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(newest.FullName, @"(profile\d+)");
                            if (match.Success) profile = match.Value;
                        }
                    }
                }
            }
            catch { }

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.StartRun,
                Id1: seed,
                Id2: character,
                Amount: ascension,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: profile
            ));

            DebugLogOverlay.Log($"[SpireOracle] StartRun: seed={seed} char={character} asc={ascension} profile={profile}");
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
            DumpRunSummary();
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
            DumpRunSummary();
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RunAbandonPatch error: {ex.Message}");
        }
    }

    private static void DumpRunSummary()
    {
        if (!LiveRunDb.IsInitialized || LiveRunDb.CurrentRunId <= 0) return;
        try
        {
            var runId = LiveRunDb.CurrentRunId;

            var topPlayed = LiveRunDb.QueryTopStats(
                @"SELECT a.SourceId, COUNT(*) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND a.ActionType='CARD_PLAYED'
                  GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 10", runId);

            var topDamage = LiveRunDb.QueryTopStats(
                @"SELECT a1.SourceId, SUM(a2.Amount) as total
                  FROM CombatActions a1
                  JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                    AND a2.ActionType='DAMAGE_DEALT'
                    AND a2.SourceId LIKE 'CHARACTER.%'
                    AND a2.TargetId NOT LIKE 'CHARACTER.%'
                    AND a2.Seq < COALESCE(
                      (SELECT MIN(a3.Seq) FROM CombatActions a3
                       WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
                  JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
                  GROUP BY a1.SourceId ORDER BY total DESC LIMIT 10", runId);

            var topBlock = LiveRunDb.QueryTopStats(
                @"SELECT a1.SourceId, SUM(a2.Amount) as total
                  FROM CombatActions a1
                  JOIN CombatActions a2 ON a2.TurnId=a1.TurnId AND a2.Seq > a1.Seq
                    AND a2.ActionType='BLOCK_GAINED'
                    AND a2.SourceId LIKE 'CHARACTER.%'
                    AND a2.Seq < COALESCE(
                      (SELECT MIN(a3.Seq) FROM CombatActions a3
                       WHERE a3.TurnId=a1.TurnId AND a3.Seq > a1.Seq AND a3.ActionType='CARD_PLAYED'), 9999)
                  JOIN Turns t ON a1.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE a1.ActionType='CARD_PLAYED' AND c.RunId=@runId
                  GROUP BY a1.SourceId ORDER BY total DESC LIMIT 10", runId);

            var combatCount = LiveRunDb.QueryTopStats(
                @"SELECT 'combats', COUNT(*) FROM Combats WHERE RunId=@runId", runId);
            var turnCount = LiveRunDb.QueryTopStats(
                @"SELECT 'turns', COUNT(*) FROM Turns t JOIN Combats c ON t.CombatId=c.Id WHERE c.RunId=@runId", runId);
            var totalDmg = LiveRunDb.QueryTopStats(
                @"SELECT 'dealt', SUM(a.Amount) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND a.ActionType='DAMAGE_DEALT'
                    AND a.SourceId LIKE 'CHARACTER.%' AND a.TargetId NOT LIKE 'CHARACTER.%'", runId);
            var totalTaken = LiveRunDb.QueryTopStats(
                @"SELECT 'taken', SUM(a.Amount) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND a.ActionType='DAMAGE_TAKEN'
                    AND a.SourceId LIKE 'CHARACTER.%'", runId);

            DebugLogOverlay.Log("=== RUN SUMMARY ===");
            var combats = combatCount.Count > 0 ? combatCount[0].value : 0;
            var turns = turnCount.Count > 0 ? turnCount[0].value : 0;
            var dealt = totalDmg.Count > 0 ? totalDmg[0].value : 0;
            var taken = totalTaken.Count > 0 ? totalTaken[0].value : 0;
            DebugLogOverlay.Log($"Combats: {combats}  Turns: {turns}  Damage dealt: {dealt}  Taken: {taken}");

            if (topPlayed.Count > 0)
            {
                DebugLogOverlay.Log("Top cards played:");
                foreach (var (card, count) in topPlayed)
                {
                    // Find matching damage/block
                    var dmg = topDamage.FirstOrDefault(d => d.label == card).value;
                    var blk = topBlock.FirstOrDefault(b => b.label == card).value;
                    var extra = "";
                    if (dmg > 0) extra += $" dmg:{dmg}";
                    if (blk > 0) extra += $" blk:{blk}";
                    DebugLogOverlay.Log($"  {FormatCardName(card)}: {count}x{extra}");
                }
            }
            if (topDamage.Count > 0)
            {
                DebugLogOverlay.Log("Top damage cards:");
                foreach (var (card, total) in topDamage)
                    DebugLogOverlay.Log($"  {FormatCardName(card)}: {total} total");
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DumpRunSummary error: {ex.Message}");
        }
    }

    private static string FormatCardName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var name = id;
        if (name.StartsWith("CARD.")) name = name.Substring(5);
        var upgrade = "";
        var plusIdx = name.IndexOf('+');
        if (plusIdx > 0) { upgrade = name.Substring(plusIdx); name = name.Substring(0, plusIdx); }
        name = string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w));
        return name + upgrade;
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

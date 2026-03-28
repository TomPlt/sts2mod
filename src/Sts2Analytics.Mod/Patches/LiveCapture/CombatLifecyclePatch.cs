using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures combat start via AfterCombatRoomLoaded (second postfix alongside CombatPatch).
/// Kind=StartCombat, Id1=encounterId, ActIndex=actIndex, FloorIndex=floorIndex.
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.AfterCombatRoomLoaded))]
public static class CombatStartCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;

            var state = cm.DebugOnlyGetState();
            if (state == null) return;

            // Get encounter ID
            var encounterId = state.Encounter?.ToString() ?? "";
            var spaceIdx = encounterId.IndexOf(' ');
            if (spaceIdx > 0) encounterId = encounterId.Substring(0, spaceIdx);

            var actIndex = state.RunState?.CurrentActIndex ?? 0;

            // Get floor index — TotalFloor is the overall floor counter
            var floorIndex = 0;
            try
            {
                var runState = state.RunState as RunState;
                if (runState != null)
                {
                    try { floorIndex = Traverse.Create(runState).Property("TotalFloor").GetValue<int>(); } catch { }
                    if (floorIndex == 0)
                        try { floorIndex = Traverse.Create(runState).Property("ActFloor").GetValue<int>(); } catch { }
                }
            }
            catch { }

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.StartCombat,
                Id1: encounterId,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] StartCombat: {encounterId} act={actIndex} floor={floorIndex}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CombatStartCapturePatch error: {ex.Message}");
        }
    }
}

/// <summary>
/// Captures combat end via CombatManager.Reset (prefix, before state is cleared).
/// Uses PendingLossState to determine win/loss — if null/default the player won.
/// Kind=EndCombat, Amount=1 (won) or 0 (lost).
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public static class CombatEndCapturePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;

            // Check if any player is dead (HP <= 0) to determine win/loss
            var won = 1;
            try
            {
                var state = cm.DebugOnlyGetState();
                var runState = state?.RunState as RunState;
                var player = InputPatch.GetLocalPlayer(RunManager.Instance, runState);
                if (player != null)
                {
                    var hp = 0;
                    try { hp = Traverse.Create(player).Property("CurrentHp").GetValue<int>(); } catch { }
                    if (hp == 0)
                    {
                        // Try on Creature
                        try
                        {
                            var creature = Traverse.Create(player).Property("Creature").GetValue<object>();
                            if (creature != null)
                                hp = Traverse.Create(creature).Property("CurrentHp").GetValue<int>();
                        }
                        catch { }
                    }
                    if (hp <= 0) won = 0;
                }
            }
            catch { }
            // Also check PendingLossState as fallback
            if (won == 1)
            {
                try
                {
                    var pendingLoss = Traverse.Create(cm).Property("PendingLossState").GetValue<object>()
                                   ?? Traverse.Create(cm).Field("PendingLossState").GetValue<object>();
                    var pendingLossStr = pendingLoss?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(pendingLossStr) && pendingLossStr != "None" && pendingLossStr != "0")
                        won = 0;
                }
                catch { }
            }

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.EndCombat,
                Id1: null,
                Id2: null,
                Amount: won,
                ActIndex: 0,
                FloorIndex: 0,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] EndCombat: won={won}");

            // Dump fight summary to F5 debug log
            DumpCombatSummary();
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CombatEndCapturePatch error: {ex.Message}");
        }
    }

    private static void DumpCombatSummary()
    {
        if (!LiveRunDb.IsInitialized || LiveRunDb.CurrentRunId <= 0) return;
        try
        {
            var runId = LiveRunDb.CurrentRunId;

            var played = LiveRunDb.QueryTopStats(
                @"SELECT a.SourceId, COUNT(*) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND c.Id=(SELECT MAX(Id) FROM Combats WHERE RunId=@runId)
                    AND a.ActionType='CARD_PLAYED'
                  GROUP BY a.SourceId ORDER BY COUNT(*) DESC LIMIT 5", runId);

            var damage = LiveRunDb.QueryTopStats(
                @"SELECT a.SourceId || ' -> ' || a.TargetId, a.Amount FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND c.Id=(SELECT MAX(Id) FROM Combats WHERE RunId=@runId)
                    AND a.ActionType='DAMAGE_DEALT'
                  ORDER BY a.Amount DESC LIMIT 5", runId);

            var totalDmgDealt = LiveRunDb.QueryTopStats(
                @"SELECT 'dealt', SUM(a.Amount) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND c.Id=(SELECT MAX(Id) FROM Combats WHERE RunId=@runId)
                    AND a.ActionType='DAMAGE_DEALT' AND a.SourceId LIKE 'CHARACTER.%' AND a.TargetId NOT LIKE 'CHARACTER.%'", runId);

            var totalDmgTaken = LiveRunDb.QueryTopStats(
                @"SELECT 'taken', SUM(a.Amount) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND c.Id=(SELECT MAX(Id) FROM Combats WHERE RunId=@runId)
                    AND a.ActionType='DAMAGE_TAKEN' AND a.SourceId LIKE 'CHARACTER.%'", runId);

            var totalBlock = LiveRunDb.QueryTopStats(
                @"SELECT 'block', SUM(a.Amount) FROM CombatActions a
                  JOIN Turns t ON a.TurnId=t.Id JOIN Combats c ON t.CombatId=c.Id
                  WHERE c.RunId=@runId AND c.Id=(SELECT MAX(Id) FROM Combats WHERE RunId=@runId)
                    AND a.ActionType='BLOCK_GAINED' AND a.SourceId LIKE 'CHARACTER.%'", runId);

            DebugLogOverlay.Log("--- Fight Summary ---");
            var dealt = totalDmgDealt.Count > 0 ? totalDmgDealt[0].value : 0;
            var taken = totalDmgTaken.Count > 0 ? totalDmgTaken[0].value : 0;
            var block = totalBlock.Count > 0 ? totalBlock[0].value : 0;
            DebugLogOverlay.Log($"Damage dealt: {dealt}  Taken: {taken}  Block: {block}");

            if (played.Count > 0)
            {
                DebugLogOverlay.Log("Cards played:");
                foreach (var (card, count) in played)
                    DebugLogOverlay.Log($"  {card}: {count}x");
            }
            if (damage.Count > 0)
            {
                DebugLogOverlay.Log("Biggest hits:");
                foreach (var (desc, amount) in damage)
                    DebugLogOverlay.Log($"  {desc}: {amount}");
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] DumpCombatSummary error: {ex.Message}");
        }
    }
}

/// <summary>
/// Captures turn start via CombatManager.SetupPlayerTurn (postfix).
/// Kind=StartTurn, Amount=turnNumber, ActIndex=startingEnergy, FloorIndex=startingHp.
/// </summary>
// SetupPlayerTurn is async; patch by string name since nameof() won't compile
[HarmonyPatch(typeof(CombatManager), "SetupPlayerTurn")]
public static class TurnStartCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;

            var state = cm.DebugOnlyGetState();
            if (state == null) return;

            // Get turn number — try multiple property/field names
            var turnNumber = 0;
            foreach (var name in new[] { "TurnNumber", "Turn", "CurrentTurn", "_turnNumber", "PlayerTurnNumber", "RoundNumber" })
            {
                if (turnNumber != 0) break;
                try { turnNumber = Traverse.Create(state).Property(name).GetValue<int>(); } catch { }
                if (turnNumber == 0)
                    try { turnNumber = Traverse.Create(state).Field(name).GetValue<int>(); } catch { }
            }
            // Also try on CombatManager directly
            if (turnNumber == 0)
            {
                foreach (var name in new[] { "TurnNumber", "Turn", "CurrentTurn", "_turnNumber", "PlayerTurnNumber", "RoundNumber" })
                {
                    if (turnNumber != 0) break;
                    try { turnNumber = Traverse.Create(cm).Property(name).GetValue<int>(); } catch { }
                    if (turnNumber == 0)
                        try { turnNumber = Traverse.Create(cm).Field(name).GetValue<int>(); } catch { }
                }
            }
            // Discovery: log int properties on CombatState if turn still 0
            if (turnNumber == 0)
            {
                try
                {
                    var props = state.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var p in props)
                    {
                        if (p.PropertyType == typeof(int))
                        {
                            try
                            {
                                var val = (int)p.GetValue(state)!;
                                if (val > 0)
                                    DebugLogOverlay.Log($"[SpireOracle] CombatState.{p.Name} = {val}");
                            }
                            catch { }
                        }
                    }
                    var fields = state.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        if (f.FieldType == typeof(int) && f.Name.Contains("urn", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var val = (int)f.GetValue(state)!;
                                DebugLogOverlay.Log($"[SpireOracle] CombatState._{f.Name} = {val}");
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // Get local player for HP and energy
            var runManager = RunManager.Instance;
            var runState = state.RunState as RunState;
            var player = InputPatch.GetLocalPlayer(runManager, runState);

            var startingHp = 0;
            var startingEnergy = 0;

            if (player != null)
            {
                // Try HP on Player, then on Player.Creature (Creature has CurrentHp)
                foreach (var name in new[] { "CurrentHp", "Hp", "Health" })
                {
                    if (startingHp != 0) break;
                    try { startingHp = Traverse.Create(player).Property(name).GetValue<int>(); } catch { }
                    if (startingHp == 0)
                        try { startingHp = Traverse.Create(player).Field(name).GetValue<int>(); } catch { }
                }
                // Try on player.Creature if Player itself doesn't have HP
                if (startingHp == 0)
                {
                    try
                    {
                        var creature = Traverse.Create(player).Property("Creature").GetValue<object>();
                        if (creature != null)
                        {
                            foreach (var name in new[] { "CurrentHp", "Hp" })
                            {
                                if (startingHp != 0) break;
                                try { startingHp = Traverse.Create(creature).Property(name).GetValue<int>(); } catch { }
                                if (startingHp == 0)
                                    try { startingHp = Traverse.Create(creature).Field(name).GetValue<int>(); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                // Try energy
                foreach (var name in new[] { "CurrentEnergy", "Energy" })
                {
                    if (startingEnergy != 0) break;
                    try { startingEnergy = Traverse.Create(player).Property(name).GetValue<int>(); } catch { }
                    if (startingEnergy == 0)
                        try { startingEnergy = Traverse.Create(player).Field(name).GetValue<int>(); } catch { }
                }
                // Try on PlayerCombatState
                if (startingEnergy == 0)
                {
                    try
                    {
                        var pcs = Traverse.Create(player).Property("PlayerCombatState").GetValue<object>();
                        if (pcs != null)
                        {
                            foreach (var name in new[] { "CurrentEnergy", "Energy" })
                            {
                                if (startingEnergy != 0) break;
                                try { startingEnergy = Traverse.Create(pcs).Property(name).GetValue<int>(); } catch { }
                                if (startingEnergy == 0)
                                    try { startingEnergy = Traverse.Create(pcs).Field(name).GetValue<int>(); } catch { }
                            }
                        }
                    }
                    catch { }
                }
            }

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.StartTurn,
                Id1: null,
                Id2: null,
                Amount: turnNumber,
                ActIndex: startingEnergy,
                FloorIndex: startingHp,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] StartTurn: turn={turnNumber} hp={startingHp} energy={startingEnergy}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] TurnStartCapturePatch error: {ex.Message}");
        }
    }
}

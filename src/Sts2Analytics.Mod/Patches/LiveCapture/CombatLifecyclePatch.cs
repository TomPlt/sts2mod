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

            // Get floor index
            var floorIndex = 0;
            try
            {
                var runState = state.RunState as RunState;
                if (runState != null)
                    floorIndex = Traverse.Create(runState).Property("CurrentFloorIndex").GetValue<int>();
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

            // PendingLossState is not publicly accessible — use Traverse
            var won = 1;
            try
            {
                var pendingLoss = Traverse.Create(cm).Property("PendingLossState").GetValue<object>();
                if (pendingLoss == null)
                    pendingLoss = Traverse.Create(cm).Field("PendingLossState").GetValue<object>();
                // PendingLossState is an enum/struct — check if it represents a loss
                // If the property exists and has a non-default/non-None value, the player lost
                var pendingLossStr = pendingLoss?.ToString() ?? "";
                if (!string.IsNullOrEmpty(pendingLossStr) && pendingLossStr != "None" && pendingLossStr != "0")
                    won = 0;
            }
            catch { }

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
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] CombatEndCapturePatch error: {ex.Message}");
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
            foreach (var name in new[] { "TurnNumber", "Turn", "CurrentTurn", "_turnNumber" })
            {
                if (turnNumber != 0) break;
                try { turnNumber = Traverse.Create(state).Property(name).GetValue<int>(); } catch { }
                if (turnNumber == 0)
                    try { turnNumber = Traverse.Create(state).Field(name).GetValue<int>(); } catch { }
            }
            // Also try on CombatManager directly
            if (turnNumber == 0)
            {
                foreach (var name in new[] { "TurnNumber", "Turn", "CurrentTurn", "_turnNumber" })
                {
                    if (turnNumber != 0) break;
                    try { turnNumber = Traverse.Create(cm).Property(name).GetValue<int>(); } catch { }
                    if (turnNumber == 0)
                        try { turnNumber = Traverse.Create(cm).Field(name).GetValue<int>(); } catch { }
                }
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

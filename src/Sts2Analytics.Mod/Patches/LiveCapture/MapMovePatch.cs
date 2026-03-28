using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures MapChoice via MoveToMapCoordAction.GoToMapCoord (postfix, string-based because async).
/// Reads _destination field (MapCoord) from the action, looks up MapPoint from RunState.Map,
/// and extracts the PointType.
/// Id1 = MapPointType string, Kind = MapChoice.
/// </summary>
[HarmonyPatch(typeof(MoveToMapCoordAction), "GoToMapCoord")]
public static class MapMoveCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(MoveToMapCoordAction __instance)
    {
        if (!LiveRunDb.IsInitialized) return;

        try
        {
            // Get destination MapCoord from private field
            var destination = Traverse.Create(__instance).Field("_destination").GetValue<MapCoord>();

            // Get RunState to look up the map point
            var runManager = RunManager.Instance;
            var state = Traverse.Create(runManager).Property("State").GetValue<RunState>();
            if (state == null) return;

            var actIndex = state.CurrentActIndex;
            var floorIndex = 0;
            try { floorIndex = Traverse.Create(state).Property("CurrentFloorIndex").GetValue<int>(); } catch { }

            // Look up map point type
            string pointTypeStr = "Unknown";
            try
            {
                var map = state.Map;
                if (map != null)
                {
                    var mapPoint = map.GetPoint(destination);
                    if (mapPoint != null)
                        pointTypeStr = mapPoint.PointType.ToString();
                }
            }
            catch { }

            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.MapChoice,
                Id1: pointTypeStr,
                Id2: null,
                Amount: 0,
                ActIndex: actIndex,
                FloorIndex: floorIndex,
                Detail: null
            ));

            DebugLogOverlay.Log($"[SpireOracle] MapChoice: type={pointTypeStr} act={actIndex} floor={floorIndex}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] MapMoveCapturePatch error: {ex.Message}");
        }
    }
}

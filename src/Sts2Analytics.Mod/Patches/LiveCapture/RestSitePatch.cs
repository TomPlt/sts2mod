using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.RestSite;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures rest site choices by patching concrete RestSiteOption subclasses.
/// Cannot patch the abstract base OnSelect — must patch each concrete implementation.
/// </summary>
public static class RestSitePatch
{
    public static void Apply(Harmony harmony)
    {
        var baseType = typeof(RestSiteOption);
        var assembly = baseType.Assembly;
        var prefix = new HarmonyMethod(typeof(RestSitePatch), nameof(OnSelectPrefix));
        var count = 0;

        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsAbstract && baseType.IsAssignableFrom(type))
            {
                try
                {
                    var method = type.GetMethod("OnSelect",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (method != null)
                    {
                        harmony.Patch(method, prefix: prefix);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch {type.Name}.OnSelect: {ex.Message}");
                }
            }
        }
        DebugLogOverlay.Log($"[SpireOracle] Patched {count} RestSiteOption.OnSelect implementations");
    }

    public static void OnSelectPrefix(object __instance)
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            var typeName = __instance.GetType().Name;
            var choice = typeName
                .Replace("RestSiteOption", "")
                .ToUpperInvariant();
            if (string.IsNullOrEmpty(choice)) choice = typeName;

            var (actIndex, floorIndex) = CardPlayedCapturePatch.GetRunPosition();
            LiveRunDb.Enqueue(new DbAction(
                Kind: DbActionKind.RewardDecision,
                Id1: choice, Id2: null,
                Amount: 1,
                ActIndex: actIndex, FloorIndex: floorIndex,
                Detail: "REST_SITE"
            ));
            DebugLogOverlay.Log($"[SpireOracle] REST_SITE: {choice}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] RestSitePatch error: {ex.Message}");
        }
    }
}

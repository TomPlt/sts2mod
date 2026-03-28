using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.RestSite;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures rest site choices by patching RestSiteOption.OnSelect (base class).
/// Each subclass (HealRestSiteOption, SmithRestSiteOption, etc.) calls base.OnSelect.
/// </summary>
[HarmonyPatch(typeof(RestSiteOption), "OnSelect")]
public static class RestSitePatch
{
    [HarmonyPrefix]
    public static void Prefix(RestSiteOption __instance)
    {
        if (!LiveRunDb.IsInitialized) return;
        try
        {
            // The subclass name tells us the choice: HealRestSiteOption -> HEAL, SmithRestSiteOption -> SMITH, etc.
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

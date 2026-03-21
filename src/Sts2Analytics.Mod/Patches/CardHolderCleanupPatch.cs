using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using SpireOracle.UI;

namespace SpireOracle.Patches;

/// <summary>
/// Cleans up SpireOracle overlays when a card holder gets reassigned to a new card.
/// Prevents stale rating badges from appearing in the deck viewer or other screens
/// that reuse NGridCardHolder instances.
/// </summary>
[HarmonyPatch]
public static class CardHolderCleanupPatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(NGridCardHolder), "SetCard");

    [HarmonyPrefix]
    public static void Prefix(NGridCardHolder __instance)
    {
        OverlayFactory.RemoveOverlay(__instance);
    }
}

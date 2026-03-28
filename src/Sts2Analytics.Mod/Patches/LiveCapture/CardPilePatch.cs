using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches.LiveCapture;

/// <summary>
/// Captures card draw events via CardPileCmd.Draw.
/// Since the card identity isn't available at draw time (resolved async),
/// we just record that a draw event occurred.
/// </summary>
public static class CardPilePatch
{
    public static void Apply(Harmony harmony)
    {
        var methods = typeof(CardPileCmd).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var patched = 0;
        foreach (var method in methods)
        {
            try
            {
                if (method.Name == "Draw")
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(CardPilePatch), nameof(DrawPrefix)));
                    patched++;
                }
            }
            catch (Exception ex)
            {
                DebugLogOverlay.LogErr($"[SpireOracle] Failed to patch CardPileCmd.Draw: {ex.Message}");
            }
        }
        if (patched > 0)
            DebugLogOverlay.Log($"[SpireOracle] Patched {patched} CardPileCmd.Draw overloads");
    }

    public static void DrawPrefix()
    {
        if (!LiveRunDb.IsInitialized) return;
        LiveRunDb.Enqueue(new DbAction(
            Kind: DbActionKind.CombatAction,
            Id1: null, Id2: null,
            Amount: 1,
            ActIndex: 0, FloorIndex: 0,
            Detail: "CARD_DRAWN"
        ));
    }
}

using HarmonyLib;
using Godot;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle.Patches;

// TODO: Discover the exact STS2 ancient choice screen class to patch.
// The pattern follows CardRewardPatch — patch the refresh/display method,
// iterate over UI children, look up data via DataLoader.GetAncientChoice(textKey),
// and call OverlayFactory.AddAncientOverlay(holder, stats).
// This requires decompiling STS2 game assemblies to find the target class.
public static class AncientChoicePatch
{
    // Placeholder — actual Harmony patch attributes and target method
    // need to be discovered from the STS2 game code.
}

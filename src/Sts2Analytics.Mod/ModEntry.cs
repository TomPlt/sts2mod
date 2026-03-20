using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SpireOracle.Data;

namespace SpireOracle;

[ModInitializer("Initialize")]
public class ModEntry
{
    private static Harmony? _harmony;
    public static bool OverlayEnabled { get; set; } = true;

    public static void Initialize()
    {
        GD.Print("[SpireOracle] Initializing...");

        // Find our mod path from ModManager
        string? modPath = null;
        foreach (var mod in ModManager.LoadedMods)
        {
            if (mod.manifest?.id == "SpireOracle")
            {
                modPath = mod.path;
                break;
            }
        }

        if (modPath == null)
        {
            GD.PrintErr("[SpireOracle] Could not find mod path!");
            return;
        }

        if (!DataLoader.Load(modPath))
        {
            GD.PrintErr("[SpireOracle] Data loading failed, overlay disabled.");
            return;
        }

        // Apply Harmony patches
        _harmony = new Harmony("com.sts2mod.spireoracle");
        _harmony.PatchAll(typeof(ModEntry).Assembly);
        GD.Print("[SpireOracle] Harmony patches applied.");

        // Add F2 toggle node to scene tree
        var toggle = new UI.OverlayToggle();
        toggle.Name = "SpireOracleToggle";
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred("add_child", toggle);

        GD.Print("[SpireOracle] Ready!");
    }
}

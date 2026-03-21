using System.IO;
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

        // Find our mod path from the DLL location
        var assemblyPath = typeof(ModEntry).Assembly.Location;
        GD.Print($"[SpireOracle] Assembly location: {assemblyPath}");

        string? modPath = null;

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            modPath = Path.GetDirectoryName(assemblyPath);
        }

        // Fallback: search ModManager
        if (modPath == null || !File.Exists(Path.Combine(modPath, "overlay_data.json")))
        {
            foreach (var mod in ModManager.AllMods)
            {
                if (mod.manifest?.id == "SpireOracle")
                {
                    modPath = mod.path;
                    GD.Print($"[SpireOracle] Found mod path via ModManager: {modPath}");
                    break;
                }
            }
        }

        if (modPath == null)
        {
            GD.PrintErr("[SpireOracle] Could not find mod path!");
            return;
        }

        GD.Print($"[SpireOracle] Mod path: {modPath}");

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

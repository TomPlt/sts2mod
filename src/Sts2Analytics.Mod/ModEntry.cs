using System;
using System.IO;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SpireOracle.Data;

namespace SpireOracle;

[ModInitializer("Initialize")]
public class ModEntry
{
    private static Harmony? _harmony;
    private static FileSystemWatcher? _watcher;
    private static System.Threading.Timer? _debounceTimer;
    private static string? _modPath;
    public static bool OverlayEnabled { get; set; } = true;

    public static void Initialize()
    {
        GD.Print("[SpireOracle] Initializing...");

        // Find our mod path from the DLL location
        var assemblyPath = typeof(ModEntry).Assembly.Location;
        GD.Print($"[SpireOracle] Assembly location: {assemblyPath}");

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            _modPath = Path.GetDirectoryName(assemblyPath);
        }

        // Fallback: search ModManager
        if (_modPath == null || !File.Exists(Path.Combine(_modPath, "overlay_data.json")))
        {
            foreach (var mod in ModManager.AllMods)
            {
                if (mod.manifest?.id == "SpireOracle")
                {
                    _modPath = mod.path;
                    GD.Print($"[SpireOracle] Found mod path via ModManager: {_modPath}");
                    break;
                }
            }
        }

        if (_modPath == null)
        {
            GD.PrintErr("[SpireOracle] Could not find mod path!");
            return;
        }

        GD.Print($"[SpireOracle] Mod path: {_modPath}");

        if (!DataLoader.Load(_modPath))
        {
            GD.PrintErr("[SpireOracle] Data loading failed, overlay disabled.");
            return;
        }

        // Watch overlay_data.json for changes and auto-reload
        WatchDataFile(_modPath);

        // Apply Harmony patches
        _harmony = new Harmony("com.sts2mod.spireoracle");
        _harmony.PatchAll(typeof(ModEntry).Assembly);
        GD.Print("[SpireOracle] Harmony patches applied.");

        GD.Print("[SpireOracle] Ready! Press F3 to toggle overlay.");
    }

    private static void WatchDataFile(string modPath)
    {
        try
        {
            _watcher = new FileSystemWatcher(modPath, "overlay_data.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += OnDataFileChanged;
            _watcher.EnableRaisingEvents = true;
            GD.Print("[SpireOracle] Watching overlay_data.json for changes");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SpireOracle] Could not watch data file: {ex.Message}");
        }
    }

    private static void OnDataFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: file writes often trigger multiple events
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (_modPath != null && DataLoader.Load(_modPath))
                    GD.Print("[SpireOracle] Data reloaded after file change");
                else
                    GD.PrintErr("[SpireOracle] Failed to reload data after file change");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SpireOracle] Error reloading data: {ex.Message}");
            }
        }, null, 500, Timeout.Infinite);
    }
}

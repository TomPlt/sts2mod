using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SpireOracle.Data;
using SpireOracle.UI;

namespace SpireOracle;

[ModInitializer("Initialize")]
public class ModEntry
{
    private static Harmony? _harmony;
    private static FileSystemWatcher? _watcher;
    private static System.Threading.Timer? _debounceTimer;
    private static string? _modPath;
    private static string? _nativeLibModPath;
    public static bool OverlayEnabled { get; set; } = true;

    private static IntPtr ResolveNativeLib(Assembly assembly, string libraryName)
    {
        if (_nativeLibModPath == null) return IntPtr.Zero;
        // Try runtimes/win-x64/native/ first
        var nativePath = Path.Combine(_nativeLibModPath, "runtimes", "win-x64", "native", libraryName + ".dll");
        if (File.Exists(nativePath) &&
            System.Runtime.InteropServices.NativeLibrary.TryLoad(nativePath, out var handle))
            return handle;
        // Try mod root
        nativePath = Path.Combine(_nativeLibModPath, libraryName + ".dll");
        if (File.Exists(nativePath) &&
            System.Runtime.InteropServices.NativeLibrary.TryLoad(nativePath, out handle))
            return handle;
        return IntPtr.Zero;
    }

    public static void Initialize()
    {
        // Read version from manifest
        string version = "?";
        foreach (var mod in ModManager.Mods)
        {
            if (mod.manifest?.id == "SpireOracle")
            {
                version = mod.manifest.version ?? "?";
                break;
            }
        }
        DebugLogOverlay.SetVersion(version);
        DebugLogOverlay.Log($"[SpireOracle] v{version} — Initializing...");

        // Find our mod path from the DLL location
        var assemblyPath = typeof(ModEntry).Assembly.Location;
        DebugLogOverlay.Log($"[SpireOracle] Assembly location: {assemblyPath}");

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            _modPath = Path.GetDirectoryName(assemblyPath);
        }

        // Fallback: search ModManager
        if (_modPath == null || !File.Exists(Path.Combine(_modPath, "overlay_data.json")))
        {
            foreach (var mod in ModManager.Mods)
            {
                if (mod.manifest?.id == "SpireOracle")
                {
                    _modPath = mod.path;
                    DebugLogOverlay.Log($"[SpireOracle] Found mod path via ModManager: {_modPath}");
                    break;
                }
            }
        }

        if (_modPath == null)
        {
            DebugLogOverlay.LogErr("[SpireOracle] Could not find mod path!");
            return;
        }

        DebugLogOverlay.Log($"[SpireOracle] Mod path: {_modPath}");

        // Register assembly resolver so the runtime can find SQLite DLLs next to our mod
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (_modPath == null) return null;
            var path = Path.Combine(_modPath, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };

        // Register native library resolver for SQLite (e_sqlite3)
        // This must be set before SQLitePCL is first used
        _nativeLibModPath = _modPath;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNativeLib;

        // Initialize live run capture DB
        LiveRunDb.Initialize(_modPath);

        // Cloud sync: load config and download latest data
        CloudSync.LoadConfig(_modPath);
        if (CloudSync.IsConfigured)
        {
            try
            {
                // Download latest overlay data (fire and forget — don't block startup)
                _ = CloudSync.DownloadLatestData(_modPath).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        DebugLogOverlay.LogErr($"[SpireOracle] Download failed: {t.Exception?.InnerException?.Message}");
                    else
                    {
                        // Reload data after download
                        DataLoader.Load(_modPath);
                        DebugLogOverlay.Log("[SpireOracle] Reloaded data after cloud sync");
                    }
                });
            }
            catch (Exception ex) { DebugLogOverlay.LogErr($"[SpireOracle] Cloud sync error: {ex.Message}"); }
        }

        if (!DataLoader.Load(_modPath))
        {
            DebugLogOverlay.LogErr("[SpireOracle] Data loading failed, overlay disabled.");
            return;
        }

        // Watch overlay_data.json for changes and auto-reload
        WatchDataFile(_modPath);

        // Apply Harmony patches
        _harmony = new Harmony("com.sts2mod.spireoracle");
        _harmony.PatchAll(typeof(ModEntry).Assembly);
        Patches.LiveCapture.CardPlayedCapturePatch.Apply(_harmony);
        DebugLogOverlay.Log("[SpireOracle] Harmony patches applied.");

        // Watch for new .run files to auto-upload
        if (CloudSync.IsConfigured)
            WatchRunFiles();

        DebugLogOverlay.Log("[SpireOracle] Ready! Press F3 to toggle overlay.");
    }

    private static readonly System.Collections.Generic.List<FileSystemWatcher> _runWatchers = new();

    private static void WatchRunFiles()
    {
        try
        {
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var sts2Dir = Path.Combine(appData, "SlayTheSpire2", "steam");
            if (!Directory.Exists(sts2Dir)) return;

            // Watch all profiles across all steam IDs
            foreach (var steamDir in Directory.GetDirectories(sts2Dir))
            {
                foreach (var profileDir in new[] { "modded", "." })
                {
                    var baseDir = Path.Combine(steamDir, profileDir);
                    if (!Directory.Exists(baseDir)) continue;

                    foreach (var profile in Directory.GetDirectories(baseDir, "profile*"))
                    {
                        var historyPath = Path.Combine(profile, "saves", "history");
                        if (!Directory.Exists(historyPath)) continue;

                        var watcher = new FileSystemWatcher(historyPath, "*.run")
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
                                | NotifyFilters.LastWrite | NotifyFilters.Size,
                            InternalBufferSize = 65536
                        };
                        // Track uploaded files to avoid duplicates — use ConcurrentDictionary for thread safety
                        var uploaded = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
                        void OnRunFile(string filePath, string fileName)
                        {
                            if (!uploaded.TryAdd(fileName, 0)) return; // already uploading
                            var timer = new System.Threading.Timer(_ =>
                            {
                                DebugLogOverlay.Log($"[SpireOracle] New run detected: {fileName}");
                                _ = CloudSync.UploadRunFile(filePath).ContinueWith(t =>
                                {
                                    if (t.IsFaulted)
                                    {
                                        uploaded.TryRemove(fileName, out byte _); // allow retry on next event
                                        DebugLogOverlay.LogErr($"[SpireOracle] Upload failed for {fileName}: {t.Exception?.InnerException?.Message}");
                                        return;
                                    }
                                    // Wait for CI to rebuild overlay_data.json, then re-download
                                    ScheduleDataRefresh();
                                });
                            }, null, 5000, System.Threading.Timeout.Infinite);
                        }
                        watcher.Created += (_, e) => OnRunFile(e.FullPath, e.Name ?? "");
                        watcher.Changed += (_, e) => OnRunFile(e.FullPath, e.Name ?? "");
                        watcher.Renamed += (_, e) => OnRunFile(e.FullPath, e.Name ?? "");
                        watcher.Error += (_, e) => DebugLogOverlay.LogErr($"[SpireOracle] FileWatcher error: {e.GetException().Message}");
                        watcher.EnableRaisingEvents = true;
                        _runWatchers.Add(watcher);
                        DebugLogOverlay.Log($"[SpireOracle] Watching for runs: {historyPath}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Could not watch run files: {ex.Message}");
        }
    }

    private static System.Threading.Timer? _refreshTimer;

    private static void ScheduleDataRefresh()
    {
        // CI takes ~3-4 minutes to rebuild. Try at 4min, then retry at 6min if unchanged.
        _refreshTimer?.Dispose();
        DebugLogOverlay.Log("[SpireOracle] Will refresh overlay data in ~4 minutes (waiting for CI)");
        _refreshTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (_modPath != null)
                {
                    DebugLogOverlay.Log("[SpireOracle] Downloading updated overlay data...");
                    await CloudSync.DownloadLatestData(_modPath);
                    // WatchDataFile will auto-reload via file change event
                }
            }
            catch (Exception ex)
            {
                DebugLogOverlay.LogErr($"[SpireOracle] Refresh failed: {ex.Message}");
            }
        }, null, 4 * 60 * 1000, System.Threading.Timeout.Infinite);
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
            DebugLogOverlay.Log("[SpireOracle] Watching overlay_data.json for changes");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Could not watch data file: {ex.Message}");
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
                    DebugLogOverlay.Log("[SpireOracle] Data reloaded after file change");
                else
                    DebugLogOverlay.LogErr("[SpireOracle] Failed to reload data after file change");
            }
            catch (Exception ex)
            {
                DebugLogOverlay.LogErr($"[SpireOracle] Error reloading data: {ex.Message}");
            }
        }, null, 500, Timeout.Infinite);
    }
}

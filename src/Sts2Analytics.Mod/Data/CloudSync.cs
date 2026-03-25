using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using SpireOracle.UI;
using HttpClient = System.Net.Http.HttpClient;
using HttpRequestMessage = System.Net.Http.HttpRequestMessage;
using HttpMethod = System.Net.Http.HttpMethod;
using StringContent = System.Net.Http.StringContent;

namespace SpireOracle.Data;

public record SyncConfig(
    [property: JsonPropertyName("githubToken")] string GithubToken,
    [property: JsonPropertyName("playerName")] string PlayerName);

public static class CloudSync
{
    private const string DataRepo = "TomPlt/spire-oracle-data";
    private const string OverlayUrl = "https://github.com/TomPlt/spire-oracle-data/releases/latest/download/overlay_data.json";

    private static SyncConfig? _config;
    private static HttpClient? _http;

    public static bool IsConfigured => _config != null;

    public static void LoadConfig(string modPath)
    {
        try
        {
            var configPath = Path.Combine(modPath, "config.json");
            if (!File.Exists(configPath))
            {
                DebugLogOverlay.Log("[SpireOracle] No config.json found — cloud sync disabled");
                return;
            }

            var json = File.ReadAllText(configPath);
            _config = JsonSerializer.Deserialize<SyncConfig>(json);

            if (_config == null || string.IsNullOrEmpty(_config.GithubToken))
            {
                DebugLogOverlay.Log("[SpireOracle] config.json missing githubToken — cloud sync disabled");
                _config = null;
                return;
            }

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.GithubToken);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SpireOracle/1.0");

            DebugLogOverlay.Log($"[SpireOracle] Cloud sync configured for player: {_config.PlayerName}");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Failed to load config.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Download the latest overlay_data.json from the data repo release.
    /// </summary>
    public static async Task DownloadLatestData(string modPath)
    {
        if (_http == null || _config == null) return;

        try
        {
            DebugLogOverlay.Log("[SpireOracle] Downloading latest overlay data...");

            // Get the release asset URL via GitHub API
            var releaseUrl = $"https://api.github.com/repos/{DataRepo}/releases/tags/latest";
            var releaseResponse = await _http.GetStringAsync(releaseUrl);
            var release = JsonSerializer.Deserialize<JsonElement>(releaseResponse);

            string? downloadUrl = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == "overlay_data.json")
                {
                    downloadUrl = asset.GetProperty("url").GetString();
                    break;
                }
            }

            if (downloadUrl == null)
            {
                DebugLogOverlay.Log("[SpireOracle] No overlay_data.json found in latest release");
                return;
            }

            // Check if the release is newer than what we have locally
            var outputPath = Path.Combine(modPath, "overlay_data.json");
            var releaseUpdatedAt = release.TryGetProperty("published_at", out var pub)
                ? pub.GetString() : null;
            if (releaseUpdatedAt != null && File.Exists(outputPath))
            {
                var localModified = File.GetLastWriteTimeUtc(outputPath);
                if (DateTime.TryParse(releaseUpdatedAt, out var releaseDate) && releaseDate < localModified)
                {
                    DebugLogOverlay.Log("[SpireOracle] Local overlay data is newer than release, skipping download");
                    return;
                }
            }

            // Download the asset
            var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsStringAsync();
            File.WriteAllText(outputPath, data);

            DebugLogOverlay.Log($"[SpireOracle] Downloaded overlay data ({data.Length / 1024} KB)");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Upload a .run file to the data repo.
    /// Uses GitHub Contents API to create/update the file.
    /// </summary>
    private static readonly System.Threading.SemaphoreSlim _uploadLock = new(1, 1);

    public static async Task UploadRunFile(string runFilePath)
    {
        if (_http == null || _config == null) return;

        // Serialize uploads to avoid SHA conflicts from concurrent GitHub API writes
        await _uploadLock.WaitAsync();
        try
        {
            var fileName = Path.GetFileName(runFilePath);
            var repoPath = $"runs/{_config.PlayerName}/{fileName}";
            var apiUrl = $"https://api.github.com/repos/{DataRepo}/contents/{repoPath}";

            var content = File.ReadAllBytes(runFilePath);
            var base64 = Convert.ToBase64String(content);

            // Check if file already exists (need SHA to update)
            string? existingSha = null;
            try
            {
                var existing = await _http.GetStringAsync(apiUrl);
                var doc = JsonSerializer.Deserialize<JsonElement>(existing);
                existingSha = doc.GetProperty("sha").GetString();
            }
            catch { } // File doesn't exist yet — that's fine

            var body = new
            {
                message = $"Add run {fileName} from {_config.PlayerName}",
                content = base64,
                sha = existingSha
            };

            var json = JsonSerializer.Serialize(body);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PutAsync(apiUrl, httpContent);

            if (response.IsSuccessStatusCode)
            {
                DebugLogOverlay.Log($"[SpireOracle] Uploaded {fileName}");
            }
            else
            {
                var msg = $"Upload failed for {fileName}: {response.StatusCode}";
                DebugLogOverlay.LogErr($"[SpireOracle] {msg}");
                throw new InvalidOperationException(msg);
            }
        }
        catch (InvalidOperationException) { throw; } // re-throw our own errors
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Upload error: {ex.Message}");
            throw;
        }
        finally
        {
            _uploadLock.Release();
        }
    }
}

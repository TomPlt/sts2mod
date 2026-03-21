using System.CommandLine;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class ExportCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var outputOption = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "./sts2analytics-export.json" };
        var modOption = new Option<bool>("--mod") { Description = "Export slimmed-down JSON for SpireOracle overlay mod" };

        var cmd = new Command("export", "Export all analytics to JSON")
        {
            dbOption, outputOption, modOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var output = parseResult.GetValue(outputOption) ?? "./sts2analytics-export.json";
            var isMod = parseResult.GetValue(modOption);

            if (isMod)
            {
                ExportMod(dbPath, output);
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            Console.WriteLine("Computing analytics...");

            // Run summary
            var runAnalytics = new RunAnalytics(conn);
            var summary = runAnalytics.GetOverallWinRate();

            // Card analytics
            var cardAnalytics = new CardAnalytics(conn);
            var cardWinRates = cardAnalytics.GetCardWinRates();
            var cardPickRates = cardAnalytics.GetCardPickRates();

            // Relic analytics
            var relicAnalytics = new RelicAnalytics(conn);
            var relicWinRates = relicAnalytics.GetRelicWinRates();
            var relicPickRates = relicAnalytics.GetRelicPickRates();

            // Glicko-2 ratings
            var g2Count = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
            if (g2Count == 0)
            {
                Console.WriteLine("Processing Glicko-2 ratings...");
                var engine = new Glicko2Engine(conn);
                engine.ProcessAllRuns();
            }
            var g2Analytics = new Glicko2Analytics(conn);
            var glicko2Ratings = g2Analytics.GetRatings();

            // Combat analytics
            var combatAnalytics = new CombatAnalytics(conn);
            var damageByEncounter = combatAnalytics.GetDamageTakenByEncounter();

            // Path analytics
            var pathAnalytics = new PathAnalytics(conn);
            var eliteCorrelation = pathAnalytics.GetEliteCountCorrelation();

            // Runs list
            var runs = conn.Query("SELECT Id, Character, Win, Ascension, Seed, GameMode FROM Runs ORDER BY Id").ToList();
            var runsList = runs.Select(r => new
            {
                id = (long)r.Id,
                character = (string)r.Character,
                win = (long)r.Win != 0,
                ascension = (long)r.Ascension,
                seed = (string)r.Seed,
                gameMode = (string)r.GameMode
            }).ToList();

            var exportData = new
            {
                summary = new
                {
                    summary.TotalRuns,
                    summary.Wins,
                    summary.Losses,
                    summary.WinRate,
                    summary.RunsByCharacter
                },
                cardWinRates,
                cardPickRates,
                relicWinRates,
                relicPickRates,
                glicko2Ratings,
                runs = runsList,
                damageByEncounter,
                eliteCorrelation
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(exportData, options);
            File.WriteAllText(output, json);

            Console.WriteLine($"Exported to: {Path.GetFullPath(output)}");
            Console.WriteLine($"  {cardWinRates.Count} cards, {relicWinRates.Count} relics, {glicko2Ratings.Count} ratings, {runs.Count} runs");
        });

        return cmd;
    }

    private static void ExportMod(string dbPath, string output)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        Console.WriteLine("Exporting mod overlay data...");

        // Ensure Glicko-2 ratings exist
        var g2Count = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
        if (g2Count == 0)
        {
            Console.WriteLine("Processing Glicko-2 ratings...");
            var engine = new Glicko2Engine(conn);
            engine.ProcessAllRuns();
        }

        // Query Skip rating
        var skipElo = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'SKIP' AND Character = 'ALL' AND Context = 'overall'")
            ?? 1500.0;

        // Get analytics data
        var cardAnalytics = new CardAnalytics(conn);
        var cardWinRates = cardAnalytics.GetCardWinRates()
            .ToDictionary(c => c.CardId);
        var cardPickRates = cardAnalytics.GetCardPickRates()
            .ToDictionary(c => c.CardId);

        var g2Analytics = new Glicko2Analytics(conn);
        var eloRatings = g2Analytics.GetRatings()
            .Where(e => e.Context == "overall")
            .ToDictionary(e => e.CardId);

        // Collect all card IDs
        var allCardIds = cardWinRates.Keys
            .Union(cardPickRates.Keys)
            .Union(eloRatings.Keys)
            .Where(id => id != "SKIP")
            .OrderBy(id => id)
            .ToList();

        var cards = allCardIds.Select(id =>
        {
            var elo = eloRatings.TryGetValue(id, out var e) ? e.Rating : 0.0;
            var pickRate = cardPickRates.TryGetValue(id, out var p) ? p.PickRate : 0.0;
            var winPicked = cardWinRates.TryGetValue(id, out var w) ? w.WinRateWhenPicked : 0.0;
            var winSkipped = w?.WinRateWhenSkipped ?? 0.0;
            var delta = w?.WinRateDelta ?? 0.0;
            return new ModCardStats(id, elo, pickRate, winPicked, winSkipped, delta);
        }).ToList();

        var overlayData = new ModOverlayData(
            Version: 1,
            ExportedAt: DateTime.UtcNow.ToString("o"),
            SkipElo: skipElo,
            Cards: cards);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(overlayData, options);
        File.WriteAllText(output, json);

        Console.WriteLine($"Mod overlay data exported to: {Path.GetFullPath(output)}");
        Console.WriteLine($"  {cards.Count} cards, skipElo: {skipElo:F1}");
    }
}

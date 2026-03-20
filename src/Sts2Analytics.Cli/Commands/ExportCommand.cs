using System.CommandLine;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class ExportCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var outputOption = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "./sts2analytics-export.json" };

        var cmd = new Command("export", "Export all analytics to JSON")
        {
            dbOption, outputOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var output = parseResult.GetValue(outputOption) ?? "./sts2analytics-export.json";

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

            // Elo ratings
            var eloCount = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM EloRatings") ?? 0;
            if (eloCount == 0)
            {
                Console.WriteLine("Processing Elo ratings...");
                var engine = new EloEngine(conn);
                engine.ProcessAllRuns();
            }
            var eloAnalytics = new EloAnalytics(conn);
            var eloRatings = eloAnalytics.GetCardEloRatings();

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
                eloRatings,
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
            Console.WriteLine($"  {cardWinRates.Count} cards, {relicWinRates.Count} relics, {eloRatings.Count} Elo ratings, {runs.Count} runs");
        });

        return cmd;
    }
}

using System.CommandLine;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class EloCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var topOption = new Option<int>("--top") { Description = "Number of results", DefaultValueFactory = _ => 20 };
        var characterOption = new Option<string?>("--character") { Description = "Filter by character" };
        var matchupOption = new Option<string[]?>("--matchup") { Description = "Head-to-head: --matchup CARD_A CARD_B", Arity = new ArgumentArity(2, 2) };

        var cmd = new Command("elo", "Show Elo leaderboard or card matchups")
        {
            dbOption, topOption, characterOption, matchupOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var top = parseResult.GetValue(topOption);
            var character = parseResult.GetValue(characterOption);
            var matchup = parseResult.GetValue(matchupOption);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Check if EloRatings table has data; if empty, process all runs first
            var eloCount = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM EloRatings") ?? 0;
            if (eloCount == 0)
            {
                Console.WriteLine("No Elo data found. Processing all runs...");
                var engine = new EloEngine(conn);
                engine.ProcessAllRuns();
                Console.WriteLine("Elo processing complete.");
            }

            var analytics = new EloAnalytics(conn);

            if (matchup is { Length: 2 })
            {
                var result = analytics.GetCardMatchups(matchup[0], matchup[1]);
                Console.WriteLine();
                Console.WriteLine("=== Card Matchup ===");
                Console.WriteLine($"{result.CardA} vs {result.CardB}");
                Console.WriteLine($"  {result.CardA} picked over {result.CardB}: {result.AWinsOverB} times");
                Console.WriteLine($"  {result.CardB} picked over {result.CardA}: {result.BWinsOverA} times");
                var total = result.AWinsOverB + result.BWinsOverA;
                if (total > 0)
                {
                    Console.WriteLine($"  {result.CardA} pick rate in matchup: {(double)result.AWinsOverB / total:P1}");
                }
                Console.WriteLine();
                return;
            }

            var filter = character != null ? new AnalyticsFilter(Character: character) : null;
            var ratings = analytics.GetCardEloRatings(filter);

            // Filter to "overall" context unless character specified
            var context = character ?? "overall";
            var filtered = ratings.Where(r => r.Context == context).OrderByDescending(r => r.Rating).ToList();

            Console.WriteLine();
            Console.WriteLine($"=== Elo Leaderboard ({context}) ===");
            Console.WriteLine($"{"#",-5} {"Card",-35} {"Elo",7} {"Matchups",9}");

            var rank = 1;
            foreach (var rating in filtered.Take(top))
            {
                Console.WriteLine($"{rank,-5} {rating.CardId,-35} {rating.Rating,7:F0} {rating.GamesPlayed,9}");
                rank++;
            }
            Console.WriteLine();
        });

        return cmd;
    }
}

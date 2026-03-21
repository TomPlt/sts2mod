using System.CommandLine;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class RatingCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var topOption = new Option<int>("--top") { Description = "Number of results", DefaultValueFactory = _ => 20 };
        var characterOption = new Option<string?>("--character") { Description = "Filter by character" };
        var actOption = new Option<int?>("--act") { Description = "Filter by act (1, 2, or 3)" };
        var minGamesOption = new Option<int>("--min-games") { Description = "Minimum games played", DefaultValueFactory = _ => 0 };
        var matchupOption = new Option<string[]?>("--matchup") { Description = "Head-to-head: --matchup CARD_A CARD_B", Arity = new ArgumentArity(2, 2) };
        var playerOption = new Option<bool>("--player") { Description = "Show personal player ratings instead of card ratings" };

        var cmd = new Command("elo", "Show Glicko-2 rating leaderboard or card matchups")
        {
            dbOption, topOption, characterOption, actOption, minGamesOption, matchupOption, playerOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var top = parseResult.GetValue(topOption);
            var character = parseResult.GetValue(characterOption);
            var act = parseResult.GetValue(actOption);
            var minGames = parseResult.GetValue(minGamesOption);
            var matchup = parseResult.GetValue(matchupOption);
            var player = parseResult.GetValue(playerOption);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            if (player)
            {
                var playerEngine = new PlayerRatingEngine(conn);
                playerEngine.ProcessAllRuns();

                var playerRatings = conn.Query(
                    "SELECT Context, Rating, RatingDeviation, Volatility, GamesPlayed FROM PlayerRatings ORDER BY Rating DESC")
                    .ToList();

                Console.WriteLine();
                Console.WriteLine("  Player Ratings");
                Console.WriteLine("  ─────────────────────────────────────────────");
                Console.WriteLine($"  {"Context",-15} {"Rating",8} {"±RD",6} {"Games",7}");
                Console.WriteLine("  ─────────────────────────────────────────────");

                foreach (var r in playerRatings)
                {
                    Console.WriteLine($"  {(string)r.Context,-15} {(double)r.Rating,8:F0} {(double)r.RatingDeviation,6:F0} {(long)r.GamesPlayed,7}");
                }

                Console.WriteLine();
                return;
            }

            var ratingCount = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
            if (ratingCount == 0)
            {
                Console.WriteLine("No rating data found. Processing all runs...");
                var engine = new Glicko2Engine(conn);
                engine.ProcessAllRuns();
                Console.WriteLine("Rating processing complete.");
            }

            var analytics = new Glicko2Analytics(conn);

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
                    Console.WriteLine($"  {result.CardA} pick rate in matchup: {(double)result.AWinsOverB / total:P1}");
                Console.WriteLine();
                return;
            }

            var filter = character != null ? new AnalyticsFilter(Character: character) : null;
            var ratings = analytics.GetRatings(filter);

            string context;
            if (act is not null && character is not null)
                context = $"{character}_ACT{act}";
            else if (character is not null)
                context = character;
            else
                context = "overall";

            var filtered = ratings
                .Where(r => r.Context == context)
                .Where(r => r.GamesPlayed >= minGames)
                .OrderByDescending(r => r.Rating)
                .ToList();

            Console.WriteLine();
            Console.WriteLine($"=== Rating Leaderboard ({context}) ===");
            Console.WriteLine($"{"#",-5} {"Card",-35} {"Rating",7} {"±",5} {"Games",6} {"Trend",5}");

            var ratingIds = conn.Query<(long Id, string CardId, string Context)>(
                "SELECT Id, CardId, Context FROM Glicko2Ratings WHERE Context = @Context",
                new { Context = context }).ToDictionary(r => r.CardId, r => r.Id);

            var rank = 1;
            foreach (var rating in filtered.Take(top))
            {
                var trend = ratingIds.TryGetValue(rating.CardId, out var ratingId)
                    ? analytics.GetTrend(ratingId)
                    : 0;
                var trendChar = trend switch { 1 => "▲", -1 => "▼", _ => "─" };
                var rd = rating.RatingDeviation;

                Console.WriteLine($"{rank,-5} {rating.CardId,-35} {rating.Rating,7:F0} {rd,5:F0} {rating.GamesPlayed,6} {trendChar,5}");
                rank++;
            }
            Console.WriteLine();
        });

        return cmd;
    }
}

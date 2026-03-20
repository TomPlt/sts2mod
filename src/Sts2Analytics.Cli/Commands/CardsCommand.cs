using System.CommandLine;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class CardsCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var sortOption = new Option<string>("--sort") { Description = "Sort by: winrate, pickrate, impact", DefaultValueFactory = _ => "impact" };
        var topOption = new Option<int>("--top") { Description = "Number of results", DefaultValueFactory = _ => 20 };
        var characterOption = new Option<string?>("--character") { Description = "Filter by character" };

        var cmd = new Command("cards", "Show card analytics")
        {
            dbOption, sortOption, topOption, characterOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var sort = parseResult.GetValue(sortOption) ?? "impact";
            var top = parseResult.GetValue(topOption);
            var character = parseResult.GetValue(characterOption);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            var filter = character != null ? new AnalyticsFilter(Character: character) : null;
            var analytics = new CardAnalytics(conn);

            var winRates = analytics.GetCardWinRates(filter);
            var pickRates = analytics.GetCardPickRates(filter);

            var pickRateMap = pickRates.ToDictionary(p => p.CardId, p => p);

            var joined = winRates
                .Where(wr => pickRateMap.ContainsKey(wr.CardId))
                .Select(wr =>
                {
                    var pr = pickRateMap[wr.CardId];
                    return new
                    {
                        wr.CardId,
                        PickRate = pr.PickRate,
                        WinPick = wr.WinRateWhenPicked,
                        WinSkip = wr.WinRateWhenSkipped,
                        Delta = wr.WinRateDelta,
                        Impact = pr.PickRate * Math.Abs(wr.WinRateDelta),
                        Offered = pr.TimesOffered
                    };
                })
                .ToList();

            joined = sort.ToLowerInvariant() switch
            {
                "winrate" => joined.OrderByDescending(x => x.WinPick).ToList(),
                "pickrate" => joined.OrderByDescending(x => x.PickRate).ToList(),
                _ => joined.OrderByDescending(x => x.Impact).ToList()
            };

            Console.WriteLine();
            Console.WriteLine("=== Card Analytics ===");
            Console.WriteLine($"{"Card",-35} {"Pick%",7} {"Win(pick)",10} {"Win(skip)",10} {"Delta",8} {"Offered",8}");

            foreach (var card in joined.Take(top))
            {
                var delta = card.Delta >= 0 ? $"+{card.Delta:P1}" : $"{card.Delta:P1}";
                Console.WriteLine($"{card.CardId,-35} {card.PickRate,7:P1} {card.WinPick,10:P1} {card.WinSkip,10:P1} {delta,8} {card.Offered,8}");
            }
            Console.WriteLine();
        });

        return cmd;
    }
}

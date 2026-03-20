using System.CommandLine;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class RelicsCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var sortOption = new Option<string>("--sort") { Description = "Sort by: winrate, pickrate", DefaultValueFactory = _ => "winrate" };
        var topOption = new Option<int>("--top") { Description = "Number of results", DefaultValueFactory = _ => 20 };
        var characterOption = new Option<string?>("--character") { Description = "Filter by character" };

        var cmd = new Command("relics", "Show relic analytics")
        {
            dbOption, sortOption, topOption, characterOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var sort = parseResult.GetValue(sortOption) ?? "winrate";
            var top = parseResult.GetValue(topOption);
            var character = parseResult.GetValue(characterOption);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            var filter = character != null ? new AnalyticsFilter(Character: character) : null;
            var analytics = new RelicAnalytics(conn);

            var winRates = analytics.GetRelicWinRates(filter);
            var pickRates = analytics.GetRelicPickRates(filter);

            var pickRateMap = pickRates.ToDictionary(p => p.RelicId, p => p);

            var joined = winRates
                .Where(wr => pickRateMap.ContainsKey(wr.RelicId))
                .Select(wr =>
                {
                    var pr = pickRateMap[wr.RelicId];
                    return new
                    {
                        wr.RelicId,
                        PickRate = pr.PickRate,
                        WinPick = wr.WinRateWhenPicked,
                        WinSkip = wr.WinRateWhenSkipped,
                        Offered = pr.TimesOffered
                    };
                })
                .ToList();

            joined = sort.ToLowerInvariant() switch
            {
                "pickrate" => joined.OrderByDescending(x => x.PickRate).ToList(),
                _ => joined.OrderByDescending(x => x.WinPick).ToList()
            };

            Console.WriteLine();
            Console.WriteLine("=== Relic Analytics ===");
            Console.WriteLine($"{"Relic",-40} {"Pick%",7} {"Win(pick)",10} {"Win(skip)",10} {"Offered",8}");

            foreach (var relic in joined.Take(top))
            {
                Console.WriteLine($"{relic.RelicId,-40} {relic.PickRate,7:P1} {relic.WinPick,10:P1} {relic.WinSkip,10:P1} {relic.Offered,8}");
            }
            Console.WriteLine();
        });

        return cmd;
    }
}

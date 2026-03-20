using System.CommandLine;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class StatsCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var characterOption = new Option<string?>("--character") { Description = "Filter by character" };
        var ascensionOption = new Option<int?>("--ascension") { Description = "Filter by ascension level" };

        var cmd = new Command("stats", "Show overall run statistics")
        {
            dbOption, characterOption, ascensionOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var character = parseResult.GetValue(characterOption);
            var ascension = parseResult.GetValue(ascensionOption);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            var filter = (character != null || ascension != null)
                ? new AnalyticsFilter(Character: character, AscensionMin: ascension, AscensionMax: ascension)
                : null;

            var analytics = new RunAnalytics(conn);
            var summary = analytics.GetOverallWinRate(filter);

            Console.WriteLine();
            Console.WriteLine("=== STS2 Run Statistics ===");
            Console.WriteLine($"Total Runs: {summary.TotalRuns}  |  Wins: {summary.Wins}  |  Losses: {summary.Losses}  |  Win Rate: {summary.WinRate:P1}");
            Console.WriteLine();

            if (summary.RunsByCharacter.Count > 0)
            {
                Console.WriteLine("By Character:");

                // Get per-character win rates
                foreach (var (charName, runCount) in summary.RunsByCharacter.OrderByDescending(kv => kv.Value))
                {
                    var charFilter = new AnalyticsFilter(Character: charName, AscensionMin: ascension, AscensionMax: ascension);
                    var charSummary = analytics.GetOverallWinRate(charFilter);
                    Console.WriteLine($"  {charName,-25} {runCount} runs  {charSummary.WinRate:P1} win rate");
                }
                Console.WriteLine();
            }
        });

        return cmd;
    }
}

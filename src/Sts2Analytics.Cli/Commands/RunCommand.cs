using System.CommandLine;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class RunCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var idArg = new Argument<string>("id-or-seed")
        {
            Description = "Run ID (number) or seed string"
        };

        var cmd = new Command("run", "Show details for a single run")
        {
            idArg, dbOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var idOrSeed = parseResult.GetValue(idArg);

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Try to find run by ID or seed
            dynamic? run;
            if (long.TryParse(idOrSeed, out var runId))
            {
                run = conn.QueryFirstOrDefault("SELECT * FROM Runs WHERE Id = @Id", new { Id = runId });
            }
            else
            {
                run = conn.QueryFirstOrDefault("SELECT * FROM Runs WHERE Seed = @Seed", new { Seed = idOrSeed });
            }

            if (run is null)
            {
                Console.WriteLine($"Run not found: {idOrSeed}");
                return;
            }

            long id = (long)run.Id;
            Console.WriteLine();
            Console.WriteLine("=== Run Details ===");
            Console.WriteLine($"ID: {id}  |  Seed: {run.Seed}  |  Character: {run.Character}");
            Console.WriteLine($"Ascension: {run.Ascension}  |  Result: {((long)run.Win != 0 ? "WIN" : "LOSS")}  |  Mode: {run.GameMode}");
            if ((long)run.Win == 0 && !string.IsNullOrEmpty((string)run.KilledByEncounter))
            {
                Console.WriteLine($"Killed by: {run.KilledByEncounter}");
            }
            Console.WriteLine();

            // Get floors
            var floors = conn.Query(
                "SELECT * FROM Floors WHERE RunId = @RunId ORDER BY ActIndex, FloorIndex",
                new { RunId = id }).ToList();

            Console.WriteLine($"{"Floor",-7} {"Act",4} {"Type",-12} {"Encounter",-30} {"HP",10} {"Gold",6}");
            Console.WriteLine(new string('-', 75));

            foreach (var floor in floors)
            {
                var floorIndex = (long)floor.FloorIndex;
                var actIndex = (long)floor.ActIndex;
                var mapType = (string)(floor.MapPointType ?? "");
                var encounter = (string)(floor.EncounterId ?? "");
                var hp = $"{(long)floor.CurrentHp}/{(long)floor.MaxHp}";
                var gold = (long)floor.CurrentGold;

                // Check for card picks on this floor
                var cardPicks = conn.Query(
                    "SELECT CardId FROM CardChoices WHERE FloorId = @FloorId AND WasPicked = 1",
                    new { FloorId = (long)floor.Id }).ToList();
                var picked = cardPicks.Count > 0
                    ? string.Join(", ", cardPicks.Select(c => (string)c.CardId))
                    : "";

                var encounterDisplay = !string.IsNullOrEmpty(encounter) ? encounter : mapType;
                Console.WriteLine($"{floorIndex,-7} {actIndex,4} {mapType,-12} {encounterDisplay,-30} {hp,10} {gold,6}");
                if (!string.IsNullOrEmpty(picked))
                {
                    Console.WriteLine($"{"",7} {"",4} {"",12}   -> Picked: {picked}");
                }
            }
            Console.WriteLine();
        });

        return cmd;
    }
}

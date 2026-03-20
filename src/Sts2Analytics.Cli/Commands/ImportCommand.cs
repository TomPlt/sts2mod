using System.CommandLine;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class ImportCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string?>("path")
        {
            Description = "Path to .run files directory (auto-detects if omitted)",
            DefaultValueFactory = _ => null,
            Arity = ArgumentArity.ZeroOrOne
        };
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var cmd = new Command("import", "Import .run files into database")
        {
            pathArg, dbOption
        };

        cmd.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArg);
            var dbPath = parseResult.GetValue(dbOption);

            dbPath ??= SavePathDetector.GetDefaultDbPath();
            var dirs = path != null
                ? new List<string> { path }
                : SavePathDetector.FindHistoryDirectories();

            if (dirs.Count == 0)
            {
                Console.WriteLine("No STS2 save directories found. Specify a path.");
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            Schema.Initialize(conn);
            var repo = new RunRepository(conn);

            var imported = 0;
            var skipped = 0;
            foreach (var dir in dirs)
            {
                Console.WriteLine($"Scanning: {dir}");
                foreach (var file in Directory.GetFiles(dir, "*.run"))
                {
                    var fileName = Path.GetFileName(file);
                    try
                    {
                        var runFile = RunFileParser.Parse(file);
                        var (run, floors, floorData) = RunFileMapper.Map(runFile, fileName);
                        var id = repo.ImportRun(run, floors, floorData, runFile.Players[0]);
                        if (id > 0) imported++; else skipped++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error parsing {fileName}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"Done: {imported} imported, {skipped} already in database.");
        });

        return cmd;
    }
}

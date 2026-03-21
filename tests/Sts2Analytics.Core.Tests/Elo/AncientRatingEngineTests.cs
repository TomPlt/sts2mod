using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class AncientRatingEngineTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportSampleRun(SqliteConnection conn, string fixture = "sample_loss.run")
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, fixture);
        return repo.ImportRun(run, floors, floorData, runFile.Players[0]);
    }

    [Fact]
    public void ProcessAllRuns_CreatesAncientRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_loss.run");
        var engine = new AncientRatingEngine(conn);
        engine.ProcessAllRuns();
        var ratings = conn.Query("SELECT * FROM AncientGlicko2Ratings").ToList();
        Assert.True(ratings.Count >= 1);
    }

    [Fact]
    public void ProcessAllRuns_RecordsHistory()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_loss.run");
        var engine = new AncientRatingEngine(conn);
        engine.ProcessAllRuns();
        var history = conn.Query("SELECT * FROM AncientGlicko2History").ToList();
        Assert.True(history.Count >= 1);
    }

    [Fact]
    public void ProcessAllRuns_Creates3ContextsPerChoice()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_loss.run");
        var engine = new AncientRatingEngine(conn);
        engine.ProcessAllRuns();
        var firstChoice = conn.QueryFirst<string>("SELECT DISTINCT ChoiceKey FROM AncientGlicko2Ratings LIMIT 1");
        var contexts = conn.Query("SELECT Character, Context FROM AncientGlicko2Ratings WHERE ChoiceKey = @Key",
            new { Key = firstChoice }).ToList();
        Assert.Equal(3, contexts.Count);
        Assert.Contains(contexts, c => (string)c.Character == "ALL" && (string)c.Context == "overall");
    }

    [Fact]
    public void ProcessAllRuns_Idempotent()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_loss.run");
        var engine = new AncientRatingEngine(conn);
        engine.ProcessAllRuns();
        var countFirst = conn.QueryFirst<long>("SELECT COUNT(*) FROM AncientGlicko2History");
        engine.ProcessAllRuns();
        var countSecond = conn.QueryFirst<long>("SELECT COUNT(*) FROM AncientGlicko2History");
        Assert.Equal(countFirst, countSecond);
    }

    [Fact]
    public void ProcessAllRuns_HandlesPostActAncients()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_win.run");
        var engine = new AncientRatingEngine(conn);
        engine.ProcessAllRuns();
        var contexts = conn.Query<string>("SELECT DISTINCT Context FROM AncientGlicko2Ratings").ToList();
        Assert.Contains("overall", contexts);
    }
}

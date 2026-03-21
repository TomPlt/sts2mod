using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class PlayerRatingEngineTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportSampleRun(SqliteConnection conn, string fixture = "sample_win.run")
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, fixture);
        return repo.ImportRun(run, floors, floorData, runFile.Players[0]);
    }

    [Fact]
    public void ProcessAllRuns_CreatesPlayerRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);

        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();

        var ratings = conn.Query("SELECT * FROM PlayerRatings").ToList();
        Assert.True(ratings.Count >= 2); // at least overall + character
    }

    [Fact]
    public void ProcessAllRuns_RecordsHistory()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();
        var history = conn.Query("SELECT * FROM PlayerRatingHistory").ToList();
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public void ProcessAllRuns_WinIncreasesRating()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_win.run");
        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();
        var history = conn.Query("SELECT RatingBefore, RatingAfter FROM PlayerRatingHistory WHERE Outcome = 1.0").First();
        Assert.True((double)history.RatingAfter > (double)history.RatingBefore);
    }

    [Fact]
    public void ProcessAllRuns_LossDecreasesRating()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn, "sample_loss.run");
        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();
        var history = conn.Query("SELECT RatingBefore, RatingAfter FROM PlayerRatingHistory WHERE Outcome = 0.0").First();
        Assert.True((double)history.RatingAfter < (double)history.RatingBefore);
    }

    [Fact]
    public void ProcessAllRuns_Idempotent()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();
        var countAfterFirst = conn.QueryFirst<long>("SELECT COUNT(*) FROM PlayerRatingHistory");
        engine.ProcessAllRuns();
        var countAfterSecond = conn.QueryFirst<long>("SELECT COUNT(*) FROM PlayerRatingHistory");
        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public void ProcessAllRuns_OpponentRatingScalesWithAscension()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new PlayerRatingEngine(conn);
        engine.ProcessAllRuns();
        var run = conn.QueryFirst("SELECT Ascension FROM Runs LIMIT 1");
        var history = conn.QueryFirst("SELECT OpponentRating FROM PlayerRatingHistory LIMIT 1");
        double expectedOpponent = 1200.0 + (long)run.Ascension * 60.0;
        Assert.Equal(expectedOpponent, (double)history.OpponentRating, 1);
    }
}

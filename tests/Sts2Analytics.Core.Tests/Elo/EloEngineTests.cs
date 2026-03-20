using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class EloEngineTests
{
    [Fact]
    public void ProcessRun_WinningRun_PickedCardsGainElo()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var engine = new EloEngine(conn);
        engine.ProcessRun(runId);

        var rating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM EloRatings WHERE CardId = 'CARD.SETUP_STRIKE' AND Context = 'overall'");
        Assert.NotNull(rating);
    }

    [Fact]
    public void ProcessRun_SkipGetsEloEntry()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var engine = new EloEngine(conn);
        engine.ProcessRun(runId);

        var skipRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM EloRatings WHERE CardId = 'SKIP' AND Context = 'overall'");
        Assert.NotNull(skipRating);
    }
}

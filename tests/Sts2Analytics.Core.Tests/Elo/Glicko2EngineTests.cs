using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class Glicko2EngineTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportSampleRun(SqliteConnection conn)
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        return repo.ImportRun(run, floors, floorData, runFile.Players[0]);
    }

    [Fact]
    public void ProcessRun_CreatesGlicko2Ratings()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2Ratings");
        Assert.True(count > 0);
    }

    [Fact]
    public void ProcessRun_SkipGetsRating()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var skipRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'SKIP' AND Context = 'overall'");
        Assert.NotNull(skipRating);
    }

    [Fact]
    public void ProcessRun_CreatesPerCharacterContext()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);
        var character = conn.QueryFirst<string>("SELECT Character FROM Runs WHERE Id = @Id", new { Id = runId });
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var charRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'SKIP' AND Context = @Character",
            new { Character = character });
        Assert.NotNull(charRating);
    }

    [Fact]
    public void ProcessRun_CreatesActSpecificContext()
    {
        using var conn = CreateDb();
        var runId = ImportSampleRun(conn);
        var character = conn.QueryFirst<string>("SELECT Character FROM Runs WHERE Id = @Id", new { Id = runId });
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var actContextCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(DISTINCT Context) FROM Glicko2Ratings WHERE Context LIKE @Pattern",
            new { Pattern = $"{character}_ACT%" });
        Assert.True(actContextCount > 0);
    }

    [Fact]
    public void ProcessRun_RecordsHistory()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var historyCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2History");
        Assert.True(historyCount > 0);
    }

    [Fact]
    public void ProcessRun_HistoryIncludesRdAndVolatility()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var history = conn.QueryFirst(
            "SELECT RdBefore, RdAfter, VolatilityBefore, VolatilityAfter FROM Glicko2History LIMIT 1");
        Assert.True((double)history.RdBefore > 0);
        Assert.True((double)history.RdAfter > 0);
        Assert.True((double)history.VolatilityBefore > 0);
        Assert.True((double)history.VolatilityAfter > 0);
    }

    [Fact]
    public void ProcessRun_RatingDeviationShrinks()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var minRd = conn.ExecuteScalar<double>(
            "SELECT MIN(RatingDeviation) FROM Glicko2Ratings WHERE GamesPlayed > 0");
        Assert.True(minRd < 350.0);
    }

    [Fact]
    public void ProcessAllRuns_Idempotent()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var countBefore = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2History");
        engine.ProcessAllRuns();
        var countAfter = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Glicko2History");
        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public void ProcessRun_UpgradedCardsGetSeparateEntity()
    {
        using var conn = CreateDb();
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('test', 'seed', 'IRONCLAD', 1, '2026-01-01')");
        var runId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = runId });
        var floorId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.INFLAME', 1, 1)", new { FloorId = floorId });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.BASH', 0, 0)", new { FloorId = floorId });
        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();
        var upgradeRating = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'CARD.INFLAME+1' AND Context = 'overall'");
        Assert.NotNull(upgradeRating);
    }

    [Fact]
    public void ProcessRun_TemporalDecay_RdGrowsBetweenRuns()
    {
        using var conn = CreateDb();
        // Run 1: card A picked over card B
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('run1', 's1', 'IRONCLAD', 1, '2026-01-01')");
        var run1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = run1Id });
        var floor1Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.AAA', 1, 0)", new { FloorId = floor1Id });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.BBB', 0, 0)", new { FloorId = floor1Id });

        // Run 2: different cards (AAA is absent)
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('run2', 's2', 'IRONCLAD', 1, '2026-01-02')");
        var run2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = run2Id });
        var floor2Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.CCC', 1, 0)", new { FloorId = floor2Id });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.DDD', 0, 0)", new { FloorId = floor2Id });

        // Run 3: AAA appears again
        conn.Execute("INSERT INTO Runs (FileName, Seed, Character, Win, StartTime) VALUES ('run3', 's3', 'IRONCLAD', 1, '2026-01-03')");
        var run3Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType) VALUES (@RunId, 0, 1, 'monster')", new { RunId = run3Id });
        var floor3Id = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.AAA', 1, 0)", new { FloorId = floor3Id });
        conn.Execute("INSERT INTO CardChoices (FloorId, CardId, WasPicked, UpgradeLevel) VALUES (@FloorId, 'CARD.EEE', 0, 0)", new { FloorId = floor3Id });

        var engine = new Glicko2Engine(conn);
        engine.ProcessAllRuns();

        var history = conn.Query<(double RdBefore, double RdAfter, long RunId)>("""
            SELECT gh.RdBefore, gh.RdAfter, gh.RunId FROM Glicko2History gh
            JOIN Glicko2Ratings gr ON gh.Glicko2RatingId = gr.Id
            WHERE gr.CardId = 'CARD.AAA' AND gr.Context = 'overall'
            ORDER BY gh.RunId
            """).ToList();

        Assert.Equal(2, history.Count);
        var rdAfterRun1 = history[0].RdAfter;
        var rdBeforeRun3 = history[1].RdBefore;
        Assert.True(rdBeforeRun3 > rdAfterRun1, "RD should grow due to inactivity decay");
    }
}

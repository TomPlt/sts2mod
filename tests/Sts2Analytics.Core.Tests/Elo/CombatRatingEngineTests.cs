using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class CombatRatingEngineTests
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
    public void ProcessAllRuns_CreatesCombatRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2Ratings");
        Assert.True(count > 0, $"Expected combat ratings to be created, but count was {count}");
    }

    [Fact]
    public void ProcessAllRuns_CreatesPoolEntityRatings()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();
        var poolCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM CombatGlicko2Ratings WHERE CardId LIKE 'POOL.%'");
        Assert.True(poolCount > 0, $"Expected pool entity ratings, but count was {poolCount}");
    }

    [Fact]
    public void ProcessAllRuns_RatingsDeviateFromDefault()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();
        var nonDefaultCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM CombatGlicko2Ratings WHERE ABS(Rating - 1500.0) > 0.01");
        Assert.True(nonDefaultCount > 0,
            $"Expected some ratings to deviate from 1500, but none did");
    }

    [Fact]
    public void ProcessAllRuns_HistoryIncludesFloorId()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();

        // All FloorIds in history should reference combat floors (floors with EncounterId)
        var invalidFloors = conn.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM CombatGlicko2History ch
            WHERE NOT EXISTS (
                SELECT 1 FROM Floors f
                WHERE f.Id = ch.FloorId AND f.EncounterId IS NOT NULL
            )
            """);
        Assert.Equal(0, invalidFloors);

        var historyCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2History");
        Assert.True(historyCount > 0, "Expected history entries to exist");
    }

    [Fact]
    public void ProcessAllRuns_Idempotent()
    {
        using var conn = CreateDb();
        ImportSampleRun(conn);
        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();
        var countBefore = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2History");
        engine.ProcessAllRuns();
        var countAfter = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CombatGlicko2History");
        Assert.Equal(countBefore, countAfter);
    }

    [Theory]
    [InlineData("ENCOUNTER.SHRINKER_BEETLE_WEAK", 0, "act1_weak")]
    [InlineData("ENCOUNTER.CUBEX_CONSTRUCT_NORMAL", 0, "act1_normal")]
    [InlineData("ENCOUNTER.BYGONE_EFFIGY_ELITE", 1, "act2_elite")]
    [InlineData("ENCOUNTER.CEREMONIAL_BEAST_BOSS", 2, "act3_boss")]
    [InlineData("ENCOUNTER.SOMETHING_WEAK", 2, "act3_weak")]
    [InlineData("ENCOUNTER.UNKNOWN_SUFFIX", 0, null)]
    [InlineData("ENCOUNTER.NO_SUFFIX", 1, null)]
    public void DerivePoolContext_ReturnsExpectedContext(string encounterId, int actIndex, string? expected)
    {
        var result = CombatRatingEngine.DerivePoolContext(encounterId, actIndex);
        Assert.Equal(expected, result);
    }
}

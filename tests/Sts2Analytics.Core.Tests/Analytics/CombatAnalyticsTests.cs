using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class CombatAnalyticsTests
{
    private SqliteConnection SetupDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        return conn;
    }

    [Fact]
    public void GetDamageTakenByEncounter_ReturnsEncounters()
    {
        using var conn = SetupDb();
        var analytics = new CombatAnalytics(conn);
        var results = analytics.GetDamageTakenByEncounter();
        Assert.True(results.Count > 0);
        Assert.All(results, r => Assert.True(r.SampleSize > 0));
    }

    [Fact]
    public void GetDeathFloorDistribution_WinningRunReturnsEmpty()
    {
        using var conn = SetupDb();
        var analytics = new CombatAnalytics(conn);
        var results = analytics.GetDeathFloorDistribution();
        Assert.Empty(results);
    }
}

using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class RunAnalyticsTests
{
    [Fact]
    public void GetOverallWinRate_WithOneWinningRun_Returns100Percent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var analytics = new RunAnalytics(conn);
        var summary = analytics.GetOverallWinRate();

        Assert.Equal(1, summary.TotalRuns);
        Assert.Equal(1, summary.Wins);
        Assert.Equal(1.0, summary.WinRate);
    }
}

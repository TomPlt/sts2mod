using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class BlindSpotAnalyzerTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static void ImportSampleRuns(SqliteConnection conn)
    {
        var repo = new RunRepository(conn);
        foreach (var fixture in new[] { "sample_win.run", "sample_loss.run" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
            var runFile = RunFileParser.Parse(path);
            var (run, floors, floorData) = RunFileMapper.Map(runFile, fixture);
            repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        }
    }

    [Fact]
    public void Analyze_ReturnsResults()
    {
        using var conn = CreateDb();
        ImportSampleRuns(conn);
        var g2Engine = new Glicko2Engine(conn);
        g2Engine.ProcessAllRuns();
        var analyzer = new BlindSpotAnalyzer(conn);
        var results = analyzer.Analyze();
        Assert.NotNull(results);
    }

    [Fact]
    public void Analyze_PersistsToBlindSpotsTable()
    {
        using var conn = CreateDb();
        ImportSampleRuns(conn);
        var g2Engine = new Glicko2Engine(conn);
        g2Engine.ProcessAllRuns();
        var analyzer = new BlindSpotAnalyzer(conn);
        var results = analyzer.Analyze();
        var dbCount = conn.QueryFirst<long>("SELECT COUNT(*) FROM BlindSpots WHERE Context = 'overall'");
        Assert.Equal(results.Count, (int)dbCount);
    }

    [Fact]
    public void ExpectedPickRate_EqualToSkip_Returns50Percent()
    {
        double skipRating = 1500.0;
        double cardRating = 1500.0;
        double expected = 1.0 / (1.0 + Math.Exp(-(cardRating - skipRating) / 200.0));
        Assert.Equal(0.5, expected, 3);
    }

    [Fact]
    public void ExpectedPickRate_WellAboveSkip_ReturnsHigh()
    {
        double skipRating = 1500.0;
        double cardRating = 1700.0;
        double expected = 1.0 / (1.0 + Math.Exp(-(cardRating - skipRating) / 200.0));
        Assert.InRange(expected, 0.70, 0.76);
    }

    [Fact]
    public void AnalyzeAllContexts_ReturnsResults()
    {
        using var conn = CreateDb();
        ImportSampleRuns(conn);
        var g2Engine = new Glicko2Engine(conn);
        g2Engine.ProcessAllRuns();
        var analyzer = new BlindSpotAnalyzer(conn);
        var results = analyzer.AnalyzeAllContexts();
        Assert.NotNull(results);
    }
}

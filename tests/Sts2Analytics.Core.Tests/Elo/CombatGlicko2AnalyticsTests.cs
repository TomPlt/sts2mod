using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class CombatGlicko2AnalyticsTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    private static long ImportAndProcess(SqliteConnection conn)
    {
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        var engine = new CombatRatingEngine(conn);
        engine.ProcessAllRuns();

        return runId;
    }

    [Fact]
    public void GetRatings_ReturnsCardCombatRatings()
    {
        using var conn = CreateDb();
        ImportAndProcess(conn);

        var analytics = new CombatGlicko2Analytics(conn);
        var ratings = analytics.GetRatings();

        Assert.NotEmpty(ratings);
        Assert.True(ratings.Any(r => r.CardId.StartsWith("CARD.")),
            "Expected at least one rating with CardId starting with 'CARD.'");
    }

    [Fact]
    public void ComputeDeckElo_WeightsByRd()
    {
        // Card with RD=100 (confident) at 1600, card with RD=300 (uncertain) at 1400.
        // The result should be closer to 1600 than to the midpoint 1500.
        var cardRatings = new List<(double Rating, double Rd)>
        {
            (1600.0, 100.0),
            (1400.0, 300.0),
        };

        var (mu, rd) = CombatGlicko2Analytics.ComputeDeckElo(cardRatings);

        Assert.True(mu > 1500.0, $"Expected deck Elo closer to 1600 but got {mu}");
        Assert.True(rd > 0, "Expected positive RD");
    }

    [Fact]
    public void ComputeDeckElo_DefaultForEmptyDeck()
    {
        var (mu, rd) = CombatGlicko2Analytics.ComputeDeckElo(Enumerable.Empty<(double, double)>());

        Assert.Equal(1500.0, mu);
        Assert.Equal(350.0, rd);
    }

    [Fact]
    public void ComputeDeckElo_HighRdCardsContributeLittle()
    {
        // One confident card at 1600 (RD=50) and many uncertain cards at 1000 (RD=340).
        // Result should be pulled toward the confident card (i.e., significantly above midpoint).
        var cardRatings = new List<(double Rating, double Rd)>
        {
            (1600.0, 50.0),
            (1000.0, 340.0),
            (1000.0, 340.0),
            (1000.0, 340.0),
            (1000.0, 340.0),
            (1000.0, 340.0),
        };

        var (mu, rd) = CombatGlicko2Analytics.ComputeDeckElo(cardRatings);

        // Weight of confident card = 1/50 = 0.02
        // Weight of each uncertain card = 1/340 ≈ 0.00294, total ≈ 0.0147
        // Simple average of all 6 cards = (1600 + 5*1000) / 6 ≈ 1083
        // Weighted result ≈ 1346, pulled well above unweighted midpoint toward the confident card
        var unweightedMean = (1600.0 + 5 * 1000.0) / 6.0;
        Assert.True(mu > unweightedMean,
            $"Expected deck Elo ({mu}) to be above unweighted mean ({unweightedMean}), pulled toward confident card");
    }
}

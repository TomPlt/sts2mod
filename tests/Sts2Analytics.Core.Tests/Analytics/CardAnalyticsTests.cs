using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Analytics;

public class CardAnalyticsTests
{
    private SqliteConnection SetupDbWithRun(string fixtureName)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, fixtureName);
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        return conn;
    }

    [Fact]
    public void GetCardWinRates_ReturnsRatesForPickedCards()
    {
        using var conn = SetupDbWithRun("sample_win.run");
        var analytics = new CardAnalytics(conn);
        var rates = analytics.GetCardWinRates();
        Assert.True(rates.Count > 0);
        var setupStrike = rates.FirstOrDefault(r => r.CardId == "CARD.SETUP_STRIKE");
        Assert.NotNull(setupStrike);
        Assert.True(setupStrike.TimesPicked >= 1);
    }

    [Fact]
    public void GetCardPickRates_ReturnsRatesForOfferedCards()
    {
        using var conn = SetupDbWithRun("sample_win.run");
        var analytics = new CardAnalytics(conn);
        var rates = analytics.GetCardPickRates();
        Assert.True(rates.Count > 0);
        var anyCard = rates.First();
        Assert.True(anyCard.TimesOffered > 0);
        Assert.True(anyCard.PickRate >= 0.0 && anyCard.PickRate <= 1.0);
    }
}

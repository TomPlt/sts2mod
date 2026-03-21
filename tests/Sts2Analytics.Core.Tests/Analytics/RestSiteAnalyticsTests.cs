using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Database;

namespace Sts2Analytics.Core.Tests.Analytics;

public class RestSiteAnalyticsTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public RestSiteAnalyticsTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Schema.Initialize(_conn);
        SeedData();
    }

    private void SeedData()
    {
        _conn.Execute("INSERT INTO Runs (Id, FileName, Character, Ascension, Win, Seed, GameMode, StartTime) VALUES (1, 'run1.run', 'CHARACTER.IRONCLAD', 5, 1, 'seed1', 'STANDARD', '2026-01-01')");
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (10, 1, 5, 0, 'rest_site', 40, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (1, 10, 'SMITH')");
        _conn.Execute("INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId) VALUES (1, 'CARD.INFLAME')");
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (11, 1, 12, 1, 'rest_site', 20, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (2, 11, 'HEAL')");

        _conn.Execute("INSERT INTO Runs (Id, FileName, Character, Ascension, Win, Seed, GameMode, StartTime) VALUES (2, 'run2.run', 'CHARACTER.IRONCLAD', 5, 0, 'seed2', 'STANDARD', '2026-01-02')");
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (20, 2, 5, 0, 'rest_site', 60, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (3, 20, 'HEAL')");
        _conn.Execute("INSERT INTO Floors (Id, RunId, FloorIndex, ActIndex, MapPointType, CurrentHp, MaxHp) VALUES (21, 2, 10, 0, 'rest_site', 30, 80)");
        _conn.Execute("INSERT INTO RestSiteChoices (Id, FloorId, Choice) VALUES (4, 21, 'SMITH')");
        _conn.Execute("INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId) VALUES (4, 'CARD.BASH')");
    }

    [Fact]
    public void GetDecisionWinRates_ReturnsCorrectRates()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetDecisionWinRates();

        var smith = results.First(r => r.Choice == "SMITH");
        Assert.Equal(2, smith.Count);
        Assert.Equal(1, smith.Wins);
        Assert.Equal(0.5, smith.WinRate);

        var heal = results.First(r => r.Choice == "HEAL");
        Assert.Equal(2, heal.Count);
        Assert.Equal(1, heal.Wins);
    }

    [Fact]
    public void GetDecisionsByHpBucket_BucketsCorrectly()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetDecisionsByHpBucket();

        var smithMid = results.Where(r => r.Choice == "SMITH" && r.HpBucketMin == 50).ToList();
        Assert.Single(smithMid);
        Assert.Equal(1, smithMid[0].Wins);

        var smithLow = results.Where(r => r.Choice == "SMITH" && r.HpBucketMin == 25).ToList();
        Assert.Single(smithLow);
        Assert.Equal(0, smithLow[0].Wins);
    }

    [Fact]
    public void GetUpgradeImpact_ReturnsCardWinRates()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetUpgradeImpact();

        var inflame = results.First(r => r.CardId == "CARD.INFLAME");
        Assert.Equal(1, inflame.TimesUpgraded);
        Assert.Equal(1, inflame.Wins);
        Assert.Equal(1.0, inflame.WinRate);

        var bash = results.First(r => r.CardId == "CARD.BASH");
        Assert.Equal(1, bash.TimesUpgraded);
        Assert.Equal(0, bash.Wins);
    }

    [Fact]
    public void GetActBreakdown_SplitsByAct()
    {
        var analytics = new RestSiteAnalytics(_conn);
        var results = analytics.GetActBreakdown();

        var act0 = results.Where(r => r.Act == 0).ToList();
        Assert.True(act0.Count >= 2);
    }

    public void Dispose() => _conn.Dispose();
}

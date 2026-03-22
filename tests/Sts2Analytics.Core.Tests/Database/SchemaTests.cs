using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;

namespace Sts2Analytics.Core.Tests.Database;

public class SchemaTests
{
    [Fact]
    public void Initialize_CreatesAllTables()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").ToList();

        Assert.Contains("Runs", tables);
        Assert.Contains("Floors", tables);
        Assert.Contains("CardChoices", tables);
        Assert.Contains("RelicChoices", tables);
        Assert.Contains("PotionChoices", tables);
        Assert.Contains("PotionEvents", tables);
        Assert.Contains("EventChoices", tables);
        Assert.Contains("RestSiteChoices", tables);
        Assert.Contains("RestSiteUpgrades", tables);
        Assert.Contains("CardTransforms", tables);
        Assert.Contains("Monsters", tables);
        Assert.Contains("FinalDecks", tables);
        Assert.Contains("FinalRelics", tables);
        Assert.Contains("FinalPotions", tables);
        Assert.Contains("CardsGained", tables);
        Assert.Contains("CardRemovals", tables);
        Assert.Contains("CardEnchantments", tables);
        Assert.Contains("AncientChoices", tables);
        Assert.Contains("Glicko2Ratings", tables);
        Assert.Contains("Glicko2History", tables);
    }

    [Fact]
    public void Initialize_CreatesPlayerRatingsTables()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);

        var playerRatings = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='PlayerRatings'").ToList();
        Assert.Single(playerRatings);

        var playerRatingHistory = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='PlayerRatingHistory'").ToList();
        Assert.Single(playerRatingHistory);
    }

    [Fact]
    public void Initialize_CreatesBlindSpotsTable()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);

        var blindSpots = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='BlindSpots'").ToList();
        Assert.Single(blindSpots);
    }

    [Fact]
    public void Initialize_CreatesAncientGlicko2Tables()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);

        var ratings = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='AncientGlicko2Ratings'").ToList();
        Assert.Single(ratings);

        var history = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='AncientGlicko2History'").ToList();
        Assert.Single(history);
    }

    [Fact]
    public void Initialize_CreatesCombatGlicko2Tables()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);

        var ratings = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='CombatGlicko2Ratings'").ToList();
        Assert.Single(ratings);

        var history = conn.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='CombatGlicko2History'").ToList();
        Assert.Single(history);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        Schema.Initialize(conn); // should not throw
        var count = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table'");
        Assert.True(count >= 20);
    }
}

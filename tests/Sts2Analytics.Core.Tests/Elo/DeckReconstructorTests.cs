using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Elo;

public class DeckReconstructorTests
{
    private static SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    // Helper to insert a run and return its ID
    private static long InsertRun(SqliteConnection conn)
    {
        conn.Execute("""
            INSERT INTO Runs (FileName, Seed, Character, Ascension, GameMode, BuildVersion,
                Win, WasAbandoned, KilledByEncounter, KilledByEvent, StartTime, RunTime,
                Acts, SchemaVersion, PlatformType, MaxPotionSlots)
            VALUES ('test.run', '12345', 'WARRIOR', 0, 'standard', 'v1.0',
                1, 0, 'NONE', 'NONE', '2024-01-01', 0, 1, 1, 'pc', 3)
            """);
        return conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    // Helper to insert a floor and return its ID
    private static long InsertFloor(SqliteConnection conn, long runId, int actIndex, int floorIndex, string mapPointType = "monster")
    {
        conn.Execute("""
            INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, TurnsTaken,
                PlayerId, CurrentHp, MaxHp, DamageTaken, HpHealed, MaxHpGained, MaxHpLost,
                CurrentGold, GoldGained, GoldSpent, GoldLost, GoldStolen)
            VALUES (@RunId, @ActIndex, @FloorIndex, @MapPointType, 0,
                1, 80, 80, 0, 0, 0, 0, 100, 0, 0, 0, 0)
            """, new { RunId = runId, ActIndex = actIndex, FloorIndex = floorIndex, MapPointType = mapPointType });
        return conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    [Fact]
    public void GetDeckAtFloor_ReturnsStarterDeck()
    {
        // Starter deck given at floor 0 (FloorIndex=0), check deck at floor 1
        using var conn = CreateDb();
        var runId = InsertRun(conn);

        // Floor 0: starter cards added
        var floor0Id = InsertFloor(conn, runId, 0, 0);
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.STRIKE', 0, 'starter')", new { F = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.STRIKE', 0, 'starter')", new { F = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.DEFEND', 0, 'starter')", new { F = floor0Id });

        // Floor 1: combat floor
        var floor1Id = InsertFloor(conn, runId, 0, 1);

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, floor1Id);

        Assert.Equal(3, deck.Count);
        Assert.Equal(2, deck.Count(c => c == "CARD.STRIKE"));
        Assert.Contains("CARD.DEFEND", deck);
    }

    [Fact]
    public void GetDeckAtFloor_IncludesCardRewards()
    {
        // Card gained at floor 1, verify deck at floor 2 includes it
        using var conn = CreateDb();
        var runId = InsertRun(conn);

        var floor0Id = InsertFloor(conn, runId, 0, 0);
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.STRIKE', 0, 'starter')", new { F = floor0Id });

        var floor1Id = InsertFloor(conn, runId, 0, 1);
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.INFLAME', 0, 'reward')", new { F = floor1Id });

        var floor2Id = InsertFloor(conn, runId, 0, 2);

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

        Assert.Equal(2, deck.Count);
        Assert.Contains("CARD.STRIKE", deck);
        Assert.Contains("CARD.INFLAME", deck);
    }

    [Fact]
    public void GetDeckAtFloor_ExcludesRemovedCards()
    {
        // Card removed at shop, verify not in deck after
        using var conn = CreateDb();
        var runId = InsertRun(conn);

        var floor0Id = InsertFloor(conn, runId, 0, 0);
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.STRIKE', 0, 'starter')", new { F = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.DEFEND', 0, 'starter')", new { F = floor0Id });

        // Shop at floor 1 — remove CARD.STRIKE
        var floor1Id = InsertFloor(conn, runId, 0, 1, "shop");
        // FloorAddedToDeck=0 means it was added at FloorIndex=0
        conn.Execute("INSERT INTO CardRemovals (FloorId, CardId, FloorAddedToDeck) VALUES (@F, 'CARD.STRIKE', 0)", new { F = floor1Id });

        var floor2Id = InsertFloor(conn, runId, 0, 2);

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

        Assert.Single(deck);
        Assert.Contains("CARD.DEFEND", deck);
        Assert.DoesNotContain("CARD.STRIKE", deck);
    }

    [Fact]
    public void GetDeckAtFloor_AppliesTransforms()
    {
        // Card transformed, verify old card gone, new card present
        using var conn = CreateDb();
        var runId = InsertRun(conn);

        var floor0Id = InsertFloor(conn, runId, 0, 0);
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.STRIKE', 0, 'starter')", new { F = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.DEFEND', 0, 'starter')", new { F = floor0Id });

        // Event floor transforms CARD.STRIKE → CARD.INFLAME
        var floor1Id = InsertFloor(conn, runId, 0, 1, "unknown");
        conn.Execute("INSERT INTO CardTransforms (FloorId, OriginalCardId, FinalCardId) VALUES (@F, 'CARD.STRIKE', 'CARD.INFLAME')", new { F = floor1Id });

        var floor2Id = InsertFloor(conn, runId, 0, 2);

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

        Assert.Equal(2, deck.Count);
        Assert.DoesNotContain("CARD.STRIKE", deck);
        Assert.Contains("CARD.INFLAME", deck);
        Assert.Contains("CARD.DEFEND", deck);
    }

    [Fact]
    public void GetDeckAtFloor_AppliesRestSiteUpgrades()
    {
        // Card upgraded at rest site, verify CARD.X becomes CARD.X+1
        using var conn = CreateDb();
        var runId = InsertRun(conn);

        var floor0Id = InsertFloor(conn, runId, 0, 0);
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.STRIKE', 0, 'starter')", new { F = floor0Id });
        conn.Execute("INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, Source) VALUES (@F, 'CARD.DEFEND', 0, 'starter')", new { F = floor0Id });

        // Rest site at floor 1 — upgrade CARD.STRIKE
        var floor1Id = InsertFloor(conn, runId, 0, 1, "rest");
        conn.Execute("INSERT INTO RestSiteChoices (FloorId, Choice) VALUES (@F, 'upgrade')", new { F = floor1Id });
        var restChoiceId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()");
        conn.Execute("INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId) VALUES (@R, 'CARD.STRIKE')", new { R = restChoiceId });

        var floor2Id = InsertFloor(conn, runId, 0, 2);

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, floor2Id);

        Assert.Equal(2, deck.Count);
        Assert.Contains("CARD.STRIKE+1", deck);
        Assert.DoesNotContain("CARD.STRIKE", deck);
        Assert.Contains("CARD.DEFEND", deck);
    }

    [Fact]
    public void GetDeckAtFloor_WithSampleRun_ReturnsReasonableDeck()
    {
        // Use real sample_win.run fixture, verify >= 5 cards at first combat floor
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_win.run");
        var runFile = RunFileParser.Parse(path);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        // Find a later combat floor where enough cards have been gained (offset to skip early floors)
        // sample_win.run gains 5 cards by Act0 Floor4, so use the 5th monster floor
        var combatFloorId = conn.ExecuteScalar<long>("""
            SELECT f.Id FROM Floors f
            WHERE f.RunId = @RunId AND f.MapPointType = 'monster'
            ORDER BY f.Id ASC
            LIMIT 1 OFFSET 4
            """, new { RunId = runId });

        Assert.True(combatFloorId > 0, "Should have at least 5 combat floors");

        var reconstructor = new DeckReconstructor(conn);
        var deck = reconstructor.GetDeckAtFloor(runId, combatFloorId);

        Assert.True(deck.Count >= 5, $"Expected >= 5 cards at combat floor 5, got {deck.Count}: [{string.Join(", ", deck)}]");
    }
}

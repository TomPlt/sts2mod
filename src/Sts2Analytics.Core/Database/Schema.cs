using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Database;

public static class Schema
{
    public static void Initialize(IDbConnection connection)
    {
        connection.Execute(Sql);
    }

    private const string Sql = """
        CREATE TABLE IF NOT EXISTS Runs (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FileName TEXT NOT NULL UNIQUE,
            Seed TEXT NOT NULL,
            Character TEXT NOT NULL,
            Ascension INTEGER NOT NULL DEFAULT 0,
            GameMode TEXT NOT NULL DEFAULT '',
            BuildVersion TEXT NOT NULL DEFAULT '',
            Win INTEGER NOT NULL DEFAULT 0,
            WasAbandoned INTEGER NOT NULL DEFAULT 0,
            KilledByEncounter TEXT NOT NULL DEFAULT '',
            KilledByEvent TEXT NOT NULL DEFAULT '',
            StartTime TEXT NOT NULL DEFAULT '',
            RunTime REAL NOT NULL DEFAULT 0,
            Acts INTEGER NOT NULL DEFAULT 0,
            SchemaVersion INTEGER NOT NULL DEFAULT 0,
            PlatformType TEXT NOT NULL DEFAULT '',
            Modifiers TEXT,
            MaxPotionSlots INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Floors (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            ActIndex INTEGER NOT NULL,
            FloorIndex INTEGER NOT NULL,
            MapPointType TEXT NOT NULL DEFAULT '',
            EncounterId TEXT,
            RoomType TEXT,
            TurnsTaken INTEGER NOT NULL DEFAULT 0,
            PlayerId INTEGER NOT NULL DEFAULT 0,
            CurrentHp INTEGER NOT NULL DEFAULT 0,
            MaxHp INTEGER NOT NULL DEFAULT 0,
            DamageTaken INTEGER NOT NULL DEFAULT 0,
            HpHealed INTEGER NOT NULL DEFAULT 0,
            MaxHpGained INTEGER NOT NULL DEFAULT 0,
            MaxHpLost INTEGER NOT NULL DEFAULT 0,
            CurrentGold INTEGER NOT NULL DEFAULT 0,
            GoldGained INTEGER NOT NULL DEFAULT 0,
            GoldSpent INTEGER NOT NULL DEFAULT 0,
            GoldLost INTEGER NOT NULL DEFAULT 0,
            GoldStolen INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CardChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            CardId TEXT NOT NULL,
            WasPicked INTEGER NOT NULL DEFAULT 0,
            WasBought INTEGER NOT NULL DEFAULT 0,
            UpgradeLevel INTEGER NOT NULL DEFAULT 0,
            EnchantmentId TEXT,
            EnchantmentAmount INTEGER NOT NULL DEFAULT 0,
            Source TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS RelicChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            RelicId TEXT NOT NULL,
            WasPicked INTEGER NOT NULL DEFAULT 0,
            WasBought INTEGER NOT NULL DEFAULT 0,
            Source TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS PotionChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            PotionId TEXT NOT NULL,
            WasPicked INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS PotionEvents (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            PotionId TEXT NOT NULL,
            Action TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS EventChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            EventId TEXT,
            ChoiceKey TEXT,
            ChoiceTable TEXT,
            Variables TEXT
        );

        CREATE TABLE IF NOT EXISTS RestSiteChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            Choice TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS RestSiteUpgrades (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RestSiteChoiceId INTEGER NOT NULL REFERENCES RestSiteChoices(Id),
            CardId TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS CardTransforms (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            OriginalCardId TEXT NOT NULL,
            FinalCardId TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Monsters (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            MonsterId TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS FinalDecks (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            CardId TEXT NOT NULL,
            UpgradeLevel INTEGER NOT NULL DEFAULT 0,
            FloorAdded INTEGER NOT NULL DEFAULT 0,
            EnchantmentId TEXT
        );

        CREATE TABLE IF NOT EXISTS FinalRelics (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            RelicId TEXT NOT NULL,
            FloorAdded INTEGER NOT NULL DEFAULT 0,
            Props TEXT
        );

        CREATE TABLE IF NOT EXISTS FinalPotions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            PotionId TEXT NOT NULL,
            SlotIndex INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CardsGained (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            CardId TEXT NOT NULL,
            UpgradeLevel INTEGER NOT NULL DEFAULT 0,
            EnchantmentId TEXT,
            Source TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS CardRemovals (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            CardId TEXT NOT NULL,
            FloorAddedToDeck INTEGER
        );

        CREATE TABLE IF NOT EXISTS CardEnchantments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            CardId TEXT NOT NULL,
            EnchantmentId TEXT NOT NULL,
            EnchantmentAmount INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS AncientChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            FloorId INTEGER NOT NULL REFERENCES Floors(Id),
            TextKey TEXT NOT NULL,
            WasChosen INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Glicko2Ratings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CardId TEXT NOT NULL,
            Character TEXT NOT NULL,
            Context TEXT NOT NULL DEFAULT 'overall',
            Rating REAL NOT NULL DEFAULT 1500.0,
            RatingDeviation REAL NOT NULL DEFAULT 350.0,
            Volatility REAL NOT NULL DEFAULT 0.06,
            GamesPlayed INTEGER NOT NULL DEFAULT 0,
            LastUpdatedRunId INTEGER,
            UNIQUE(CardId, Character, Context)
        );

        CREATE TABLE IF NOT EXISTS Glicko2History (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Glicko2RatingId INTEGER NOT NULL REFERENCES Glicko2Ratings(Id),
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            RatingBefore REAL NOT NULL DEFAULT 0,
            RatingAfter REAL NOT NULL DEFAULT 0,
            RdBefore REAL NOT NULL DEFAULT 0,
            RdAfter REAL NOT NULL DEFAULT 0,
            VolatilityBefore REAL NOT NULL DEFAULT 0,
            VolatilityAfter REAL NOT NULL DEFAULT 0,
            Timestamp TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS PlayerRatings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Context TEXT NOT NULL UNIQUE,
            Rating REAL NOT NULL DEFAULT 1500.0,
            RatingDeviation REAL NOT NULL DEFAULT 350.0,
            Volatility REAL NOT NULL DEFAULT 0.06,
            GamesPlayed INTEGER NOT NULL DEFAULT 0,
            LastUpdatedRunId INTEGER
        );

        CREATE TABLE IF NOT EXISTS PlayerRatingHistory (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PlayerRatingId INTEGER NOT NULL REFERENCES PlayerRatings(Id),
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            RatingBefore REAL NOT NULL DEFAULT 0,
            RatingAfter REAL NOT NULL DEFAULT 0,
            RdBefore REAL NOT NULL DEFAULT 0,
            RdAfter REAL NOT NULL DEFAULT 0,
            VolatilityBefore REAL NOT NULL DEFAULT 0,
            VolatilityAfter REAL NOT NULL DEFAULT 0,
            Opponent TEXT NOT NULL DEFAULT '',
            OpponentRating REAL NOT NULL DEFAULT 0,
            Outcome REAL NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS IX_PlayerRatingHistory_PlayerRatingId ON PlayerRatingHistory(PlayerRatingId);
        CREATE INDEX IF NOT EXISTS IX_PlayerRatingHistory_RunId ON PlayerRatingHistory(RunId);

        CREATE TABLE IF NOT EXISTS BlindSpots (
            CardId TEXT NOT NULL,
            Context TEXT NOT NULL,
            BlindSpotType TEXT NOT NULL,
            Score REAL NOT NULL DEFAULT 0,
            PickRate REAL NOT NULL DEFAULT 0,
            ExpectedPickRate REAL NOT NULL DEFAULT 0,
            WinRateDelta REAL NOT NULL DEFAULT 0,
            GamesAnalyzed INTEGER NOT NULL DEFAULT 0,
            LastUpdated TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (CardId, Context)
        );

        CREATE TABLE IF NOT EXISTS AncientGlicko2Ratings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ChoiceKey TEXT NOT NULL,
            Character TEXT NOT NULL,
            Context TEXT NOT NULL DEFAULT 'overall',
            Rating REAL NOT NULL DEFAULT 1500.0,
            RatingDeviation REAL NOT NULL DEFAULT 350.0,
            Volatility REAL NOT NULL DEFAULT 0.06,
            GamesPlayed INTEGER NOT NULL DEFAULT 0,
            LastUpdatedRunId INTEGER,
            UNIQUE(ChoiceKey, Character, Context)
        );

        CREATE TABLE IF NOT EXISTS AncientGlicko2History (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            AncientGlicko2RatingId INTEGER NOT NULL REFERENCES AncientGlicko2Ratings(Id),
            RunId INTEGER NOT NULL REFERENCES Runs(Id),
            RatingBefore REAL NOT NULL DEFAULT 0,
            RatingAfter REAL NOT NULL DEFAULT 0,
            RdBefore REAL NOT NULL DEFAULT 0,
            RdAfter REAL NOT NULL DEFAULT 0,
            VolatilityBefore REAL NOT NULL DEFAULT 0,
            VolatilityAfter REAL NOT NULL DEFAULT 0,
            Timestamp TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS IX_AncientGlicko2Ratings_ChoiceKey ON AncientGlicko2Ratings(ChoiceKey);
        CREATE INDEX IF NOT EXISTS IX_AncientGlicko2History_RatingId ON AncientGlicko2History(AncientGlicko2RatingId);

        CREATE INDEX IF NOT EXISTS IX_Floors_RunId ON Floors(RunId);
        CREATE INDEX IF NOT EXISTS IX_CardChoices_FloorId ON CardChoices(FloorId);
        CREATE INDEX IF NOT EXISTS IX_CardChoices_CardId ON CardChoices(CardId);
        CREATE INDEX IF NOT EXISTS IX_RelicChoices_FloorId ON RelicChoices(FloorId);
        CREATE INDEX IF NOT EXISTS IX_FinalDecks_RunId ON FinalDecks(RunId);
        CREATE INDEX IF NOT EXISTS IX_Glicko2Ratings_CardId ON Glicko2Ratings(CardId);
        CREATE INDEX IF NOT EXISTS IX_Glicko2History_Glicko2RatingId ON Glicko2History(Glicko2RatingId);
        """;
}

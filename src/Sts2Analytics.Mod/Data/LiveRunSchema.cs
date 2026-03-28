namespace SpireOracle.Data;

public static class LiveRunSchema
{
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS LiveRuns (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Seed TEXT NOT NULL,
            Character TEXT NOT NULL,
            Ascension INTEGER NOT NULL DEFAULT 0,
            StartedAt TEXT NOT NULL,
            EndedAt TEXT,
            Win INTEGER,
            RunFileName TEXT
        );

        CREATE TABLE IF NOT EXISTS Combats (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
            EncounterId TEXT NOT NULL,
            ActIndex INTEGER NOT NULL,
            FloorIndex INTEGER NOT NULL,
            StartedAt TEXT NOT NULL,
            EndedAt TEXT,
            Won INTEGER
        );

        CREATE TABLE IF NOT EXISTS Turns (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CombatId INTEGER NOT NULL REFERENCES Combats(Id),
            TurnNumber INTEGER NOT NULL,
            StartingEnergy INTEGER NOT NULL DEFAULT 0,
            StartingHp INTEGER NOT NULL DEFAULT 0,
            StartingBlock INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CombatActions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TurnId INTEGER NOT NULL REFERENCES Turns(Id),
            Seq INTEGER NOT NULL,
            ActionType TEXT NOT NULL,
            SourceId TEXT,
            TargetId TEXT,
            Amount INTEGER NOT NULL DEFAULT 0,
            Detail TEXT
        );

        CREATE TABLE IF NOT EXISTS EventDecisions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
            ActIndex INTEGER NOT NULL,
            FloorIndex INTEGER NOT NULL,
            EventId TEXT,
            OptionChosen TEXT,
            Timestamp TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS MapChoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
            ActIndex INTEGER NOT NULL,
            FloorIndex INTEGER NOT NULL,
            MapPointType TEXT NOT NULL,
            Timestamp TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS RewardDecisions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL REFERENCES LiveRuns(Id),
            ActIndex INTEGER NOT NULL,
            FloorIndex INTEGER NOT NULL,
            DecisionType TEXT NOT NULL,
            ItemId TEXT,
            WasPicked INTEGER NOT NULL DEFAULT 0,
            Timestamp TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Combats_RunId ON Combats(RunId);
        CREATE INDEX IF NOT EXISTS IX_Turns_CombatId ON Turns(CombatId);
        CREATE INDEX IF NOT EXISTS IX_CombatActions_TurnId ON CombatActions(TurnId);
        CREATE INDEX IF NOT EXISTS IX_EventDecisions_RunId ON EventDecisions(RunId);
        CREATE INDEX IF NOT EXISTS IX_MapChoices_RunId ON MapChoices(RunId);
        CREATE INDEX IF NOT EXISTS IX_RewardDecisions_RunId ON RewardDecisions(RunId);
        """;
}
